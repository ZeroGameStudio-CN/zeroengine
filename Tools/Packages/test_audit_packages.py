import json
from pathlib import Path
import tempfile
import unittest

from audit_packages import CANONICAL_REPOSITORY, inspect


class PackageAuditTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def package(self, suffix, references=(), dependencies=(), **assembly_fields):
        name = "com.zerogamestudio." + suffix
        folder = self.root / name
        folder.mkdir()
        manifest = dict(name=name, version="1.0.0", displayName=suffix, description="Fixture", unity="2022.3", author=dict(name="Fixture"), license="MIT", repository=dict(url=CANONICAL_REPOSITORY, directory=name), dependencies={"com.zerogamestudio." + x: "1.0.0" for x in dependencies})
        (folder / "package.json").write_text(json.dumps(manifest), encoding="utf-8")
        (folder / "README.md").write_text("Fixture", encoding="utf-8")
        (folder / "Runtime").mkdir()
        assembly = dict(name="ZeroEngine." + suffix, references=list(references), **assembly_fields)
        (folder / "Runtime" / "Fixture.asmdef").write_text(json.dumps(assembly), encoding="utf-8")
        return folder

    def codes(self):
        return [x["code"] for x in inspect(self.root)["findings"]]

    def test_declared_transitive_dependency_resolves(self):
        self.package("core")
        self.package("data", dependencies=["core"])
        self.package("feature", references=["ZeroEngine.core"], dependencies=["data"])
        self.assertEqual([], self.codes())

    def test_missing_package_and_assembly_dependencies_are_distinct(self):
        self.package("core")
        self.package("feature", references=["ZeroEngine.core"], dependencies=["missing"])
        self.assertCountEqual(["dependency.missing-package", "dependency.undeclared"], self.codes())

    def test_editor_constraint_allows_editor_reference(self):
        self.package("editor", includePlatforms=["Editor"])
        self.package("test", references=["ZeroEngine.editor"], dependencies=["editor"], defineConstraints=["UNITY_INCLUDE_TESTS", "UNITY_EDITOR"])
        self.assertNotIn("assembly.runtime-to-editor", self.codes())

    def test_runtime_editor_dependency_is_rejected(self):
        self.package("editor", includePlatforms=["Editor"])
        self.package("runtime", references=["ZeroEngine.editor"], dependencies=["editor"])
        self.assertIn("assembly.runtime-to-editor", self.codes())

    def test_duplicate_guid_and_assembly_are_reported(self):
        first = self.package("one")
        second = self.package("two")
        for folder in (first, second):
            (folder / "Runtime" / "Fixture.asmdef").write_text(json.dumps(dict(name="Duplicate", references=[])), encoding="utf-8")
            (folder / "Runtime" / "Fixture.asmdef.meta").write_text("guid: " + "a" * 32 + "\n", encoding="utf-8")
        self.assertCountEqual(["assembly.duplicate", "asset.duplicate-guid"], self.codes())

    def test_cycle_reports_real_dependency_cycle(self):
        self.package("one", references=["ZeroEngine.two"], dependencies=["two"])
        self.package("two", references=["ZeroEngine.one"], dependencies=["one"])
        self.assertEqual(["assembly.cycle"], self.codes())

    def test_missing_optional_integration_is_warning(self):
        self.package("core")
        self.package("optional", references=["ZeroEngine.core"], defineConstraints=["OPT_IN"])
        findings = inspect(self.root)["findings"]
        self.assertEqual("dependency.optional-undeclared", findings[0]["code"])
        self.assertEqual("warning", findings[0]["severity"])

    def test_two_different_assemblies_in_one_folder_are_rejected(self):
        folder = self.package("one")
        (folder / "Runtime" / "Other.asmdef").write_text(json.dumps(dict(name="Other", references=[])), encoding="utf-8")
        self.assertEqual(["assembly.multiple-definitions"], self.codes())

    def test_duplicate_assembly_reference_is_reported(self):
        self.package("one", references=["External", "External"])
        self.assertEqual(["assembly.duplicate-reference"], self.codes())

    def test_missing_file_metadata_is_rejected_and_empty_folder_metadata_is_preserved(self):
        folder = self.package("one")
        (folder / "Removed.cs.meta").write_text("guid: " + "b" * 32 + "\n", encoding="utf-8")
        (folder / "Empty.meta").write_text("guid: " + "c" * 32 + "\nfolderAsset: yes\n", encoding="utf-8")
        self.assertEqual(["asset.orphan-meta"], self.codes())

    def test_json_calls_require_their_own_builtin_module_declaration(self):
        folder = self.package("json")
        (folder / "Runtime/Save.cs").write_text("class Save { string Encode() => UnityEngine.JsonUtility.ToJson(this); }", encoding="utf-8")
        self.assertEqual(["dependency.jsonserialize"], self.codes())
        manifest_path = folder / "package.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["dependencies"]["com.unity.modules.jsonserialize"] = "1.0.0"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        self.assertEqual([], self.codes())

    def test_production_test_runner_tool_requires_the_framework_package(self):
        self.package("tools", references=["UnityEditor.TestRunner"], includePlatforms=["Editor"])
        self.assertEqual(["dependency.test-framework"], self.codes())
        self.package("tests", references=["UnityEditor.TestRunner"], defineConstraints=["UNITY_INCLUDE_TESTS"])
        self.assertEqual(["dependency.test-framework"], self.codes())


if __name__ == "__main__":
    unittest.main()
