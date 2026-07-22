using UnityEngine;

namespace ZeroEngine.Character.Exploration
{
    public static class ExplorationDirectionResolver
    {
        private const float DirectionSectorDegrees = 45f;
        private const float HalfDirectionSectorDegrees = DirectionSectorDegrees * 0.5f;

        public static Vector2 NormalizeInput(Vector2 input, float deadZone)
        {
            if (input.magnitude <= Mathf.Max(0f, deadZone))
            {
                return Vector2.zero;
            }

            return Vector2.ClampMagnitude(input, 1f);
        }

        public static Facing8 ResolveFacing8(Vector2 input, float deadZone, Facing8 lastFacing)
        {
            if (input.magnitude <= Mathf.Max(0f, deadZone))
            {
                return lastFacing;
            }

            var clockwiseDegreesFromNorth = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
            if (clockwiseDegreesFromNorth < 0f)
            {
                clockwiseDegreesFromNorth += 360f;
            }

            var sector = Mathf.FloorToInt(
                (clockwiseDegreesFromNorth + HalfDirectionSectorDegrees) / DirectionSectorDegrees) % 8;
            return (Facing8)sector;
        }

        public static VisualFacing4 MapToFour(
            Vector2 direction,
            float tieBand,
            bool hasLastVisualFacing,
            VisualFacing4 lastVisualFacing,
            FourDirectionTieBreakAxis tieBreakAxis)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return hasLastVisualFacing ? lastVisualFacing : VisualFacing4.South;
            }

            var absX = Mathf.Abs(direction.x);
            var absY = Mathf.Abs(direction.y);
            var clampedTieBand = Mathf.Clamp01(tieBand);
            var horizontal = direction.x >= 0f ? VisualFacing4.East : VisualFacing4.West;
            var vertical = direction.y >= 0f ? VisualFacing4.North : VisualFacing4.South;

            if (absX > absY + clampedTieBand)
            {
                return horizontal;
            }

            if (absY > absX + clampedTieBand)
            {
                return vertical;
            }

            if (hasLastVisualFacing
                && (lastVisualFacing == horizontal || lastVisualFacing == vertical))
            {
                return lastVisualFacing;
            }

            return tieBreakAxis == FourDirectionTieBreakAxis.Horizontal
                ? horizontal
                : vertical;
        }

        public static Vector2 ToVector(Facing8 facing)
        {
            return facing switch
            {
                Facing8.North => Vector2.up,
                Facing8.NorthEast => new Vector2(1f, 1f).normalized,
                Facing8.East => Vector2.right,
                Facing8.SouthEast => new Vector2(1f, -1f).normalized,
                Facing8.South => Vector2.down,
                Facing8.SouthWest => new Vector2(-1f, -1f).normalized,
                Facing8.West => Vector2.left,
                Facing8.NorthWest => new Vector2(-1f, 1f).normalized,
                _ => Vector2.down
            };
        }
    }
}
