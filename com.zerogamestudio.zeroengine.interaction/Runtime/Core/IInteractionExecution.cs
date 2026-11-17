using System;
using System.Threading;

namespace ZeroEngine.Interaction.Core
{
    /// <summary>
    /// A single admitted interaction execution. Independent content can depend on this port or adapt
    /// it to its own contract. Gameplay, UI, persistence and content registration remain consumer-owned.
    /// Calls and callbacks belong to the owning gameplay thread.
    /// </summary>
    public interface IInteractionExecution
    {
        InteractionContext Context { get; }
        long Id { get; }
        bool IsSettled { get; }
        bool IsCurrent { get; }
        CancellationToken LifetimeToken { get; }
        InteractionResult? Outcome { get; }
        Exception NotificationFailure { get; }
        event Action<InteractionResult> Settled;
        bool TryCommit(Action mutation = null);
        bool TryCommitResult(Func<InteractionCheck> mutation);
        bool TryDeny(string reason);
        bool TryCancel(string reason = "Cancelled by caller.");
        bool TryFault(Exception error);
    }
}
