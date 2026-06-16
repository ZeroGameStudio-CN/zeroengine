using System.Collections.Generic;
using ZeroEngine.Economy;
using ZeroEngine.Inventory;

namespace ZeroEngine.Economy.Tests
{
    internal sealed class RecordingCurrencyProvider : ICurrencyProvider
    {
        private readonly Dictionary<string, int> _balances = new();

        public string LastAddedCurrencyId { get; private set; }
        public int AddedAmount { get; private set; }
        public int ConsumedAmount { get; private set; }

        public bool HasCurrency(string currencyId, int amount)
        {
            return GetCurrencyBalance(currencyId) >= amount;
        }

        public bool ConsumeCurrency(string currencyId, int amount)
        {
            if (!HasCurrency(currencyId, amount))
            {
                return false;
            }

            _balances[currencyId] -= amount;
            ConsumedAmount += amount;
            return true;
        }

        public void AddCurrency(string currencyId, int amount)
        {
            LastAddedCurrencyId = currencyId;
            AddedAmount += amount;
            _balances[currencyId] = GetCurrencyBalance(currencyId) + amount;
        }

        public int GetCurrencyBalance(string currencyId)
        {
            return _balances.TryGetValue(currencyId, out int balance) ? balance : 1000;
        }
    }

    internal sealed class RecordingInventoryProvider : IInventoryProvider
    {
        private readonly Dictionary<string, int> _counts = new();

        public InventoryItemSO LastAddedItem { get; private set; }
        public int LastAddedAmount { get; private set; }
        public int RemovedAmount { get; private set; }
        public bool IsFull { get; set; }

        public bool AddItem(InventoryItemSO item, int amount)
        {
            LastAddedItem = item;
            LastAddedAmount += amount;
            if (item != null)
            {
                _counts[item.Id] = GetItemCount(item.Id) + amount;
            }
            return true;
        }

        public void RemoveItem(InventoryItemSO item, int amount)
        {
            if (item != null)
            {
                RemoveItem(item.Id, amount);
            }
        }

        public void RemoveItem(string itemId, int amount)
        {
            RemovedAmount += amount;
            _counts[itemId] = GetItemCount(itemId) - amount;
        }

        public int GetItemCount(string itemId)
        {
            return _counts.TryGetValue(itemId, out int count) ? count : 0;
        }

        public int GetItemCount(InventoryItemSO item)
        {
            return item != null ? GetItemCount(item.Id) : 0;
        }

        public bool HasItem(string itemId, int amount)
        {
            return GetItemCount(itemId) >= amount;
        }

        public InventoryItemSO GetItemData(string itemId)
        {
            return null;
        }

        public void SetCount(string itemId, int amount)
        {
            _counts[itemId] = amount;
        }
    }

    internal sealed class FixedExternalSystemProvider : IExternalSystemProvider
    {
        private readonly int _level;
        private readonly int _reputation;

        public FixedExternalSystemProvider(int level, int reputation)
        {
            _level = level;
            _reputation = reputation;
        }

        public int GetPlayerLevel()
        {
            return _level;
        }

        public int GetPlayerReputation(string reputationId = null)
        {
            return _reputation;
        }

        public bool IsQuestCompleted(string questId)
        {
            return questId == "completed";
        }

        public int GetRelationshipLevel(string npcId)
        {
            return _reputation;
        }
    }
}
