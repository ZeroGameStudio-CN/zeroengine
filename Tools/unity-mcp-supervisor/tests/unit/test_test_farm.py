from __future__ import annotations

import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

from unity_mcp_supervisor import test_farm
from unity_mcp_supervisor.errors import ProjectBusyError, UsageError
from unity_mcp_supervisor.service_state import StatePaths


def request(tmp_path: Path, task: str) -> test_farm.TestJobRequest:
    return test_farm.TestJobRequest(
        project_root=str(tmp_path / "project"),
        task_id=task,
        platform="EditMode",
        filters=(f"Tests.{task}",),
        artifact_root=str(tmp_path / "artifacts" / task),
        snapshot_id=f"snapshot-{task}",
        snapshot_manifest=str(tmp_path / "artifacts" / task / "snapshot.json"),
    )


def test_submit_requires_provision_and_exact_scope(tmp_path: Path) -> None:
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    with pytest.raises(UsageError, match="not provisioned"):
        store.submit(request(tmp_path, "one"))
    store.provision(1)
    invalid = request(tmp_path, "one")
    invalid = test_farm.TestJobRequest(
        project_root=invalid.project_root,
        task_id=invalid.task_id,
        platform=invalid.platform,
    )
    with pytest.raises(UsageError, match="filter"):
        store.submit(invalid)


def test_fifo_claims_and_owner_cancel(tmp_path: Path) -> None:
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    store.provision(1)
    first = store.submit(request(tmp_path, "one"))
    second = store.submit(request(tmp_path, "two"))
    assert first["queue_position"] == 1
    assert second["queue_position"] == 2
    assert store.claim_next(worker_pid=100)["job_id"] == first["job_id"]
    with pytest.raises(UsageError, match="submitting"):
        store.cancel(second["job_id"], "another-task")
    cancelled = store.cancel(second["job_id"], "two")
    assert cancelled["state"] == "cancelled"


def test_two_workers_execute_jobs_concurrently(tmp_path: Path) -> None:
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    store.provision(2)
    jobs = [store.submit(request(tmp_path, name)) for name in ("one", "two")]
    barrier = threading.Barrier(2)
    intervals: list[tuple[float, float]] = []
    lock = threading.Lock()

    def execute(_job: dict, _slot: dict) -> test_farm.WorkerResult:
        barrier.wait(timeout=2)
        started = time.monotonic()
        time.sleep(0.05)
        finished = time.monotonic()
        with lock:
            intervals.append((started, finished))
        return test_farm.WorkerResult("passed", {"tests": {"total": 1, "passed": 1}})

    with ThreadPoolExecutor(max_workers=2) as executor:
        results = list(
            executor.map(
                lambda _: test_farm.TestFarmWorker(store, execute).run_once(), jobs
            )
        )
    assert {value["state"] for value in results if value} == {"passed"}
    assert len(intervals) == 2
    assert max(value[0] for value in intervals) < min(value[1] for value in intervals)
    assert len({value["slot_id"] for value in results if value}) == 2


def test_worker_exception_quarantines_only_its_slot(tmp_path: Path) -> None:
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    store.provision(2)
    job = store.submit(request(tmp_path, "one"))

    def fail(_job: dict, _slot: dict) -> test_farm.WorkerResult:
        raise RuntimeError("boom")

    result = test_farm.TestFarmWorker(store, fail).run_once(worker_pid=123)
    assert result and result["job_id"] == job["job_id"]
    assert result["state"] == "infra_failed"
    slots = store.status()["slots"]
    assert slots[0]["state"] == "quarantined"
    assert slots[1]["state"] == "available"

    sentinel = Path(slots[0]["root"]) / "corrupt.txt"
    sentinel.write_text("corrupt", encoding="utf-8")
    repaired = store.provision(2)
    assert repaired["slots"][0]["state"] == "available"
    assert not sentinel.exists()


def test_running_slot_blocks_slot_root_change(tmp_path: Path) -> None:
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    store.provision(1, tmp_path / "slots-one")
    store.submit(request(tmp_path, "one"))
    store.claim_next(worker_pid=123)
    with pytest.raises(ProjectBusyError, match="slot root"):
        store.provision(1, tmp_path / "slots-two")


def test_dead_worker_becomes_unknown_and_slot_is_quarantined(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    store.provision(1)
    job = store.submit(request(tmp_path, "one"))
    store.claim_next(worker_pid=999999)
    monkeypatch.setattr(
        "unity_mcp_supervisor.test_farm.process_alive", lambda _pid: False
    )
    assert store.recover_dead_workers() == [job["job_id"]]
    assert store.job(job["job_id"])["state"] == "outcome_unknown"
    assert store.status()["slots"][0]["state"] == "quarantined"


def test_wait_recovers_a_dead_worker_without_a_separate_status_poll(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    store.provision(1)
    job = store.submit(request(tmp_path, "one"))
    store.claim_next(worker_pid=999999)
    monkeypatch.setattr(
        "unity_mcp_supervisor.test_farm.process_alive", lambda _pid: False
    )
    result = store.wait(job["job_id"], timeout_seconds=1)
    assert result["state"] == "outcome_unknown"
