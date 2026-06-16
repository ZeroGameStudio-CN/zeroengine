# TCE Graph Canvas Evaluation

## Decision

Decision: keep the productized lane editor as the default TCE authoring surface for now. The current editor already uses the stable graph model, component catalog, validation panel, preview route, schema versioning, migration, and safe external graph import surface added during productization.

A full graph canvas remains valuable for large graphs, but it should be planned as a separate implementation phase. The preferred future direction is a custom UI Toolkit canvas that preserves all current package APIs and runtime semantics. GraphView is not selected as the default path because it would tie the package to an experimental editor API and can encourage an editor rewrite before the model contracts need it.

## Options Evaluated

| Option | Strengths | Risks | Decision |
| --- | --- | --- | --- |
| Keep the lane editor | Already works with `TceGraph`, catalog metadata, validation focus, preview, schema, migration, and import tests. Low maintenance cost. | Large graphs are harder to navigate than a spatial canvas. | Keep as default. |
| GraphView canvas | Built-in graph editing concepts such as nodes, edges, groups, search, and minimap. | Uses `UnityEditor.Experimental.GraphView`; higher compatibility risk across Unity versions. Could drive a broad rewrite before the TCE data contracts need it. | Do not adopt as the default path. |
| Custom UI Toolkit canvas | Can be built around package-owned node/lane models while keeping `TceGraph` as the source of truth. Easier to constrain serialization and validation behavior. | More package-owned editor code to build and maintain. Needs a dedicated implementation plan and visual QA. | Preferred future path after explicit planning. |

## API Invariants

Any future canvas editor must reuse these existing package surfaces:

- `TceGraph` remains the runtime graph model and saved graph data source.
- `TceComponentCatalogBuilder` remains the catalog source for palette labels, component IDs, field metadata, and documentation.
- `TceGraphValidator` remains the validation kernel for editor-authored and imported graphs.
- `TcePreviewRunner` remains the editor preview path.
- `TceGraphSchema` remains the version contract for graph assets and external graph documents.
- `TceGraphMigrationRegistry` remains the graph upgrade path for old content.
- `TceExternalGraphImporter` remains the safe import path for external graph documents.

## Runtime Semantic Invariants

A canvas editor must not change:

- trigger, condition, and effect execution order;
- accepted execution and cooldown observer timing;
- `TceRuntime` install and uninstall behavior;
- stable `componentId` import semantics;
- graph asset schema version meaning;
- migration behavior for old graph documents;
- package boundaries between TCE, ModSystem bridge code, and project adapters.

The canvas may change editor layout and interaction, but it must write the same `TceGraph` data that the lane editor writes today.

## Future Canvas Acceptance Gates

A future canvas implementation plan must include:

- source tests proving the canvas has no runtime assembly dependency;
- editor tests for create, select, inspect, duplicate, delete, group, search, preview, and validation focus;
- migration tests proving lane-editor-authored assets open unchanged in the canvas;
- import/export tests proving external JSON still uses stable `componentId` values;
- visual QA for a small graph, a medium graph, and a graph with validation errors;
- explicit rollback path to the lane editor if canvas editing fails.

Until those gates exist, the lane editor remains the production authoring surface.
