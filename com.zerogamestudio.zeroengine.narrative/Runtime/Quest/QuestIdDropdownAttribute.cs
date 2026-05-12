namespace ZeroEngine.Quest
{
    /// <summary>
    /// Marks a serialized string as a QuestConfigSO.questId reference.
    /// Runtime keeps the value as a plain string; editor drawers may show a dropdown.
    /// </summary>
    public sealed class QuestIdDropdownAttribute : QuestStringDropdownAttribute
    {
        public QuestIdDropdownAttribute() : base(QuestStringDropdownKind.QuestId) { }
    }
}
