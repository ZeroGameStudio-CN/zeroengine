using System;
using System.Collections.Generic;

namespace ZeroEngine.Gameplay.Rewards
{
    public readonly struct RewardResult
    {
        private RewardResult(
            bool success,
            bool skipped,
            string rewardId,
            string failureReason,
            IReadOnlyList<string> summaryLines,
            IReadOnlyList<string> appliedPayloadIds)
        {
            Success = success;
            Skipped = skipped;
            RewardId = rewardId ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;
            SummaryLines = summaryLines ?? Array.Empty<string>();
            AppliedPayloadIds = appliedPayloadIds ?? Array.Empty<string>();
        }

        public bool Success { get; }
        public bool Skipped { get; }
        public string RewardId { get; }
        public string FailureReason { get; }
        public IReadOnlyList<string> SummaryLines { get; }
        public IReadOnlyList<string> AppliedPayloadIds { get; }

        public static RewardResult Succeeded(string rewardId, string summaryLine = null, string appliedPayloadId = null)
        {
            var summaries = string.IsNullOrEmpty(summaryLine) ? Array.Empty<string>() : new[] { summaryLine };
            var payloads = string.IsNullOrEmpty(appliedPayloadId) ? Array.Empty<string>() : new[] { appliedPayloadId };
            return new RewardResult(true, false, rewardId, string.Empty, summaries, payloads);
        }

        public static RewardResult Skip(string rewardId, string reason)
        {
            return new RewardResult(true, true, rewardId, reason, Array.Empty<string>(), Array.Empty<string>());
        }

        public static RewardResult Failed(string rewardId, string reason)
        {
            return new RewardResult(false, false, rewardId, reason, Array.Empty<string>(), Array.Empty<string>());
        }
    }
}
