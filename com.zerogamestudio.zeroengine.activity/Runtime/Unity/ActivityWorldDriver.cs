using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Activity.Unity
{
    /// <summary>Explicitly created per-world bridge. It owns no gameplay objects or gameplay rules.</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class ActivityWorldDriver : MonoBehaviour
    {
        private readonly List<ActivityAgent> agents = new List<ActivityAgent>();
        private readonly List<ActivityObserver> observers = new List<ActivityObserver>();
        private readonly List<ActivityAgent> snapshot = new List<ActivityAgent>();
        private readonly List<ActivityAgent> stoppingSnapshot = new List<ActivityAgent>();
        private bool ticking;
        private int generation;
        private BudgetedWorkQueue work;
        public BudgetedWorkQueue Work => work ?? (work = new BudgetedWorkQueue(
            ownerAvailable: () => this && isActiveAndEnabled));
        [Min(1)] public int MaxCreationsPerFrame = 4;
        [Min(0.01f)] public float CreationBudgetMilliseconds = 2;

        public void Register(ActivityAgent agent) { if (agent && !agents.Contains(agent)) agents.Add(agent); }
        public void Unregister(ActivityAgent agent) => agents.Remove(agent);
        public void Register(ActivityObserver observer) { if (observer && !observers.Contains(observer)) observers.Add(observer); }
        public void Unregister(ActivityObserver observer) => observers.Remove(observer);

        public bool TryGetDistance(Vector3 position, out double distance)
        {
            distance = double.PositiveInfinity;
            for (int i = observers.Count - 1; i >= 0; i--)
            {
                ActivityObserver observer = observers[i];
                if (!observer) { observers.RemoveAt(i); continue; }
                if (!observer.isActiveAndEnabled) continue;
                double current = Math.Max(0, Vector3.Distance(position, observer.transform.position) - observer.InfluenceRadius);
                if (current < distance) distance = current;
            }
            return !double.IsInfinity(distance) && !double.IsNaN(distance);
        }

        private void Update() => Tick();

        /// <summary>Also supports an explicitly owned external update loop.</summary>
        public int Tick()
        {
            if (!isActiveAndEnabled || ticking) return 0;
            ticking = true;
            int currentGeneration = generation;
            try
            {
                snapshot.Clear();
                snapshot.AddRange(agents);
                foreach (ActivityAgent agent in snapshot)
                {
                    if (generation != currentGeneration || !isActiveAndEnabled) return 0;
                    if (!agent || !agent.isActiveAndEnabled) continue;
                    agent.Evaluate(TryGetDistance(agent.transform.position, out double distance) ? distance : double.NaN);
                }
                if (generation != currentGeneration || !isActiveAndEnabled) return 0;
                double budget = float.IsNaN(CreationBudgetMilliseconds) || float.IsInfinity(CreationBudgetMilliseconds)
                    ? 2 : Math.Max(0.01, CreationBudgetMilliseconds);
                return Work.Pump(Math.Max(1, MaxCreationsPerFrame), budget);
            }
            finally { snapshot.Clear(); ticking = false; }
        }

        private void OnEnable() { generation++; Work.Resume(); }

        private void OnDisable()
        {
            generation++;
            Work.Suspend();
            stoppingSnapshot.Clear();
            stoppingSnapshot.AddRange(agents);
            foreach (ActivityAgent agent in stoppingSnapshot) if (agent) agent.Wake();
            stoppingSnapshot.Clear();
        }

        private void OnDestroy()
        {
            Work.Dispose();
            agents.Clear();
            observers.Clear();
        }
    }
}
