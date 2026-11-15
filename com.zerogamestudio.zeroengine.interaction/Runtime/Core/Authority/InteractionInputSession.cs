using System;

namespace ZeroEngine.Interaction.Core.Authority
{
    /// <summary>Creates ordered intent edges for either an in-process receiver or an external transport. No business execution.</summary>
    public sealed class InteractionInputSession
    {
        private long sequence, action;
        private InteractionTargetIdentity target;
        public uint Epoch { get; }
        public long ActorActivation { get; }
        public bool IsHeld { get; private set; }
        public long ActionId => action;
        public InteractionTargetIdentity Target => target;
        public InteractionInputSession(uint epoch, long actorActivation)
        {
            if (epoch == 0 || actorActivation <= 0) throw new ArgumentException("Bind an authority-issued actor session.");
            Epoch = epoch; ActorActivation = actorActivation;
        }
        public bool TryPress(InteractionTargetIdentity selected, out InteractionCommand command)
        {
            if (IsHeld || !selected.IsValid) { command = default; return false; }
            target = selected; action = checked(sequence + 1); IsHeld = true;
            command = Next(InteractionCommandKind.Press); return true;
        }
        public bool TryRelease(out InteractionCommand command)
        {
            if (!IsHeld) { command = default; return false; }
            IsHeld = false; command = Next(InteractionCommandKind.Release); return true;
        }
        public bool TryCancel(out InteractionCommand command)
        {
            if (action == 0) { command = default; return false; }
            IsHeld = false; command = Next(InteractionCommandKind.Cancel); return true;
        }
        public bool TryConfirm(out InteractionCommand command)
        {
            if (action == 0) { command = default; return false; }
            command = Next(InteractionCommandKind.Confirm); return true;
        }
        private InteractionCommand Next(InteractionCommandKind kind) =>
            new InteractionCommand(Epoch, ActorActivation, checked(++sequence), action, target, kind);
    }
}
