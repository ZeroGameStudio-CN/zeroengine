using System;

namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldGraphRuntimeSessionOptions
    {
        public WorldGraphRuntimeSessionOptions(
            string expectedWorldGraphId,
            string startCellId,
            string startAnchorId,
            int maxLoadedBudgetWeight,
            TimeSpan minimumCellResidency,
            bool loadBoundaryCells = true)
        {
            ExpectedWorldGraphId = expectedWorldGraphId ?? string.Empty;
            StartCellId = startCellId ?? string.Empty;
            StartAnchorId = startAnchorId ?? string.Empty;
            MaxLoadedBudgetWeight = maxLoadedBudgetWeight <= 0 ? int.MaxValue : maxLoadedBudgetWeight;
            MinimumCellResidency = minimumCellResidency <= TimeSpan.Zero ? TimeSpan.Zero : minimumCellResidency;
            LoadBoundaryCells = loadBoundaryCells;
        }

        public string ExpectedWorldGraphId { get; }
        public string StartCellId { get; }
        public string StartAnchorId { get; }
        public int MaxLoadedBudgetWeight { get; }
        public TimeSpan MinimumCellResidency { get; }
        public bool LoadBoundaryCells { get; }
    }
}
