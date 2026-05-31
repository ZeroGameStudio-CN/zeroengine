using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZeroEngine.Crafting;
using ZeroEngine.Economy;
using ZeroEngine.Inventory;
using ZeroEngine.Inventory.UI;
using ZeroEngine.Loot;
using ZeroEngine.Shop;

namespace ZeroEngine.Economy.Tests
{
    [TestFixture]
    public class EconomyManagerIntegrationTests
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
        public void ShopManager_Purchase_UsesCurrencyAndInventoryProviders()
        {
            var manager = CreateGameObject("ShopManager").AddComponent<ShopManager>();
            var currency = new RecordingCurrencyProvider();
            var inventory = new RecordingInventoryProvider();
            manager.SetCurrencyProvider(currency);
            manager.SetInventoryProvider(inventory);
            manager.SetExternalSystemProvider(new FixedExternalSystemProvider(level: 10, reputation: 10));

            var item = CreateItem("shop_item");
            var shopItem = ScriptableObject.CreateInstance<ShopItemSO>();
            shopItem.Item = item;
            shopItem.ItemId = item.Id;
            shopItem.Quantity = 2;
            shopItem.BuyPrice.Amount = 15;
            var shop = ScriptableObject.CreateInstance<ShopSO>();
            shop.ShopId = "shop";
            shop.Items.Add(shopItem);

            Assert.AreEqual(PurchaseResult.Success, manager.Purchase(shop, shopItem, 3));
            Assert.AreEqual(45, currency.ConsumedAmount);
            Assert.AreSame(item, inventory.LastAddedItem);
            Assert.AreEqual(6, inventory.LastAddedAmount);
        }

        [Test]
        public void CraftingManager_UnlockConditions_UseExternalProviderFields()
        {
            var manager = CreateGameObject("CraftingManager").AddComponent<CraftingManager>();
            manager.SetInventoryProvider(new RecordingInventoryProvider());
            manager.SetExternalSystemProvider(new FixedExternalSystemProvider(level: 3, reputation: 0));

            var recipe = ScriptableObject.CreateInstance<CraftingRecipeSO>();
            recipe.RecipeId = "level_recipe";
            recipe.UnlockType = RecipeUnlockType.Level;
            recipe.UnlockLevel = 5;

            Assert.IsFalse(manager.TryUnlockRecipe(recipe));

            manager.SetExternalSystemProvider(new FixedExternalSystemProvider(level: 5, reputation: 0));
            Assert.IsTrue(manager.TryUnlockRecipe(recipe));
        }

        [Test]
        public void CraftingManager_StartCraft_ConsumesAndOutputsThroughInventoryProvider()
        {
            var manager = CreateGameObject("CraftingManager").AddComponent<CraftingManager>();
            var inventory = new RecordingInventoryProvider();
            manager.SetInventoryProvider(inventory);

            var input = CreateItem("ore");
            var output = CreateItem("ingot");
            inventory.SetCount(input.Id, 5);
            var recipe = ScriptableObject.CreateInstance<CraftingRecipeSO>();
            recipe.RecipeId = "craft";
            recipe.UnlockType = RecipeUnlockType.Default;
            recipe.SuccessRate = 1f;
            recipe.Ingredients.Add(new RecipeIngredient { Item = input, Amount = 2, IsConsumed = true });
            recipe.Outputs.Add(new RecipeOutput { Item = output, BaseAmount = 1, Probability = 1f });
            manager.ForceUnlock(recipe);

            Assert.AreEqual(CraftingResult.Success, manager.StartCraft(recipe));
            Assert.AreEqual(2, inventory.RemovedAmount);
            Assert.AreSame(output, inventory.LastAddedItem);
            Assert.AreEqual(1, inventory.LastAddedAmount);
        }

