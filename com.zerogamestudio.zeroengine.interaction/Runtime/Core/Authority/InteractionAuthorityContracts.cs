using System;

namespace ZeroEngine.Interaction.Core.Authority
{
    public readonly struct InteractionTargetIdentity : IEquatable<InteractionTargetIdentity>
    {
        public string Id { get; }
        public long Activation { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Id) && Id.Length <= 512 && Activation > 0;
        public InteractionTargetIdentity(string id, long activation) { Id = id; Activation = activation; }
        public bool Equals(InteractionTargetIdentity other) => Activation == other.Activation && StringComparer.Ordinal.Equals(Id, other.Id);
        public override bool Equals(object value) => value is InteractionTargetIdentity other && Equals(other);
        public override int GetHashCode() => unchecked((Id == null ? 0 : StringComparer.Ordinal.GetHashCode(Id)) * 397 ^ Activation.GetHashCode());
    }

    public enum InteractionCommandKind : byte { Press = 1, Release = 2, Cancel = 3, Confirm = 4 }
    public enum InteractionCommandRejection : byte
    {
        None, InvalidCommand, WrongEpoch, ActorEnded, WrongActorActivation, StaleSequence,
        ConflictingReplay, Busy, TargetEnded, UnknownAction, WrongTarget, UnsupportedConfirmation, HandlerFault, UnauthorizedActor
    }

    /// <summary>Data only. Actor ownership comes from the receiving connection's binding, never this payload.</summary>
    public readonly struct InteractionCommand : IEquatable<InteractionCommand>
    {
        public uint Epoch { get; }
        public long ActorActivation { get; }
        public long Sequence { get; }
        public long ActionId { get; }
        public InteractionTargetIdentity Target { get; }
        public InteractionCommandKind Kind { get; }
        public InteractionCommand(uint epoch, long actorActivation, long sequence, long actionId,
            InteractionTargetIdentity target, InteractionCommandKind kind)
        { Epoch = epoch; ActorActivation = actorActivation; Sequence = sequence; ActionId = actionId; Target = target; Kind = kind; }
        public bool Equals(InteractionCommand other) => Epoch == other.Epoch && ActorActivation == other.ActorActivation &&
            Sequence == other.Sequence && ActionId == other.ActionId && Target.Equals(other.Target) && Kind == other.Kind;
        public override bool Equals(object value) => value is InteractionCommand other && Equals(other);
        public override int GetHashCode() => Sequence.GetHashCode();
    }

    /// <summary>Authority outcome has no Unity object, delegate or client-side mutation.</summary>
    public readonly struct InteractionAuthorityOutcome
    {
        public uint Epoch { get; }
        public string ActorId { get; }
        public long ActorActivation { get; }
        public long ActionId { get; }
        public long Revision { get; }
        public long ExecutionId { get; }
        public InteractionTargetIdentity Target { get; }
        public InteractionGesture Gesture { get; }
        public InteractionStatus Status { get; }
        public string Reason { get; }
        public InteractionAuthorityOutcome(uint epoch, string actorId, long actorActivation, long actionId,
            long revision, long executionId, InteractionTargetIdentity target, InteractionGesture gesture,
            InteractionStatus status, string reason)
        {
            Epoch = epoch; ActorId = actorId; ActorActivation = actorActivation; ActionId = actionId;
            Revision = revision; ExecutionId = executionId; Target = target; Gesture = gesture; Status = status; Reason = reason;
        }
    }

    /// <summary>Handled/InputAdmitted describe command admission, not purchase or reward success.</summary>
    public readonly struct InteractionCommandReceipt
    {
        public uint Epoch { get; }
        public string ActorId { get; }
        public long ActorActivation { get; }
        public long Sequence { get; }
        public long ActionId { get; }
        public bool Handled { get; }
        public bool InputAdmitted { get; }
        public bool IsReplay { get; }
        public bool IsHeld { get; }
        public bool IsPending { get; }
        public InteractionCommandRejection Rejection { get; }
        public InteractionAuthorityOutcome? Outcome { get; }
        public InteractionCommandReceipt(uint epoch, string actorId, long actorActivation, long sequence, long actionId,
            bool handled, bool inputAdmitted, bool replay, bool held, bool pending,
            InteractionCommandRejection rejection, InteractionAuthorityOutcome? outcome)
        {
            Epoch = epoch; ActorId = actorId; ActorActivation = actorActivation; Sequence = sequence; ActionId = actionId;
            Handled = handled; InputAdmitted = inputAdmitted; IsReplay = replay; IsHeld = held; IsPending = pending;
            Rejection = rejection; Outcome = outcome;
        }
    }
}
