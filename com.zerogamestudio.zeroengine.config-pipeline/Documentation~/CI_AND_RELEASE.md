# CI and release

Add `com.unity.test-framework` (1.1.33 for the Unity 2022.3 validation baseline)
and `"testables": ["com.zerogamestudio.zeroengine.config-pipeline"]` to the
consumer project's `Packages/manifest.json`, then run package CoreContract
EditMode tests in Unity 2022.3.62f3. The test framework is a consumer test-only
prerequisite, not a runtime package dependency. Fail CI unless the expected
tests are discovered and all pass; Unity may exit with code 0 when zero tests
run. For package 2.1.0, filter EditMode tests by NUnit category
`ZGS.ConfigPipeline.CoreContract` and require exactly 197 discovered and passed
tests; update this expected count with the package test contract. Run batch
Check for every config set, build a Player, and verify Open XML
assemblies are absent. Publish only a commit-pinned package for which package
tests, Sample roundtrip, transaction recovery and Player dependency audit all
refer to the same commit.

When a batch launcher flattens its argument list, retain literal double quotes
around any path value containing spaces as described in `PROJECT_INTEGRATION.md`.
Every Unity `-batchmode -executeMethod` invocation must also include `-quit` so
the Editor exits after the command. Do not trust that process exit code: a
script-compilation restart can return 0 for a stale/failed command or discard
`-executeMethod`. Delete the requested `--config-result-output` file before each
attempt, wait synchronously, then fail CI unless a new parseable result exists
with `success: true`. A missing result may be retried only after compilation has
settled; never treat it as success.

Never run Apply in CI. Preserve failed transaction directories as evidence; the
next authorized Apply or explicit recovery acquires the project operation lock
(and the legacy lock during migration) and restores the complete previous output
set before doing new work.
