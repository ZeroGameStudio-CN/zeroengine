using System;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.Utils;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 物品奖励 (v1.2.0+)
    /// </summary>
    [Serializable]
    public class ItemReward : QuestReward
    {
        [Tooltip("物品 ID")]
        public string ItemId;

        [Tooltip("物品数量")]
        public int Quantity = 1;

        [Tooltip("物品配置（可选，用于预览）")]
        public InventoryItemSO ItemData;

        public override string RewardType => "Item";

        public override bool Grant()
        {
            if (string.IsNullOrEmpty(ItemId) || Quantity <= 0) return false;

            // 通过 InventoryManager 添加物品
            if (InventoryManager.Instance != null)
            {
                bool success;
                if (ItemData != null)
                {
                    success = InventoryManager.Instance.AddItem(ItemData, Quantity);
                }
                else
                {
                    success = InventoryManager.Instance.AddItem(ItemId, Quantity);
                }

                if (success)
                {
                    ZeroLog.Info(ZeroLog.Modules.Quest, $"Granted {Quantity}x {ItemId}");
                    return true;
                }
                else
                {
                    ZeroLog.Warning(ZeroLog.Modules.Quest, $"Failed to grant item {ItemId} (inventory full?)");
                    return false;
                }
            }

            ZeroLog.Warning(ZeroLog.Modules.Quest, "InventoryManager not available");
            return false;
        }

        public override string GetPreviewText()
        {
            string itemName = ItemData != null ? ItemData.ItemName : ItemId;
            return Quantity > 1 ? $"{itemName} x{Quantity}" : itemName;
        }

        public override Sprite GetIcon()
        {
            return ItemData != null ? ItemData.Icon : null;
        }
    }
}