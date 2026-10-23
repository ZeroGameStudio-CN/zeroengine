using ZeroEngine.StatSystem;

namespace ZeroEngine.Equipment
{
    /// <summary>A modifier together with the stat it affects. The loadout copies it before use.</summary>
    public readonly struct EquipmentStatModifier
    {
        public StatId StatId { get; }
        public StatModifier Modifier { get; }

        public EquipmentStatModifier(StatId statId, StatModifier modifier)
        {
            StatId = statId;
            Modifier = modifier;
        }
    }
}
