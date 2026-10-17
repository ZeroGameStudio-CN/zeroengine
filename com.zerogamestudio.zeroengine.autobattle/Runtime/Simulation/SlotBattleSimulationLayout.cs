using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AutoBattle.Simulation
{
    public sealed class SlotBattleSimulationLayout : IBattleSimulationLayout
    {
        public int GetDistance(ISimulationUnit a, ISimulationUnit b)
        {
            return a.Team == b.Team ? Mathf.Abs(a.SlotIndex - b.SlotIndex) : 1;
        }

        public bool CanAttack(ISimulationUnit attacker, ISimulationUnit target, IReadOnlyList<ISimulationUnit> allUnits)
        {
            if (attacker == null || target == null || !target.IsAlive)
            {
                return false;
            }

            if (attacker.AttackRange >= 2 || attacker.Role == SimulationUnitRole.Assassin)
            {
                return true;
            }

            return target.SlotIndex == GetFrontSlot(target.Team, allUnits);
        }

        public bool MoveTowards(ISimulationUnit unit, ISimulationUnit target, IReadOnlyList<ISimulationUnit> allUnits)
        {
            return false;
        }

        public List<ISimulationUnit> FindUnitsInRange(
            ISimulationUnit center,
            int radius,
            bool hostileToCenter,
            IReadOnlyList<ISimulationUnit> allUnits)
        {
            var result = new List<ISimulationUnit>();

            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                bool isHostile = unit.Team != center.Team;
                if (isHostile != hostileToCenter)
                {
                    continue;
                }

                if (unit.Team != center.Team || Mathf.Abs(unit.SlotIndex - center.SlotIndex) <= radius)
                {
                    result.Add(unit);
                }
            }

            return result;
        }

        private static int GetFrontSlot(SimulationTeam team, IReadOnlyList<ISimulationUnit> allUnits)
        {
            int frontSlot = int.MaxValue;

            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit != null && unit.IsAlive && unit.Team == team && unit.SlotIndex < frontSlot)
                {
                    frontSlot = unit.SlotIndex;
                }
            }

            return frontSlot == int.MaxValue ? 0 : frontSlot;
        }
    }
}
