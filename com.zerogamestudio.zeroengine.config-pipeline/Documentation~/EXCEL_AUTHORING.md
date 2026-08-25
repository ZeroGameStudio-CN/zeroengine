# Excel authoring

Designers edit only row 3 onward on declared data sheets. Row 1 is the hidden
machine header, row 2 is the localized title. `_zgs_schema` explains fields;
`_zgs_meta` and `_zgs_lists` are protected internal sheets.

Blank means absent. Defaults and required checks come from Schema. Use stable IDs
for primary keys, references, content IDs and localization keys. A table may have
several primary-key columns; every component is required, and only the complete
ordered tuple must be unique. Do not create a combined helper `id`. Child records
go in their own sheet with parent ID, explicit order and child ID; their parent
table must still have one primary-key column. References likewise target only a
single-field primary key. Do not add columns, rename sheets, edit internal sheets,
use formulas/macros/links, or place JSON in a cell. Save the workbook and ask the
AI maintainer to run Plan, Check and Apply.
