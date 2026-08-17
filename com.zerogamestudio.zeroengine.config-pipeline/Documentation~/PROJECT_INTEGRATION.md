# Project integration

Add the package and `com.unity.nuget.newtonsoft-json`, then create
`Config/config-project.json`. Each config set declares its Schema, Excel source,
one owner workbook per top-level table, generated DTO namespace/path, and target
JSON, Manifest, source-map and optional Workshop Schema paths.
For a brand-new config set with no workbook metadata yet, call
`ConfigPipelineService.WriteTemplates(projectRoot, profilePath, configSetId,
candidateOutputDirectory)` from project Editor automation. It reads the declared
Schema and workbook ownership and creates the first reviewable workbooks in the
candidate directory. Review them, then promote each exact declared filename into
the profile's official authoring path; do not relabel a workbook from another
config set.

Project Editor code registers business validators and an asset resolver with
`ConfigMaintenanceRegistry`; JSON never names arbitrary code types. Runtime code
uses `ConfigArtifactReader` with a compiled `ConfigArtifactContract`, then maps the
immutable `ConfigDocument` to the project's domain model/catalog.
Apply catalog changes through `ConfigCatalogMaintenanceService`; new artifacts
under `Assets/` and their new directories receive deterministic paired `.meta`
artifacts transactionally.
Transaction locks, journals, staging, and backups are kept under
`Library/ZeroEngine/ConfigPipeline`, not in the project root. On upgrade, the
pipeline obtains the legacy lock when a `.zgs-config` root exists, recovers any
pending legacy transaction before beginning a new operation, then removes the
legacy root only when it is empty. Fresh operations never create that root. Do
not manually remove a non-empty legacy transaction directory until recovery has
completed. The journal persists through normal Editor restarts, but clearing
`Library/` discards unfinished transaction evidence; for the deterministic
generated artifacts owned by this pipeline, re-plan and Apply after such a
cleanup.
Manifest `sourceHash` and `baseSourceHash` identify the canonical normalized
source document used for semantic merge/conflict checks; they are not byte
fingerprints of the XLSX container. Check detects workbook drift by reading and
normalizing the current workbook again.

Call `ConfigPipelineBatch.Run` for automation or
`ZeroGameStudio.ConfigPipeline.Editor.ConfigPipelineBatch.Execute` from Unity
batchmode. Include Unity's `-quit` argument with `-batchmode -executeMethod`.
Unity can return 0 after a script-compilation restart even when the command failed,
and can drop `-executeMethod` during that restart, so process exit code and log text
are not the automation contract. Before every launch, delete the intended result
file. After synchronous process exit, require a newly created, parseable
`--config-result-output` JSON file and require its `success` value to be true;
an absent result is failure and may be retried only after Unity finishes compiling.
Required command arguments are `--config-project-root`, `--config-profile`,
`--config-set`, `--config-result-output` and
`--config-mode Plan|Check|Apply|Compile|ExportCandidate|UpgradeCandidate`.
`--config-package-identity` is additionally required for every mode except
ExportCandidate. Check is read-only and fails when artifacts are stale. Apply
revalidates every baseline and commits the declared set transactionally. Compile
is an Apply alias.
Package identity is the immutable output-affecting pipeline identity included in
the Plan: use `<package-name>@<version>` for a published package (for example,
`com.zerogamestudio.zeroengine.config-pipeline@1.0.0`) and
`<package-name>@<commit-or-frozen-content-hash>` for local or Git iteration.
Keep it identical across Plan, Apply, and Check for one frozen package, and
change it whenever Plan validation or generated-artifact behavior changes so
existing output is reported stale. Editor-only operational changes, such as the
transaction scratch location, do not by themselves require regenerating
artifacts. ExportCandidate reads and compares existing authoring/runtime data
but does not create a Plan or bind package identity into candidate workbook
metadata, so that mode neither accepts identity as a safety guarantee nor
requires the argument.
ExportCandidate writes candidate `.xlsx` workbooks from current generated JSON;
it does not emit JSON. It additionally requires `--config-candidate-output` and
`--config-target-scope shared|client|server`; choose the scope of the generated
artifact being exported. Relative candidate-output paths resolve from the Unity
project root; absolute paths are accepted when the candidate should live outside
the project. Candidate workbook metadata remains bound to the selected
config-set ID; adopt and apply it under that same ID, or deliberately create a
new config set and regenerate its workbooks instead of relabeling the candidate.
Candidate files are named `<source-stem>.candidate.xlsx`; after review, rename
each accepted file to the exact workbook filename declared by the target profile
when promoting it into the official authoring location.

For a Schema version change, first require the current profile to pass Check.
Keep it unchanged, create a next-version profile that points to the next Schema
and workbook ownership, then run UpgradeCandidate with `--config-next-profile`,
`--config-candidate-output`, and the normal required arguments. The command reads
all data through the current Schema, validates every next-profile target, and
writes data-preserving candidate workbooks without changing official files.
Compatible optional additions need no migration. Adding a required field or
table, rename, removal, or semantic conversion requires project Editor code to
register an `IConfigMigration` through
`ConfigMaintenanceRegistry.RegisterMigration` before the command runs.

Preserve every option value as one command-line token. Launchers that flatten an
argument array into a command line, including PowerShell `Start-Process
-ArgumentList`, require literal double quotes around values containing spaces.
For example, pass the imported Sample profile as
`'"Assets/Samples/ZeroEngine Config Pipeline/1.0.0/Minimal Item Drop/Config/config-project.json"'`.

To run the package's CoreContract tests from a consuming project, add
`com.unity.test-framework` (1.1.33 for the Unity 2022.3 validation baseline) and
`"testables": ["com.zerogamestudio.zeroengine.config-pipeline"]` to that
project’s `Packages/manifest.json`. The test framework is a consumer test-only
prerequisite and is intentionally not a runtime dependency of this package.
Treat a zero-test result as failure even when the Unity process exits with code
0. For package 2.0.2, run EditMode tests with NUnit category
`ZGS.ConfigPipeline.CoreContract` and require exactly 72 discovered and passed
tests; update the documented expected count when the package test contract
changes.
