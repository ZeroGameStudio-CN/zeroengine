using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Inventory
{
    public partial class InventoryManager
    {
        /// <summary>Checks the complete resulting inventory without changing slots or publishing events.</summary>
        public bool CanExchangeItems(
            IReadOnlyList<InventoryItemAmount> consumed,
            IReadOnlyList<InventoryItemAmount> produced)
        {
            return TryStageExchange(consumed, produced, out _, out _, out _);
        }

        /// <summary>
        /// Removes and adds the entire exchange, or leaves the inventory unchanged.
        /// Callers with their own save transaction can disable the legacy immediate save.
        /// </summary>
        public bool TryExchangeItems(
            IReadOnlyList<InventoryItemAmount> consumed,
            IReadOnlyList<InventoryItemAmount> produced,
            bool persistImmediately = true)
        {
            return TryExchangeItems(consumed, produced, persistImmediately, null);
        }

        // Crafting joins its progress/experience state before observers can capture a save.
        // The callback belongs to this assembly and must only update the owning in-memory state.
        internal bool TryExchangeItems(
            IReadOnlyList<InventoryItemAmount> consumed,
            IReadOnlyList<InventoryItemAmount> produced,
            bool persistImmediately,
            Action commitState)
        {
            if (!TryStageExchange(consumed, produced, out var staged, out var removed, out var added))
                return false;

            var previous = _slots;
            _slots = staged;
            try
            {
                commitState?.Invoke();
            }
            catch
            {
                _slots = previous;
                throw;
            }

            // A subscriber/save error cannot turn an already committed exchange into a retryable rejection.
            if (persistImmediately)
                Observe(SaveInventory);
            for (var i = 0; i < staged.Count; i++)
            {
                var before = i < previous.Count ? previous[i] : null;
                var after = staged[i];
                if (before != null && before.ItemId == after.ItemId && before.Amount == after.Amount
                    && InventoryInstanceData.SameState(before.InstanceData, after.InstanceData))
                    continue;
                Publish(OnSlotChanged, new SlotChangedEventArgs(i, after, InventoryEventType.SlotChanged,
                    after.ItemData ?? before?.ItemData, before?.Amount ?? 0, after.Amount));
            }
            foreach (var entry in removed.Values)
            {
                var amount = entry.Amount - (added.TryGetValue(entry.Item.Id, out var returned) ? returned.Amount : 0);
                if (amount > 0) Publish(OnItemRemoved, entry.Item, amount);
            }
            foreach (var entry in added.Values)
            {
                var amount = entry.Amount - (removed.TryGetValue(entry.Item.Id, out var spent) ? spent.Amount : 0);
                if (amount > 0) Publish(OnItemAdded, entry.Item, amount);
            }
            Observe(TriggerInventoryUpdated);
            return true;
        }

        private bool TryStageExchange(
            IReadOnlyList<InventoryItemAmount> consumed,
            IReadOnlyList<InventoryItemAmount> produced,
            out List<InventorySlot> staged,
            out Dictionary<string, InventoryItemAmount> removed,
            out Dictionary<string, InventoryItemAmount> added)
        {
            staged = null;
            removed = null;
            added = null;
            if (!TryAggregateExchange(consumed, out var inputs) || !TryAggregateExchange(produced, out var outputs))
                return false;
            removed = inputs.Totals;
            added = outputs.Totals;
            foreach (var pair in outputs.Totals)
            {
                if (inputs.Totals.TryGetValue(pair.Key, out var input) && input.Item != pair.Value.Item)
                    return false;
            }
            foreach (var pair in outputs.Instances)
            {
                if (inputs.Instances.TryGetValue(pair.Key, out var input)
                    && input.Item.Id != pair.Value.Item.Id)
                    return false;
            }

            if (!TryCloneSlots(_slots, slot => slot.ItemData, false, out staged, out _)) return false;
            foreach (var pair in inputs.Definitions)
            {
                var remaining = pair.Value.Amount;
                foreach (var slot in staged)
                {
                    if (slot.IsEmpty || slot.HasInstance || slot.ItemId != pair.Key) continue;
                    var count = Math.Min(slot.Amount, remaining);
                    slot.RemoveAmount(count);
                    remaining -= count;
                    if (remaining == 0) break;
                }
                if (remaining != 0) return false;
            }
            foreach (var entry in inputs.Instances.Values)
            {
                var slot = staged.Find(candidate => !candidate.IsEmpty && candidate.ItemId == entry.Item.Id
                    && candidate.HasInstance && candidate.InstanceData.InstanceId == entry.InstanceData.InstanceId);
                if (slot == null || !InventoryInstanceData.SameState(slot.InstanceData, entry.InstanceData)) return false;
                slot.Clear();
            }
            foreach (var pair in outputs.Definitions)
            {
                var item = pair.Value.Item;
                var remaining = pair.Value.Amount;
                var limit = item.IsStackable ? item.MaxStack : 1;
                if (item.IsStackable)
                {
                    foreach (var slot in staged)
                    {
                        if (slot.IsEmpty || slot.HasInstance || slot.ItemId != pair.Key) continue;
                        var count = Math.Min(Math.Max(0, limit - slot.Amount), remaining);
                        slot.Amount += count;
                        remaining -= count;
                        if (remaining == 0) break;
                    }
                }
                while (remaining > 0)
                {
                    var slot = GetStagedEmptySlot(staged);
                    if (slot == null) return false;
                    var count = Math.Min(limit, remaining);
                    slot.Set(item, count);
                    remaining -= count;
                }
            }
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in staged)
                if (slot.HasInstance && !instanceIds.Add(slot.InstanceData.InstanceId)) return false;
            foreach (var entry in outputs.Instances.Values)
            {
                if (!instanceIds.Add(entry.InstanceData.InstanceId)) return false;
                var slot = GetStagedEmptySlot(staged);
                if (slot == null) return false;
                slot.Set(entry.Item, entry.InstanceData);
            }
            return true;
        }

        private static InventorySlot GetStagedEmptySlot(List<InventorySlot> staged)
        {
            var slot = staged.Find(candidate => candidate.IsEmpty);
            if (slot == null && staged.Count < MaxSlots)
            {
                slot = new InventorySlot { SlotIndex = staged.Count };
                staged.Add(slot);
            }
            return slot;
        }

        private sealed class ExchangeItems
        {
            internal readonly Dictionary<string, InventoryItemAmount> Definitions =
                new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            internal readonly Dictionary<string, InventoryItemAmount> Instances =
                new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            // Acquisition/removal events describe the net item count, including instances.
            internal readonly Dictionary<string, InventoryItemAmount> Totals =
                new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
        }

        private static bool TryAggregateExchange(
            IReadOnlyList<InventoryItemAmount> entries,
            out ExchangeItems result)
        {
            result = new ExchangeItems();
            if (entries == null) return true;
            foreach (var entry in entries)
            {
                var item = entry.Item;
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.MaxStack <= 0 || entry.Amount <= 0)
                    return false;
                if (entry.InstanceData != null && (!entry.InstanceData.IsValid || entry.Amount != 1
                    || result.Instances.ContainsKey(entry.InstanceData.InstanceId)))
                    return false;
                if (!TryAddDefinitionTotal(result.Totals, entry)) return false;
                if (entry.InstanceData != null)
                {
                    result.Instances.Add(entry.InstanceData.InstanceId, entry);
                }
                else if (!TryAddDefinitionTotal(result.Definitions, entry)) return false;
            }
            return true;
        }

        private static bool TryAddDefinitionTotal(Dictionary<string, InventoryItemAmount> totals,
            InventoryItemAmount entry)
        {
            var amount = (long)entry.Amount;
            if (totals.TryGetValue(entry.Item.Id, out var current))
            {
                if (current.Item != entry.Item) return false;
                amount += current.Amount;
            }
            if (amount > int.MaxValue) return false;
            totals[entry.Item.Id] = new InventoryItemAmount(entry.Item, (int)amount);
            return true;
        }

        private static void Publish<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                Observe(() => handler(value));
        }

        private static void Publish<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second)
        {
            if (handlers == null) return;
            foreach (Action<T1, T2> handler in handlers.GetInvocationList())
                Observe(() => handler(first, second));
        }

        private static void Observe(Action action)
        {
            try { action(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
