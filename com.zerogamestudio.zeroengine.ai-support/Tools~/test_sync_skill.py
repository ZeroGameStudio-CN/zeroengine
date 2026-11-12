import copy
import importlib.util
import json
from pathlib import Path
import shutil
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
            (source / "package.json").write_bytes(SYNC.encode({"name": name, "version": "0.1.0"}))
            (source / "README.md").write_text("# " + name + "\n已安装模块说明。\n", encoding="utf-8")
            self.packages.append({"name": name, "version": "0.1.0", "source": "Git",
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
        (self.project / "Packages/manifest.json").write_bytes(SYNC.encode(manifest))
        (self.project / "Packages/packages-lock.json").write_bytes(SYNC.encode(lock))
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

    def test_modified_managed_file_is_preserved(self):
        target = self.installed()
        (target / "SKILL.md").write_text("local edits", encoding="utf-8")
        with self.assertRaisesRegex(SYNC.Conflict, "locally modified"):
            self.prepare()
        self.assertEqual("local edits", (target / "SKILL.md").read_text(encoding="utf-8"))

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
