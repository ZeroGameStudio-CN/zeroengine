using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;
using ZeroEngine.Inventory;
using ZeroEngine.Wallet;

namespace ZeroEngine.Shop
{
    /// <summary>
    /// 商店系统管理器
    /// </summary>
    public class ShopManager : MonoSingleton<ShopManager>, ISaveable
    {
        [Header("配置")]
        [SerializeField] private List<ShopSO> _allShops = new List<ShopSO>();

        [Header("调试")]
        [SerializeField] private bool _debugMode;

        // 商店数据
        private readonly Dictionary<string, ShopSO> _shopLookup = new Dictionary<string, ShopSO>();
        private readonly Dictionary<string, ShopRuntimeData> _shopData = new Dictionary<string, ShopRuntimeData>();

        // 当前打开的商店
        private ShopSO _currentShop;

        // 临时列表
        private readonly List<ShopItemSO> _tempItemList = new List<ShopItemSO>(32);

        // 上次补货日期
        private string _lastRestockDate;

        #region Events

        public event Action<ShopEventArgs> OnShopEvent;

        #endregion

        #region Properties

        public ShopSO CurrentShop => _currentShop;
        public bool IsShopOpen => _currentShop != null;
        public ICurrencyWallet Wallet { get; private set; }

        #endregion

        #region ISaveable

        public string SaveKey => "ShopManager";

        public void Register()
        {
            SaveSlotManager.Instance?.Register(this);
        }

        public void Unregister()
        {
            SaveSlotManager.Instance?.Unregister(this);
        }

        public object ExportSaveData()
        {
            return new ShopSaveData
            {
                ShopData = new Dictionary<string, ShopRuntimeData>(_shopData),
                LastRestockDate = _lastRestockDate
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is not ShopSaveData saveData) return;

            _shopData.Clear();
            if (saveData.ShopData != null)
            {
                foreach (var kvp in saveData.ShopData)
                {
                    _shopData[kvp.Key] = kvp.Value;
                }
            }

            // 检查是否需要补货
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (saveData.LastRestockDate != today)
            {
                ProcessDailyRestock(saveData.LastRestockDate, today);
            }
            _lastRestockDate = today;
        }

        public void ResetToDefault()
        {
            _shopData.Clear();
            _lastRestockDate = DateTime.Now.ToString("yyyy-MM-dd");
            InitializeAllShops();
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            BuildLookup();
            _lastRestockDate = DateTime.Now.ToString("yyyy-MM-dd");
        }

