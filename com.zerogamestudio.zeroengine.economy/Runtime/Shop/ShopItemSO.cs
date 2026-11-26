using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Inventory;

namespace ZeroEngine.Shop
{
    /// <summary>
    /// 商品定义
    /// </summary>
    [CreateAssetMenu(fileName = "New Shop Item", menuName = "ZeroEngine/Items and Economy/Shops/Shop Item")]
    public class ShopItemSO : ScriptableObject
    {
        [Header("基础信息")]
        [Tooltip("商品ID")]
        public string ItemId;

        [Tooltip("关联物品")]
        public InventoryItemSO Item;

        [Tooltip("商品数量 (每次购买获得)")]
        public int Quantity = 1;

        [Header("价格")]
        [Tooltip("购买价格")]
        public ShopPrice BuyPrice = new ShopPrice();

        [Tooltip("出售价格 (玩家卖给商店)")]
        public ShopPrice SellPrice = new ShopPrice { Amount = 25 };

        [Tooltip("折扣配置")]
        public DiscountConfig Discount = new DiscountConfig();

        [Header("限制")]
        [Tooltip("限购配置")]
        public PurchaseLimit Limit = new PurchaseLimit();

        [Tooltip("库存配置")]
        public StockConfig Stock = new StockConfig();

        [Tooltip("需求等级")]
        public int RequiredLevel;

        [Tooltip("需求声望")]
        public int RequiredReputation;

        [Header("显示")]
        [Tooltip("是否显示 (隐藏商品)")]
        public bool IsVisible = true;

        [Tooltip("是否推荐/热销")]
        public bool IsFeatured;

        [Tooltip("自定义描述 (覆盖物品描述)")]
        [TextArea]
        public string CustomDescription;

        [Tooltip("标签")]
        public List<string> Tags = new List<string>();

        #region Properties

        public string DisplayName => Item != null ? Item.ItemName : ItemId;
        public string Description => !string.IsNullOrEmpty(CustomDescription) ? CustomDescription : Item?.Description;
        public Sprite Icon => Item?.Icon;

        public int GetFinalBuyPrice()
        {
            return Discount.ApplyDiscount(BuyPrice.Amount);
        }

        public bool HasDiscount => Discount.Type != DiscountType.None;

        public float DiscountPercentage
        {
            get
            {
                if (Discount.Type == DiscountType.Percentage)
                    return Discount.Value;
                if (Discount.Type == DiscountType.FlatAmount && BuyPrice.Amount > 0)
                    return Discount.Value / BuyPrice.Amount;
                return 0;
            }
        }

        #endregion

        #region Validation

        public bool CanPurchase(int playerLevel, int playerReputation, out PurchaseResult reason)
        {
            if (!IsVisible)
            {
                reason = PurchaseResult.NotAvailable;
                return false;
            }

            if (RequiredLevel > 0 && playerLevel < RequiredLevel)
            {
                reason = PurchaseResult.LevelTooLow;
                return false;
            }

            if (RequiredReputation > 0 && playerReputation < RequiredReputation)
            {
                reason = PurchaseResult.ReputationTooLow;
                return false;
            }

            if (!Stock.HasStock)
            {
                reason = PurchaseResult.InsufficientStock;
                return false;
            }

            if (Limit.MaxCount > 0 && Limit.CurrentCount >= Limit.MaxCount)
            {
                reason = PurchaseResult.LimitReached;
                return false;
            }

            reason = PurchaseResult.Success;
            return true;
        }

        #endregion

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(ItemId) && Item != null)
            {
                ItemId = Item.Id;
            }
        }
    }

    /// <summary>
    /// 商店定义
    /// </summary>
    [CreateAssetMenu(fileName = "New Shop", menuName = "ZeroEngine/Items and Economy/Shops/Shop")]
    public class ShopSO : ScriptableObject
    {
        [Header("基础信息")]
        [Tooltip("商店ID")]
        public string ShopId;

        [Tooltip("商店名称")]
        public string DisplayName;

        [Tooltip("商店类型")]
        public ShopType ShopType = ShopType.General;

        [Tooltip("商店图标")]
        public Sprite Icon;

        [Tooltip("商店描述")]
        [TextArea]
        public string Description;

        [Header("商品")]
        [Tooltip("商品列表")]
        public List<ShopItemSO> Items = new List<ShopItemSO>();

        [Header("设置")]
        [Tooltip("营业时间")]
        public ShopSchedule Schedule = new ShopSchedule();

        [Tooltip("补货配置")]
        public RestockType RestockType = RestockType.Daily;

        [Tooltip("是否支持出售 (玩家卖给商店)")]
        public bool AllowSelling = true;

        [Tooltip("出售价格倍率")]
        [Range(0.1f, 1f)]
        public float SellPriceMultiplier = 0.5f;

        [Header("关联")]
        [Tooltip("关联NPC ID")]
        public string NpcId;

        [Tooltip("需求声望等级")]
        public int RequiredReputation;

        #region Query

        public ShopItemSO GetItem(string itemId)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] != null && Items[i].ItemId == itemId)
                    return Items[i];
            }
            return null;
        }

        public void GetItemsByTag(string tag, List<ShopItemSO> results)
        {
            results.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (item != null && item.IsVisible && item.Tags.Contains(tag))
                {
                    results.Add(item);
                }
            }
        }

        public void GetFeaturedItems(List<ShopItemSO> results)
        {
            results.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (item != null && item.IsVisible && item.IsFeatured)
                {
                    results.Add(item);
                }
            }
        }

        public void GetVisibleItems(List<ShopItemSO> results)
        {
            results.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (item != null && item.IsVisible)
                {
                    results.Add(item);
                }
            }
        }

        public bool IsOpen(int hour, int dayOfWeek)
        {
            if (Schedule.AlwaysOpen) return true;
            if (!Schedule.OpenDays[dayOfWeek]) return false;
            return hour >= Schedule.OpenHour && hour < Schedule.CloseHour;
        }

        #endregion

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(ShopId))
            {
                ShopId = name;
            }
        }
    }
}