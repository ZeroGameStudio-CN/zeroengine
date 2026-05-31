using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Currency;
using ZeroEngine.Economy;
using ZeroEngine.Inventory;
using ZeroEngine.Shop;

namespace ZeroEngine.Economy.Tests
{
    [TestFixture]
    public class EconomyProviderCurrencyTests
    {
        private readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }
            _objects.Clear();
        }

        [Test]
        public void CurrencyManager_AddConsumeSet_ClampsAndRaisesEvents()
        {
            var manager = CreateGameObject("CurrencyManager").AddComponent<CurrencyManager>();
            var definition = ScriptableObject.CreateInstance<CurrencyDefinitionSO>();
            ConfigureDefinition(definition, CurrencyIds.Gold, "Gold", 100, 10, false);
            ConfigureDefinitions(manager, definition);
            manager.ResetToDefault();

            CurrencyChangedEventArgs lastArgs = default;
            int eventCount = 0;
            manager.OnCurrencyChanged += args =>
            {
                lastArgs = args;
                eventCount++;
            };

            manager.AddCurrency(CurrencyIds.Gold, 200);
            Assert.AreEqual(100, manager.GetCurrencyBalance(CurrencyIds.Gold));
            Assert.AreEqual(CurrencyEventType.Added, lastArgs.EventType);

            Assert.IsTrue(manager.ConsumeCurrency(CurrencyIds.Gold, 30));
            Assert.AreEqual(70, manager.GetCurrencyBalance(CurrencyIds.Gold));
            Assert.AreEqual(-30, lastArgs.Delta);
            Assert.AreEqual(CurrencyEventType.Consumed, lastArgs.EventType);

            Assert.IsFalse(manager.ConsumeCurrency(CurrencyIds.Gold, 1000));
            Assert.AreEqual(70, manager.GetCurrencyBalance(CurrencyIds.Gold));
            Assert.AreEqual(2, eventCount);
        }

        [Test]
        public void CurrencyManager_SaveRoundTrip_RestoresBalances()
        {
            var manager = CreateGameObject("CurrencyManager").AddComponent<CurrencyManager>();
            manager.SetBalance(CurrencyIds.Honor, 25);

            var saveData = manager.ExportSaveData();
            manager.SetBalance(CurrencyIds.Honor, 0);
            manager.ImportSaveData(saveData);

            Assert.AreEqual(25, manager.GetCurrencyBalance(CurrencyIds.Honor));
        }

        [Test]
        public void ShopManager_MissingCurrencyProvider_ReturnsInsufficientCurrencyWithoutCreatingSingleton()
        {
            var manager = CreateGameObject("ShopManager").AddComponent<ShopManager>();
            manager.SetCurrencyProvider(null);
            manager.SetInventoryProvider(new RecordingInventoryProvider());
            manager.SetExternalSystemProvider(new FixedExternalSystemProvider(level: 10, reputation: 10));

            var item = ScriptableObject.CreateInstance<InventoryItemSO>();
            item.Id = "test_item";
            item.ItemName = "Test Item";
            var shopItem = ScriptableObject.CreateInstance<ShopItemSO>();
            shopItem.Item = item;
            shopItem.ItemId = item.Id;
            shopItem.BuyPrice.Amount = 10;
            var shop = ScriptableObject.CreateInstance<ShopSO>();
            shop.ShopId = "test_shop";
            shop.Items.Add(shopItem);

            Assert.AreEqual(PurchaseResult.InsufficientCurrency, manager.CanPurchase(shop, shopItem));
            Assert.IsNull(Object.FindFirstObjectByType<CurrencyManager>(FindObjectsInactive.Include));
        }

        [Test]
        public void EconomyProviderResolver_DoesNotCreateMissingSingletons()
        {
            Assert.IsNull(Object.FindFirstObjectByType<InventoryManager>(FindObjectsInactive.Include));

            var provider = EconomyProviderResolver.FindInventoryProvider();

            Assert.IsNull(provider);
            Assert.IsNull(Object.FindFirstObjectByType<InventoryManager>(FindObjectsInactive.Include));
        }

        private GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        private static void ConfigureDefinition(
            CurrencyDefinitionSO definition,
            string currencyId,
            string displayName,
            int maxBalance,
            int startingBalance,
            bool allowNegative)
        {
            SetField(definition, "_currencyId", currencyId);
            SetField(definition, "_displayName", displayName);
            SetField(definition, "_maxBalance", maxBalance);
            SetField(definition, "_startingBalance", startingBalance);
            SetField(definition, "_allowNegative", allowNegative);
        }

        private static void ConfigureDefinitions(CurrencyManager manager, params CurrencyDefinitionSO[] definitions)
        {
            SetField(manager, "_definitions", new List<CurrencyDefinitionSO>(definitions));
            manager.RebuildDefinitionCache();
        }

        private static void SetField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} should exist on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
