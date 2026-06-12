using System;
using System.Collections.Generic;
using System.Linq;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldGraphWorkbenchModel
    {
        public const string RunValidationActionId = "worldgraph.run.validation";
        public const string RunGraduationActionId = "worldgraph.run.graduation";
        public const string OpenGraphActionId = "worldgraph.open.graph";
        public const string OpenSceneActionPrefix = "worldgraph.open.scene:";
        public const string OpenNavigationActionPrefix = "worldgraph.open.navigation:";

        public static WorldGraphWorkbenchSnapshot CreateSnapshot(
            string title,
            WorldGraphGraduationProfile profile,
            IEnumerable<AreaAuthoringIssue> issues,
            IEnumerable<WorldGraphWorkbenchAction> customActions,
            IEnumerable<WorldGraphWorkbenchRunRecord> history)
        {
            var issueList = SortIssues(issues).ToArray();
            var cells = BuildCells(profile, issueList).ToArray();
            var links = BuildLinks(profile?.Graph).ToArray();
            var descriptors = BuildActions(profile, cells, customActions).ToArray();

            return new WorldGraphWorkbenchSnapshot(
                string.IsNullOrWhiteSpace(title) ? "WorldGraph Workbench" : title.Trim(),
                profile?.Graph?.WorldGraphId,
                profile?.GraphAssetPath,
                profile?.ExpectedWorldGraphId,
                profile?.StartCellId,
                profile?.StartAnchorId,
                cells,
                links,
                issueList.Select(issue => new WorldGraphWorkbenchIssueSummary(issue)).ToArray(),
                descriptors,
                history?.ToArray() ?? Array.Empty<WorldGraphWorkbenchRunRecord>(),
                DateTime.UtcNow);
        }

        private static IEnumerable<AreaAuthoringIssue> SortIssues(IEnumerable<AreaAuthoringIssue> issues)
        {
            return (issues ?? Array.Empty<AreaAuthoringIssue>())
                .OrderByDescending(issue => issue.IsError)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.AssetPath, StringComparer.Ordinal)
                .ThenBy(issue => issue.ContextId, StringComparer.Ordinal)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal);
        }

        private static IEnumerable<WorldGraphWorkbenchCellSummary> BuildCells(
            WorldGraphGraduationProfile profile,
            IReadOnlyList<AreaAuthoringIssue> issues)
        {
            var graph = profile?.Graph;
            if (graph == null)
            {
                yield break;
            }

            foreach (var cell in EnumerateCells(graph)
                         .OrderBy(cell => cell.CellId, StringComparer.Ordinal))
            {
                var scenePath = SafeGet(profile.GetCellScenePath, cell);
                var navPath = SafeGet(profile.GetNavigationAssetPath, cell);
                yield return new WorldGraphWorkbenchCellSummary(
                    cell.CellId,
                    cell.DisplayName,
                    cell.CellKind,
                    cell.Layers,
                    cell.BudgetWeight,
                    cell.SceneAddress,
                    scenePath,
                    navPath,
                    cell.Anchors.Count,
                    cell.StreamingBoundaries.Count,
                    ResolveCellStatus(cell, scenePath, navPath, issues));
            }
        }

        private static IEnumerable<WorldGraphWorkbenchLinkSummary> BuildLinks(WorldGraphSO graph)
        {
            if (graph == null)
            {
                yield break;
            }

            foreach (var link in graph.TravelLinks
                         .Where(link => link != null)
                         .OrderBy(link => link.LinkId, StringComparer.Ordinal))
            {
                yield return new WorldGraphWorkbenchLinkSummary(
                    link.LinkId,
                    link.FromAnchorId,
                    link.ToAnchorId,
                    link.TravelMode,
                    link.Bidirectional);
            }
        }

        private static IEnumerable<WorldGraphWorkbenchActionDescriptor> BuildActions(
            WorldGraphGraduationProfile profile,
            IReadOnlyList<WorldGraphWorkbenchCellSummary> cells,
            IEnumerable<WorldGraphWorkbenchAction> customActions)
        {
            yield return new WorldGraphWorkbenchActionDescriptor(
                RunValidationActionId,
                "Run Validation",
                "Run the current profile validation pass.",
                WorldGraphWorkbenchActionKind.RunValidation,
                WorldGraphWorkbenchActionRisk.None,
                string.Empty,
                false);

            yield return new WorldGraphWorkbenchActionDescriptor(
                RunGraduationActionId,
                "Run Graduation",
                "Run the full graduation validation pass and record the result.",
                WorldGraphWorkbenchActionKind.RunGraduation,
                WorldGraphWorkbenchActionRisk.None,
                string.Empty,
                false);

            if (!string.IsNullOrWhiteSpace(profile?.GraphAssetPath))
            {
                yield return new WorldGraphWorkbenchActionDescriptor(
                    OpenGraphActionId,
                    "Open Graph Asset",
                    "Select the WorldGraph asset in the Project window.",
                    WorldGraphWorkbenchActionKind.OpenAsset,
                    WorldGraphWorkbenchActionRisk.None,
                    profile.GraphAssetPath,
                    false);
            }

            foreach (var cell in cells)
            {
                if (!string.IsNullOrWhiteSpace(cell.ScenePath))
                {
                    yield return new WorldGraphWorkbenchActionDescriptor(
                        OpenSceneActionPrefix + cell.CellId,
                        "Open Scene " + cell.CellId,
                        "Open the authored scene for this world cell.",
                        WorldGraphWorkbenchActionKind.OpenScene,
                        WorldGraphWorkbenchActionRisk.OpensScene,
                        cell.ScenePath,
                        true);
                }

                if (!string.IsNullOrWhiteSpace(cell.NavigationAssetPath))
                {
                    yield return new WorldGraphWorkbenchActionDescriptor(
                        OpenNavigationActionPrefix + cell.CellId,
                        "Open Nav " + cell.CellId,
                        "Select the baked navigation asset for this world cell.",
                        WorldGraphWorkbenchActionKind.OpenAsset,
                        WorldGraphWorkbenchActionRisk.None,
                        cell.NavigationAssetPath,
                        false);
                }
            }

            foreach (var action in customActions ?? Array.Empty<WorldGraphWorkbenchAction>())
            {
                yield return action.ToDescriptor();
            }
        }

        private static WorldGraphWorkbenchCellStatus ResolveCellStatus(
            WorldCellDefinition cell,
            string scenePath,
            string navPath,
            IReadOnlyList<AreaAuthoringIssue> issues)
        {
            var status = WorldGraphWorkbenchCellStatus.Ready;
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                if (!IssueAffectsCell(issue, cell, scenePath, navPath))
                {
                    continue;
                }

                if (issue.IsError)
                {
                    return WorldGraphWorkbenchCellStatus.Error;
                }

                status = WorldGraphWorkbenchCellStatus.Warning;
            }

            return status;
        }

        private static bool IssueAffectsCell(
            AreaAuthoringIssue issue,
            WorldCellDefinition cell,
            string scenePath,
            string navPath)
        {
            if (!string.IsNullOrWhiteSpace(issue.ContextId)
                && string.Equals(issue.ContextId, cell.CellId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(issue.AssetPath)
                && (string.Equals(issue.AssetPath, scenePath, StringComparison.Ordinal)
                    || string.Equals(issue.AssetPath, navPath, StringComparison.Ordinal)))
            {
                return true;
            }

            return cell.Anchors.Any(anchor => anchor != null && issue.ContextId == anchor.AnchorId)
                   || cell.StreamingBoundaries.Any(boundary => boundary != null && issue.ContextId == boundary.BoundaryId);
        }

        private static IEnumerable<WorldCellDefinition> EnumerateCells(WorldGraphSO graph)
        {
            return graph.Regions
                .Where(region => region != null)
                .SelectMany(region => region.Cells)
                .Where(cell => cell != null);
        }

        private static string SafeGet(Func<WorldCellDefinition, string> getter, WorldCellDefinition cell)
        {
            if (getter == null || cell == null)
            {
                return string.Empty;
            }

            try
            {
                return getter(cell) ?? string.Empty;
            }
            catch (Exception exception)
            {
                return "ERROR: " + exception.Message;
            }
        }
    }
}