        [Test]
        public void LootTableManager_GrantResults_AddsCurrencyAndItemsThroughProviders()
        {
            var manager = CreateGameObject("LootTableManager").AddComponent<LootTableManager>();
            var inventory = new RecordingInventoryProvider();
            var currency = new RecordingCurrencyProvider();
            manager.SetInventoryProvider(inventory);
            manager.SetCurrencyProvider(currency);
            var item = CreateItem("loot_item");

            manager.GrantResults(new List<LootResult>
            {
                LootResult.FromItem(item, 2),
                LootResult.FromCurrency(CurrencyType.Gold, 7)
            });

            Assert.AreSame(item, inventory.LastAddedItem);
            Assert.AreEqual(2, inventory.LastAddedAmount);
            Assert.AreEqual("Gold", currency.LastAddedCurrencyId);
            Assert.AreEqual(7, currency.AddedAmount);
        }

        [Test]
        public void HasItemCondition_UsesLootContextInventoryProvider()
        {
            var item = CreateItem("key");
            var inventory = new RecordingInventoryProvider();
            inventory.SetCount(item.Id, 1);
            var condition = new HasItemCondition { Item = item, MinAmount = 1 };

            Assert.IsTrue(condition.Check(new LootContext { InventoryProvider = inventory }));
            Assert.IsFalse(condition.Check(new LootContext { InventoryProvider = new RecordingInventoryProvider() }));
        }

        [Test]
        public void InventoryManager_QueryMethods_FillProvidedLists()
        {
            var manager = CreateGameObject("InventoryManager").AddComponent<InventoryManager>();
            manager.ResetToDefault();
            var herb = CreateItem("herb", InventoryItemType.Consumable, ItemCategory.CraftingMaterial, ItemRarity.Uncommon);
            var sword = CreateItem("sword", InventoryItemType.Equip, ItemCategory.Weapon, ItemRarity.Rare);
            manager.AddItem(herb, 1);
            manager.AddItem(sword, 1);
            var results = new List<InventorySlot>();

            manager.GetItemsByType(InventoryItemType.Consumable, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreSame(herb, results[0].ItemData);

            manager.GetAllItems(results);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void InventorySlotUI_Drop_EmitsSwapRequestWithoutCallingInventorySingleton()
        {
            var source = CreateSlotUI("source", 0);
            var target = CreateSlotUI("target", 1);
            int from = -1;
            int to = -1;
            target.OnSlotDropRequested += (sourceIndex, targetIndex) =>
            {
                from = sourceIndex;
                to = targetIndex;
            };
            var slot = new InventorySlot(CreateItem("drag_item"), 1);
            source.Refresh(slot);

            source.OnBeginDrag(new PointerEventData(EventSystem.current));
            target.OnDrop(new PointerEventData(EventSystem.current));
            source.OnEndDrag(new PointerEventData(EventSystem.current));

            Assert.AreEqual(0, from);
            Assert.AreEqual(1, to);
            Assert.IsNull(Object.FindFirstObjectByType<InventoryManager>(FindObjectsInactive.Include));
        }

        private InventoryItemSO CreateItem(
            string id,
            InventoryItemType type = InventoryItemType.Material,
            ItemCategory category = ItemCategory.Miscellaneous,
            ItemRarity rarity = ItemRarity.Common)
        {
            var item = ScriptableObject.CreateInstance<InventoryItemSO>();
            item.Id = id;
            item.ItemName = id;
            item.Type = type;
            item.Category = category;
            item.Rarity = rarity;
            return item;
        }

        private InventorySlotUI CreateSlotUI(string name, int index)
        {
            var go = CreateGameObject(name);
            go.AddComponent<CanvasGroup>();
            go.AddComponent<RectTransform>();
            var ui = go.AddComponent<InventorySlotUI>();
            var icon = new GameObject("Icon").AddComponent<Image>();
            icon.transform.SetParent(go.transform);
            var amount = new GameObject("Amount").AddComponent<Text>();
            amount.transform.SetParent(go.transform);
            ui.ConfigureForTests(icon, amount, null, null);
            ui.Setup(index);
            return ui;
        }

        private GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }
    }
}