        private void Start()
        {
            Register();
            if (_shopData.Count == 0)
            {
                InitializeAllShops();
            }
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Public API - Shop Management

        /// <summary>
        /// 打开商店
        /// </summary>
        public bool OpenShop(string shopId)
        {
            if (!_shopLookup.TryGetValue(shopId, out var shop))
            {
                Log($"商店不存在: {shopId}");
                return false;
            }

            return OpenShop(shop);
        }

        /// <summary>
        /// 打开商店
        /// </summary>
        public bool OpenShop(ShopSO shop)
        {
            if (shop == null) return false;

            // 检查营业时间
            var now = DateTime.Now;
            if (!shop.IsOpen(now.Hour, (int)now.DayOfWeek))
            {
                Log($"商店已关门: {shop.DisplayName}");
                return false;
            }

            _currentShop = shop;
            OnShopEvent?.Invoke(ShopEventArgs.Opened(shop));
            Log($"打开商店: {shop.DisplayName}");

            return true;
        }

        /// <summary>
        /// 关闭商店
        /// </summary>
        public void CloseShop()
        {
            if (_currentShop != null)
            {
                var shop = _currentShop;
                _currentShop = null;
                OnShopEvent?.Invoke(ShopEventArgs.Closed(shop));
                Log($"关闭商店: {shop.DisplayName}");
            }
        }

        /// <summary>
        /// 获取商店
        /// </summary>
        public ShopSO GetShop(string shopId)
        {
            _shopLookup.TryGetValue(shopId, out var shop);
            return shop;
        }

        /// <summary>
        /// 获取所有商店
        /// </summary>
        public IReadOnlyList<ShopSO> GetAllShops() => _allShops;

        public void SetWallet(ICurrencyWallet wallet)
        {
            Wallet = wallet;
        }

        #endregion

        #region Public API - Purchase

        /// <summary>
        /// 检查是否可以购买
        /// </summary>
        public PurchaseResult CanPurchase(ShopSO shop, ShopItemSO item, int amount = 1)
        {
            if (shop == null || item == null)
                return PurchaseResult.Error;

            // 检查商品可用性
            if (!item.CanPurchase(GetPlayerLevel(), GetPlayerReputation(), out var reason))
            {
                if (reason != PurchaseResult.InsufficientStock && reason != PurchaseResult.LimitReached)
                    return reason;
            }

            // 检查库存
            var data = GetOrCreateData(shop.ShopId);
            var itemData = data.GetItemData(item.ItemId);

            if (item.Stock.InitialStock >= 0)
            {
                int currentStock = itemData?.CurrentStock ?? item.Stock.InitialStock;
                if (currentStock < amount)
                    return PurchaseResult.InsufficientStock;
            }

            // 检查限购
            if (item.Limit.MaxCount > 0)
            {
                int purchased = itemData?.PurchaseCount ?? 0;
                if (purchased + amount > item.Limit.MaxCount)
                    return PurchaseResult.LimitReached;
            }

            // 检查货币
            int totalPrice = item.GetFinalBuyPrice() * amount;
            if (!HasCurrency(item.BuyPrice.GetCurrencyId(), totalPrice))
                return PurchaseResult.InsufficientCurrency;

            // 检查背包空间
            var inventory = InventoryManager.Instance;
            if (inventory != null && !CanFitItem(inventory, item.Item, item.Quantity * amount))
                return PurchaseResult.InventoryFull;

            return PurchaseResult.Success;
        }

        /// <summary>
        /// 购买商品
        /// </summary>
        public PurchaseResult Purchase(ShopSO shop, ShopItemSO item, int amount = 1)
        {
            var result = CanPurchase(shop, item, amount);
            if (result != PurchaseResult.Success)
            {
                Log($"购买失败: {item.DisplayName} - {result}");
                return result;
            }

            int totalPrice = item.GetFinalBuyPrice() * amount;
            string currencyId = item.BuyPrice.GetCurrencyId();

            // 扣除货币
            if (!ConsumeCurrency(currencyId, totalPrice))
            {
                return PurchaseResult.InsufficientCurrency;
            }

            // 添加物品
            var inventory = InventoryManager.Instance;
            if (inventory != null && item.Item != null)
            {
                int itemAmount = item.Quantity * amount;
                int itemCountBeforePurchase = inventory.GetItemCount(item.Item);
                if (!inventory.AddItem(item.Item, itemAmount))
                {
                    int addedAmount = inventory.GetItemCount(item.Item) - itemCountBeforePurchase;
                    if (addedAmount > 0)
                    {
                        inventory.RemoveItem(item.Item, addedAmount);
                    }

                    AddCurrency(currencyId, totalPrice);
                    return PurchaseResult.InventoryFull;
                }
            }

            // 更新库存和限购
            var data = GetOrCreateData(shop.ShopId);
            var itemData = data.GetItemData(item.ItemId);
            if (itemData == null)
            {
                itemData = data.GetOrCreateItemData(item.ItemId);
                itemData.CurrentStock = item.Stock.InitialStock;
            }

            if (item.Stock.InitialStock >= 0)
            {
                itemData.CurrentStock -= amount;
            }

            itemData.PurchaseCount += amount;

            // 触发事件
            OnShopEvent?.Invoke(ShopEventArgs.Purchased(shop, item, amount, totalPrice));

            // 触发成就
#if ZEROENGINE_NARRATIVE
            var achievementMgr = Achievement.AchievementManager.Instance;
            achievementMgr?.TriggerEvent("ShopPurchase", item.ItemId);
            achievementMgr?.TriggerEvent("SpendCurrency", totalPrice);
#endif

            Log($"购买成功: {item.DisplayName} x{amount}, 花费 {totalPrice}");

            return PurchaseResult.Success;
        }

        /// <summary>
        /// 出售物品给商店
        /// </summary>
        public PurchaseResult SellToShop(ShopSO shop, InventoryItemSO item, int amount = 1)
        {
            if (shop == null || item == null)
                return PurchaseResult.Error;

            if (!shop.AllowSelling)
                return PurchaseResult.NotAvailable;

            var inventory = InventoryManager.Instance;
            if (inventory == null || inventory.GetItemCount(item) < amount)
                return PurchaseResult.InsufficientStock;

            // 计算出售价格
            int sellPrice = Mathf.RoundToInt(item.SellPrice * shop.SellPriceMultiplier) * amount;

            // 移除物品
            inventory.RemoveItem(item, amount);

            // 添加货币
            AddCurrency("Gold", sellPrice);

            // 触发事件
            var shopItem = shop.GetItem(item.Id);
            OnShopEvent?.Invoke(ShopEventArgs.Sold(shop, shopItem, amount, sellPrice));

            Log($"出售成功: {item.ItemName} x{amount}, 获得 {sellPrice}");

            return PurchaseResult.Success;
        }

        #endregion

        #region Public API - Query

        /// <summary>
        /// 获取商品当前库存
        /// </summary>
        public int GetStock(ShopSO shop, ShopItemSO item)
        {
            if (item.Stock.InitialStock < 0) return -1; // 无限

            var data = GetOrCreateData(shop.ShopId);
            var itemData = data.GetItemData(item.ItemId);
            return itemData?.CurrentStock ?? item.Stock.InitialStock;
        }

        /// <summary>
        /// 获取商品已购买数量
        /// </summary>
        public int GetPurchaseCount(ShopSO shop, ShopItemSO item)
        {
            var data = GetOrCreateData(shop.ShopId);
            var itemData = data.GetItemData(item.ItemId);
            return itemData?.PurchaseCount ?? 0;
        }

        /// <summary>
        /// 获取商品剩余可购买数量
        /// </summary>
        public int GetRemainingPurchaseLimit(ShopSO shop, ShopItemSO item)
        {
            if (item.Limit.MaxCount <= 0) return -1; // 无限

            int purchased = GetPurchaseCount(shop, item);
            return Mathf.Max(0, item.Limit.MaxCount - purchased);
        }

        #endregion

        #region Public API - Restock

        /// <summary>
        /// 手动补货
        /// </summary>
        public void RestockShop(ShopSO shop)
        {
            if (shop == null) return;

            var data = GetOrCreateData(shop.ShopId);

            foreach (var item in shop.Items)
            {
                if (item == null) continue;

                var itemData = data.GetOrCreateItemData(item.ItemId);

                // 补货库存
                if (item.Stock.RestockType != RestockType.Never)
                {
                    itemData.CurrentStock = Mathf.Min(
                        itemData.CurrentStock + item.Stock.RestockAmount,
                        item.Stock.InitialStock
                    );
                }

                // 重置限购
                if (item.Limit.ResetPeriod != RestockType.Never)
                {
                    itemData.PurchaseCount = 0;
                }
            }

            OnShopEvent?.Invoke(ShopEventArgs.Restocked(shop));
            Log($"商店补货: {shop.DisplayName}");
        }

        #endregion

        #region Internal

        private void BuildLookup()
        {
            _shopLookup.Clear();
            foreach (var shop in _allShops)
            {
                if (shop != null)
                {
                    _shopLookup[shop.ShopId] = shop;
                }
            }
        }

        private void InitializeAllShops()
        {
            foreach (var shop in _allShops)
            {
                if (shop == null) continue;

                var data = GetOrCreateData(shop.ShopId);

                foreach (var item in shop.Items)
                {
                    if (item == null) continue;

                    var itemData = data.GetOrCreateItemData(item.ItemId);
                    itemData.CurrentStock = item.Stock.InitialStock;
                    itemData.PurchaseCount = 0;
                }
            }
        }

        private ShopRuntimeData GetOrCreateData(string shopId)
        {
            if (!_shopData.TryGetValue(shopId, out var data))
            {
                data = new ShopRuntimeData { ShopId = shopId };
                _shopData[shopId] = data;
            }
            return data;
        }

        private void ProcessDailyRestock(string oldDate, string newDate)
        {
            foreach (var shop in _allShops)
            {
                if (shop == null) continue;

                if (shop.RestockType == RestockType.Daily)
                {
                    RestockShop(shop);
                }
            }

            Log($"每日补货完成: {oldDate} -> {newDate}");
        }

        // 货币系统接口 (需要外部实现)
        private bool HasCurrency(string currencyId, int amount)
        {
            return Wallet == null || Wallet.CanSpend(currencyId, amount);
        }

        private bool ConsumeCurrency(string currencyId, int amount)
        {
            if (Wallet != null)
            {
                return Wallet.TrySpend(currencyId, amount);
            }

            Log($"消耗货币: {currencyId} x{amount}");
            return true;
        }

        private void AddCurrency(string currencyId, int amount)
        {
            Wallet?.Add(currencyId, amount);
            Log($"获得货币: {currencyId} x{amount}");
        }

        private static bool CanFitItem(InventoryManager inventory, InventoryItemSO item, int amount)
        {
            if (inventory == null || item == null || amount <= 0)
            {
                return true;
            }

            int remaining = amount;
            var slots = inventory.Slots;

            if (item.IsStackable)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot != null && !slot.IsEmpty && slot.ItemId == item.Id)
                    {
                        remaining -= Mathf.Max(0, item.MaxStack - slot.Amount);
                        if (remaining <= 0)
                        {
                            return true;
                        }
                    }
                }
            }

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.IsEmpty)
                {
                    remaining -= item.MaxStack;
                    if (remaining <= 0)
                    {
                        return true;
                    }
                }
            }

            int uncreatedSlotCount = Mathf.Max(0, InventoryManager.MaxSlots - slots.Count);
            remaining -= uncreatedSlotCount * item.MaxStack;

            return remaining <= 0;
        }

        private int GetPlayerLevel()
        {
            // TODO: 接入角色系统
            return 99;
        }

        private int GetPlayerReputation()
        {
            // TODO: 接入声望系统
            return 999;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[Shop] {message}");
            }
        }

        #endregion
    }

    #region Save Data

    [Serializable]
    public class ShopSaveData
    {
        public Dictionary<string, ShopRuntimeData> ShopData;
        public string LastRestockDate;
    }

    [Serializable]
    public class ShopRuntimeData
    {
        public string ShopId;
        public Dictionary<string, ShopItemRuntimeData> ItemData = new Dictionary<string, ShopItemRuntimeData>();

        public ShopItemRuntimeData GetItemData(string itemId)
        {
            ItemData.TryGetValue(itemId, out var data);
            return data;
        }

        public ShopItemRuntimeData GetOrCreateItemData(string itemId)
        {
            if (!ItemData.TryGetValue(itemId, out var data))
            {
                data = new ShopItemRuntimeData { ItemId = itemId };
                ItemData[itemId] = data;
            }
            return data;
        }
    }

    [Serializable]
    public class ShopItemRuntimeData
    {
        public string ItemId;
        public int CurrentStock;
        public int PurchaseCount;
    }

    #endregion
}
