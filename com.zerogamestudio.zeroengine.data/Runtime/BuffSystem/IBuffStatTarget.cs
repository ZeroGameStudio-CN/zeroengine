using ZeroEngine.StatSystem;

namespace ZeroEngine.BuffSystem
{
    /// <summary>
    /// Minimal contract a Buff-applying system needs from its stat host.
    /// Lets BuffHandler apply/remove modifiers without depending on the
    /// concrete MonoBehaviour-based StatController.
    /// </summary>
    public interface IBuffStatTarget
    {
        void AddModifier(StatId statId, StatModifier modifier);
        void RemoveModifier(StatId statId, StatModifier modifier);
    }
}
