namespace POB.Extraction
{
    // M2 SS.1：上涌危险的纯函数核心——高度公式 + 淹没判定，不碰 Unity 引擎类型，
    // 方便 EditMode 直接喂数字断言，不需要场景/物理系统。
    public static class ExtractionRisingHazardService
    {
        // 倒计时归零(elapsedSeconds >= durationSeconds)前按 riseRate 匀速上升；
        // 归零后速率翻倍，用归零那一刻的高度接着算，不产生跳变。
        public static float GetCurrentHeight(
            int elapsedSeconds,
            int durationSeconds,
            float startY,
            float riseRate)
        {
            if (elapsedSeconds <= durationSeconds)
                return startY + riseRate * elapsedSeconds;

            float heightAtTimeout = startY + riseRate * durationSeconds;
            int secondsPastTimeout = elapsedSeconds - durationSeconds;
            return heightAtTimeout + riseRate * 2f * secondsPastTimeout;
        }

        // 玩家 Y 落在当前水位及以下即视为淹没。
        public static bool IsSubmerged(float entityY, float currentHeight)
        {
            return entityY <= currentHeight;
        }
    }
}
