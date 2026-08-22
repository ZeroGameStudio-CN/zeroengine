using System;
using System.Collections.Generic;

namespace ZeroEngine.AutoBattle
{
    public sealed class TacticalGridTraversalScratch
    {
        internal TacticalGridPosition[] Queue { get; private set; }
        internal int[] QueueDistances { get; private set; }
        internal bool[] Visited { get; private set; }
        internal int QueueCount { get; set; }

        internal void EnsureCapacity(int cellCount)
        {
            if (Queue != null && Queue.Length >= cellCount)
            {
                return;
            }

            Queue = new TacticalGridPosition[cellCount];
            QueueDistances = new int[cellCount];
            Visited = new bool[cellCount];
        }

        internal void Clear()
        {
            QueueCount = 0;
            if (Visited != null)
            {
                Array.Clear(Visited, 0, Visited.Length);
            }
        }
    }

    public sealed class TacticalGrid
    {
        private readonly bool[] _blocked;
        private readonly bool[] _occupied;

        public TacticalGrid(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Grid width must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Grid height must be positive.");
            }

            int cellCount;
            try
            {
                cellCount = checked(width * height);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions are too large.");
            }

            Width = width;
            Height = height;
            _blocked = new bool[cellCount];
            _occupied = new bool[cellCount];
        }

        public int Width { get; }

        public int Height { get; }

        internal int CellCount => _blocked.Length;

        public void SetBlocked(TacticalGridPosition position, bool blocked)
        {
            if (!IsInside(position))
            {
                return;
            }

            _blocked[GetIndex(position)] = blocked;
        }

        public void SetOccupied(TacticalGridPosition position, bool occupied)
        {
            if (!IsInside(position))
            {
                return;
            }

            _occupied[GetIndex(position)] = occupied;
        }

        public bool IsInside(TacticalGridPosition position)
        {
            return position.X >= 0 && position.Y >= 0 && position.X < Width && position.Y < Height;
        }

        public bool IsWalkable(TacticalGridPosition position)
        {
            return IsInside(position) && !_blocked[GetIndex(position)] && !_occupied[GetIndex(position)];
        }

        public void CollectReachable(
            TacticalGridPosition origin,
            int moveBudget,
            List<TacticalGridPosition> result,
            TacticalGridTraversalScratch scratch)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (scratch == null)
            {
                throw new ArgumentNullException(nameof(scratch));
            }

            if (!IsInside(origin))
            {
                throw new ArgumentOutOfRangeException(nameof(origin), "Origin must be inside the grid.");
            }

            result.Clear();
            scratch.EnsureCapacity(CellCount);
            scratch.Clear();

            try
            {
                int originIndex = GetIndex(origin);
                scratch.Visited[originIndex] = true;
                scratch.Queue[0] = origin;
                scratch.QueueDistances[0] = 0;
                scratch.QueueCount = 1;
                result.Add(origin);

                int queueIndex = 0;
                while (queueIndex < scratch.QueueCount)
                {
                    TacticalGridPosition current = scratch.Queue[queueIndex];
                    int distance = scratch.QueueDistances[queueIndex];
                    queueIndex++;
                    if (distance >= moveBudget)
                    {
                        continue;
                    }

                    TryVisit(current.X + 1, current.Y, distance + 1, result, scratch);
                    TryVisit(current.X - 1, current.Y, distance + 1, result, scratch);
                    TryVisit(current.X, current.Y + 1, distance + 1, result, scratch);
                    TryVisit(current.X, current.Y - 1, distance + 1, result, scratch);
                }
            }
            finally
            {
                scratch.Clear();
            }
        }

        public void CollectAttackRange(
            TacticalGridPosition origin,
            int range,
            List<TacticalGridPosition> result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            result.Clear();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    TacticalGridPosition position = new TacticalGridPosition(x, y);
                    if (!position.Equals(origin) && origin.ManhattanDistanceTo(position) <= range)
                    {
                        result.Add(position);
                    }
                }
            }
        }

        private void TryVisit(
            int x,
            int y,
            int distance,
            List<TacticalGridPosition> result,
            TacticalGridTraversalScratch scratch)
        {
            TacticalGridPosition position = new TacticalGridPosition(x, y);
            if (!IsInside(position))
            {
                return;
            }

            int index = GetIndex(position);
            if (scratch.Visited[index])
            {
                return;
            }

            scratch.Visited[index] = true;
            if (!IsWalkable(position))
            {
                return;
            }

            scratch.Queue[scratch.QueueCount] = position;
            scratch.QueueDistances[scratch.QueueCount] = distance;
            scratch.QueueCount++;
            result.Add(position);
        }

        private int GetIndex(TacticalGridPosition position)
        {
            return position.Y * Width + position.X;
        }
    }
}
