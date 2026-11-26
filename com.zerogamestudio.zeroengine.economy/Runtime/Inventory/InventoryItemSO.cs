using System;
using UnityEngine;

namespace ZeroEngine.Inventory
{
    [CreateAssetMenu(fileName = "NewItem", menuName = "ZeroEngine/Items and Economy/Inventory/Item Data")]
    public class InventoryItemSO : ScriptableObject
    {
        [Header("Basic Info")]
        public string Id;
        public string ItemName;
        public InventoryItemType Type;
        public Sprite Icon;
        [TextArea] public string Description;

        [Header("Classification (v1.2.0+)")]
        public ItemCategory Category = ItemCategory.Miscellaneous;
        public ItemRarity Rarity = ItemRarity.Common;

        [Header("Stacking")]
        public int MaxStack = 99;

        [Header("Economy")]
        public int BuyPrice;
        public int SellPrice;

        /// <summary>
        /// 是否可堆叠 (v1.2.0+)
        /// </summary>
        public bool IsStackable => MaxStack > 1;

        /// <summary>
        /// 使用物品 (可重写)
        /// </summary>
        public virtual bool Use()
        {
            Debug.Log($"[Inventory] Using item: {ItemName}");
            return true;
        }

        /// <summary>
        /// 获取稀有度颜色 (v1.2.0+)
        /// </summary>
        public Color GetRarityColor()
        {
            return Rarity switch
            {
                ItemRarity.Common => Color.white,
                ItemRarity.Uncommon => Color.green,
                ItemRarity.Rare => Color.blue,
                ItemRarity.Epic => new Color(0.6f, 0.2f, 0.8f), // Purple
                ItemRarity.Legendary => new Color(1f, 0.5f, 0f), // Orange
                ItemRarity.Mythic => Color.red,
                _ => Color.white
            };
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id))
            {
                Id = Guid.NewGuid().ToString();
            }

            // Auto-set sell price as percentage of buy price
            if (SellPrice == 0 && BuyPrice > 0)
            {
                SellPrice = Mathf.RoundToInt(BuyPrice * 0.5f);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Generate New ID")]
        private void GenerateNewId()
        {
            Id = Guid.NewGuid().ToString();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
