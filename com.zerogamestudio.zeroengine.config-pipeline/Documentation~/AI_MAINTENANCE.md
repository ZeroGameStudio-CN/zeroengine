# AI maintenance contract

Discover the profile, Schema, owned workbooks, registered validators/resolver,
generated outputs and repository status. Treat designer Excel and production
configuration as read-only during tests.

For a data-only request: read the workbook, run Plan and inspect field-level
semantic change, run Check, Apply the unchanged plan, then Check again. For a
structure request, keep the official Schema/profile unchanged and current while
creating next-version Schema/profile files. Register an `IConfigMigration` with
`ConfigMaintenanceRegistry` when old data cannot validate unchanged, then run
`UpgradeCandidate` with `--config-next-profile` and
`--config-candidate-output`. Review the data-preserving candidate workbooks before
promoting the next Schema/profile/workbooks together. Update adapters, validators,
catalog API calls, tests and docs, then follow the same Plan/Check/Apply/Check route.
After promotion, rollback, or an abandoned request, remove only task-owned
temporary next-Schema/profile files and their Unity-generated paired `.meta`
files. Never leave orphan temporary metadata, and never delete designer-owned
current configuration while cleaning task artifacts.

Never edit generated JSON/code/Manifest/source-map by hand or edit catalog SO YAML.
Never infer an asset from a path/name when identity is ambiguous. Stop and ask only
when old-data meaning, destructive migration, content identity or business behavior
cannot be determined objectively. Apply is forbidden when the plan baseline or
allowed path set changed. Record request, package identity, hashes, plan ID, diff,
tests and rollback evidence.
