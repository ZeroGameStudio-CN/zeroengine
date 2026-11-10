using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Activity.Unity
{
    /// <summary>
    /// Caller-owned pure-read batch. Capture again after any callback or observer
    /// mutation; a snapshot never promises validity across gameplay phases.
    /// </summary>
    public sealed class ActivityObservationSnapshot
    {
        private readonly List<Observation> observations = new List<Observation>();
        private struct Observation
        {
            internal Vector3 Position;
            internal float Influence;
        }
        public int Count => observations.Count;
        public void Clear() => observations.Clear();
        internal void Add(Vector3 position, float influence) => observations.Add(new Observation
            { Position = position, Influence = influence });

        public bool TryGetDistance(Vector3 position, out double distance)
        {
            distance = double.PositiveInfinity;
            for (int i = observations.Count - 1; i >= 0; i--)
            {
                var observer = observations[i];
                double current = Math.Max(0, Vector3.Distance(position, observer.Position) - observer.Influence);
                if (current < distance) distance = current;
            }
            return !double.IsInfinity(distance) && !double.IsNaN(distance);
        }
    }
}
