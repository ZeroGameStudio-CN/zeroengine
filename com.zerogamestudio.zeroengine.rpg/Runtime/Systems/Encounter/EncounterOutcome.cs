using System;
using System.Collections.Generic;
using ZeroEngine.RPG.TurnBased;

namespace ZeroEngine.RPG.Encounter
{
    public enum EncounterOutcomeType
    {
        Started,
        Victory,
        Defeat,
        Escape,
        Cancelled,
        Failed
    }

    public readonly struct EncounterOutcome
    {
        private EncounterOutcome(
            string encounterId,
            BattleMode battleMode,
            EncounterOutcomeType outcomeType,
            IReadOnlyList<string> enemyIds,
            IReadOnlyList<string> rewardSummaryLines,
            IReadOnlyList<string> appliedRewardIds,
            string projectResultName)
        {
            EncounterId = encounterId ?? string.Empty;
            BattleMode = battleMode;
            OutcomeType = outcomeType;
            EnemyIds = enemyIds ?? Array.Empty<string>();
            RewardSummaryLines = rewardSummaryLines ?? Array.Empty<string>();
            AppliedRewardIds = appliedRewardIds ?? Array.Empty<string>();
            ProjectResultName = projectResultName ?? string.Empty;
        }

        public string EncounterId { get; }
        public BattleMode BattleMode { get; }
        public EncounterOutcomeType OutcomeType { get; }
        public IReadOnlyList<string> EnemyIds { get; }
        public IReadOnlyList<string> RewardSummaryLines { get; }
        public IReadOnlyList<string> AppliedRewardIds { get; }
        public string ProjectResultName { get; }

        public static EncounterOutcome Create(
            string encounterId,
            BattleMode battleMode,
            EncounterOutcomeType outcomeType,
            IEnumerable<string> enemyIds = null,
            IEnumerable<string> rewardSummaryLines = null,
            IEnumerable<string> appliedRewardIds = null,
            string projectResultName = null)
        {
            return new EncounterOutcome(
                encounterId,
                battleMode,
                outcomeType,
                ToArray(enemyIds),
                ToArray(rewardSummaryLines),
                ToArray(appliedRewardIds),
                projectResultName);
        }

        private static string[] ToArray(IEnumerable<string> source)
        {
            if (source == null)
            {
                return Array.Empty<string>();
            }

            return source is string[] array ? array : new List<string>(source).ToArray();
        }
    }
}
