using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.AutoBattle.Formation;
using ZeroEngine.AutoBattle.Grid;

namespace ZeroEngine.AutoBattle.Editor.Tests
{
    public sealed class GridBoardAndFormationTests
    {
        [Test]
        public void PlaceMoveAndRemoveUnitKeepsCellAndUnitInSync()
        {
            var board = new GridBoard(3, 2);
            var unit = new TestBattleUnit("unit");

            Assert.IsTrue(board.PlaceUnit(unit, 0, 1));
            Assert.AreSame(board.GetCell(0, 1), unit.CurrentCell);
            Assert.IsTrue(board.GetCell(0, 1).IsOccupied);

            Assert.IsTrue(board.MoveUnit(unit, 2, 1));
            Assert.IsFalse(board.GetCell(0, 1).IsOccupied);
            Assert.AreSame(board.GetCell(2, 1), unit.CurrentCell);

            Assert.IsTrue(board.RemoveUnit(unit));
            Assert.IsNull(unit.CurrentCell);
            Assert.IsFalse(board.GetCell(2, 1).IsOccupied);
        }

        [Test]
        public void OccupiedCellRejectsSecondUnit()
        {
            var board = new GridBoard(2, 2);
            var first = new TestBattleUnit("first");
            var second = new TestBattleUnit("second");

            Assert.IsTrue(board.PlaceUnit(first, 0, 0));
            Assert.IsFalse(board.PlaceUnit(second, 0, 0));
            Assert.AreSame(first, board.GetCell(0, 0).OccupyingUnit);
            Assert.IsNull(second.CurrentCell);
        }

        [Test]
        public void FormationDataRespectsMaxUnitsAndClearsAssignments()
        {
            var formation = new FormationData("line", "Line", maxUnits: 1);
            var slot = formation.AddSlot(new Vector2Int(1, 2));
            slot.UnitId = "hero";

            Assert.IsNull(formation.AddSlot(new Vector2Int(2, 2)));
            Assert.IsTrue(formation.IsPositionOccupied(new Vector2Int(1, 2)));

            formation.ClearAllUnits();

            Assert.IsFalse(formation.Slots.Single().IsOccupied);
        }

        private sealed class TestBattleUnit : IBattleUnit
        {
            public TestBattleUnit(string unitId)
            {
                UnitId = unitId;
            }

            public string UnitId { get; }
            public BattleTeam Team => BattleTeam.Player;
            public GridCell CurrentCell { get; private set; }
            public bool IsAlive => true;
            public void SetCell(GridCell cell) => CurrentCell = cell;
        }
    }
}
