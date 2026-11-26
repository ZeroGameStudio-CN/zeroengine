using System;

namespace ZeroEngine.Interaction.Core.Authority
{
    /// <summary>Receipt projection only. It never executes rewards, costs or other gameplay mutations.</summary>
    public sealed class InteractionOutcomeMirror
    {
        public uint Epoch { get; }
        public string ActorId { get; }
        public long ActorActivation { get; }
        public long LastReceiptSequence { get; private set; }
        public InteractionCommandReceipt? LatestReceipt { get; private set; }
        public InteractionAuthorityOutcome? LatestOutcome { get; private set; }
        public InteractionOutcomeMirror(uint epoch, string actorId, long actorActivation)
        {
            if (epoch == 0 || actorActivation <= 0 || string.IsNullOrWhiteSpace(actorId)) throw new ArgumentException("Bind an authenticated authority session.");
            Epoch = epoch; ActorId = actorId; ActorActivation = actorActivation;
        }
        public bool TryApply(InteractionAuthorityOutcome outcome)
            => MatchesOutcome(outcome) && ApplyOutcome(outcome);

        private bool ApplyOutcome(InteractionAuthorityOutcome outcome)
        {
            if (LatestOutcome.HasValue && outcome.Revision <= LatestOutcome.Value.Revision) return false;
            LatestOutcome = outcome; return true;
        }

        private bool MatchesOutcome(InteractionAuthorityOutcome outcome) =>
            outcome.Epoch == Epoch && outcome.ActorActivation == ActorActivation && outcome.ActorId == ActorId &&
            outcome.Revision > 0 && outcome.ActionId > 0 && outcome.ExecutionId > 0 && outcome.Target.IsValid &&
            Enum.IsDefined(typeof(InteractionStatus), outcome.Status) && Enum.IsDefined(typeof(InteractionGesture), outcome.Gesture);

        public bool TryApply(InteractionCommandReceipt receipt)
        {
            if (receipt.Epoch != Epoch || receipt.ActorActivation != ActorActivation || receipt.ActorId != ActorId ||
                receipt.Sequence <= 0 || receipt.ActionId <= 0 || receipt.Rejection != InteractionCommandRejection.None ||
                receipt.Outcome.HasValue && (receipt.Outcome.Value.ActionId != receipt.ActionId || !MatchesOutcome(receipt.Outcome.Value))) return false;
            // An authoritative success is valid even after local focus/input cancellation or an older request's late reply.
            bool changed = receipt.Outcome.HasValue && ApplyOutcome(receipt.Outcome.Value);
            if (receipt.Sequence > LastReceiptSequence)
            { LastReceiptSequence = receipt.Sequence; LatestReceipt = receipt; changed = true; }
            return changed;
        }
    }
}
