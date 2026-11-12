using System.Collections.Generic;

namespace ZeroEngine.AutoBattle
{
    public readonly struct TacticalDecisionCandidate<TActor, TPayload>
        where TActor : struct
        where TPayload : struct
    {
        public TacticalDecisionCandidate(
            TacticalGridPosition destination,
            TActor target,
            TPayload payload,
            float score,
            long stableOrder,
            bool hasAction)
        {
            Destination = destination;
            Target = target;
            Payload = payload;
            Score = score;
            StableOrder = stableOrder;
            HasAction = hasAction;
        }

        public TacticalGridPosition Destination { get; }

        public TActor Target { get; }

        public TPayload Payload { get; }

        public float Score { get; }

        public long StableOrder { get; }

        public bool HasAction { get; }
    }

    public enum TacticalDecisionStatus
    {
        Selected,
        NoLegalAction
    }

    public readonly struct TacticalDecisionResult<TActor, TPayload>
        where TActor : struct
        where TPayload : struct
    {
        public TacticalDecisionResult(
            TacticalDecisionStatus status,
            TacticalDecisionCandidate<TActor, TPayload> candidate,
            int invalidScoreCount)
        {
            Status = status;
            Candidate = candidate;
            InvalidScoreCount = invalidScoreCount;
        }

        public TacticalDecisionStatus Status { get; }

        public TacticalDecisionCandidate<TActor, TPayload> Candidate { get; }

        public int InvalidScoreCount { get; }
    }

    public interface ITacticalDecisionPolicy<TActor, TPayload>
        where TActor : struct
        where TPayload : struct
    {
        TacticalGridPosition GetPosition(in TActor actor);

        long GetStableActorOrder(in TActor actor);

        bool IsTargetValid(in TActor actor, in TActor target);

        bool TryEvaluateAction(
            in TActor actor,
            TacticalGridPosition destination,
            in TActor target,
            out TPayload payload,
            out float score);

        float ScoreMovement(
            in TActor actor,
            TacticalGridPosition destination,
            IReadOnlyList<TActor> stableTargets);
    }
}
