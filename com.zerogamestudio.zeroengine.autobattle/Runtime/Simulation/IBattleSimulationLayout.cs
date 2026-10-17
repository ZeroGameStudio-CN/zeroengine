using System.Collections.Generic;

namespace ZeroEngine.AutoBattle.Simulation
{
    public interface IBattleSimulationLayout
    {
        int GetDistance(ISimulationUnit a, ISimulationUnit b);
        bool CanAttack(ISimulationUnit attacker, ISimulationUnit target, IReadOnlyList<ISimulationUnit> allUnits);
        bool MoveTowards(ISimulationUnit unit, ISimulationUnit target, IReadOnlyList<ISimulationUnit> allUnits);
        List<ISimulationUnit> FindUnitsInRange(
            ISimulationUnit center,
            int radius,
            bool hostileToCenter,
            IReadOnlyList<ISimulationUnit> allUnits);
    }
}
