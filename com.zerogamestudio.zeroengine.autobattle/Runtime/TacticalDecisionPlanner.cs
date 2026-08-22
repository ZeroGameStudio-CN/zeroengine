using System;
using System.Collections.Generic;

namespace ZeroEngine.AutoBattle
{
    public sealed class TacticalDecisionScratch<TActor, TPayload>
        where TActor : struct
        where TPayload : struct
    {
        internal readonly List<TActor> StableActors = new List<TActor>(16);
        internal readonly List<long> StableActorOrders = new List<long>(16);
        internal readonly List<TacticalGridPosition> Reachable = new List<TacticalGridPosition>(16);
        internal readonly TacticalGridTraversalScratch Traversal = new TacticalGridTraversalScratch();

        internal void EnsureActorCapacity(int actorCount)
        {
            if (StableActors.Capacity < actorCount)
            {
                StableActors.Capacity = actorCount;
            }

            if (StableActorOrders.Capacity < actorCount)
            {
                StableActorOrders.Capacity = actorCount;
            }
        }

        internal void EnsureReachableCapacity(int cellCount)
        {
            if (Reachable.Capacity < cellCount)
            {
                Reachable.Capacity = cellCount;
            }
        }

        internal void Clear()
        {
            StableActors.Clear();
            StableActorOrders.Clear();
            Reachable.Clear();
            Traversal.Clear();
        }
    }

    public static class TacticalDecisionPlanner
    {
        public static TacticalDecisionResult<TActor, TPayload> Decide<TActor, TPayload>(
            TacticalGrid grid,
            in TActor actor,
            IReadOnlyList<TActor> actors,
            int moveBudget,
            ITacticalDecisionPolicy<TActor, TPayload> policy,
            TacticalDecisionScratch<TActor, TPayload> scratch)
            where TActor : struct
            where TPayload : struct
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (actors == null)
            {
                throw new ArgumentNullException(nameof(actors));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (scratch == null)
            {
                throw new ArgumentNullException(nameof(scratch));
            }

            scratch.Clear();
            try
            {
                scratch.EnsureActorCapacity(actors.Count);
                for (int index = 0; index < actors.Count; index++)
                {
                    TActor current = actors[index];
                    scratch.StableActors.Add(current);
                    scratch.StableActorOrders.Add(policy.GetStableActorOrder(in current));
                }

                SortByStableOrder(scratch.StableActors, scratch.StableActorOrders);
                EnsureUniqueStableOrders(scratch.StableActorOrders);
                scratch.EnsureReachableCapacity(grid.CellCount);
                grid.CollectReachable(
                    policy.GetPosition(in actor),
                    moveBudget,
                    scratch.Reachable,
                    scratch.Traversal);

                int invalidScoreCount = 0;
                TacticalDecisionCandidate<TActor, TPayload> bestAction = default;
                bool hasAction = false;
                for (int reachableIndex = 0; reachableIndex < scratch.Reachable.Count; reachableIndex++)
                {
                    TacticalGridPosition destination = scratch.Reachable[reachableIndex];
                    for (int stableTargetIndex = 0; stableTargetIndex < scratch.StableActors.Count; stableTargetIndex++)
                    {
                        TActor target = scratch.StableActors[stableTargetIndex];
                        if (!policy.IsTargetValid(in actor, in target))
                        {
                            continue;
                        }

                        TPayload payload;
                        float score;
                        if (!policy.TryEvaluateAction(
                                in actor,
                                destination,
                                in target,
                                out payload,
                                out score))
                        {
                            continue;
                        }

                        if (!IsFinite(score))
                        {
                            invalidScoreCount = checked(invalidScoreCount + 1);
                            continue;
                        }

                        long stableOrder = checked((long)reachableIndex * scratch.StableActors.Count + stableTargetIndex);
                        TacticalDecisionCandidate<TActor, TPayload> candidate = new TacticalDecisionCandidate<TActor, TPayload>(
                            destination,
                            target,
                            payload,
                            score,
                            stableOrder,
                            true);
                        if (!hasAction || IsBetter(candidate, bestAction))
                        {
                            bestAction = candidate;
                            hasAction = true;
                        }
                    }
                }

                if (hasAction)
                {
                    return new TacticalDecisionResult<TActor, TPayload>(
                        TacticalDecisionStatus.Selected,
                        bestAction,
                        invalidScoreCount);
                }

                TacticalDecisionCandidate<TActor, TPayload> bestMovement = default;
                bool hasMovement = false;
                for (int reachableIndex = 0; reachableIndex < scratch.Reachable.Count; reachableIndex++)
                {
                    TacticalGridPosition destination = scratch.Reachable[reachableIndex];
                    float score = policy.ScoreMovement(in actor, destination, scratch.StableActors);
                    if (!IsFinite(score))
                    {
                        invalidScoreCount = checked(invalidScoreCount + 1);
                        continue;
                    }

                    TacticalDecisionCandidate<TActor, TPayload> candidate = new TacticalDecisionCandidate<TActor, TPayload>(
                        destination,
                        default,
                        default,
                        score,
                        reachableIndex,
                        false);
                    if (!hasMovement || IsBetter(candidate, bestMovement))
                    {
                        bestMovement = candidate;
                        hasMovement = true;
                    }
                }

                return new TacticalDecisionResult<TActor, TPayload>(
                    hasMovement ? TacticalDecisionStatus.Selected : TacticalDecisionStatus.NoLegalAction,
                    hasMovement ? bestMovement : default,
                    invalidScoreCount);
            }
            finally
            {
                scratch.Clear();
            }
        }

        private static void SortByStableOrder<TActor>(List<TActor> actors, List<long> stableOrders)
            where TActor : struct
        {
            for (int index = 1; index < actors.Count; index++)
            {
                TActor actor = actors[index];
                long stableOrder = stableOrders[index];
                int insertionIndex = index - 1;
                while (insertionIndex >= 0 && stableOrders[insertionIndex] > stableOrder)
                {
                    actors[insertionIndex + 1] = actors[insertionIndex];
                    stableOrders[insertionIndex + 1] = stableOrders[insertionIndex];
                    insertionIndex--;
                }

                actors[insertionIndex + 1] = actor;
                stableOrders[insertionIndex + 1] = stableOrder;
            }
        }

        private static void EnsureUniqueStableOrders(List<long> stableOrders)
        {
            for (int index = 1; index < stableOrders.Count; index++)
            {
                if (stableOrders[index] == stableOrders[index - 1])
                {
                    throw new ArgumentException("Stable actor order values must be unique.", nameof(stableOrders));
                }
            }
        }

        private static bool IsBetter<TActor, TPayload>(
            TacticalDecisionCandidate<TActor, TPayload> candidate,
            TacticalDecisionCandidate<TActor, TPayload> current)
            where TActor : struct
            where TPayload : struct
        {
            return candidate.Score > current.Score ||
                (candidate.Score == current.Score && candidate.StableOrder < current.StableOrder);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
