using System;
using UnityEngine;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 物品奖励
    /// </summary>
    [Serializable]
    public class ItemReward : AchievementReward
    {
        [Tooltip("奖励物品")]
        public Inventory.InventoryItemSO Item;

        [Tooltip("数量")]
        public int Amount = 1;

        public override string Description =>
            Item != null ? $"{Item.ItemName} x{Amount}" : "物品奖励";

        public override void Grant()
        {
            if (Item == null || Amount <= 0) return;

            var inventory = Inventory.InventoryManager.Instance;
            if (inventory != null)
            {
                inventory.AddItem(Item, Amount);
            }
        }

        public override Sprite GetIcon() => Item?.Icon;
    }

    /// <summary>
    /// 货币奖励
    /// </summary>
    [Serializable]
    public class CurrencyReward : AchievementReward
    {
        [Tooltip("货币类型")]
        public Loot.CurrencyType CurrencyType;

        [Tooltip("数量")]
        public int Amount = 100;

        public override string Description =>
            $"{CurrencyType} x{Amount}";

        public override void Grant()
        {
            if (Amount <= 0) return;

            // 需要外部货币系统支持
            // CurrencyManager.Instance?.AddCurrency(CurrencyType, Amount);
        }
    }

    /// <summary>
    /// 经验奖励
    /// </summary>
    [Serializable]
    public class ExpReward : AchievementReward
    {
        [Tooltip("经验类型")]
        public ExpType ExpType = ExpType.Character;

        [Tooltip("经验值")]
        public int Amount = 100;

        public override string Description =>
            $"{ExpType}经验 +{Amount}";

        public override void Grant()
        {
            if (Amount <= 0) return;

            // 需要外部经验系统支持
            // ExpManager.Instance?.AddExp(ExpType, Amount);
        }
    }

    /// <summary>
    /// 经验类型
    /// </summary>
    public enum ExpType
    {
        Character,
        Skill,
        Profession
    }

    /// <summary>
    /// Buff 奖励
    /// </summary>
    [Serializable]
    public class BuffReward : AchievementReward
    {
        [Tooltip("Buff ID")]
        public string BuffId;

        [Tooltip("Buff 名称")]
        public string BuffName;

        [Tooltip("持续时间（秒，0=永久）")]
        public float Duration;

        [Tooltip("Buff 层数")]
        public int Stacks = 1;

        public override string Description =>
            !string.IsNullOrEmpty(BuffName)
                ? (Duration > 0 ? $"{BuffName} ({Duration}秒)" : $"{BuffName} (永久)")
                : $"Buff: {BuffId}";

        public override void Grant()
        {
            if (string.IsNullOrEmpty(BuffId)) return;

            // 需要外部 Buff 系统支持
            // BuffManager.Instance?.AddBuff(BuffId, Duration, Stacks);
        }
    }

    /// <summary>
    /// 好感度奖励
    /// </summary>
    [Serializable]
    public class RelationshipReward : AchievementReward
    {
        [Tooltip("NPC ID")]
        public string NpcId;

        [Tooltip("NPC 名称（显示用）")]
        public string NpcName;

        [Tooltip("好感度变化")]
        public int RelationshipChange = 10;

        public override string Description =>
            !string.IsNullOrEmpty(NpcName)
                ? $"{NpcName} 好感度 {(RelationshipChange >= 0 ? "+" : "")}{RelationshipChange}"
                : $"好感度 {(RelationshipChange >= 0 ? "+" : "")}{RelationshipChange}";

        public override void Grant()
        {
            if (string.IsNullOrEmpty(NpcId)) return;

            // 将由 RelationshipManager 处理
            // RelationshipManager.Instance?.AddRelationship(NpcId, RelationshipChange);
        }
    }
}
