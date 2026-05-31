using UnityEngine;

namespace ZeroEngine.Combat
{
    public static class PositionalAttackRule
    {
        public static PositionalAttackKind Classify(
            Vector2Int attackerCell,
            Vector2Int targetCell,
            GridDirection targetFacing)
        {
            var delta = attackerCell - targetCell;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
            {
                return PositionalAttackKind.Front;
            }

            var facing = ToVector(targetFacing);
            if (delta == facing)
            {
                return PositionalAttackKind.Front;
            }

            if (delta == -facing)
            {
                return PositionalAttackKind.Back;
            }

            return PositionalAttackKind.Side;
        }

        public static Vector2Int ToVector(GridDirection direction)
        {
            return direction switch
            {
                GridDirection.North => Vector2Int.up,
                GridDirection.East => Vector2Int.right,
                GridDirection.South => Vector2Int.down,
                GridDirection.West => Vector2Int.left,
                _ => Vector2Int.up
            };
        }
    }
}
