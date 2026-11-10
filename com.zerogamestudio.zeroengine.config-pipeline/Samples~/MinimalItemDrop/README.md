# Minimal Item Drop

This sample uses schema version 2 and authoring policy v2: every table and input
is explicitly classified. Its macro-free workbooks keep required IDs, references
and ordering editable; no hidden input depends on an unavailable authoring helper.

This sample demonstrates two owner workbooks, a foreign key, a nested child table,
deterministic client artifacts and generated DTOs. After Package Manager import,
keep this folder path in `config-project.json`, open `ZGS > 工作台 > 内容创作 > 配置管线`, enter
config set `sample.item-drop`, then Plan/Apply/Check. Designers subsequently edit
only the workbook data rows. For batchmode, quote this imported folder's profile
path as one argument because the Package Manager display path contains spaces;
see `Documentation~/PROJECT_INTEGRATION.md`.
