using System.Collections.Generic;

namespace ZeroEngine.AutoBattle.Simulation
{
    public interface ISimulationUnit
    {
        string UnitId { get; }
        SimulationTeam Team { get; }
        SimulationUnitRole Role { get; }
        bool IsAlive { get; }
        int Row { get; set; }
        int Col { get; set; }
        int SlotIndex { get; set; }
        int AttackRange { get; }
        float CurrentHealth { get; }
        float MaxHealth { get; }
        IReadOnlyDictionary<string, float> Threats { get; }

        void ApplyDamage(float amount, ISimulationUnit attacker);
        void ApplyHeal(float amount, ISimulationUnit healer);
    }
}
