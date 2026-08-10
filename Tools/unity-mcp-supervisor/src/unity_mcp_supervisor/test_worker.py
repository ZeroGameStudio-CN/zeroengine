from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

import psutil

from .errors import UsageError
from .service_state import StatePaths, _atomic_write, ensure_private_directory
from .test_farm import TestFarmStore, TestFarmWorker, WorkerResult
from .test_snapshot import (
    CRITICAL_INPUTS,
    materialize_snapshot,
    observe_vcs,
    sha256_file,
)


def read_project_version(project: Path) -> str:
    path = project / "ProjectSettings" / "ProjectVersion.txt"
    try:
        content = path.read_text(encoding="utf-8-sig")
    except OSError as exc:
        raise UsageError(f"Cannot read Unity project version: {path}") from exc
    match = re.search(r"^m_EditorVersion:\s*(\S+)\s*$", content, re.MULTILINE)
    if not match:
        raise UsageError(f"Unity project version is invalid: {path}")
    return match.group(1)


def resolve_unity(project: Path) -> Path:
    version = read_project_version(project)
    candidates: list[Path] = []
    version_key = "UNITY_" + re.sub(r"[^A-Za-z0-9]", "_", version).upper()
    for name in (version_key, "UNITY_EDITOR_PATH", "UNITY_PATH"):
        if os.environ.get(name):
            candidates.append(Path(os.environ[name]))
    if sys.platform == "win32":
        roots = (
            Path(os.environ.get("ProgramFiles", "C:/Program Files"))
            / "Unity"
            / "Hub"
            / "Editor",
            Path("D:/unity/editors"),
        )
        for root in roots:
            candidates.extend(
                (
                    root / version / "Editor" / "Unity.exe",
                    root / f"Unity {version}" / "Editor" / "Unity.exe",
                )
            )
    elif sys.platform == "darwin":
        for root in (
            Path("/Applications/Unity/Hub/Editor"),
            Path.home() / "Unity" / "Hub" / "Editor",
        ):
            candidates.append(root / version / "Unity.app" / "Contents/MacOS/Unity")
    else:
        for root in (Path.home() / "Unity/Hub/Editor", Path("/opt/unity/editors")):
            candidates.append(root / version / "Editor/Unity")
    for candidate in candidates:
        resolved = candidate.expanduser().resolve(strict=False)
        if resolved.is_file():
            return resolved
    raise UsageError(f"Exact Unity Editor {version} is not installed.")


def build_unity_command(
    unity: Path, project: Path, job: dict[str, Any], artifact_root: Path
) -> list[str]:
    command = [
        str(unity),
        "-batchmode",
        "-runTests",
        "-forgetProjectPath",
        "-projectPath",
        str(project),
        "-testPlatform",
        job["platform"],
        "-testResults",
        str(artifact_root / "results.xml"),
        "-logFile",
        str(artifact_root / "unity.log"),
    ]
    for flag, values in (
        ("-testFilter", job["filters"]),
        ("-testCategory", job["categories"]),
        ("-assemblyNames", job["assemblies"]),
    ):
        if values:
            command.extend((flag, ";".join(values)))
    return command


def _attribute_int(attributes: dict[str, str], *names: str) -> int | None:
    for name in names:
        try:
            return int(float(attributes[name]))
        except (KeyError, ValueError):
            continue
    return None


def parse_test_results(path: Path) -> dict[str, Any]:
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as exc:
        raise UsageError(f"Cannot parse Unity test results: {path}") from exc
    cases = list(root.iter("test-case"))
    total = _attribute_int(root.attrib, "total", "testcasecount")
    passed = _attribute_int(root.attrib, "passed")
    failed = _attribute_int(root.attrib, "failed", "failures")
    errors = _attribute_int(root.attrib, "errors") or 0
    inconclusive = _attribute_int(root.attrib, "inconclusive") or 0
    if total is None:
        total = len(cases)
    if passed is None:
        passed = sum(
            case.attrib.get("result", "").casefold() == "passed" for case in cases
        )
    if failed is None:
        failed = sum(
            case.attrib.get("result", "").casefold() == "failed" for case in cases
        )
    result = root.attrib.get("result", root.attrib.get("outcome", "Unknown"))
    success = (
        total > 0
        and passed > 0
        and failed == 0
        and errors == 0
        and inconclusive == 0
        and result.casefold() not in {"failed", "failure", "error"}
    )
    return {
        "result": result,
        "total": total,
        "passed": passed,
        "failed": failed,
        "errors": errors,
        "inconclusive": inconclusive,
        "success": success,
        "cases": sorted(
            [
                {
                    "name": case.attrib.get("fullname", case.attrib.get("name", "")),
                    "result": case.attrib.get("result", "Unknown"),
                }
                for case in cases
            ],
            key=lambda value: (value["name"], value["result"]),
        ),
    }


