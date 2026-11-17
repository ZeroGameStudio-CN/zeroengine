using System;
using System.Collections.Generic;

namespace ZeroEngine.Interaction.Core.Authority
{
    /// <summary>Server/local owner binds this receiver to an actor. Never select it from an untrusted payload's actor ID.</summary>
    public sealed class InteractionAuthorityActor : IDisposable
    {
        private sealed class DirectedRange : IInteractionRange
        {
            private readonly IInteractionRange spatial;
            internal InteractionAuthorityTarget Selected;
            internal DirectedRange(IInteractionRange spatial) { this.spatial = spatial ?? throw new ArgumentNullException(nameof(spatial)); }
            public bool TryGetSquaredDistance(IInteractionTarget target, out double distance)
            {
                distance = 0;
                return Selected != null && Selected.IsCurrent && ReferenceEquals(Selected.Registration.Target, target)
                    && spatial.TryGetSquaredDistance(target, out distance);
            }
        }
        private sealed class ActionRecord
        {
            internal InteractionTargetIdentity Target;
            internal InteractionAuthorityOutcome? Outcome;
        }
        private readonly struct CachedCommand
        {
            internal readonly InteractionCommand Command;
            internal readonly InteractionCommandReceipt Receipt;
            internal CachedCommand(InteractionCommand command, InteractionCommandReceipt receipt) { Command = command; Receipt = receipt; }
        }
        private readonly InteractionAuthority owner;
        private readonly InteractionAgent agent;
        private readonly DirectedRange range;
        private readonly int historyLimit;
        private readonly Dictionary<long, CachedCommand> commands = new Dictionary<long, CachedCommand>();
        private readonly Queue<long> commandOrder = new Queue<long>();
        private readonly Dictionary<long, ActionRecord> actions = new Dictionary<long, ActionRecord>();
        private readonly Queue<long> actionOrder = new Queue<long>();
        private readonly Queue<InteractionAuthorityOutcome> notifications = new Queue<InteractionAuthorityOutcome>();
        private readonly Dictionary<long, long> requestActions = new Dictionary<long, long>();
        private long sequence, heldAction, pendingAction, invocationAction, revision;
        private bool disposed, dispatching;
        public string ActorId { get; }
        public long Activation { get; }
        public uint Epoch => owner.Epoch;
        public bool IsCurrent => !disposed && owner.IsCurrent(this);
        public double HoldProgress => agent.Progress;
        public long DroppedNotifications { get; private set; }
        public long LastSequence => sequence;

        internal InteractionAuthorityActor(InteractionAuthority owner, InteractionWorld world, string id, long activation,
            object subject, IInteractionClock clock, IInteractionRange spatial, double maxDistance, int historyLimit)
        {
            this.owner = owner; ActorId = id; Activation = activation; this.historyLimit = historyLimit;
            range = new DirectedRange(spatial);
            agent = new InteractionAgent(world, id, subject, clock, range, maxDistance);
            agent.Completed += Completed;
            agent.RequestCreated += RequestCreated;
        }

        public InteractionCommandReceipt Receive(InteractionCommand command)
        {
            if (!IsCurrent) return Receipt(command, false, false, InteractionCommandRejection.ActorEnded);
            if (command.Epoch != Epoch) return Receipt(command, false, false, InteractionCommandRejection.WrongEpoch);
            if (command.ActorActivation != Activation) return Receipt(command, false, false, InteractionCommandRejection.WrongActorActivation);
            if (command.Sequence <= 0 || command.ActionId <= 0 || !command.Target.IsValid ||
                !Enum.IsDefined(typeof(InteractionCommandKind), command.Kind) ||
                command.Kind == InteractionCommandKind.Press && command.ActionId != command.Sequence)
                return Receipt(command, false, false, InteractionCommandRejection.InvalidCommand);
            if (dispatching) return Receipt(command, false, false, InteractionCommandRejection.Busy);
            if (commands.TryGetValue(command.Sequence, out var cached))
            {
                if (!cached.Command.Equals(command)) return Receipt(command, false, false, InteractionCommandRejection.ConflictingReplay);
                return Receipt(command, cached.Receipt.Handled, cached.Receipt.InputAdmitted, cached.Receipt.Rejection, true);
            }
            if (command.Sequence <= sequence) return Receipt(command, false, false, InteractionCommandRejection.StaleSequence);
            sequence = command.Sequence;
            dispatching = true;
            InteractionCommandReceipt receipt;
            try { receipt = Dispatch(command); }
            catch (Exception error)
            {
                var pending = agent.Pending;
                if (pending != null && !pending.IsSettled) agent.Settle(pending, InteractionStatus.Faulted, error.Message);
                receipt = Receipt(command, false, false, InteractionCommandRejection.HandlerFault);
            }
            finally { invocationAction = 0; dispatching = false; }
            commands.Add(command.Sequence, new CachedCommand(command, receipt)); commandOrder.Enqueue(command.Sequence);
            while (commandOrder.Count > historyLimit) commands.Remove(commandOrder.Dequeue());
            TrimActions();
            return receipt;
        }

