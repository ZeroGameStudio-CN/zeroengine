# Changelog

- Batch ExportCandidate no longer requires the unused package-identity argument;
  all Plan-producing or current-base-validated modes retain the requirement.
- Generated Manifests persist the package identity as `toolVersion`, so Check
  reports stale when the pipeline implementation identity changes.
- Apply transactionally generates deterministic `.meta` files for new Unity
  artifact directories as well as files; shared-directory identity is
  config-set-order independent.
- Generated workbooks include the complete default Excel style records and
  pass Open XML validation before designer use.
- Batch execution writes a required machine-result file so CI does not trust
  Unity restart exit codes or silently dropped execute methods.
- Candidate documentation specifies the generated suffix and promotion rename.
- Integration guidance covers first-workbook bootstrap, required-addition
  migrations, and cleanup of task-owned temporary Unity metadata.

## 1.0.0

- Initial schema, immutable document, canonical JSON, manifest, hashing, and
  runtime artifact contracts.
- Excel authoring, deterministic generation, transaction recovery, and Unity
  project extension APIs.
- Data-preserving Schema upgrade candidates, registered migration routing, and
  deterministic `.meta` pairing for transactional Unity catalog artifacts.