def mutation_fingerprint(project: Path) -> dict[str, Any]:
    observation = observe_vcs(project)
    pending: list[dict[str, Any]] = []
    for entry in observation.entries:
        value: dict[str, Any] = {
            "path": entry.path,
            "operation": entry.operation,
            "status": entry.status,
        }
        path = project / Path(entry.path)
        if entry.operation == "copy" and path.is_file():
            value.update(size=path.stat().st_size, sha256=sha256_file(path))
        pending.append(value)
    critical = {}
    for relative in CRITICAL_INPUTS:
        path = project / relative
        critical[relative] = sha256_file(path) if path.is_file() else None
    return {
        "revision": observation.revision,
        "pending": sorted(
            pending, key=lambda value: (value["path"], value["operation"])
        ),
        "critical": critical,
    }


def _terminate_process_tree(process: subprocess.Popen[Any]) -> None:
    try:
        parent = psutil.Process(process.pid)
        descendants = parent.children(recursive=True)
        for child in descendants:
            child.terminate()
        parent.terminate()
        _, alive = psutil.wait_procs([*descendants, parent], timeout=5)
        for item in alive:
            item.kill()
    except psutil.Error:
        process.kill()
    process.wait(timeout=10)


def _write_json(path: Path, value: dict[str, Any]) -> None:
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )


def _run_materialized(
    store: TestFarmStore,
    job: dict[str, Any],
    project: Path,
    artifact_root: Path,
    *,
    timeout_seconds: float,
) -> WorkerResult:
    ensure_private_directory(artifact_root)
    timings: dict[str, float] = {
        "queue_wait_seconds": max(
            0.0, float(job["started_at"] or time.time()) - float(job["created_at"])
        )
    }
    before = mutation_fingerprint(project)
    _write_json(artifact_root / "slot-before.json", before)
    unity = resolve_unity(project)
    command = build_unity_command(unity, project, job, artifact_root)
    command_summary = {
        "unity": str(unity),
        "project": str(project),
        "platform": job["platform"],
        "filters": job["filters"],
        "categories": job["categories"],
        "assemblies": job["assemblies"],
    }
    _write_json(artifact_root / "command.json", command_summary)
    kwargs: dict[str, Any] = {"cwd": str(project)}
    if os.name == "nt":
        kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
    process_started_at = dt.datetime.now(dt.timezone.utc).isoformat()
    process = subprocess.Popen(command, **kwargs)
    test_started = time.monotonic()
    timed_out = False
    cancelled = False
    peak_memory_bytes = 0
    while process.poll() is None:
        try:
            process_state = psutil.Process(process.pid)
            memory = process_state.memory_info().rss + sum(
                child.memory_info().rss
                for child in process_state.children(recursive=True)
            )
            peak_memory_bytes = max(peak_memory_bytes, memory)
        except psutil.Error:
            pass
        current = store.job(job["job_id"])
        if current["cancel_requested"]:
            cancelled = True
            _terminate_process_tree(process)
            break
        if time.monotonic() - test_started >= timeout_seconds:
            timed_out = True
            _terminate_process_tree(process)
            break
        time.sleep(0.1)
    timings["test_seconds"] = time.monotonic() - test_started
    after = mutation_fingerprint(project)
    _write_json(artifact_root / "slot-after.json", after)
    mutated = before != after
    results_path = artifact_root / "results.xml"
    summary: dict[str, Any] = {
        "schema_version": 1,
        "job_id": job["job_id"],
        "started_at": process_started_at,
        "unity_version": read_project_version(project),
        "timings": timings,
        "unity_exit_code": process.returncode,
        "timed_out": timed_out,
        "cancelled": cancelled,
        "slot_mutated": mutated,
        "peak_memory_bytes": peak_memory_bytes,
    }
    if cancelled:
        summary["error"] = "test job cancelled"
        _write_json(artifact_root / "summary.json", summary)
        return WorkerResult("infra_failed", summary, quarantine=True)
    if timed_out:
        summary["error"] = "Unity test process timed out"
        _write_json(artifact_root / "summary.json", summary)
        return WorkerResult("infra_failed", summary, quarantine=True)
    if mutated:
        summary["error"] = "Unity test mutated managed project inputs"
        _write_json(artifact_root / "summary.json", summary)
        return WorkerResult("infra_failed", summary, quarantine=True)
    if not results_path.is_file():
        log = (
            (artifact_root / "unity.log").read_text(encoding="utf-8", errors="replace")
            if (artifact_root / "unity.log").is_file()
            else ""
        )
        classification = "license" if "licens" in log.casefold() else "unity-process"
        summary.update(
            error="Unity did not produce test results", infrastructure=classification
        )
        _write_json(artifact_root / "summary.json", summary)
        return WorkerResult("infra_failed", summary, quarantine=False)
    tests = parse_test_results(results_path)
    summary["tests"] = tests
    state = "passed" if tests["success"] else "failed"
    _write_json(artifact_root / "summary.json", summary)
    return WorkerResult(state, summary)