        private InteractionCommandReceipt Dispatch(InteractionCommand command)
        {
            if (command.Kind == InteractionCommandKind.Press)
            {
                if (heldAction != 0 || agent.Pending != null) return Receipt(command, false, false, InteractionCommandRejection.Busy);
                if (!owner.TryGetTarget(command.Target, out var target)) return Receipt(command, false, false, InteractionCommandRejection.TargetEnded);
                actions.Add(command.ActionId, new ActionRecord { Target = command.Target }); actionOrder.Enqueue(command.ActionId);
                range.Selected = target; heldAction = invocationAction = command.ActionId;
                bool admitted = agent.Press();
                pendingAction = agent.Pending == null ? 0 : command.ActionId;
                return Receipt(command, true, admitted, InteractionCommandRejection.None);
            }
            if (!actions.TryGetValue(command.ActionId, out var action)) return Receipt(command, false, false, InteractionCommandRejection.UnknownAction);
            if (!command.Target.Equals(action.Target)) return Receipt(command, false, false, InteractionCommandRejection.WrongTarget);
            invocationAction = command.ActionId;
            switch (command.Kind)
            {
                case InteractionCommandKind.Release:
                    if (heldAction != command.ActionId) return Receipt(command, false, false, InteractionCommandRejection.UnknownAction);
                    agent.Release(); heldAction = 0;
                    pendingAction = agent.Pending == null ? 0 : command.ActionId;
                    return Receipt(command, true, true, InteractionCommandRejection.None);
                case InteractionCommandKind.Cancel:
                    if (heldAction == command.ActionId || pendingAction == command.ActionId)
                    { agent.Cancel(); heldAction = pendingAction = 0; range.Selected = null; }
                    // A settled success is never reversed by a late cancellation.
                    return Receipt(command, true, true, InteractionCommandRejection.None);
                case InteractionCommandKind.Confirm:
                    if (pendingAction != command.ActionId || agent.Pending == null)
                        return Receipt(command, action.Outcome.HasValue, false, action.Outcome.HasValue
                            ? InteractionCommandRejection.None : InteractionCommandRejection.UnknownAction);
                    if (!owner.TryGetTarget(command.Target, out var target) || target.Confirm == null)
                        return Receipt(command, false, false, InteractionCommandRejection.UnsupportedConfirmation);
                    var request = agent.Pending;
                    try { target.Confirm(request); }
                    catch (Exception error)
                    {
                        if (!request.IsSettled) agent.Settle(request, InteractionStatus.Faulted, error.Message);
                        return Receipt(command, false, false, InteractionCommandRejection.HandlerFault);
                    }
                    pendingAction = agent.Pending == null ? 0 : command.ActionId;
                    return Receipt(command, true, true, InteractionCommandRejection.None);
                default: return Receipt(command, false, false, InteractionCommandRejection.InvalidCommand);
            }
        }

        private InteractionCommandReceipt Receipt(InteractionCommand command, bool handled, bool admitted,
            InteractionCommandRejection rejection, bool replay = false)
        {
            actions.TryGetValue(command.ActionId, out var action);
            return new InteractionCommandReceipt(Epoch, ActorId, Activation, command.Sequence, command.ActionId,
                handled, admitted, replay, heldAction == command.ActionId, pendingAction == command.ActionId && agent.Pending != null,
                rejection, action?.Outcome);
        }

        private void RequestCreated(InteractionRequest request)
        {
            long actionId = invocationAction != 0 ? invocationAction : heldAction != 0 ? heldAction : pendingAction;
            if (actionId != 0) requestActions.Add(request.Id, actionId);
        }

        private void Completed(InteractionResult result)
        {
            if (!requestActions.TryGetValue(result.RequestId, out long actionId)) return;
            requestActions.Remove(result.RequestId);
            if (actionId == 0 || !actions.TryGetValue(actionId, out var action)) return;
            var outcome = new InteractionAuthorityOutcome(Epoch, ActorId, Activation, actionId, checked(++revision),
                result.RequestId, new InteractionTargetIdentity(result.Context.TargetId, result.Context.Activation),
                result.Context.Gesture, result.Status, result.Reason);
            action.Outcome = outcome;
            notifications.Enqueue(outcome);
            if (notifications.Count > historyLimit) { notifications.Dequeue(); DroppedNotifications++; }
        }

        public bool TryGetOutcome(long actionId, out InteractionAuthorityOutcome outcome)
        {
            if (actions.TryGetValue(actionId, out var action) && action.Outcome.HasValue)
            { outcome = action.Outcome.Value; return true; }
            outcome = default; return false;
        }

        /// <summary>Transient notifications are bounded. Replays expose the latest retained outcome; domain snapshots recover older state.</summary>
        public void DrainOutcomes(List<InteractionAuthorityOutcome> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            while (notifications.Count != 0) destination.Add(notifications.Dequeue());
        }

        public void Tick()
        {
            if (!IsCurrent) return;
            if (dispatching) throw new InvalidOperationException("Tick cannot run inside a command or business callback.");
            dispatching = true;
            try { agent.Tick(); }
            finally
            {
                dispatching = false;
                if (agent.Pending == null) pendingAction = 0;
            }
        }

        private void TrimActions()
        {
            int inspected = actionOrder.Count;
            while (actions.Count > historyLimit && inspected-- > 0)
            {
                long id = actionOrder.Dequeue();
                if (id == heldAction || id == pendingAction || requestActions.ContainsValue(id)) actionOrder.Enqueue(id);
                else actions.Remove(id);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; owner.Remove(this);
            try { agent.Dispose(); }
            finally { heldAction = pendingAction = invocationAction = 0; range.Selected = null; requestActions.Clear(); }
        }
    }
}
