using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Relationship
{
    /// <summary>Optional Unity host. The application explicitly supplies its actor identity and game calendar.</summary>
    public sealed class RelationshipManager : MonoBehaviour
    {
        [SerializeField] private List<RelationshipDataSO> _allNpcs = new();
        [SerializeField] private List<RelationshipGroupSO> _npcGroups = new();
        private RelationshipState _state;
        private IReadOnlyList<RelationshipDataSO> _configuredNpcs;
        public RelationshipState State => _state ?? throw new InvalidOperationException("Configure the relationship owner and game clock first.");
        public IReadOnlyList<RelationshipDataSO> AllNpcs => _configuredNpcs ?? _allNpcs;
        public IReadOnlyList<RelationshipGroupSO> NpcGroups => _npcGroups;

        public void Configure(string ownerId, Func<DateTime> gameDate) => Configure(ownerId, _allNpcs, gameDate);

        public void Configure(string ownerId, IReadOnlyList<RelationshipDataSO> npcs, Func<DateTime> gameDate)
        {
            if (npcs == null) throw new ArgumentNullException(nameof(npcs));
            if (_state != null) throw new InvalidOperationException("Retire the current relationship host before replacing its owner.");
            var prepared = new Dictionary<string, RelationshipDataSO>(StringComparer.Ordinal);
            foreach (var data in npcs)
            {
                if (data == null || string.IsNullOrWhiteSpace(data.NpcId) || !prepared.TryAdd(data.NpcId, data))
                    throw new InvalidOperationException("Relationship catalog has a missing or duplicate stable NPC identity.");
            }
            _state = new RelationshipState(ownerId, id => prepared.TryGetValue(id, out var data) ? data : null, gameDate);
            _configuredNpcs = new List<RelationshipDataSO>(npcs).AsReadOnly();
        }

        public RelationshipSaveParticipant CreateSaveParticipant(string key) => new(key, State);

        private void OnDestroy()
        {
            // The host owns the injected scope for its entire lifetime; application code owns save registration.
            _state?.Dispose(); _state = null; _configuredNpcs = null;
        }
    }
}
