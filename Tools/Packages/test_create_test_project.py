import json
from pathlib import Path
import tempfile
import unittest

from create_test_project import create_project, select_packages


class TestProjectSelectionTests(unittest.TestCase):
    def test_legacy_keeps_required_modsystem_and_transitive_dependencies(self):
        packages = {
            "com.zerogamestudio.zeroengine": dict(unity="2022.3", dependencies={"com.zerogamestudio.zeroengine.modsystem": "0.4.0"}),
            "com.zerogamestudio.zeroengine.modsystem": dict(unity="2022.3", dependencies={"com.zerogamestudio.zeroengine.core": "2.0.0"}),
            "com.zerogamestudio.zeroengine.core": dict(unity="2022.3"),
        }
        selected, _ = select_packages(packages, "legacy", "2022.3.62f1")
        self.assertCountEqual(packages, selected)

    def test_incompatible_dependency_excludes_its_dependents(self):
        packages = {
            "com.zerogamestudio.zeroengine.core": dict(unity="2022.3"),
            "com.zerogamestudio.zeroengine.render": dict(unity="6000.6"),
            "com.zerogamestudio.zeroengine.feature": dict(unity="2022.3", dependencies={"com.zerogamestudio.zeroengine.render": "1.0.0"}),
        }
        selected, excluded = select_packages(packages, "modular", "2022.3.62f1")
        self.assertEqual(["com.zerogamestudio.zeroengine.core"], selected)
        self.assertIn("com.zerogamestudio.zeroengine.feature", excluded)
        modern, excluded = select_packages(packages, "modular", "6000.6.4f1")
        self.assertCountEqual(packages, modern)
        self.assertEqual({}, excluded)

    def test_missing_sibling_cannot_silently_pass_an_explicit_lane(self):
        packages = {"com.zerogamestudio.zeroengine": dict(unity="2022.3", dependencies={"com.zerogamestudio.missing": "1.0.0"})}
        with self.assertRaisesRegex(ValueError, "unavailable packages"):
            select_packages(packages, "legacy", "2022.3.62f1")

    def test_creation_preserves_source_and_refuses_existing_project(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "source"
            package = root / "com.zerogamestudio.zeroengine.core"
            package.mkdir(parents=True)
            manifest = dict(name=package.name, unity="2022.3", version="2.0.0")
            original = json.dumps(manifest)
            (package / "package.json").write_text(original, encoding="utf-8")
            destination = Path(temporary) / "fixture"
            create_project(root, destination, "modular", "2022.3.62f1")
            self.assertEqual(original, (package / "package.json").read_text(encoding="utf-8"))
            with self.assertRaisesRegex(ValueError, "existing project"):
                create_project(root, destination, "modular", "2022.3.62f1")


if __name__ == "__main__":
    unittest.main()
