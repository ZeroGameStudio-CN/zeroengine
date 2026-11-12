# Generic authoring VBA

`ZgsAuthoring.bas` and `ThisWorkbook.cls` are the reviewed source of the generic
Excel authoring operations. The pipeline never executes this code. The explicit
installer is a release-time compiler for macro-enabled authoring workbooks and
requires desktop Excel with `Trust access to the VBA project object model`
enabled by an authorized maintainer. It never changes that setting.

Run the installer only on reviewed `.xlsm` candidates. It replaces every
non-document VBA component and clears document handlers, then installs the one
generic module and workbook dispatch, assigns the documented Ctrl+Shift
shortcuts, and saves the specified workbooks. This is intentional: formal
authoring workbooks do not carry per-workbook or per-Sheet VBA forks. Afterward
run the pipeline refresh/Plan/Check gates; the source-preserving writer treats
`vbaProject.bin` as immutable authored content.
