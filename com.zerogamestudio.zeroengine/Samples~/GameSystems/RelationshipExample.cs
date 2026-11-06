using System;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.Relationship;

namespace ZeroEngine.Samples
{
    /// <summary>Explicit scene wiring example; the application supplies its own game calendar and save registration.</summary>
    public sealed class RelationshipExample : MonoBehaviour
    {
        [SerializeField] private RelationshipDataSO _npc;
        [SerializeField] private InventoryManager _inventory;
        [SerializeField] private string _ownerId = "sample.player";
        private RelationshipState _state;

        public void Configure(Func<DateTime> gameDate)
        {
            if (_state != null) throw new InvalidOperationException("This sample already owns a relationship scope.");
            if (_npc == null) throw new InvalidOperationException("Assign the authored NPC definition first.");
            _state = new RelationshipState(_ownerId, id => id == _npc.NpcId ? _npc : null, gameDate);
        }
        public bool TryTalk()
        {
            if (_state == null || !_state.AdvanceGameDate()) return false;
            return _state.TryTalk(_state.CaptureContext(Guid.NewGuid().ToString("N")), _npc.NpcId, out _, out _);
        }
        public bool TryGiveGift(InventoryItemSO item)
        {
            if (_state == null || !_state.AdvanceGameDate()) return false;
            return _state.TryGiveGift(_state.CaptureContext(Guid.NewGuid().ToString("N")), _npc.NpcId, item, _inventory, out _, out _);
        }
        public RelationshipSaveParticipant CreateSaveParticipant(string key) => new(key, _state);
        private void OnDestroy() { _state?.Dispose(); _state = null; }
    }
}
