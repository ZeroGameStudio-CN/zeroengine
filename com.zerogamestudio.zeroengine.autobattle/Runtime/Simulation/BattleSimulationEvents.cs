using System.Collections.Generic;

namespace ZeroEngine.AutoBattle.Simulation
{
    public enum BattleSimulationEventType
    {
        Moved,
        ActionResolved,
        UnitDefeated
    }

    public readonly struct BattleSimulationEvent
    {
        public BattleSimulationEvent(BattleSimulationEventType type, ISimulationUnit actor, ISimulationUnit target)
        {
            Type = type;
            Actor = actor;
            Target = target;
        }

        public BattleSimulationEventType Type { get; }
        public ISimulationUnit Actor { get; }
        public ISimulationUnit Target { get; }
    }

    public sealed class BattleSimulationContext
    {
        private readonly List<BattleSimulationEvent> _events = new List<BattleSimulationEvent>(16);

        public IReadOnlyList<ISimulationUnit> Units { get; internal set; }
        public IBattleSimulationLayout Layout { get; internal set; }
        public IReadOnlyList<BattleSimulationEvent> Events => _events;

        public void AddEvent(BattleSimulationEventType type, ISimulationUnit actor, ISimulationUnit target)
        {
            _events.Add(new BattleSimulationEvent(type, actor, target));
        }

        internal void Clear()
        {
            _events.Clear();
        }
    }
}
