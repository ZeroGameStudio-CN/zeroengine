using System;
using System.Threading;
using System.Threading.Tasks;
using ZeroEngine.Persistence;

namespace ZeroEngine.Relationship
{
    /// <summary>The consumer owns registration and the save key; no legacy save singleton is resolved.</summary>
    public sealed class RelationshipSaveParticipant : ISaveParticipantAdapter, IAsyncSaveParticipantAdapter
    {
        private readonly RelationshipState _state;
        public string Key { get; }
        public RelationshipSaveParticipant(string key, RelationshipState state)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A stable save key is required.", nameof(key));
            Key = key; _state = state ?? throw new ArgumentNullException(nameof(state));
        }
        public object Capture() => _state.GetSnapshot();
        public Task PrepareRestoreAsync(object state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state is not RelationshipStateSnapshot snapshot || !_state.CanRestore(snapshot, out _))
                throw new ArgumentException("Invalid relationship participant state.", nameof(state));
            return Task.CompletedTask;
        }
        public void Restore(object state)
        {
            if (state is not RelationshipStateSnapshot snapshot || !_state.TryRestore(snapshot, out _))
                throw new ArgumentException("Invalid relationship participant state.", nameof(state));
        }
    }
}
