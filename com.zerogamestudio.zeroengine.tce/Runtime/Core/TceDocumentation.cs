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
            : this(category, string.Empty, displayName, shortDescription, expandedDescription)
        {
        }

        public TceComponentDocAttribute(TceComponentDocCategory category, string componentId, string displayName, string shortDescription, string expandedDescription)
        {
            Category = category;
            ComponentId = componentId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ShortDescription = shortDescription ?? string.Empty;
            ExpandedDescription = expandedDescription ?? string.Empty;
        }

        public TceComponentDocCategory Category { get; }
        public string ComponentId { get; }
        public string DisplayName { get; }
        public string ShortDescription { get; }
        public string ExpandedDescription { get; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class TceFieldDocAttribute : Attribute
    {
        public TceFieldDocAttribute(string description)
        {
            Description = description ?? string.Empty;
        }

        public string Description { get; }
    }
}
