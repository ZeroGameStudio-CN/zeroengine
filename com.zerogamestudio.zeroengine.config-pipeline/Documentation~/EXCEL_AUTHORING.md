# Excel authoring

Designers edit only row 3 onward on declared data sheets. Row 1 is the hidden
machine header, row 2 is the localized title. A visible title ending in
`（仅策划，不导出）` is retained in the workbook for authoring but omitted from
runtime JSON and generated DTOs. Child-sheet parent keys are similarly marked
`（关联键，不导出）`; they reconstruct nesting and are not runtime object fields.
The hidden row-1 names remain unchanged, so adding these labels does not change
the workbook read contract. `_zgs_schema` explains fields; `_zgs_meta` and
`_zgs_lists` are protected internal sheets.

Blank means absent. Defaults and required checks come from Schema. Use stable IDs
for primary keys, references, content IDs and localization keys. Child records go
in their own sheet with parent ID, explicit order and child ID. Do not add columns,
rename sheets, edit internal sheets, use formulas/macros/links, or place JSON in a
cell. Save the workbook and ask the AI maintainer to run Plan, Check and Apply.
