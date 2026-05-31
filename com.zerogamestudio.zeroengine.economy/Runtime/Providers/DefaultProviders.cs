using ZeroEngine.Inventory;

namespace ZeroEngine.Economy
{
    public sealed class NullCurrencyProvider : ICurrencyProvider
    {
        public static readonly NullCurrencyProvider Instance = new NullCurrencyProvider();

        private NullCurrencyProvider()
        {
        }

        public bool HasCurrency(string currencyId, int amount)
        {
            return false;
        }

        public bool ConsumeCurrency(string currencyId, int amount)
        {
            return false;
        }

        public void AddCurrency(string currencyId, int amount)
        {
        }

        public int GetCurrencyBalance(string currencyId)
        {
            return 0;
        }
    }

    public sealed class NullInventoryProvider : IInventoryProvider
    {
        public static readonly NullInventoryProvider Instance = new NullInventoryProvider();

        private NullInventoryProvider()
        {
        }

        public bool AddItem(InventoryItemSO item, int amount)
        {
            return false;
        }

        public void RemoveItem(InventoryItemSO item, int amount)
        {
        }

        public void RemoveItem(string itemId, int amount)
        {
        }

        public int GetItemCount(string itemId)
        {
            return 0;
        }

        public int GetItemCount(InventoryItemSO item)
        {
            return 0;
        }

        public bool HasItem(string itemId, int amount)
        {
            return false;
        }

        public bool IsFull => true;

        public InventoryItemSO GetItemData(string itemId)
        {
            return null;
        }
    }

    public sealed class DefaultExternalSystemProvider : IExternalSystemProvider
    {
        public static readonly DefaultExternalSystemProvider Shared = new DefaultExternalSystemProvider();

        private DefaultExternalSystemProvider()
        {
        }

        public int GetPlayerLevel()
        {
            return 1;
        }

        public int GetPlayerReputation(string reputationId = null)
        {
            return 0;
        }

        public bool IsQuestCompleted(string questId)
        {
            return false;
        }

        public int GetRelationshipLevel(string npcId)
        {
            return 0;
        }
    }
}
