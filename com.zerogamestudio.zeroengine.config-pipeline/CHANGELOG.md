# Changelog

## 2.1.0

- Allow flat XLSX tables to declare an ordered composite identity with multiple
  top-level string `x-zgs-primary-key` fields. Reader ordering and runtime
  duplicate validation use the complete tuple, so repeated individual
  components remain valid.
- Continue to require one parent identity column for child-sheet joins. A table
  with a composite identity cannot own child sheets until an explicit tuple
  parent-column schema is introduced; writer and reader fail closed instead of
  inventing a synthetic key.
- Keep `x-zgs-ref` targets limited to a single-field primary key. References to
  one component of a composite identity are rejected as ambiguous.

## 2.0.2

- Store transaction locks, journals, staging, and backups under
  `Library/ZeroEngine/ConfigPipeline` so they do not appear as project-root
  private files. During migration, serialize against any existing legacy
  `.zgs-config` lock, recover its pending transaction before a new operation,
  and remove the obsolete root once it is empty.

## 2.0.1

- Localized the Dashboard module label, description, and tooltip to Simplified Chinese without changing the menu route.

## 2.0.0

- Unified the Config Pipeline window on `com.zerogamestudio.zeroengine.editor-ui@1.0.0`.
- Git URL consumers must directly pin editor-ui to the same ZeroEngine commit.

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
