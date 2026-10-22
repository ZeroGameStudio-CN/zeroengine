using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AutoBattle.Simulation
{
    public sealed class GridBattleSimulationLayout : IBattleSimulationLayout
    {
        public int Rows { get; }
        public int Cols { get; }

        public GridBattleSimulationLayout(int rows, int cols)
        {
            Rows = rows;
            Cols = cols;
        }

        public int GetDistance(ISimulationUnit a, ISimulationUnit b)
        {
            return Mathf.Abs(a.Row - b.Row) + Mathf.Abs(a.Col - b.Col);
        }

        public bool CanAttack(ISimulationUnit attacker, ISimulationUnit target, IReadOnlyList<ISimulationUnit> allUnits)
        {
            return attacker != null
                && target != null
                && target.IsAlive
                && GetDistance(attacker, target) <= attacker.AttackRange;
        }

        public bool MoveTowards(ISimulationUnit unit, ISimulationUnit target, IReadOnlyList<ISimulationUnit> allUnits)
        {
            if (unit == null || target == null || GetDistance(unit, target) <= unit.AttackRange)
            {
                return false;
            }

            int rowStep = target.Row > unit.Row ? 1 : target.Row < unit.Row ? -1 : 0;
            if (rowStep != 0 && TryMove(unit, rowStep, 0, allUnits))
            {
                return true;
            }

            int colStep = target.Col > unit.Col ? 1 : target.Col < unit.Col ? -1 : 0;
            return colStep != 0 && TryMove(unit, 0, colStep, allUnits);
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
                if (isHostile == hostileToCenter && GetDistance(center, unit) <= radius)
                {
                    result.Add(unit);
                }
            }

            return result;
        }

        private bool TryMove(ISimulationUnit unit, int rowDelta, int colDelta, IReadOnlyList<ISimulationUnit> allUnits)
        {
            int nextRow = unit.Row + rowDelta;
            int nextCol = unit.Col + colDelta;

            if (nextRow < 0 || nextRow >= Rows || nextCol < 0 || nextCol >= Cols)
            {
                return false;
            }

            if (IsOccupied(nextRow, nextCol, unit, allUnits))
            {
                return false;
            }

            unit.Row = nextRow;
            unit.Col = nextCol;
            return true;
        }

        private static bool IsOccupied(
            int row,
            int col,
            ISimulationUnit movingUnit,
            IReadOnlyList<ISimulationUnit> allUnits)
        {
            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit != null && unit != movingUnit && unit.IsAlive && unit.Row == row && unit.Col == col)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