def _cache_key(job: dict[str, Any], project: Path) -> str:
    try:
        manifest = json.loads(
            Path(job["snapshot_manifest"]).read_text(encoding="utf-8")
        )
        snapshot_inputs = {
            "vcs": manifest["vcs"],
            "critical_inputs": manifest["critical_inputs"],
            "overlay": manifest["overlay"],
        }
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as exc:
        raise UsageError("Cannot derive the Unity warm-cache input key.") from exc
    value = {
        "project_root": job["project_root"],
        "unity_version": read_project_version(project),
        "snapshot_inputs": snapshot_inputs,
    }
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def _read_cache_state(marker: Path, cache_key: str) -> str:
    try:
        value = json.loads(marker.read_text(encoding="utf-8"))
        if value.get("cache_key") == cache_key:
            return str(value.get("state", "uncertified"))
    except (OSError, ValueError, TypeError):
        pass
    return "uncertified"


def _write_cache_state(marker: Path, cache_key: str, state: str) -> None:
    _atomic_write(
        marker,
        json.dumps(
            {
                "schema_version": 1,
                "cache_key": cache_key,
                "state": state,
                "updated_at": time.time(),
            },
            sort_keys=True,
        )
        + "\n",
    )


def _remove_slot_library(project: Path, slot_root: Path) -> None:
    project = project.resolve()
    slot_root = slot_root.resolve()
    try:
        project.relative_to(slot_root)
    except ValueError as exc:
        raise UsageError("Refusing to clean a Library outside the test slot.") from exc
    library = project / "Library"
    if library.is_dir():
        shutil.rmtree(library)


def _result_signature(result: WorkerResult) -> dict[str, Any]:
    return {
        "state": result.state,
        "tests": result.summary.get("tests"),
        "infrastructure": result.summary.get("infrastructure"),
        "error": result.summary.get("error"),
    }


