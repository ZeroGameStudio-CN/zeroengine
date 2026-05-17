namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformRouteCost
    {
        public readonly float Total;
        public readonly float HorizontalDistance;
        public readonly float VerticalDistance;
        public readonly int JumpCount;
        public readonly int FallCount;
        public readonly int DropDownCount;
        public readonly int CommandCount;

        public PlatformRouteCost(
            float total,
            float horizontalDistance,
            float verticalDistance,
            int jumpCount,
            int fallCount,
            int dropDownCount,
            int commandCount)
        {
            Total = total;
            HorizontalDistance = horizontalDistance;
            VerticalDistance = verticalDistance;
            JumpCount = jumpCount;
            FallCount = fallCount;
            DropDownCount = dropDownCount;
            CommandCount = commandCount;
        }

        public static PlatformRouteCost Unreachable => new PlatformRouteCost(
            float.PositiveInfinity,
            0f,
            0f,
            0,
            0,
            0,
            0);

        public bool IsReachable => !float.IsPositiveInfinity(Total);
    }
}
