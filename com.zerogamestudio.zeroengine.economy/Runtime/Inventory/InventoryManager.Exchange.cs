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
                if (before != null && before.ItemId == after.ItemId && before.Amount == after.Amount)
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
            if (!TryAggregateExchange(consumed, out removed) || !TryAggregateExchange(produced, out added))
                return false;
            foreach (var pair in added)
            {
                if (removed.TryGetValue(pair.Key, out var input) && input.Item != pair.Value.Item)
                    return false;
            }

            staged = new List<InventorySlot>(_slots.Count);
            foreach (var slot in _slots)
            {
                if (slot == null) return false;
                staged.Add(slot.Clone());
            }
            foreach (var pair in removed)
            {
                var remaining = pair.Value.Amount;
                foreach (var slot in staged)
                {
                    if (slot.IsEmpty || slot.ItemId != pair.Key) continue;
                    var count = Math.Min(slot.Amount, remaining);
                    slot.RemoveAmount(count);
                    remaining -= count;
                    if (remaining == 0) break;
                }
                if (remaining != 0) return false;
            }
            foreach (var pair in added)
            {
                var item = pair.Value.Item;
                var remaining = pair.Value.Amount;
                var limit = item.IsStackable ? item.MaxStack : 1;
                if (item.IsStackable)
                {
                    foreach (var slot in staged)
                    {
                        if (slot.IsEmpty || slot.ItemId != pair.Key) continue;
                        var count = Math.Min(Math.Max(0, limit - slot.Amount), remaining);
                        slot.Amount += count;
                        remaining -= count;
                        if (remaining == 0) break;
                    }
                }
                while (remaining > 0)
                {
                    var slot = staged.Find(candidate => candidate.IsEmpty);
                    if (slot == null)
                    {
                        if (staged.Count >= MaxSlots) return false;
                        slot = new InventorySlot { SlotIndex = staged.Count };
                        staged.Add(slot);
                    }
                    var count = Math.Min(limit, remaining);
                    slot.Set(item, count);
                    remaining -= count;
                }
            }
            return true;
        }

        private static bool TryAggregateExchange(
            IReadOnlyList<InventoryItemAmount> entries,
            out Dictionary<string, InventoryItemAmount> totals)
        {
            totals = new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            if (entries == null) return true;
            foreach (var entry in entries)
            {
                var item = entry.Item;
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.MaxStack <= 0 || entry.Amount <= 0)
                    return false;
                var amount = (long)entry.Amount;
                if (totals.TryGetValue(item.Id, out var current))
                {
                    if (current.Item != item) return false;
                    amount += current.Amount;
                }
                if (amount > int.MaxValue) return false;
                totals[item.Id] = new InventoryItemAmount(item, (int)amount);
            }
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
