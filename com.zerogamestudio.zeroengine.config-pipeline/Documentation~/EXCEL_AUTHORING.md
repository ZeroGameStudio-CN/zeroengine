# Excel authoring

Without authoring operations, designers edit only row 3 onward in declared Excel
tables: row 1 is the hidden machine header and row 2 is the localized title.
With `authoringOperationsVersion: 1`, row 1 is the visible shared action bar,
row 2 is the hidden machine header, row 3 is the localized title, and data starts
on row 4. The action bar and headers stay frozen. A visible title ending in
`（仅策划，不导出）` is retained in the workbook for authoring but omitted from
runtime JSON and generated DTOs. Child-sheet parent keys are similarly marked
`（关联键，不导出）`; they reconstruct nesting and are not runtime object fields.
The hidden machine names remain unchanged, so adding these labels does not change
the workbook read contract. A project may group several root and child tables on
one visible authoring Sheet; each table keeps its own headers and remains a
separate normalized JSON array. `_zgs_schema` explains fields; `_zgs_meta` and
`_zgs_lists` are protected internal sheets.

Blank means absent. Defaults and required checks come from Schema. Use stable IDs
for primary keys, references, content IDs and localization keys. Child records go
in their own Excel table with parent ID, explicit order and child ID; the table may
share its visible Sheet with its root table when `authoringSheets` declares that
group. Do not add columns or tables, rename Sheets, edit internal Sheets, use
formulas, external links, or place JSON in a cell. Project-authoring workbooks
may use the declared .xlsm format. Macros are never executed by the pipeline;
they are explicit designer helpers only and must not change internal sheets or
the generated JSON contract. ActiveX, OLE/embedded packages, queries and
external data connections are rejected. Save the workbook and ask the AI
maintainer to run Plan, Check and Apply.

For a long-lived project profile, set `"authoringWorkbookFormat": "xlsm"` and give
every declared workbook the .xlsm extension. The setting is per config set, not
per workbook, so a set cannot silently mix .xlsx and .xlsm. Omitting it retains
the backwards-compatible .xlsx default. Refresh and JSON export candidates, plus
Schema upgrade targets with an unambiguous current-workbook mapping, use the same
extension and start from a byte-for-byte copy of the source package; VBA,
worksheet code names, defined names and designer-owned cells outside pipeline
table ranges are retained. A Schema upgrade target with no current workbook or
managed-table overlap is created as a fresh template.

Long-lived designer config sets may also declare
`"authoringOperationsVersion": 1`. This requires `.xlsm` and enables one generic
operation contract for every declared business Sheet: add, copy, safe delete,
simple relation editing, technical-area toggle and help. Native row insertion
and deletion are blocked by worksheet protection while data cells remain
editable. The writer emits `ZGS_ACTION_*` and `ZGS_META_*` defined names so VBA
uses table identity, schema references and ownership metadata rather than fixed
Sheet names or column numbers. Workbook `protectedRecordIds` maps owned root
table names to stable IDs that safe delete must always reject. Independent
inbound references block deletion; owned child rows are cleaned together.
The six action cells use compact plain-text labels only. Shortcut hints stay in
the workbook help action and project configurator guidance so the action bar
does not widen or overlap project-owned business columns.

The reviewed VBA source and explicit desktop-Excel installer live under
`Editor/Excel/AuthoringVba~`. The installer is a release-time compiler for new
or migrated formal workbooks; it never changes Excel Trust Center settings.
The pipeline never executes VBA and later refreshes preserve the compiled
`vbaProject.bin` byte-for-byte.
Desktop Excel may reorder equivalent cell-format indexes when it saves a
workbook. Source-preserving refresh therefore maps generated styles to
semantically equivalent source styles before copying managed cells; a style
with no exact semantic match still fails closed.

Schema upgrade candidates may add business Sheets and add, rename, or detach
managed tables when the target layout is empty and unambiguous. Detaching a
table preserves its former cells as recovery evidence but removes the Excel
table relationship so they no longer enter configuration data. Removing an
entire existing business Sheet fails closed and requires an explicit workbook
migration because that Sheet may contain designer-owned assets or VBA bindings.
