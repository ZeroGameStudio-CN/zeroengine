using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZeroEngine.Activity
{
    /// <summary>
    /// Synchronous work only: preload asynchronously, then enqueue the actual creation here.
    /// Budgets stop the next work item; they cannot preempt one expensive callback.
    /// One queue is shared by the producers in one world. No global static state.
    /// </summary>
    public sealed class BudgetedWorkQueue : IDisposable
    {
        private sealed class Work
        {
            public string Key;
            public Action Execute;
            public Func<bool> Eligible;
            public Action<Exception> Failed;
            public int Priority;
            public long Sequence;
            public long EligibleWaits;
        }

        private readonly Dictionary<string, Work> pending = new Dictionary<string, Work>(StringComparer.Ordinal);
        private readonly List<Work> snapshot = new List<Work>();
        private readonly Func<double> milliseconds;
        private readonly Func<bool> ownerAvailable;
        private long sequence;
        private int generation;
        private bool pumping;
        private bool disposed;
        private bool accepting;
        public bool IsAccepting => accepting && !disposed && (ownerAvailable == null || ownerAvailable());
        public int Count => pending.Count;

        public BudgetedWorkQueue(Func<double> milliseconds = null, bool accepting = true, Func<bool> ownerAvailable = null)
        {
            this.milliseconds = milliseconds ?? (() => Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));
            this.accepting = accepting;
            this.ownerAvailable = ownerAvailable;
        }

        public bool Enqueue(string key, Action execute, Func<bool> eligible = null, int priority = 0,
            Action<Exception> failed = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A stable key is required.", nameof(key));
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            if (priority < 0 || priority > 3) throw new ArgumentOutOfRangeException(nameof(priority));
            if (!IsAccepting || disposed) return false;
            if (pending.ContainsKey(key)) return false;
            pending.Add(key, new Work { Key = key, Execute = execute, Eligible = eligible, Failed = failed,
                Priority = priority, Sequence = sequence++ });
            return true;
        }

        public bool Cancel(string key) => key != null && pending.Remove(key);

        public void Clear()
        {
            generation++;
            pending.Clear();
        }

        public void Suspend() { accepting = false; Clear(); }
        public void Resume()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BudgetedWorkQueue));
            accepting = true;
        }
        public void Dispose() { disposed = true; Suspend(); }

        /// <returns>Number of attempted callbacks, including failures.</returns>
        public int Pump(int maxWorkItems, double budgetMilliseconds)
        {
            if (maxWorkItems <= 0 || !ActivityPolicy.Finite(budgetMilliseconds) || budgetMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxWorkItems));
            if (pumping) throw new InvalidOperationException("A work queue cannot pump recursively.");
            if (!IsAccepting || disposed) return 0;
            pumping = true;
            int currentGeneration = generation;
            long sequenceLimit = sequence; // Work enqueued by callbacks waits until the next pump.
            int executed = 0;
            try
            {
                double start = milliseconds();
                snapshot.Clear();
                snapshot.AddRange(pending.Values);
                while (generation == currentGeneration && IsAccepting && executed < maxWorkItems)
                {
                    double elapsed = milliseconds() - start;
                    if (executed > 0 && (!ActivityPolicy.Finite(elapsed) || elapsed < 0 || elapsed >= budgetMilliseconds)) break;
                    Work selected = null;
                    long bestScore = long.MinValue;
                    foreach (Work work in snapshot)
                    {
                        if (work.Sequence >= sequenceLimit || !pending.TryGetValue(work.Key, out Work current)
                            || !ReferenceEquals(current, work)) continue;
                        bool eligible;
                        try { eligible = work.Eligible == null || work.Eligible(); }
                        catch (Exception exception)
                        {
                            if (pending.TryGetValue(work.Key, out Work failedCurrent) && ReferenceEquals(failedCurrent, work))
                                pending.Remove(work.Key);
                            executed++;
                            if (work.Failed == null) throw;
                            work.Failed(exception);
                            if (executed >= maxWorkItems || generation != currentGeneration) break;
                            continue;
                        }
                        if (!eligible) continue;
                        // Bounded base priorities plus ageing prevent a stream of nearby work starving older work.
                        long score = work.Priority + work.EligibleWaits / 4;
                        work.EligibleWaits++;
                        if (selected == null || score > bestScore || (score == bestScore && work.Sequence < selected.Sequence))
                        {
                            selected = work;
                            bestScore = score;
                        }
                    }
                    if (selected == null || generation != currentGeneration || executed >= maxWorkItems) break;
                    if (!pending.TryGetValue(selected.Key, out Work stillPending) || !ReferenceEquals(stillPending, selected)) continue;
                    pending.Remove(selected.Key);
                    executed++;
                    try { selected.Execute(); }
                    catch (Exception exception)
                    {
                        if (selected.Failed == null) throw;
                        selected.Failed(exception);
                    }
                }
                return executed;
            }
            finally { snapshot.Clear(); pumping = false; }
        }
    }
}
