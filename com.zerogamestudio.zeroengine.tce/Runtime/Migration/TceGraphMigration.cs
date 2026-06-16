using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.TCE
{
    public delegate void TceGraphMigrationOperation(TceExternalGraphDocument document, List<TceValidationIssue> issues);

    public sealed class TceGraphMigrationResult
    {
        private TceGraphMigrationResult(
            bool succeeded,
            TceExternalGraphDocument document,
            IReadOnlyList<TceValidationIssue> issues)
        {
            Succeeded = succeeded;
            Document = document;
            Issues = issues ?? Array.Empty<TceValidationIssue>();
        }

        public bool Succeeded { get; }
        public TceExternalGraphDocument Document { get; }
        public IReadOnlyList<TceValidationIssue> Issues { get; }

        public static TceGraphMigrationResult Success(TceExternalGraphDocument document)
        {
            return new TceGraphMigrationResult(true, document, Array.Empty<TceValidationIssue>());
        }

        public static TceGraphMigrationResult Failed(TceExternalGraphDocument document, IReadOnlyList<TceValidationIssue> issues)
        {
            return new TceGraphMigrationResult(false, document, issues);
        }
    }

    public sealed class TceGraphMigrationRegistry
    {
        private readonly Dictionary<int, TceGraphMigrationStep> stepsByVersion;

        public TceGraphMigrationRegistry()
            : this(Array.Empty<TceGraphMigrationStep>())
        {
        }

        public TceGraphMigrationRegistry(IEnumerable<TceGraphMigrationStep> steps)
        {
            stepsByVersion = new Dictionary<int, TceGraphMigrationStep>();

            foreach (TceGraphMigrationStep step in steps ?? Array.Empty<TceGraphMigrationStep>())
            {
                if (stepsByVersion.ContainsKey(step.FromVersion))
                    throw new ArgumentException($"Duplicate TCE graph migration step for version {step.FromVersion}.", nameof(steps));

                stepsByVersion.Add(step.FromVersion, step);
            }
        }

        public TceGraphMigrationResult Migrate(TceExternalGraphDocument document)
        {
            if (document == null)
            {
                return TceGraphMigrationResult.Failed(null, new[]
                {
                    new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.NullGraph, "graph", "Graph document must not be null.")
                });
            }

            if (document.SchemaVersion > TceGraphSchema.CurrentVersion)
            {
                return TceGraphMigrationResult.Failed(document, new[]
                {
                    new TceValidationIssue(
                        TceValidationSeverity.Error,
                        TceValidationCodes.GraphVersionUnsupported,
                        "schemaVersion",
                        $"Graph schema version {document.SchemaVersion} is newer than supported version {TceGraphSchema.CurrentVersion}.")
                });
            }

            while (document.SchemaVersion < TceGraphSchema.CurrentVersion)
            {
                if (!stepsByVersion.TryGetValue(document.SchemaVersion, out TceGraphMigrationStep step))
                {
                    return TceGraphMigrationResult.Failed(document, new[]
                    {
                        new TceValidationIssue(
                            TceValidationSeverity.Error,
                            TceValidationCodes.GraphMigrationRequired,
                            "schemaVersion",
                            $"Graph schema version {document.SchemaVersion} requires a migration step.")
                    });
                }

                TceGraphMigrationResult result = step.Migrate(document);
                if (!result.Succeeded)
                    return result;
            }

            return TceGraphMigrationResult.Success(document);
        }
    }

    public sealed class TceGraphMigrationStep
    {
        private readonly IReadOnlyList<TceGraphMigrationOperation> operations;

        public TceGraphMigrationStep(int fromVersion, int toVersion, params TceGraphMigrationOperation[] operations)
        {
            if (toVersion <= fromVersion)
                throw new ArgumentOutOfRangeException(nameof(toVersion), "Migration target version must be greater than the source version.");

            FromVersion = fromVersion;
            ToVersion = toVersion;
            this.operations = operations ?? Array.Empty<TceGraphMigrationOperation>();
        }

        public int FromVersion { get; }
        public int ToVersion { get; }

        public TceGraphMigrationResult Migrate(TceExternalGraphDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var issues = new List<TceValidationIssue>();
            foreach (TceGraphMigrationOperation operation in operations)
                operation?.Invoke(document, issues);

            if (issues.Any(issue => issue.Severity == TceValidationSeverity.Error))
                return TceGraphMigrationResult.Failed(document, issues);

            document.SchemaVersion = ToVersion;
            return TceGraphMigrationResult.Success(document);
        }

        public static TceGraphMigrationOperation RenameComponent(string oldComponentId, string newComponentId)
        {
            return (document, _) =>
            {
                ForEachNode(document, (node, _) =>
                {
                    if (string.Equals(node.ComponentId, oldComponentId, StringComparison.Ordinal))
                        node.ComponentId = newComponentId ?? string.Empty;
                });
            };
        }

        public static TceGraphMigrationOperation RenameField(string componentId, string oldFieldName, string newFieldName)
        {
            return (document, _) =>
            {
                ForEachNode(document, (node, _) =>
                {
                    if (!string.Equals(node.ComponentId, componentId, StringComparison.Ordinal))
                        return;

                    if (!node.Fields.TryGetValue(oldFieldName, out object value))
                        return;

                    if (!node.Fields.ContainsKey(newFieldName))
                        node.Fields.Add(newFieldName, value);

                    node.Fields.Remove(oldFieldName);
                });
            };
        }

        public static TceGraphMigrationOperation AddDefaultField(string componentId, string fieldName, object value)
        {
            return (document, _) =>
            {
                ForEachNode(document, (node, _) =>
                {
                    if (!string.Equals(node.ComponentId, componentId, StringComparison.Ordinal))
                        return;

                    if (!node.Fields.ContainsKey(fieldName))
                        node.Fields.Add(fieldName, value);
                });
            };
        }

        public static TceGraphMigrationOperation FailRemovedComponent(string componentId, string message)
        {
            return (document, issues) =>
            {
                ForEachNode(document, (node, path) =>
                {
                    if (!string.Equals(node.ComponentId, componentId, StringComparison.Ordinal))
                        return;

                    issues.Add(new TceValidationIssue(
                        TceValidationSeverity.Error,
                        TceValidationCodes.GraphMigrationFailed,
                        path,
                        message ?? $"{componentId} has no migration."));
                });
            };
        }

        private static void ForEachNode(TceExternalGraphDocument document, Action<TceExternalGraphNode, string> action)
        {
            ForEachNode(document.Triggers, "triggers", action);
            ForEachNode(document.Conditions, "conditions", action);
            ForEachNode(document.Effects, "effects", action);
        }

        private static void ForEachNode(IReadOnlyList<TceExternalGraphNode> nodes, string laneName, Action<TceExternalGraphNode, string> action)
        {
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                TceExternalGraphNode node = nodes[i];
                if (node != null)
                    action(node, $"{laneName}[{i}]");
            }
        }
    }
}
