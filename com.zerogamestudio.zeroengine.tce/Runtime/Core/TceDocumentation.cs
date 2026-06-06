using System;

namespace ZeroEngine.TCE
{
    public enum TceComponentDocCategory
    {
        Trigger = 0,
        Condition = 1,
        Effect = 2
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TceComponentDocAttribute : Attribute
    {
        public TceComponentDocAttribute(TceComponentDocCategory category, string displayName, string shortDescription, string expandedDescription)
        {
            Category = category;
            DisplayName = displayName ?? string.Empty;
            ShortDescription = shortDescription ?? string.Empty;
            ExpandedDescription = expandedDescription ?? string.Empty;
        }

        public TceComponentDocCategory Category { get; }
        public string DisplayName { get; }
        public string ShortDescription { get; }
        public string ExpandedDescription { get; }
    }
}
