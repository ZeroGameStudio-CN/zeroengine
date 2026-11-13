import json
from pathlib import Path
import tempfile
import unittest

from create_test_project import create_project, select_packages


class TestProjectSelectionTests(unittest.TestCase):
    def test_interaction_lane_excludes_gameplay_inventory_and_other_framework_modules(self):
        packages = {PREFIX: dict(unity="6000.6") for PREFIX in (
            "com.zerogamestudio.zeroengine.interaction",
            "com.zerogamestudio.zeroengine.gameplay",
            "com.zerogamestudio.zeroengine.economy",
            "com.zerogamestudio.zeroengine.core")}
        selected, _ = select_packages(packages, "interaction", "6000.6.4f1")
        self.assertEqual(selected, ["com.zerogamestudio.zeroengine.interaction"])

    def test_interaction_project_imports_its_standalone_sample_without_sibling_packages(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "source"
            package = root / "com.zerogamestudio.zeroengine.interaction"
            sample = package / "Samples~/InteractionPlayground/Runtime"
            sample.mkdir(parents=True)
            (sample / "Consumer.cs").write_text("// standalone consumer", encoding="utf-8")
            (package / "package.json").write_text(json.dumps(dict(name=package.name, unity="6000.6")), encoding="utf-8")
            destination = Path(temporary) / "project"
            receipt = create_project(root, destination, "interaction", "6000.6.4f1")
            self.assertEqual(receipt["selected"], [package.name])
            self.assertTrue((destination / "Assets/InteractionPlayground/Runtime/Consumer.cs").is_file())
            self.assertEqual(json.loads((destination / "Packages/manifest.json").read_text())["testables"], [package.name])

    def test_console_lanes_keep_only_the_requested_frontend_closure(self):
        packages = {
            "com.zerogamestudio.zeroengine.console": dict(unity="6000.6"),
            "com.zerogamestudio.zeroengine.console.ui": dict(unity="6000.6", dependencies={"com.zerogamestudio.zeroengine.console": "0.1.0"}),
            "com.zerogamestudio.zeroengine.console.quantum": dict(unity="6000.6", dependencies={"com.zerogamestudio.zeroengine.console.ui": "0.1.0"}),
            "com.zerogamestudio.zeroengine.core": dict(unity="6000.6"),
        }
        core, _ = select_packages(packages, "console-core", "6000.6.4f1")
        self.assertEqual(core, ["com.zerogamestudio.zeroengine.console"])
        native, _ = select_packages(packages, "console-native", "6000.6.4f1")
        self.assertCountEqual(native, ["com.zerogamestudio.zeroengine.console", "com.zerogamestudio.zeroengine.console.ui"])
        quantum, _ = select_packages(packages, "console-quantum", "6000.6.4f1")
        self.assertCountEqual(quantum, [name for name in packages if ".console" in name])

    def test_lifecycle_keeps_audio_core_render_closure_without_unrelated_modules(self):
        packages = {
            "com.zerogamestudio.zeroengine.core": dict(unity="6000.6"),
            "com.zerogamestudio.zeroengine.audio": dict(unity="6000.6", dependencies={"com.zerogamestudio.zeroengine.persistence": "2.0.0"}),
            "com.zerogamestudio.zeroengine.render": dict(unity="6000.6"),
            "com.zerogamestudio.zeroengine.persistence": dict(unity="6000.6", dependencies={"com.zerogamestudio.zeroengine.core": "2.0.0"}),
            "com.zerogamestudio.zeroengine.dashboard": dict(unity="6000.6"),
        }
        selected, _ = select_packages(packages, "lifecycle", "6000.6.4f1")
        self.assertCountEqual([
            "com.zerogamestudio.zeroengine.core",
            "com.zerogamestudio.zeroengine.audio",
            "com.zerogamestudio.zeroengine.render",
            "com.zerogamestudio.zeroengine.persistence",
        ], selected)

    def test_legacy_keeps_required_modsystem_and_transitive_dependencies(self):
        packages = {
            "com.zerogamestudio.zeroengine": dict(unity="6000.6", dependencies={"com.zerogamestudio.zeroengine.modsystem": "0.4.0"}),
            "com.zerogamestudio.zeroengine.modsystem": dict(unity="6000.6", dependencies={"com.zerogamestudio.zeroengine.core": "2.0.0"}),
            "com.zerogamestudio.zeroengine.core": dict(unity="6000.6"),
        }
        selected, _ = select_packages(packages, "legacy", "6000.6.4f1")
        self.assertCountEqual(packages, selected)

    def test_incompatible_dependency_excludes_its_dependents(self):
        packages = {
            "com.zerogamestudio.zeroengine.core": dict(unity="6000.6"),
            "com.zerogamestudio.zeroengine.render": dict(unity="6000.7"),
            "com.zerogamestudio.zeroengine.feature": dict(unity="6000.6", dependencies={"com.zerogamestudio.zeroengine.render": "1.0.0"}),
        }
        selected, excluded = select_packages(packages, "modular", "6000.6.4f1")
        self.assertEqual(["com.zerogamestudio.zeroengine.core"], selected)
        self.assertIn("com.zerogamestudio.zeroengine.feature", excluded)
        modern, excluded = select_packages(packages, "modular", "6000.7.1f1")
        self.assertCountEqual(packages, modern)
        self.assertEqual({}, excluded)

    def test_older_editor_is_refused_before_any_project_files_are_created(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "source"
            root.mkdir()
            destination = Path(temporary) / "must-remain-absent"
            with self.assertRaisesRegex(ValueError, "requires Unity 6000.6"):
                create_project(root, destination, "modular", "6000.5.4f1")
            self.assertFalse(destination.exists())

    def test_default_project_uses_supported_editor_and_test_framework(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "source"
            root.mkdir()
            destination = Path(temporary) / "project"
            receipt = create_project(root, destination, "modular")
            self.assertEqual(receipt["editorVersion"], "6000.6.4f1")
            manifest = json.loads((destination / "Packages/manifest.json").read_text())
            self.assertEqual(manifest["dependencies"]["com.unity.test-framework"], "1.8.0")

    def test_missing_sibling_cannot_silently_pass_an_explicit_lane(self):
        packages = {"com.zerogamestudio.zeroengine": dict(unity="6000.6", dependencies={"com.zerogamestudio.missing": "1.0.0"})}
        with self.assertRaisesRegex(ValueError, "unavailable packages"):
            select_packages(packages, "legacy", "6000.6.4f1")

    def test_creation_preserves_source_and_refuses_existing_project(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "source"
            package = root / "com.zerogamestudio.zeroengine.core"
            package.mkdir(parents=True)
            manifest = dict(name=package.name, unity="6000.6", version="2.0.0")
            original = json.dumps(manifest)
            (package / "package.json").write_text(original, encoding="utf-8")
            destination = Path(temporary) / "fixture"
            create_project(root, destination, "modular", "6000.6.4f1")
            self.assertEqual(original, (package / "package.json").read_text(encoding="utf-8"))
            with self.assertRaisesRegex(ValueError, "existing project"):
                create_project(root, destination, "modular", "6000.6.4f1")


if __name__ == "__main__":
    unittest.main()
