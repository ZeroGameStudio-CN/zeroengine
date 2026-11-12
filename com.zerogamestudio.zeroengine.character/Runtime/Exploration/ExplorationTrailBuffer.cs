using System;
using UnityEngine;

namespace ZeroEngine.Character.Exploration
{
    /// <summary>Bounded walked-path history. Callers own collision, actors, timing and relocation.</summary>
    public sealed class ExplorationTrailBuffer
    {
        private readonly Vector3[] _positions;
        private readonly double[] _distances;
        private readonly float _minimumStep;
        private readonly float _maximumStep;
        private int _first;
        public int Count { get; private set; }
        public double OldestDistance => Count == 0 ? 0d : _distances[_first];
        public double HeadDistance => Count == 0 ? 0d : _distances[(_first + Count - 1) % _positions.Length];

        public ExplorationTrailBuffer(int capacity = 512, float minimumStep = 0.06f, float maximumStep = 8f)
        {
            if (capacity < 2 || capacity > 4096 || !Finite(minimumStep) || !Finite(maximumStep)
                || minimumStep <= 0f || maximumStep < minimumStep)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _positions = new Vector3[capacity];
            _distances = new double[capacity];
            _minimumStep = minimumStep;
            _maximumStep = maximumStep;
        }

        public void Clear() { Count = 0; _first = 0; }

        public bool Reset(Vector3 position)
        {
            if (!Finite(position)) return false;
            Clear();
            return TryAppend(position);
        }

        public bool TryAppend(Vector3 position)
        {
            if (!Finite(position)) return false;
            double distance = HeadDistance;
            if (Count > 0)
            {
                float step = Vector3.Distance(_positions[(_first + Count - 1) % _positions.Length], position);
                if (!Finite(step) || step < _minimumStep || step > _maximumStep) return false;
                distance += step;
            }
            if (Count == _positions.Length) { _first = (_first + 1) % _positions.Length; Count--; }
            int index = (_first + Count++) % _positions.Length;
            _positions[index] = position;
            _distances[index] = distance;
            return true;
        }

        public bool TrySample(double distance, out Vector3 position)
        {
            position = default;
            if (Count == 0 || double.IsNaN(distance) || double.IsInfinity(distance)) return false;
            int previous = _first;
            if (distance <= OldestDistance) { position = _positions[previous]; return true; }
            for (int i = 1; i < Count; i++)
            {
                int current = (_first + i) % _positions.Length;
                if (_distances[current] >= distance)
                {
                    position = Vector3.Lerp(_positions[previous], _positions[current],
                        (float)((distance - _distances[previous]) / (_distances[current] - _distances[previous])));
                    return true;
                }
                previous = current;
            }
            position = _positions[previous];
            return true;
        }

        public bool TryFindClosestDistance(Vector3 position, out double distance) =>
            TryFindClosestDistance(position, OldestDistance, HeadDistance, out distance);

        public bool TryFindClosestDistance(Vector3 position, double minimumDistance,
            double maximumDistance, out double distance)
        {
            distance = 0d;
            if (Count == 0 || !Finite(position) || double.IsNaN(minimumDistance)
                || double.IsNaN(maximumDistance) || double.IsInfinity(minimumDistance)
                || double.IsInfinity(maximumDistance) || minimumDistance > maximumDistance
                || maximumDistance < OldestDistance || minimumDistance > HeadDistance) return false;
            double lower = Math.Max(minimumDistance, OldestDistance);
            double upper = Math.Min(maximumDistance, HeadDistance);
            if (Count == 1) { distance = OldestDistance; return true; }
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 1; i < Count; i++)
            {
                int previous = (_first + i - 1) % _positions.Length;
                int current = (_first + i) % _positions.Length;
                double segmentStart = _distances[previous];
                double segmentEnd = _distances[current];
                if (segmentEnd < lower || segmentStart > upper) continue;
                Vector3 edge = _positions[current] - _positions[previous];
                float minimum = (float)((Math.Max(lower, segmentStart) - segmentStart) / (segmentEnd - segmentStart));
                float maximum = (float)((Math.Min(upper, segmentEnd) - segmentStart) / (segmentEnd - segmentStart));
                float fraction = Mathf.Clamp(Vector3.Dot(position - _positions[previous], edge) / edge.sqrMagnitude,
                    minimum, maximum);
                float squared = (position - Vector3.Lerp(_positions[previous], _positions[current], fraction)).sqrMagnitude;
                if (squared < nearest)
                {
                    found = true;
                    nearest = squared;
                    distance = segmentStart + fraction * (segmentEnd - segmentStart);
                }
            }
            return found;
        }

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
