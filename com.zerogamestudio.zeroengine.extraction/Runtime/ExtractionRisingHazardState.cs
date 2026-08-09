namespace POB.Extraction
{
    // M2 SS.1：喂给上涌危险组件的一次性快照——纯数据，不含 Unity 引擎类型，
    // 方便 facade 一次查询打包返回，调用方不用分开查 5 个值。
    public readonly struct ExtractionRisingHazardState
    {
        public readonly int ElapsedSeconds;
        public readonly int DurationSeconds;
        public readonly float StartY;
        public readonly float RiseRate;
        public readonly float DamagePerSecond;

        public ExtractionRisingHazardState(
            int elapsedSeconds,
            int durationSeconds,
            float startY,
            float riseRate,
            float damagePerSecond)
        {
            ElapsedSeconds = elapsedSeconds;
            DurationSeconds = durationSeconds;
            StartY = startY;
            RiseRate = riseRate;
            DamagePerSecond = damagePerSecond;
        }
    }
}
