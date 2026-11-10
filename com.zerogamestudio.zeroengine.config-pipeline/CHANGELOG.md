# Changelog

## Unreleased

- Consolidate shared preset/composite-key authoring and governed grouped/inline views.
- Verify JSON candidate readback in workbook row order and refuse overwriting existing candidates.
- Keep test cleanup scoped to its owned configuration set.

## 2.3.0

- Expose canonical effective values with target, field-level Schema/Preset/Instance
  provenance, source JSON/schema paths, and exact workbook coordinates in every
  prepared Plan.
- Add Config Pipeline Workbench filtering and field inspection for effective
  values and provenance.
- Add guarded Reset-to-Preset preview/apply. Only concrete instance cells that
  resolve to a preset after clearing are eligible; source and reset Plan IDs are
  revalidated and the workbook plus generated artifacts are committed in one
  rollback-capable transaction.

## 2.2.2

- Insert refreshed data validations in schema order, before page settings, and
  normalize legacy misplaced containers without changing rules or VBA. Cover
  both missing and misplaced validation containers with synthetic regressions.

## 2.2.1

- Give inline lists a wrapped, editable text style and a readable default width.
  Verify the format alongside typed workbook round trips.

## 2.2.0

- Add single-layer typed preset contracts. Schema-declared instance tables can
  inherit compatible scalar/object fields from one typed preset, while child
  collections require explicit `Inherit` or whole-list `Replace` semantics.
- Flatten preset values during generation; missing, cross-type, chained, and
  implicit collection-merge cases fail closed. Runtime artifacts contain no
  inheritance resolver.
- Add nullable scalar authoring and the reserved Excel values `@empty` and
  `@clear`; literal strings beginning with `@` use `@@` escaping.
- Upgrade generated source maps to format 2 and record every final leaf or
  empty collection as `Schema`, `Preset`, or `Instance`, with schema and source
  paths plus the original workbook location when available.

- Add opt-in single-cell lists for child arrays with one scalar business value;
  accept English/Chinese commas and quoted values without changing runtime shape.
- Keep complex records in detail tables and validation in the configurator.
  Existing workbooks require an explicit backed-up migration before adoption.
- Verify typed round trips and source-preserving candidates, including large
  integers, duplicates, empty lists and unchanged runtime projections.

## 2.1.1

- Generate byte-identical XLSX packages for identical schema, source document,
  config-set ID, and base hash inputs by assigning stable Open XML relationship
  IDs, sorting ZIP entries, and fixing package timestamps.

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

- Add config-set-wide `.xlsm` authoring, validate macro package structure, and
  preserve VBA plus designer-owned workbook affordances across every candidate
  export without executing macros; reject ActiveX, OLE, queries and external
  data connections.
- Preserve authoring assets while safely adding, renaming or detaching managed
  tables during Schema upgrade; ambiguous layouts and whole-Sheet removal fail
  closed.
- Report workbook, Sheet, row, and safe column locations for import errors,
  including dangling child rows left after a parent record is deleted.
- Add optional workbook `authoringSheets` groups so several normalized root and
  child Excel tables can share one designer-visible worksheet without changing
  JSON structure or source-map precision.
- Reject config sets whose Schema root tables are missing from workbook
  ownership, preventing orphan workbooks that never appear in the Configurator.
- Read workbook data by deterministic Excel table identity and retain support
  for existing one-table-per-Sheet workbooks.

## 2.0.3

- Label authoring-only and child parent-key columns as non-exported in visible
  Excel headers and the navigation legend while preserving hidden machine
  headers and workbook round-trip behavior.

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
