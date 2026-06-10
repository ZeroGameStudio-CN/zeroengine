using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public sealed class TceGraphRegistry
    {
        private readonly Dictionary<string, TceGraph> graphsById = new(StringComparer.Ordinal);

        public bool TryRegister(string graphId, TceGraph graph, out TceValidationIssue issue)
        {
            if (string.IsNullOrWhiteSpace(graphId))
            {
                issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, "graphId", "Graph ID must not be empty.");
                return false;
            }

            if (graph == null)
            {
                issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.NullGraph, graphId, "Graph must not be null.");
                return false;
            }

            if (graphsById.ContainsKey(graphId))
            {
                issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.DuplicateGraphId, graphId, $"Graph ID '{graphId}' is already registered.");
                return false;
            }

            graphsById.Add(graphId, graph);
            issue = default;
            return true;
        }

        public bool TryGet(string graphId, out TceGraph graph)
        {
            if (string.IsNullOrEmpty(graphId))
            {
                graph = null;
                return false;
            }

            return graphsById.TryGetValue(graphId, out graph);
        }
    }

    public sealed class TceExternalGraphImportBatchResult
    {
        public TceExternalGraphImportBatchResult(TceGraphRegistry registry, IReadOnlyList<TceExternalGraphImportResult> results)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Results = results ?? Array.Empty<TceExternalGraphImportResult>();
        }

        public TceGraphRegistry Registry { get; }
        public IReadOnlyList<TceExternalGraphImportResult> Results { get; }
    }

    public static class TceExternalGraphImportBatch
    {
        public static TceExternalGraphImportBatchResult Import(
            IEnumerable<TceExternalGraphDocument> documents,
            TceComponentRegistry componentRegistry,
            TceGraphRegistry graphRegistry,
            TceGraphMigrationRegistry migrationRegistry = null)
        {
            graphRegistry ??= new TceGraphRegistry();
            migrationRegistry ??= new TceGraphMigrationRegistry();

            var results = new List<TceExternalGraphImportResult>();
            foreach (TceExternalGraphDocument document in documents ?? Array.Empty<TceExternalGraphDocument>())
            {
                TceExternalGraphImportResult importResult = TceExternalGraphImporter.Import(document, componentRegistry, migrationRegistry);
                if (!importResult.Succeeded)
                {
                    results.Add(importResult);
                    continue;
                }

                if (graphRegistry.TryRegister(importResult.GraphId, importResult.Graph, out TceValidationIssue issue))
                {
                    results.Add(importResult);
                    continue;
                }

                results.Add(TceExternalGraphImportResult.Failed(importResult.GraphId, importResult.Graph, new[] { issue }));
            }

            return new TceExternalGraphImportBatchResult(graphRegistry, results);
        }
    }
}
