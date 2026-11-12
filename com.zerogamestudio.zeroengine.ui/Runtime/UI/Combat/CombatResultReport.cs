using System;
using System.Collections.Generic;

namespace ZeroEngine.UI.Combat
{
    public enum CombatResultType
    {
        Victory,
        Defeat,
        Escape
    }

    [Serializable]
    public struct CombatResultLine
    {
        public string Label;
        public string Value;

        public CombatResultLine(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    public sealed class CombatResultReport
    {
        public CombatResultType Result;
        public string Title;
        public string Subtitle;
        public string EmptyRewardText = "无奖励";

        public List<CombatResultLine> Summary { get; } = new();
        public List<string> Tags { get; } = new();
        public List<CombatResultLine> Rewards { get; } = new();
        public List<CombatResultLine> Growth { get; } = new();

        public bool ShouldShowRewardArea => Result == CombatResultType.Victory;
        public bool HasRewards => Rewards.Count > 0 || Growth.Count > 0;
    }
}
