using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AutoBattle.Simulation
{
    public static class BattleSimulationTargeting
    {
        public static ISimulationUnit FindTarget(ISimulationUnit unit, IReadOnlyList<ISimulationUnit> allUnits)
        {
            if (unit == null || allUnits == null)
            {
                return null;
            }

            if (unit.Role == SimulationUnitRole.Healer)
            {
                return FindLowestHealthAlly(unit, allUnits) ?? FindNearestEnemy(unit, allUnits);
            }

            var enemies = GetAliveEnemies(unit, allUnits);
            if (enemies.Count == 0)
            {
                return null;
            }

            if (unit.Role == SimulationUnitRole.Assassin)
            {
                return FindLowestHealth(enemies);
            }

            if (unit.Role == SimulationUnitRole.Tank)
            {
                return FindHighestThreatTarget(unit, enemies) ?? FindNearestInList(unit, enemies);
            }

            if (unit.AttackRange > 1)
            {
                return FindHighestThreatNonTank(unit, enemies) ?? FindNearestInList(unit, enemies);
            }

            return FindHighestThreatTarget(unit, enemies) ?? FindNearestInList(unit, enemies);
        }

        public static ISimulationUnit FindNearestEnemy(ISimulationUnit self, IReadOnlyList<ISimulationUnit> allUnits)
        {
            ISimulationUnit nearest = null;
            int minDistance = int.MaxValue;

            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit == null || !unit.IsAlive || unit.Team == self.Team)
                {
                    continue;
                }

                int distance = ManhattanDistance(self, unit);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = unit;
                }
            }

            return nearest;
        }

        public static ISimulationUnit FindLowestHealthAlly(ISimulationUnit self, IReadOnlyList<ISimulationUnit> allUnits)
        {
            ISimulationUnit lowest = null;
            float lowestRatio = 1f;

            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit == null || unit == self || !unit.IsAlive || unit.Team != self.Team || unit.MaxHealth <= 0f)
                {
                    continue;
                }

                float ratio = unit.CurrentHealth / unit.MaxHealth;
                if (ratio < lowestRatio)
                {
                    lowestRatio = ratio;
                    lowest = unit;
                }
            }

            return lowestRatio < 0.9f ? lowest : null;
        }

        public static List<ISimulationUnit> GetAliveEnemies(ISimulationUnit self, IReadOnlyList<ISimulationUnit> allUnits)
        {
            var result = new List<ISimulationUnit>();

            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit != null && unit.IsAlive && unit.Team != self.Team)
                {
                    result.Add(unit);
                }
            }

            return result;
        }

        private static ISimulationUnit FindLowestHealth(IReadOnlyList<ISimulationUnit> units)
        {
            ISimulationUnit best = null;
            float bestHealth = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit.CurrentHealth < bestHealth)
                {
                    bestHealth = unit.CurrentHealth;
                    best = unit;
                }
            }

            return best;
        }

        private static ISimulationUnit FindHighestThreatTarget(ISimulationUnit self, IReadOnlyList<ISimulationUnit> enemies)
        {
            if (self.Threats == null || self.Threats.Count == 0)
            {
                return null;
            }

            ISimulationUnit best = null;
            float bestThreat = float.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (self.Threats.TryGetValue(enemy.UnitId, out float threat) && threat > bestThreat)
                {
                    bestThreat = threat;
                    best = enemy;
                }
            }

            return best;
        }

        private static ISimulationUnit FindHighestThreatNonTank(ISimulationUnit self, IReadOnlyList<ISimulationUnit> enemies)
        {
            if (self.Threats == null || self.Threats.Count == 0)
            {
                return null;
            }

            ISimulationUnit best = null;
            float bestThreat = float.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy.Role == SimulationUnitRole.Tank)
                {
                    continue;
                }

                if (self.Threats.TryGetValue(enemy.UnitId, out float threat) && threat > bestThreat)
                {
                    bestThreat = threat;
                    best = enemy;
                }
            }

            return best;
        }

        private static ISimulationUnit FindNearestInList(ISimulationUnit self, IReadOnlyList<ISimulationUnit> units)
        {
            ISimulationUnit nearest = null;
            int minDistance = int.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                int distance = ManhattanDistance(self, unit);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = unit;
                }
            }

            return nearest;
        }

        private static int ManhattanDistance(ISimulationUnit a, ISimulationUnit b)
        {
            return Mathf.Abs(a.Row - b.Row) + Mathf.Abs(a.Col - b.Col);
        }
    }
}
