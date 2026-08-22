using System;

namespace ZeroEngine.AutoBattle
{
    public readonly struct TacticalGridPosition : IEquatable<TacticalGridPosition>
    {
        public TacticalGridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        public int ManhattanDistanceTo(TacticalGridPosition other)
        {
            long deltaX = (long)X - other.X;
            long deltaY = (long)Y - other.Y;
            if (deltaX < 0)
            {
                deltaX = -deltaX;
            }

            if (deltaY < 0)
            {
                deltaY = -deltaY;
            }

            return checked((int)(deltaX + deltaY));
        }

        public bool Equals(TacticalGridPosition other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object other)
        {
            return other is TacticalGridPosition position && Equals(position);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public override string ToString()
        {
            return $"({X},{Y})";
        }
    }
}
