using System;
using System.Collections.Generic;
using System.IO;
using ZeroEngine.TCE;
using ModPathResolver = ZeroEngine.ModSystem.ModPathResolver;

namespace ZeroEngine.TCE.ModSystem
{
    public sealed class TceModGraphImportManifest
    {
        public TceModGraphImportManifest(string modId, string rootPath, IEnumerable<string> tceGraphs)
        {
            ModId = modId ?? string.Empty;
            RootPath = rootPath ?? string.Empty;
            TceGraphs = tceGraphs == null
                ? Array.Empty<string>()
                : new List<string>(tceGraphs);
        }

        public string ModId { get; }
        public string RootPath { get; }
        public IReadOnlyList<string> TceGraphs { get; }
    }

    public sealed class TceModGraphFileImportResult
    {
        private TceModGraphFileImportResult(
            bool succeeded,
            string sourcePath,
            string graphId,
            TceGraph graph,
            IReadOnlyList<TceValidationIssue> issues)
        {
            Succeeded = succeeded;
            SourcePath = sourcePath ?? string.Empty;
            GraphId = graphId ?? string.Empty;
            Graph = graph;
            Issues = issues ?? Array.Empty<TceValidationIssue>();
        }

        public bool Succeeded { get; }
        public string SourcePath { get; }
        public string GraphId { get; }
        public TceGraph Graph { get; }
        public IReadOnlyList<TceValidationIssue> Issues { get; }

        public static TceModGraphFileImportResult Success(string sourcePath, string graphId, TceGraph graph)
        {
            return new TceModGraphFileImportResult(true, sourcePath, graphId, graph, Array.Empty<TceValidationIssue>());
        }

        public static TceModGraphFileImportResult Failed(
            string sourcePath,
            string graphId,
            TceGraph graph,
            IReadOnlyList<TceValidationIssue> issues)
        {
            return new TceModGraphFileImportResult(false, sourcePath, graphId, graph, issues);
        }
    }

    public sealed class TceModGraphImportBatchResult
    {
        public TceModGraphImportBatchResult(TceGraphRegistry registry, IReadOnlyList<TceModGraphFileImportResult> results)
        {
            Registry = registry ?? new TceGraphRegistry();
            Results = results ?? Array.Empty<TceModGraphFileImportResult>();
        }

        public TceGraphRegistry Registry { get; }
        public IReadOnlyList<TceModGraphFileImportResult> Results { get; }
    }

    public static class TceModGraphImporter
    {
        public static TceModGraphImportBatchResult Import(
            TceModGraphImportManifest manifest,
            TceComponentRegistry componentRegistry,
            TceGraphRegistry graphRegistry = null,
            TceGraphMigrationRegistry migrationRegistry = null)
        {
            graphRegistry ??= new TceGraphRegistry();
            componentRegistry ??= TceComponentRegistry.CreateDefault();
            migrationRegistry ??= new TceGraphMigrationRegistry();

            var results = new List<TceModGraphFileImportResult>();
            if (manifest == null)
            {
                results.Add(TceModGraphFileImportResult.Failed(
                    string.Empty,
                    string.Empty,
                    null,
                    new[] { CreateIssue("manifest", "Mod graph import manifest must not be null.") }));
                return new TceModGraphImportBatchResult(graphRegistry, results);
            }

            foreach (string graphPath in manifest.TceGraphs)
            {
                results.Add(ImportFile(manifest.RootPath, graphPath, componentRegistry, graphRegistry, migrationRegistry));
            }

            return new TceModGraphImportBatchResult(graphRegistry, results);
        }

        private static TceModGraphFileImportResult ImportFile(
            string rootPath,
            string graphPath,
            TceComponentRegistry componentRegistry,
            TceGraphRegistry graphRegistry,
            TceGraphMigrationRegistry migrationRegistry)
        {
            if (!ModPathResolver.TryResolveRelativePath(rootPath, graphPath, out string fullPath, out string pathError))
            {
                return TceModGraphFileImportResult.Failed(
                    graphPath,
                    string.Empty,
                    null,
                    new[] { CreateIssue(graphPath, pathError) });
            }

            if (!File.Exists(fullPath))
            {
                return TceModGraphFileImportResult.Failed(
                    graphPath,
                    string.Empty,
                    null,
                    new[] { CreateIssue(graphPath, "TCE graph file was not found.") });
            }

            string json = File.ReadAllText(fullPath);
            if (!TceModGraphJsonParser.TryParse(json, graphPath, out TceExternalGraphDocument document, out IReadOnlyList<TceValidationIssue> parseIssues))
            {
                return TceModGraphFileImportResult.Failed(graphPath, string.Empty, null, parseIssues);
            }

            TceExternalGraphImportResult importResult = TceExternalGraphImporter.Import(document, componentRegistry, migrationRegistry);
            if (!importResult.Succeeded)
            {
                return TceModGraphFileImportResult.Failed(
                    graphPath,
                    importResult.GraphId,
                    importResult.Graph,
                    importResult.Issues);
            }

            if (!graphRegistry.TryRegister(importResult.GraphId, importResult.Graph, out TceValidationIssue registryIssue))
            {
                return TceModGraphFileImportResult.Failed(
                    graphPath,
                    importResult.GraphId,
                    importResult.Graph,
                    new[] { registryIssue });
            }

            return TceModGraphFileImportResult.Success(graphPath, importResult.GraphId, importResult.Graph);
        }

        private static TceValidationIssue CreateIssue(string path, string message)
        {
            return new TceValidationIssue(
                TceValidationSeverity.Error,
                TceValidationCodes.InvalidField,
                path ?? string.Empty,
                message ?? string.Empty);
        }
    }
}
