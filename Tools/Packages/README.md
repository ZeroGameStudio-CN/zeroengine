# Package maintenance

Run the static package audit and its small synthetic regression suite:

```text
python -B -m unittest discover -s Tools/Packages -p "test_*.py"
python -B Tools/Packages/audit_packages.py --root . --output <external-report.json> --check
```

The audit checks repository metadata, sibling dependencies, assembly cycles,
duplicate assembly names/GUIDs, multiple assembly definitions in a directory,
duplicate references, Runtime-to-Editor references and orphan file metadata. It
also checks direct JSON Serialize calls and production Test Runner assembly
references against their package declarations.
It does not prove SDK installation, Unity compilation, runtime behavior or
consumer adoption.

Create a new, disposable test project with a compatible sibling dependency closure:

```text
python -B Tools/Packages/create_test_project.py --destination <new-project-path> --lane modular --unity-version 6000.6.4f1 --test-framework 1.8.0
```

The generator refuses a populated destination or a source-package destination.
It selects packages by their declared minimum Unity version and records excluded
packages and dependent exclusions in `package-selection.json`. It never deletes
an existing project, installs an Editor or runs tests. Package Manager resolves
registry dependencies when the selected Editor opens the new project.

The existing GitHub Actions lanes use Unity 2022.3.62f1 and therefore exclude
packages declaring a newer minimum. Legacy tests include the legacy bundle's
actual sibling closure, including ModSystem. Dashboard-only and module-removal
lanes retain their separate installation contracts. The separate package-quality
job audits every source package, including packages excluded from those Unity
2022.3 lanes. A successful 2022.3 lane is not verification of Unity 6-only packages;
verify those with the matching Editor and package-specific tests before publishing.

The `lifecycle` lane selects Core, Audio and their required siblings. CI runs it
in PlayMode so singleton shutdown and audio enable/disable ownership are exercised
through real Unity lifecycle callbacks, independently of the EditMode lanes.

The test-runner CLI is pinned to the observed `v0.1.72` release; resolving a
floating latest release is unnecessary and can fail before Unity starts.

`console-core` and `console-native` are clean Console installations: the kernel
alone, and the kernel plus native UI. CI runs their EditMode and PlayMode cases
without other ZE gameplay packages, QC or Input System as incidental dependencies.
