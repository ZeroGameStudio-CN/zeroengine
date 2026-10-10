using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.Save;
using ZeroEngine.Shop;
using ZeroEngine.Wallet;
using UnityObject = UnityEngine.Object;

namespace ZeroEngine.Economy.Tests.Editor
{
    public class ShopManagerWalletTests
    {
        private readonly List<UnityObject> _objects = new List<UnityObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    UnityObject.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();

            DestroyIfExists<InventoryManager>();
            DestroyIfExists<SaveManager>();
            DestroyIfExists<SaveSlotManager>();
        }

        [Test]
        public void CurrencyWallet_TrySpend_RejectsInsufficientFunds()
        {
            var wallet = new TestCurrencyWallet();
            wallet.Add("Gold", 50);

            Assert.IsFalse(wallet.TrySpend("Gold", 100));
            Assert.AreEqual(50, wallet.GetBalance("Gold"));
        }

        [Test]
        public void Purchase_WithInsufficientWallet_DoesNotSpendOrReduceStock()
        {
            var manager = CreateManager();
            var wallet = new TestCurrencyWallet();
            wallet.Add("Gold", 50);
            manager.SetWallet(wallet);
            var shop = CreateShop("shop");
            var item = CreateShopItem("item", 100, 2);
            shop.Items.Add(item);

            var result = manager.Purchase(shop, item);

            Assert.AreEqual(PurchaseResult.InsufficientCurrency, result);
            Assert.AreEqual(50, wallet.GetBalance("Gold"));
            Assert.AreEqual(2, manager.GetStock(shop, item));
            Assert.AreEqual(0, manager.GetPurchaseCount(shop, item));
        }

        [Test]
        public void Purchase_WithWallet_DeductsFundsAndStock()
        {
            var manager = CreateManager();
            var wallet = new TestCurrencyWallet();
            wallet.Add("Gold", 150);
            manager.SetWallet(wallet);
            var shop = CreateShop("shop");
            var item = CreateShopItem("item", 100, 2);
            shop.Items.Add(item);

            var result = manager.Purchase(shop, item);

            Assert.AreEqual(PurchaseResult.Success, result);
            Assert.AreEqual(50, wallet.GetBalance("Gold"));
            Assert.AreEqual(1, manager.GetStock(shop, item));
            Assert.AreEqual(1, manager.GetPurchaseCount(shop, item));
        }

        [Test]
        public void Purchase_WhenWalletSpendFails_DoesNotReduceStock()
        {
            var manager = CreateManager();
            var wallet = new TestCurrencyWallet();
            wallet.Add("Gold", 150);
            wallet.FailNextSpend = true;
            manager.SetWallet(wallet);
            var shop = CreateShop("shop");
            var item = CreateShopItem("item", 100, 2);
            shop.Items.Add(item);

            var result = manager.Purchase(shop, item);

            Assert.AreEqual(PurchaseResult.InsufficientCurrency, result);
            Assert.AreEqual(150, wallet.GetBalance("Gold"));
            Assert.AreEqual(2, manager.GetStock(shop, item));
            Assert.AreEqual(0, manager.GetPurchaseCount(shop, item));
        }

        [Test]
        public void Purchase_WhenInventoryAddFails_RefundsWalletAndDoesNotReduceStock()
        {
            var manager = CreateManager();
            var inventory = InventoryManager.Instance;
            inventory.ResetToDefault();
            var wallet = new TestCurrencyWallet();
            wallet.Add("Gold", 100);
            wallet.AfterSpend = () => FillInventory(inventory);
            manager.SetWallet(wallet);
            var shop = CreateShop("shop");
            var item = CreateShopItem("item", 100, 2);
            item.Item.MaxStack = 1;
            shop.Items.Add(item);

            var result = manager.Purchase(shop, item);

            Assert.AreEqual(PurchaseResult.InventoryFull, result);
            Assert.AreEqual(100, wallet.GetBalance("Gold"));
            Assert.AreEqual(2, manager.GetStock(shop, item));
            Assert.AreEqual(0, manager.GetPurchaseCount(shop, item));
            Assert.AreEqual(0, inventory.GetItemCount(item.Item));
        }

