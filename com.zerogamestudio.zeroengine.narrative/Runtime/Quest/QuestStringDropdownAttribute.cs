using UnityEngine;

namespace ZeroEngine.Quest
{
    public enum QuestStringDropdownKind
    {
        QuestId,
        NpcId,
        EntityId,
        InteractTargetId,
        ItemId,
        LocationId,
        EventName
    }

    /// <summary>
    /// Marks a serialized string as a project-provided quest authoring reference.
    /// Runtime keeps the value as a plain string; editor drawers may show cached dropdown options.
    /// </summary>
    public class QuestStringDropdownAttribute : PropertyAttribute
    {
        public QuestStringDropdownKind Kind { get; }

        public QuestStringDropdownAttribute(QuestStringDropdownKind kind)
        {
            Kind = kind;
        }
    }

    public sealed class QuestNpcIdDropdownAttribute : QuestStringDropdownAttribute
    {
        public QuestNpcIdDropdownAttribute() : base(QuestStringDropdownKind.NpcId) { }
    }

    public sealed class QuestEntityIdDropdownAttribute : QuestStringDropdownAttribute
    {
        public QuestEntityIdDropdownAttribute() : base(QuestStringDropdownKind.EntityId) { }
    }

    public sealed class QuestInteractTargetIdDropdownAttribute : QuestStringDropdownAttribute
    {
        public QuestInteractTargetIdDropdownAttribute() : base(QuestStringDropdownKind.InteractTargetId) { }
    }

    public sealed class QuestItemIdDropdownAttribute : QuestStringDropdownAttribute
    {
        public QuestItemIdDropdownAttribute() : base(QuestStringDropdownKind.ItemId) { }
    }

    public sealed class QuestLocationIdDropdownAttribute : QuestStringDropdownAttribute
    {
        public QuestLocationIdDropdownAttribute() : base(QuestStringDropdownKind.LocationId) { }
    }

    public sealed class QuestEventNameDropdownAttribute : QuestStringDropdownAttribute
    {
        public QuestEventNameDropdownAttribute() : base(QuestStringDropdownKind.EventName) { }
    }
}
