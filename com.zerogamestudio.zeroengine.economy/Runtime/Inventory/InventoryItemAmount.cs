using System;

namespace ZeroEngine.Inventory
{
    /// <summary>A positive item quantity used by an all-or-nothing inventory exchange.</summary>
    public readonly struct InventoryItemAmount
    {
        public InventoryItemAmount(InventoryItemSO item, int amount)
        {
            Item = item;
            Amount = amount;
        }

        public InventoryItemSO Item { get; }
        public int Amount { get; }
    }
}
