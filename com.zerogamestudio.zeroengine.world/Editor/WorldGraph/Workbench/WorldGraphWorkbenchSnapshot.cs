using System;
using System.Collections.Generic;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public enum WorldGraphWorkbenchRunMode
    {
        Validation,
        Graduation
    }

    public enum WorldGraphWorkbenchCellStatus
    {
        Unknown,
        Ready,
        Warning,
        Error
    }

    public readonly struct WorldGraphWorkbenchRunRecord
    {
        public WorldGraphWorkbenchRunRecord(
            WorldGraphWorkbenchRunMode mode,
            DateTime completedAtUtc,
            int issueCount,
            int blockingIssueCount)
        {
            Mode = mode;
            CompletedAtUtc = completedAtUtc;
            IssueCount = issueCount;
            BlockingIssueCount = blockingIssueCount;
        }

        public WorldGraphWorkbenchRunMode Mode { get; }
        public DateTime CompletedAtUtc { get; }
        public int IssueCount { get; }
        public int BlockingIssueCount { get; }
        public bool Passed => BlockingIssueCount == 0;
    }

    public readonly struct WorldGraphWorkbenchCellSummary
    {
        public WorldGraphWorkbenchCellSummary(
            string cellId,
            string displayName,
            WorldCellKind cellKind,
            WorldCellLayer layers,
            int budgetWeight,
            string sceneAddress,
            string scenePath,
            string navigationAssetPath,
            int anchorCount,
            int streamingBoundaryCount,
            WorldGraphWorkbenchCellStatus status)
        {
            CellId = cellId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            CellKind = cellKind;
            Layers = layers;
            BudgetWeight = budgetWeight;
            SceneAddress = sceneAddress ?? string.Empty;
            ScenePath = scenePath ?? string.Empty;
            NavigationAssetPath = navigationAssetPath ?? string.Empty;
            AnchorCount = anchorCount;
            StreamingBoundaryCount = streamingBoundaryCount;
            Status = status;
        }

        public string CellId { get; }
        public string DisplayName { get; }
        public WorldCellKind CellKind { get; }
        public WorldCellLayer Layers { get; }
        public int BudgetWeight { get; }
        public string SceneAddress { get; }
        public string ScenePath { get; }
        public string NavigationAssetPath { get; }
        public int AnchorCount { get; }
        public int StreamingBoundaryCount { get; }
        public WorldGraphWorkbenchCellStatus Status { get; }
    }

    public readonly struct WorldGraphWorkbenchLinkSummary
    {
        public WorldGraphWorkbenchLinkSummary(
            string linkId,
            string fromAnchorId,
            string toAnchorId,
            WorldTravelMode travelMode,
            bool bidirectional)
        {
            LinkId = linkId ?? string.Empty;
            FromAnchorId = fromAnchorId ?? string.Empty;
            ToAnchorId = toAnchorId ?? string.Empty;
            TravelMode = travelMode;
            Bidirectional = bidirectional;
        }

        public string LinkId { get; }
        public string FromAnchorId { get; }
        public string ToAnchorId { get; }
        public WorldTravelMode TravelMode { get; }
        public bool Bidirectional { get; }
    }

    public readonly struct WorldGraphWorkbenchIssueSummary
    {
        public WorldGraphWorkbenchIssueSummary(AreaAuthoringIssue issue)
        {
            Severity = issue.Severity;
            Code = issue.Code ?? string.Empty;
            Message = issue.Message ?? string.Empty;
            AssetPath = issue.AssetPath ?? string.Empty;
            ContextId = issue.ContextId ?? string.Empty;
            IsBlocking = issue.IsError;
        }

        public AreaAuthoringIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string AssetPath { get; }
        public string ContextId { get; }
        public bool IsBlocking { get; }
    }

    public sealed class WorldGraphWorkbenchSnapshot
    {
        public WorldGraphWorkbenchSnapshot(
            string title,
            string worldGraphId,
            string graphAssetPath,
            string expectedWorldGraphId,
            string startCellId,
            string startAnchorId,
            IReadOnlyList<WorldGraphWorkbenchCellSummary> cells,
            IReadOnlyList<WorldGraphWorkbenchLinkSummary> links,
            IReadOnlyList<WorldGraphWorkbenchIssueSummary> issues,
            IReadOnlyList<WorldGraphWorkbenchActionDescriptor> actions,
            IReadOnlyList<WorldGraphWorkbenchRunRecord> history,
            DateTime generatedAtUtc)
        {
            Title = title ?? string.Empty;
            WorldGraphId = worldGraphId ?? string.Empty;
            GraphAssetPath = graphAssetPath ?? string.Empty;
            ExpectedWorldGraphId = expectedWorldGraphId ?? string.Empty;
            StartCellId = startCellId ?? string.Empty;
            StartAnchorId = startAnchorId ?? string.Empty;
            Cells = cells ?? Array.Empty<WorldGraphWorkbenchCellSummary>();
            Links = links ?? Array.Empty<WorldGraphWorkbenchLinkSummary>();
            Issues = issues ?? Array.Empty<WorldGraphWorkbenchIssueSummary>();
            Actions = actions ?? Array.Empty<WorldGraphWorkbenchActionDescriptor>();
            History = history ?? Array.Empty<WorldGraphWorkbenchRunRecord>();
            GeneratedAtUtc = generatedAtUtc;
        }

        public string Title { get; }
        public string WorldGraphId { get; }
        public string GraphAssetPath { get; }
        public string ExpectedWorldGraphId { get; }
        public string StartCellId { get; }
        public string StartAnchorId { get; }
        public IReadOnlyList<WorldGraphWorkbenchCellSummary> Cells { get; }
        public IReadOnlyList<WorldGraphWorkbenchLinkSummary> Links { get; }
        public IReadOnlyList<WorldGraphWorkbenchIssueSummary> Issues { get; }
        public IReadOnlyList<WorldGraphWorkbenchActionDescriptor> Actions { get; }
        public IReadOnlyList<WorldGraphWorkbenchRunRecord> History { get; }
        public DateTime GeneratedAtUtc { get; }
        public bool HasBlockingIssues
        {
            get
            {
                for (var i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].IsBlocking)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
