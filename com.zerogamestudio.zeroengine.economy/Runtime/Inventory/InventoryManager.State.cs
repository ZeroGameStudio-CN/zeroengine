using System;
using System.Collections.Generic;

namespace ZeroEngine.Inventory
{
    public partial class InventoryManager
    {
        /// <summary>Validates a target save against the registered definitions without changing live state.</summary>
        public bool CanImportSaveData(IReadOnlyList<InventorySlot> slots)
        {
            return TryCloneSlots(slots, ResolveSavedItem, true, out _, out _);
        }

        /// <summary>Prepares a target state using session-owned definitions, without registering them live.</summary>
        public bool CanImportSaveData(IReadOnlyList<InventorySlot> slots, Func<string, InventoryItemSO> itemResolver)
        {
            if (itemResolver == null) return false;
            return TryCloneSlots(slots, slot => itemResolver(slot.ItemId), true, out _, out _);
        }

        private InventoryItemSO ResolveSavedItem(InventorySlot slot)
        {
            return _itemDatabase.TryGetValue(slot.ItemId, out var item) ? item : null;
        }

        private static bool TryCloneSlots(IReadOnlyList<InventorySlot> source,
            Func<InventorySlot, InventoryItemSO> resolveItem, bool padDefaultSlots,
            out List<InventorySlot> cloned, out string error)
        {
            cloned = null;
            error = null;
            if (source == null || source.Count > MaxSlots)
            {
                error = "Inventory slots must be a non-null list within capacity.";
                return false;
            }

            var candidate = new List<InventorySlot>(Math.Max(source.Count, padDefaultSlots ? 10 : 0));
            var instances = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < source.Count; i++)
            {
                var slot = source[i];
                if (slot == null)
                {
                    error = $"Inventory slot {i} is null.";
                    return false;
                }
                if (string.IsNullOrEmpty(slot.ItemId))
                {
                    if (slot.Amount != 0 || slot.HasInstance)
                    {
                        error = $"Empty inventory slot {i} contains quantity or instance state.";
                        return false;
                    }
                    candidate.Add(new InventorySlot { SlotIndex = i });
                    continue;
                }

                var item = resolveItem(slot);
                if (string.IsNullOrWhiteSpace(slot.ItemId) || item == null || item.Id != slot.ItemId
                    || item.MaxStack <= 0 || slot.Amount <= 0 || slot.Amount > item.MaxStack)
                {
                    error = $"Inventory slot {i} has an unknown definition or invalid quantity.";
                    return false;
                }
                if (slot.HasInstance && (!slot.InstanceData.IsValid || slot.Amount != 1
                    || !instances.Add(slot.InstanceData.InstanceId)))
                {
                    error = $"Inventory slot {i} has invalid or duplicate instance state.";
                    return false;
                }

                var copy = slot.Clone();
                copy.ItemData = item;
                copy.SlotIndex = i;
                candidate.Add(copy);
            }
            if (padDefaultSlots)
                while (candidate.Count < 10)
                    candidate.Add(new InventorySlot { SlotIndex = candidate.Count });
            cloned = candidate;
            return true;
        }
    }
}
