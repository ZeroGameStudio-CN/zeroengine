# Mod Graph Import Sample

This sample shows the reusable TCE side of a mod graph import flow:

1. A manifest declares a graph file path.
2. The graph JSON uses `format`, `schemaVersion`, `graphId`, and stable `componentId` values.
3. Runtime code imports the graph through `TceExternalGraphImportBatch`.
4. Valid graphs are registered by ID in `TceGraphRegistry`.

The JSON file does not contain CLR type names, Unity managed-reference names, or project gameplay types. A later bridge package can add file IO and path guards around this same import surface.

## Files

- `mod-manifest.json` is a minimal bridge-facing manifest shape.
- `content/graphs/burning_hit.tce.json` is the external graph document.
- `ModGraphImportExample.cs` mirrors the JSON fixture with an in-memory document and shows registry lookup.
