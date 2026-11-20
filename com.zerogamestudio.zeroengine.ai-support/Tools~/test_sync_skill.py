import copy
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

SCRIPT = Path(__file__).with_name("sync_skill.py")
SPEC = importlib.util.spec_from_file_location("zeroengine_skill_sync", SCRIPT)
SYNC = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SYNC)


class SynchronizationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="zeroengine-sync-test-")
        self.root = Path(self.temporary.name)
        self.project = self.root / "Unity 项目"
        (self.project / "Packages").mkdir(parents=True)
        self.packages = []
        for name in (SYNC.PACKAGE, "com.zerogamestudio.zeroengine.console"):
            source = self.root / "cache" / name
            source.mkdir(parents=True)
            package_version = SYNC.TOOL_VERSION if name == SYNC.PACKAGE else "0.1.0"
            (source / "package.json").write_bytes(SYNC.encode({"name": name, "version": package_version}))
            (source / "README.md").write_text("# " + name + "\n已安装模块说明。\n", encoding="utf-8")
            self.packages.append({"name": name, "version": package_version, "source": "Git",
                                  "gitHash": "a" * 40, "resolvedPath": str(source)})
        source = Path(self.packages[0]["resolvedPath"])
        (source / "Tools~").mkdir()
        shutil.copyfile(SCRIPT, source / "Tools~/sync_skill.py")
        shutil.copytree(SCRIPT.parents[1] / "Skills~/zeroengine", source / "Skills~/zeroengine")
        self.snapshot = self.root / "snapshot.json"
        self.refresh()

    def tearDown(self):
        self.temporary.cleanup()

    def refresh(self):
        manifest = {"dependencies": {package["name"]:
            "https://example.invalid/zeroengine.git?path=" + package["name"] + "#" + package["gitHash"]
            for package in self.packages}}
        lock = {"dependencies": {package["name"]: {"source": "git", "hash": package["gitHash"], "depth": 0}
                                 for package in self.packages}}
        for package in self.packages:
            if package["source"] == "Local":
                manifest["dependencies"][package["name"]] = "file:../local-trial"
                lock["dependencies"][package["name"]] = {"source": "local", "version": "file:../local-trial", "depth": 0}
            if package["source"] == "Registry":
                manifest["dependencies"][package["name"]] = package["version"]
                lock["dependencies"][package["name"]] = {"source": "registry", "version": package["version"], "depth": 0}
        (self.project / "Packages/manifest.json").write_bytes(SYNC.encode(manifest))
        (self.project / "Packages/packages-lock.json").write_bytes(SYNC.encode(lock))
        self.write_snapshot()

    def write_snapshot(self):
        self.snapshot.write_bytes(SYNC.encode({"schemaVersion": 1, "projectPath": str(self.project),
            "manifestSha256": SYNC.digest((self.project / "Packages/manifest.json").read_bytes()),
            "lockSha256": SYNC.digest((self.project / "Packages/packages-lock.json").read_bytes()), "packages": self.packages}))

    def prepare(self, **kwargs):
        return SYNC.prepare(self.project, self.snapshot, **kwargs)

    def installed(self):
        plan = self.prepare()
        SYNC.apply(plan)
        return plan["target"]

    def test_preview_does_not_write_and_apply_is_idempotent(self):
        manifest = (self.project / "Packages/manifest.json").read_bytes()
        plan = self.prepare()
        self.assertTrue(plan["changed"])
        self.assertFalse(plan["target"].exists())
        SYNC.apply(plan)
        self.assertEqual([], self.prepare()["changed"])
        self.assertEqual(manifest, (self.project / "Packages/manifest.json").read_bytes())

    def test_generated_guidance_survives_checkout_and_cache_relocation(self):
        target = self.installed()
        inventory = SYNC.load(target / "references/installed-packages.json")["packages"]
        for name, package in inventory.items():
            self.assertEqual(f"Packages/{name}", package["assetPath"])
            self.assertNotIn("resolvedPath", package)

        self.project = self.project.rename(self.root / "Second checkout")
        cache = (self.root / "cache").rename(self.root / "Another package cache")
        for package in self.packages:
            package["resolvedPath"] = str(cache / package["name"])
        self.refresh()

        self.assertEqual([], self.prepare()["changed"])

    def test_checkout_line_endings_are_not_drift_or_local_edits(self):
        target = self.installed()
        for name in ("SKILL.md", "references/installed-packages.json", SYNC.RECEIPT):
            leaf = target / name
            leaf.write_bytes(leaf.read_bytes().replace(b"\n", b"\r\n"))
        for name in ("manifest.json", "packages-lock.json"):
            leaf = self.project / "Packages" / name
            leaf.write_bytes(leaf.read_bytes().replace(b"\n", b"\r\n"))
        self.write_snapshot()
        self.assertEqual([], self.prepare()["changed"])
        self.assertIn("SKILL.md", SYNC.prepare_remove(self.project)["changed"])

    def test_older_crlf_receipt_migrates_after_lf_checkout(self):
        target = self.installed()
        old_receipt = SYNC.load(target / SYNC.RECEIPT)
        old_receipt["toolVersion"] = "0.2.3"
        crlf = (target / "SKILL.md").read_bytes().replace(b"\n", b"\r\n")
        old_receipt["files"]["SKILL.md"] = SYNC.digest(crlf)
        (target / SYNC.RECEIPT).write_bytes(SYNC.encode(old_receipt))

        plan = self.prepare()
        self.assertEqual([SYNC.RECEIPT], plan["changed"])
        SYNC.apply(plan)
        self.assertEqual([], self.prepare()["changed"])

    def test_modified_managed_file_is_preserved(self):
        target = self.installed()
        (target / "SKILL.md").write_text("local edits", encoding="utf-8")
        with self.assertRaisesRegex(SYNC.Conflict, "locally modified"):
            self.prepare()
        self.assertEqual("local edits", (target / "SKILL.md").read_text(encoding="utf-8"))

    def test_registry_upgrade_refreshes_the_owned_020_skill(self):
        target = self.installed()
        old_skill = b"Use pinned Git UPM URLs for every ZeroEngine install.\n"
        (target / "SKILL.md").write_bytes(old_skill)
        old_receipt = SYNC.load(target / SYNC.RECEIPT)
        old_receipt["toolVersion"] = "0.2.0"
        old_receipt["files"]["SKILL.md"] = SYNC.digest(old_skill)
        legacy_tool = target / "scripts/sync_skill.py"
        legacy_tool.parent.mkdir()
        legacy_tool.write_bytes(b"Old generated synchronizer")
        old_receipt["files"]["scripts/sync_skill.py"] = SYNC.digest(legacy_tool.read_bytes())
        (target / SYNC.RECEIPT).write_bytes(SYNC.encode(old_receipt))
        (target / "personal-notes.md").write_text("keep", encoding="utf-8")

        self.packages[0].update(source="Registry", gitHash="")
        self.refresh()
        SYNC.apply(self.prepare())
        updated = (target / "SKILL.md").read_text(encoding="utf-8")
        self.assertIn("ZE Registry", updated)
        self.assertIn("registry-preview/README.md#publish-a-version", updated)
        self.assertFalse(legacy_tool.exists())
        self.assertEqual("keep", (target / "personal-notes.md").read_text(encoding="utf-8"))
        self.assertEqual([], self.prepare()["changed"])

    def test_remove_preserves_local_additions_and_project_packages(self):
        target = self.installed()
        extra = target / "personal-notes.md"
        extra.write_text("keep", encoding="utf-8")
        packages = (self.project / "Packages/manifest.json").read_bytes()
        plan = SYNC.prepare_remove(self.project)
        self.assertTrue(plan["changed"])
        SYNC.apply(plan)
        SYNC.prune_empty_guidance_directories(plan)
        self.assertEqual(extra.read_text(), "keep")
        self.assertFalse((target / SYNC.RECEIPT).exists())
        self.assertEqual(packages, (self.project / "Packages/manifest.json").read_bytes())

    def test_remove_absent_and_empty_owned_guidance(self):
        self.assertEqual(SYNC.prepare_remove(self.project)["changed"], [])
        target = self.installed()
        plan = SYNC.prepare_remove(self.project)
        SYNC.apply(plan)
        SYNC.prune_empty_guidance_directories(plan)
        self.assertFalse(target.exists())

    def test_remove_cli_preview_check_and_apply(self):
        target = self.installed()
        args = [sys.executable, "-B", "-X", "utf8", str(SCRIPT), "--project", str(self.project), "--remove"]
        preview = subprocess.run(args + ["--check"], capture_output=True, text=True, encoding="utf-8", timeout=15)
        self.assertEqual(preview.returncode, 3, preview.stdout + preview.stderr)
        self.assertEqual(json.loads(preview.stdout)["status"], "removal-available")
        self.assertTrue(target.exists())
        result = subprocess.run(args + ["--apply"], capture_output=True, text=True, encoding="utf-8", timeout=15)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout)["status"], "removed")
        self.assertFalse(target.exists())

    def test_remove_cli_refuses_deleting_its_running_copy(self):
        target = self.installed()
        copied_tool = target / "scripts/sync_skill.py"
        copied_tool.parent.mkdir()
        shutil.copyfile(SCRIPT, copied_tool)
        receipt = SYNC.load(target / SYNC.RECEIPT)
        receipt["files"]["scripts/sync_skill.py"] = SYNC.digest(copied_tool.read_bytes())
        (target / SYNC.RECEIPT).write_bytes(SYNC.encode(receipt))
        result = subprocess.run([sys.executable, "-B", "-X", "utf8", str(copied_tool),
            "--project", str(self.project), "--remove", "--apply"], capture_output=True, text=True, encoding="utf-8", timeout=15)
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("running copy", result.stdout)
        self.assertTrue((target / SYNC.RECEIPT).exists())

    def test_remove_refuses_modified_files_and_unknown_schema(self):
        target = self.installed()
        skill = target / "SKILL.md"
        original = skill.read_bytes()
        skill.write_text("local changes", encoding="utf-8")
        with self.assertRaisesRegex(SYNC.Conflict, "modified"):
            SYNC.prepare_remove(self.project)
        self.assertTrue((target / SYNC.RECEIPT).exists())
        skill.write_bytes(original)
        receipt = SYNC.load(target / SYNC.RECEIPT)
        (target / SYNC.RECEIPT).write_bytes(SYNC.encode(dict(receipt, schemaVersion=99)))
        with self.assertRaisesRegex(SYNC.Conflict, "schema"):
            SYNC.prepare_remove(self.project)

    def test_remove_known_failure_restores_guidance(self):
        target = self.installed()
        before = {path.relative_to(target).as_posix(): path.read_bytes() for path in target.rglob("*") if path.is_file()}
        plan = SYNC.prepare_remove(self.project)
        original = Path.unlink
        calls = 0
        def fail_second(path, *args, **kwargs):
            nonlocal calls
            if path.is_relative_to(target):
                calls += 1
                if calls == 2:
                    raise OSError("controlled failure")
            return original(path, *args, **kwargs)
        with mock.patch.object(Path, "unlink", fail_second), self.assertRaises(OSError):
            SYNC.apply(plan)
        self.assertEqual(before, {path.relative_to(target).as_posix(): path.read_bytes() for path in target.rglob("*") if path.is_file()})

    def test_unowned_target_is_preserved(self):
        target = self.project / ".agents/skills/zeroengine"
        target.mkdir(parents=True)
        (target / "SKILL.md").write_text("existing user Skill", encoding="utf-8")
        with self.assertRaisesRegex(SYNC.Conflict, "unowned"):
            self.prepare()

    def test_unmanaged_addition_survives_update(self):
        target = self.installed()
        extra = target / "personal-notes.md"
        extra.write_text("keep", encoding="utf-8")
        readme = Path(self.packages[1]["resolvedPath"]) / "README.md"
        readme.write_text("# Updated module contract\n", encoding="utf-8")
        SYNC.apply(self.prepare())
        self.assertEqual("keep", extra.read_text(encoding="utf-8"))

    def test_newer_tool_and_unknown_schema_are_refused(self):
        target = self.installed()
        receipt = target / SYNC.RECEIPT
        original = SYNC.load(receipt)
        changed = dict(original, toolVersion="9.0.0")
        receipt.write_bytes(SYNC.encode(changed))
        with self.assertRaisesRegex(SYNC.Conflict, "newer"):
            self.prepare()
        receipt.write_bytes(SYNC.encode(dict(original, schemaVersion=99)))
        with self.assertRaisesRegex(SYNC.Conflict, "schema"):
            self.prepare()

    def test_stale_snapshot_and_changed_apply_inputs_are_refused(self):
        plan = self.prepare()
        (self.project / "Packages/manifest.json").write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(SYNC.Conflict, "stale"):
            self.prepare()
        with self.assertRaisesRegex(SYNC.Conflict, "changed after preview"):
            SYNC.apply(plan)
        self.assertFalse(plan["target"].exists())

    def test_archive_appearing_after_preview_blocks_skill_apply(self):
        folder = self.project / "Packages/ZEArchives"
        folder.mkdir()
        (folder / "archive-lock.json").write_bytes(SYNC.encode({"schemaVersion": 1, "packages": {}}))
        plan = self.prepare()
        orphan = folder / "stale.tgz"
        orphan.write_bytes(b"keep for review")
        with self.assertRaisesRegex(SYNC.Conflict, "Unused ZE archives: stale.tgz"):
            SYNC.apply(plan)
        self.assertFalse(plan["target"].exists())
        with self.assertRaisesRegex(SYNC.Conflict, "Unused ZE archives"):
            self.prepare()
        self.assertEqual(orphan.read_bytes(), b"keep for review")

    def test_checks_exact_archive_pins_files_and_hash(self):
        folder = self.project / "Packages/ZEArchives"
        folder.mkdir()
        name = self.packages[0]["name"]
        version = SYNC.TOOL_VERSION
        asset = f"{name}-{version}.tgz"
        pin = f"file:ZEArchives/{asset}"
        data = b"validated archive bytes"
        (folder / asset).write_bytes(data)
        record = {"name": name, "version": version, "asset": asset, "bytes": len(data), "sha256": SYNC.digest(data)}
        (folder / "archive-lock.json").write_bytes(SYNC.encode({"schemaVersion": 1, "packages": {name: record}}))
        manifest = {"dependencies": {name: pin}}
        lock = {"dependencies": {name: {"version": pin, "source": "local-tarball"}}}
        self.assertEqual(SYNC.check_archive_delivery(self.project, manifest, lock), 1)
        with self.assertRaisesRegex(SYNC.Conflict, "pin differs"):
            SYNC.check_archive_delivery(self.project, {"dependencies": {name: "0.2.1"}}, lock)
        path = folder / asset
        path.write_bytes(data.upper())
        with self.assertRaisesRegex(SYNC.Conflict, "SHA-256 differs"):
            SYNC.check_archive_delivery(self.project, manifest, lock)
        path.unlink()
        with self.assertRaisesRegex(SYNC.Conflict, "Missing ZE archives"):
            SYNC.check_archive_delivery(self.project, manifest, lock)

    def test_signed_archive_requires_the_pinned_version_to_be_registered(self):
        row = self.packages[1]
        name, version = row["name"], "0.1.1"
        asset = f"{name}-{version}.tgz"
        pin = f"file:ZEArchives/{asset}"
        data = b"next reviewed archive"
        folder = self.project / "Packages/ZEArchives"
        folder.mkdir()
        (folder / asset).write_bytes(data)
        record = {"name": name, "version": version, "asset": asset, "bytes": len(data), "sha256": SYNC.digest(data)}
        (folder / "archive-lock.json").write_bytes(SYNC.encode({"schemaVersion": 1, "packages": {name: record}}))
        for path, entry in (("manifest.json", pin), ("packages-lock.json", {"version": pin, "source": "local-tarball"})):
            leaf = self.project / "Packages" / path
            content = SYNC.load(leaf)
            content["dependencies"][name] = entry
            leaf.write_bytes(SYNC.encode(content))
        row.update(source="LocalTarball", gitHash="")
        self.write_snapshot()

        with self.assertRaisesRegex(SYNC.Conflict, "Unity has not registered the pinned ZE archive"):
            self.prepare(include_local_trials=True)
        self.assertFalse((self.project / ".agents").exists())

        row["version"] = version
        (Path(row["resolvedPath"]) / "package.json").write_bytes(SYNC.encode({"name": name, "version": version}))
        self.write_snapshot()
        plan = self.prepare(include_local_trials=True)
        inventory = json.loads(plan["desired"]["references/installed-packages.json"])["packages"]
        self.assertEqual(version, inventory[name]["version"])

    def test_version_split_is_explicit_and_preserves_per_module_pins(self):
        self.packages[1]["gitHash"] = "b" * 40
        self.refresh()
        with self.assertRaisesRegex(SYNC.Conflict, "Mixed Git pins"):
            self.prepare()
        plan = self.prepare(allow_version_split=True)
        graph = json.loads(plan["desired"]["references/installed-packages.json"])
        self.assertEqual("a" * 40, graph["packages"][SYNC.PACKAGE]["gitHash"])
        self.assertEqual("b" * 40, graph["packages"][self.packages[1]["name"]]["gitHash"])

    def test_local_trial_is_explicit_and_remains_unverified(self):
        self.packages[1].update(source="Local", gitHash="")
        self.refresh()
        with self.assertRaisesRegex(SYNC.Conflict, "local trial"):
            self.prepare()
        plan = self.prepare(include_local_trials=True)
        graph = json.loads(plan["desired"]["references/installed-packages.json"])
        self.assertEqual("local-trial-unverified", graph["packages"][self.packages[1]["name"]]["verification"])

    def test_different_repositories_do_not_fabricate_a_version_split(self):
        self.packages[1]["gitHash"] = "b" * 40
        self.refresh()
        manifest_path = self.project / "Packages/manifest.json"
        manifest = SYNC.load(manifest_path)
        manifest["dependencies"][self.packages[1]["name"]] = "https://example.invalid/independent.git#" + "b" * 40
        manifest_path.write_bytes(SYNC.encode(manifest))
        snapshot = SYNC.load(self.snapshot)
        snapshot["manifestSha256"] = SYNC.digest(manifest_path.read_bytes())
        self.snapshot.write_bytes(SYNC.encode(snapshot))
        self.assertFalse(self.prepare()["versionSplit"])

    def test_same_version_commit_change_needs_explicit_source_selection(self):
        self.installed()
        for package in self.packages:
            package["gitHash"] = "c" * 40
        self.refresh()
        with self.assertRaisesRegex(SYNC.Conflict, "same-version"):
            self.prepare()
        self.assertTrue(self.prepare(accept_current_commit=True)["changed"])

    def test_known_failed_write_restores_preimages(self):
        plan = self.prepare()
        original = SYNC.os.replace
        calls = 0

        def fail_second(source, destination):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError("injected write failure")
            return original(source, destination)

        with mock.patch.object(SYNC.os, "replace", side_effect=fail_second):
            with self.assertRaisesRegex(OSError, "injected"):
                SYNC.apply(plan)
        self.assertFalse(plan["target"].exists())
        self.assertTrue(self.prepare()["changed"])

    def test_concurrent_user_write_is_not_overwritten(self):
        target = self.installed()
        readme = Path(self.packages[1]["resolvedPath"]) / "README.md"
        readme.write_text("updated source", encoding="utf-8")
        plan = self.prepare()
        path = target / ("references/packages/" + self.packages[1]["name"] + ".md")
        path.write_text("new user edit", encoding="utf-8")
        with self.assertRaisesRegex(SYNC.Conflict, "changed after preview"):
            SYNC.apply(plan)
        self.assertEqual("new user edit", path.read_text(encoding="utf-8"))

    def test_receipt_path_traversal_is_refused(self):
        target = self.installed()
        receipt = SYNC.load(target / SYNC.RECEIPT)
        receipt["files"]["../outside.md"] = "0" * 64
        (target / SYNC.RECEIPT).write_bytes(SYNC.encode(receipt))
        with self.assertRaisesRegex(SYNC.Conflict, "relative path"):
            self.prepare()

    def test_relative_package_path_and_changed_tool_are_refused(self):
        snapshot = SYNC.load(self.snapshot)
        snapshot["packages"][0]["resolvedPath"] = "relative"
        self.snapshot.write_bytes(SYNC.encode(snapshot))
        with self.assertRaisesRegex(SYNC.Conflict, "absolute"):
            self.prepare()
        self.refresh()
        (Path(self.packages[0]["resolvedPath"]) / "Tools~/sync_skill.py").write_text("old tool", encoding="utf-8")
        with self.assertRaisesRegex(SYNC.Conflict, "older or modified"):
            self.prepare()


if __name__ == "__main__":
    unittest.main()
