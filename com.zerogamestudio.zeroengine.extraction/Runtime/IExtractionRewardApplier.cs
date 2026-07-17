using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionRewardGrant
    {
        public string RewardId;
        public int Quantity;

        public bool IsValid => !string.IsNullOrEmpty(RewardId) && Quantity > 0;

        public ExtractionRewardGrant(string rewardId, int quantity)
        {
            RewardId = rewardId;
            Quantity = quantity;
        }
    }

    public class ExtractionRewardApplyReport
    {
        public int AppliedCount { get; }

        public ExtractionRewardApplyReport(int appliedCount)
        {
            AppliedCount = appliedCount < 0 ? 0 : appliedCount;
        }
    }

    public interface IExtractionRewardApplier
    {
        bool TryApplyRewards(
            List<ExtractionRewardGrant> rewards,
            out ExtractionRewardApplyReport report);
    }

    public sealed class ExtractionNoOpRewardApplier : IExtractionRewardApplier
    {
        public bool TryApplyRewards(
            List<ExtractionRewardGrant> rewards,
            out ExtractionRewardApplyReport report)
        {
            report = new ExtractionRewardApplyReport(0);
            return rewards == null || rewards.Count == 0;
        }
    }
}
