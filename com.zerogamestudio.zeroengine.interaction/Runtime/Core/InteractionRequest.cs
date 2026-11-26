using System;
using System.Collections.Generic;
using System.Threading;

namespace ZeroEngine.Interaction.Core
{
    /// <summary>
    /// Main-thread completion handle. Retain it for delayed confirmation. Commit rechecks current
    /// activation, focus, spatial admission and conditions before invoking the business mutation once.
    /// </summary>
    public sealed class InteractionRequest : IInteractionExecution
    {
        private readonly InteractionAgent owner;
        private CancellationTokenSource lifetime;
        internal Action<InteractionResult> RecordResult { private get; set; }
        internal InteractionRegistration Registration { get; }
        public InteractionContext Context { get; }
        public long Id { get; }
        public bool IsSettled { get; private set; }
        public bool IsCurrent => !IsSettled && owner.IsCurrent(this);
        public event Action<string> Cancelled;
        public event Action<InteractionResult> Settled;
        public InteractionResult? Outcome { get; private set; }
        public Exception NotificationFailure { get; private set; }
        /// <summary>Cancelled after every terminal outcome, including success. Allocation is lazy; capture this token for async work.</summary>
        public CancellationToken LifetimeToken
        {
            get
            {
                if (Outcome.HasValue) return new CancellationToken(true);
                if (lifetime == null) lifetime = new CancellationTokenSource();
                return lifetime.Token;
            }
        }

        internal InteractionRequest(InteractionAgent owner, InteractionRegistration registration, InteractionContext context, long id)
        { this.owner = owner; Registration = registration; Context = context; Id = id; }

        public bool TryCommit(Action mutation = null)
        {
            if (IsSettled) return false;
            return owner.Commit(this, mutation);
        }

        /// <summary>For providers with an explicit business result. Kept separately named so existing Action lambdas retain their binding and semantics.</summary>
        public bool TryCommitResult(Func<InteractionCheck> mutation)
        {
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));
            if (IsSettled) return false;
            return owner.CommitResult(this, mutation);
        }

        public bool TryDeny(string reason) => owner.Settle(this, InteractionStatus.Denied, reason);
        public bool TryCancel(string reason = "Cancelled by caller.") => owner.Settle(this, InteractionStatus.Cancelled, reason);
        public bool TryFault(Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            return owner.Settle(this, InteractionStatus.Faulted, error.Message);
        }

        internal void PublishResult(InteractionResult result, Action<InteractionResult> completed)
        {
            // The agent can be disposed during a business callback. The request still owns its result.
            var record = RecordResult;
            RecordResult = null;
            try
            {
                record?.Invoke(result);
                completed?.Invoke(result);
            }
            finally { NotifySettled(result); }
        }

        private void NotifySettled(InteractionResult result)
        {
            if (Outcome.HasValue) return;
            Outcome = result;
            var callbacks = Settled; Settled = null;
            var previous = lifetime; lifetime = null;
            List<Exception> failures = null;
            try { previous?.Cancel(); }
            catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
            finally { previous?.Dispose(); }
            if (callbacks != null)
                foreach (Action<InteractionResult> callback in callbacks.GetInvocationList())
                    try { callback(result); }
                    catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
            if (failures != null) NotificationFailure = new AggregateException("Interaction terminal notification failed.", failures);
        }

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
