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
            InstanceData = null;
        }

        public InventoryItemAmount(InventoryItemSO item, InventoryInstanceData instanceData)
        {
            Item = item;
            Amount = 1;
            // A null instance is invalid, rather than silently becoming a definition item.
            InstanceData = instanceData?.Clone() ?? new InventoryInstanceData(null, null, 0, null);
        }

        public InventoryItemSO Item { get; }
        public int Amount { get; }
        public InventoryInstanceData InstanceData { get; }
    }
}
