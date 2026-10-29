using System.Collections.Generic;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// The project grants the entire batch atomically and idempotently by sequenceId.
    /// A retry after a save interruption must not grant the batch twice.
    /// </summary>
    public interface ITutorialRewardService
    {
        bool TryGrant(string sequenceId, IReadOnlyList<TutorialReward> rewards, out string error);
    }
}
