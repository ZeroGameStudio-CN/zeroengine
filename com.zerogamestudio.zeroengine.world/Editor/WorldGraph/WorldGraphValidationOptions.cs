namespace ZeroEngine.World.Editor.WorldGraph
{
    public readonly struct WorldGraphValidationOptions
    {
        public WorldGraphValidationOptions(
            bool requireSceneAddresses,
            bool requireInteriorReturnLinks,
            int maxCellBudgetWeight)
        {
            RequireSceneAddresses = requireSceneAddresses;
            RequireInteriorReturnLinks = requireInteriorReturnLinks;
            MaxCellBudgetWeight = maxCellBudgetWeight;
        }

        public bool RequireSceneAddresses { get; }
        public bool RequireInteriorReturnLinks { get; }
        public int MaxCellBudgetWeight { get; }

        public static WorldGraphValidationOptions StrictProduction { get; } =
            new WorldGraphValidationOptions(true, true, 64);
    }
}
