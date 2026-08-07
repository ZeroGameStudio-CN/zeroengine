using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionEncumbranceService
    {
        // 纯函数：给定总重/容量，挑出"MinLoadPercent 最大且仍 <= 当前负重比例"的档——即已跨过的
        // 最高档位。容量<=0 或空档位表视为不参与负重系统(不生成任何 tier，调用方保持无惩罚状态)。
        public static bool TrySelectTier(
            List<ExtractionEncumbranceTierDefinition> tiers,
            int totalWeight,
            int capacity,
            out ExtractionEncumbranceTierDefinition tier)
        {
            tier = null;
            if (tiers == null || tiers.Count == 0) return false;
            if (capacity <= 0) return false;

            int loadPercent = (int)((long)totalWeight * 100 / capacity);

            foreach (var candidate in tiers)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (loadPercent < candidate.MinLoadPercent) continue;
                if (tier == null || candidate.MinLoadPercent > tier.MinLoadPercent) tier = candidate;
            }

            return tier != null;
        }
    }
}