        [Test]
        public void SellToShop_WithWallet_AddsFunds()
        {
            var manager = CreateManager();
            var wallet = new TestCurrencyWallet();
            manager.SetWallet(wallet);
            var shop = CreateShop("shop");
            var item = CreateInventoryItem("item");
            item.SellPrice = 40;
            shop.SellPriceMultiplier = 0.5f;
            var inventory = InventoryManager.Instance;
            inventory.ResetToDefault();
            inventory.AddItem(item);

            var result = manager.SellToShop(shop, item);

            Assert.AreEqual(PurchaseResult.Success, result);
            Assert.AreEqual(20, wallet.GetBalance("Gold"));
            Assert.AreEqual(0, inventory.GetItemCount(item));
        }

        private ShopManager CreateManager()
        {
            var go = new GameObject("ShopManagerWalletTests.ShopManager");
            _objects.Add(go);
            return go.AddComponent<ShopManager>();
        }

        private ShopSO CreateShop(string id)
        {
            var shop = ScriptableObject.CreateInstance<ShopSO>();
            _objects.Add(shop);
            shop.ShopId = id;
            shop.DisplayName = id;
            shop.Schedule.AlwaysOpen = true;
            return shop;
        }

        private ShopItemSO CreateShopItem(string id, int price, int stock)
        {
            var item = ScriptableObject.CreateInstance<ShopItemSO>();
            _objects.Add(item);
            item.ItemId = id;
            item.Item = CreateInventoryItem(id);
            item.Quantity = 1;
            item.BuyPrice.CurrencyType = ShopCurrencyType.Gold;
            item.BuyPrice.Amount = price;
            item.Stock.InitialStock = stock;
            item.Limit.MaxCount = 0;
            return item;
        }

        private InventoryItemSO CreateInventoryItem(string id)
        {
            var item = ScriptableObject.CreateInstance<InventoryItemSO>();
            _objects.Add(item);
            item.Id = id;
            item.ItemName = id;
            item.MaxStack = 99;
            return item;
        }

        private void FillInventory(InventoryManager inventory)
        {
            var slots = new List<InventorySlot>(InventoryManager.MaxSlots);
            for (int i = 0; i < InventoryManager.MaxSlots; i++)
            {
                var filler = CreateInventoryItem("filler_" + i);
                filler.MaxStack = 1;
                slots.Add(new InventorySlot(filler, 1) { SlotIndex = i });
            }

            inventory.ImportSaveData(slots);
        }

        private static void DestroyIfExists<T>() where T : Component
        {
            var component = UnityObject.FindFirstObjectByType<T>();
            if (component != null)
            {
                UnityObject.DestroyImmediate(component.gameObject);
            }
        }

        private sealed class TestCurrencyWallet : ICurrencyWallet
        {
            private readonly Dictionary<string, int> _balances = new Dictionary<string, int>();

            public bool FailNextSpend { get; set; }
            public Action AfterSpend { get; set; }

            public int GetBalance(string currencyId)
            {
                return _balances.TryGetValue(currencyId, out var amount) ? amount : 0;
            }

            public bool CanSpend(string currencyId, int amount)
            {
                return amount >= 0 && GetBalance(currencyId) >= amount;
            }

            public bool TrySpend(string currencyId, int amount)
            {
                if (FailNextSpend)
                {
                    FailNextSpend = false;
                    return false;
                }

                if (!CanSpend(currencyId, amount))
                {
                    return false;
                }

                _balances[currencyId] = GetBalance(currencyId) - amount;
                AfterSpend?.Invoke();
                return true;
            }

            public void Add(string currencyId, int amount)
            {
                if (amount <= 0)
                {
                    return;
                }

                _balances[currencyId] = GetBalance(currencyId) + amount;
            }
        }
    }
}
