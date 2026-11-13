using System;

namespace ZeroEngine.Interaction.Core
{
    /// <summary>
    /// Main-thread completion handle. Retain it for delayed confirmation. Commit rechecks current
    /// activation, focus, spatial admission and conditions before invoking the business mutation once.
    /// </summary>
    public sealed class InteractionRequest
    {
        private readonly InteractionAgent owner;
        internal InteractionRegistration Registration { get; }
        public InteractionContext Context { get; }
        public long Id { get; }
        public bool IsSettled { get; private set; }
        public bool IsCurrent => !IsSettled && owner.IsCurrent(this);
        public event Action<string> Cancelled;

        internal InteractionRequest(InteractionAgent owner, InteractionRegistration registration, InteractionContext context, long id)
        { this.owner = owner; Registration = registration; Context = context; Id = id; }

        public bool TryCommit(Action mutation = null)
        {
            if (IsSettled) return false;
            return owner.Commit(this, mutation);
        }

        public bool TryDeny(string reason) => owner.Settle(this, InteractionStatus.Denied, reason);
        public bool TryCancel(string reason = "Cancelled by caller.") => owner.Settle(this, InteractionStatus.Cancelled, reason);

        internal void MarkSettled() { IsSettled = true; }
        internal void NotifyCancelled(string reason)
        {
            var cancelled = Cancelled;
            Cancelled = null;
            cancelled?.Invoke(reason);
        }
        internal void ClearCallbacks() { Cancelled = null; }
    }
}