def execute_job(
    store: TestFarmStore,
    job: dict[str, Any],
    slot: dict[str, Any],
    *,
    timeout_seconds: float | None = None,
) -> WorkerResult:
    artifact_root = Path(job["artifact_root"]).resolve()
    ensure_private_directory(artifact_root)
    slot_root = Path(slot["root"]).resolve()
    materialize_started = time.monotonic()
    project = materialize_snapshot(Path(job["snapshot_manifest"]), slot_root)
    materialize_seconds = time.monotonic() - materialize_started
    timeout_seconds = timeout_seconds or float(job["timeout_seconds"])
    marker = slot_root / "warm-cache.json"
    cache_key = _cache_key(job, project)
    cache_state = _read_cache_state(marker, cache_key)
    if cache_state == "disabled":
        _remove_slot_library(project, slot_root)
        result = _run_materialized(
            store, job, project, artifact_root, timeout_seconds=timeout_seconds
        )
        result.summary["warm_cache"] = "disabled-cold"
        result.summary["timings"]["materialize_seconds"] = materialize_seconds
        _write_json(artifact_root / "summary.json", result.summary)
        return result
    if cache_state == "certified":
        result = _run_materialized(
            store, job, project, artifact_root, timeout_seconds=timeout_seconds
        )
        result.summary["warm_cache"] = "hit"
        result.summary["timings"]["materialize_seconds"] = materialize_seconds
        _write_json(artifact_root / "summary.json", result.summary)
        return result

    _remove_slot_library(project, slot_root)
    cold = _run_materialized(
        store, job, project, artifact_root / "cold", timeout_seconds=timeout_seconds
    )
    if cold.state == "infra_failed":
        cold.summary["warm_cache"] = "cold-certification-incomplete"
        cold.summary["timings"]["materialize_seconds"] = materialize_seconds
        _write_json(artifact_root / "summary.json", cold.summary)
        return cold
    warm_materialize_started = time.monotonic()
    project = materialize_snapshot(Path(job["snapshot_manifest"]), slot_root)
    warm_materialize_seconds = time.monotonic() - warm_materialize_started
    warm = _run_materialized(
        store, job, project, artifact_root / "warm", timeout_seconds=timeout_seconds
    )
    equivalent = _result_signature(cold) == _result_signature(warm)
    if not equivalent:
        _write_cache_state(marker, cache_key, "disabled")
        summary = {
            "schema_version": 1,
            "job_id": job["job_id"],
            "error": "Cold and warm Unity test results differ",
            "warm_cache": "disabled",
            "cold": _result_signature(cold),
            "warm": _result_signature(warm),
            "timings": {
                "materialize_seconds": materialize_seconds,
                "warm_materialize_seconds": warm_materialize_seconds,
            },
        }
        _write_json(artifact_root / "summary.json", summary)
        return WorkerResult("infra_failed", summary, quarantine=True)
    if warm.state == "infra_failed":
        warm.summary["warm_cache"] = "warm-certification-incomplete"
        _write_json(artifact_root / "summary.json", warm.summary)
        return warm
    _write_cache_state(marker, cache_key, "certified")
    summary = dict(warm.summary)
    summary.update(
        warm_cache="certified",
        cold_artifact=str(artifact_root / "cold"),
        warm_artifact=str(artifact_root / "warm"),
    )
    summary["timings"]["materialize_seconds"] = materialize_seconds
    summary["timings"]["warm_materialize_seconds"] = warm_materialize_seconds
    _write_json(artifact_root / "summary.json", summary)
    return WorkerResult(warm.state, summary)


def run_worker(paths: StatePaths) -> int:
    store = TestFarmStore(paths)
    worker = TestFarmWorker(
        store,
        lambda job, slot: execute_job(store, job, slot),
    )
    completed = 0
    while worker.run_once() is not None:
        completed += 1
    return completed


def launch_workers(paths: StatePaths, count: int) -> list[int]:
    command = [
        sys.executable,
        "-m",
        "unity_mcp_supervisor.cli",
        "--state-dir",
        str(paths.root),
        "test",
        "_worker",
    ]
    pids: list[int] = []
    for _ in range(max(1, count)):
        kwargs: dict[str, Any] = {
            "stdin": subprocess.DEVNULL,
            "stdout": subprocess.DEVNULL,
            "stderr": subprocess.DEVNULL,
            "close_fds": True,
        }
        if os.name == "nt":
            kwargs["creationflags"] = (
                subprocess.CREATE_NEW_PROCESS_GROUP
                | subprocess.DETACHED_PROCESS
                | subprocess.CREATE_NO_WINDOW
                | subprocess.CREATE_BREAKAWAY_FROM_JOB
            )
        else:
            kwargs["start_new_session"] = True
        process = subprocess.Popen(command, **kwargs)
        pids.append(process.pid)
    return pids
