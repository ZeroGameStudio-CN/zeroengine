# ZeroEngine Config Pipeline

Schema-first configuration pipeline for Unity 2022.3 projects. It keeps one
authoring source, validates through a typed intermediate document, emits
deterministic artifacts, and loads immutable runtime snapshots.

## Minimal authoring views

New template and schema-upgrade entry points require authoring policy v2, including
explicit table classification. The durable cross-project contract and acceptance
checks live in `Documentation~/EXCEL_AUTHORING.md`; legacy read/refresh compatibility
does not waive the new-project rule.

New governed schemas set `x-zgs-require-authoring-visibility:true` on the root
and classify each scalar with `x-zgs-authoring-visibility`: `basic` for necessary
business inputs, `advanced` for infrequent inputs, `technical` for maintained
identifiers/derived values, or `inactive` for unavailable authoring controls.
Unclassified fields and hidden required inputs without a default (except managed
primary keys/order) fail schema validation. A business selection or the only
readable row identity must remain basic; an `Id` suffix or a default value alone
is not a reason to hide it. Legacy schemas keep their existing layout until adopted.

Templates and source-preserving refreshes start with only basic columns visible,
keep every stored value, and place all six action buttons in visible columns.
The common explicit `高级/技术` action expands advanced/technical fields without
changing values; inactive fields stay hidden. Classified technical primary keys
are allocated by Add/Copy, while semantic keys remain explicit inputs. No new
Workbook Open/Save event executes macros. Refresh never executes or replaces VBA;
the controlled VBA installer must be run explicitly to deploy changed macro code,
and must not change Excel Trust Center settings.

Version 2.0.2 uses `com.zerogamestudio.zeroengine.editor-ui@1.3.0` for its Editor window and typed workbench action. Git URL consumers must directly pin both packages to the same ZeroEngine commit because Unity 2022.3 does not resolve same-repository sibling dependencies transitively.

Version 2.0.2 stores transaction scratch state under
`Library/ZeroEngine/ConfigPipeline`, keeping project-root private-file views
clean while retaining crash recovery for pending legacy transactions. During
upgrade it also honors an existing legacy operation lock, then removes the
legacy root once recovery has left it empty; fresh operations never create it.

Version 2.0.1 localizes the Dashboard module label, description, and tooltip to
Simplified Chinese without changing the menu route.

Start with `Documentation~/PROJECT_INTEGRATION.md`. The 1.0 contract, Excel
authoring rules, CI commands, recovery behavior and AI maintenance workflow are
all documented under `Documentation~/`. Import `Minimal Item Drop` from Package
Manager for a project-neutral end-to-end sample. Consuming projects that run the
package tests must expose it through the manifest `testables` entry described in
the integration and CI guides and install the documented test-framework
prerequisite.

## ZeroEngine Dashboard

The optional Dashboard discovers this package through its schema v2 descriptor and invokes the package-owned typed provider. Open it from `ZGS > 工作台 > 内容创作`; this package does not reference or require Dashboard.
