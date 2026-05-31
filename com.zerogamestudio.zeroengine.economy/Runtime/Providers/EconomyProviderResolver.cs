using UnityEngine;
using ZeroEngine.Currency;
using ZeroEngine.Inventory;

namespace ZeroEngine.Economy
{
    public static class EconomyProviderResolver
    {
        public static ICurrencyProvider FindCurrencyProvider()
        {
            return Object.FindFirstObjectByType<CurrencyManager>(FindObjectsInactive.Include);
        }

        public static IInventoryProvider FindInventoryProvider()
        {
            return Object.FindFirstObjectByType<InventoryManager>(FindObjectsInactive.Include);
        }

        public static IExternalSystemProvider DefaultExternalSystemProvider()
        {
            return ZeroEngine.Economy.DefaultExternalSystemProvider.Shared;
        }
    }
}
