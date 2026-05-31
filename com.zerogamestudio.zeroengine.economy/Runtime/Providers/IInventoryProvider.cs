using ZeroEngine.Inventory;

namespace ZeroEngine.Economy
{
    public interface IInventoryProvider
    {
        bool AddItem(InventoryItemSO item, int amount);
        void RemoveItem(InventoryItemSO item, int amount);
        void RemoveItem(string itemId, int amount);
        int GetItemCount(string itemId);
        int GetItemCount(InventoryItemSO item);
        bool HasItem(string itemId, int amount);
        bool IsFull { get; }
        InventoryItemSO GetItemData(string itemId);
    }
}
