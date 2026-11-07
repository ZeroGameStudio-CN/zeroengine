using System;
using UnityEngine;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 条件检查类型
    /// </summary>
    public enum StateCheckType
    {
        /// <summary>玩家等级</summary>
        PlayerLevel,

        /// <summary>拥有物品</summary>
        HasItem,

        /// <summary>货币数量</summary>
        CurrencyAmount,

        /// <summary>属性值</summary>
        StatValue,

        /// <summary>自定义</summary>
        Custom
    }

    /// <summary>
    /// 状态条件
    /// </summary>
    [Serializable]
    public class StateCondition : AchievementCondition
    {
        [Tooltip("检查类型")]
        public StateCheckType CheckType = StateCheckType.PlayerLevel;

        [Tooltip("目标值")]
        public int TargetValue = 1;

        [Tooltip("物品引用（CheckType=HasItem时使用）")]
        public Inventory.InventoryItemSO Item;

        [Tooltip("货币类型（CheckType=CurrencyAmount时使用）")]
        public Loot.CurrencyType CurrencyType;

        [Tooltip("属性类型（CheckType=StatValue时使用）")]
        public StatSystem.StatType StatType;

        [Tooltip("自定义检查ID（CheckType=Custom时使用）")]
        public string CustomCheckId;

        public override string Description => CheckType switch
        {
            StateCheckType.PlayerLevel => $"达到等级 {TargetValue}",
            StateCheckType.HasItem => $"拥有 {Item?.ItemName} x{TargetValue}",
            StateCheckType.CurrencyAmount => $"拥有 {CurrencyType} x{TargetValue}",
            StateCheckType.StatValue => $"{StatType} >= {TargetValue}",
            StateCheckType.Custom => $"自定义: {CustomCheckId}",
            _ => "状态条件"
        };

        public override bool IsCompleted(AchievementProgress progress, int conditionIndex)
        {
            return GetCurrentValue() >= TargetValue;
        }

        public override float GetProgress(AchievementProgress progress, int conditionIndex)
        {
            int current = GetCurrentValue();
            return Mathf.Clamp01((float)current / TargetValue);
        }

        public override string GetProgressText(AchievementProgress progress, int conditionIndex)
        {
            int current = GetCurrentValue();
            return $"{current}/{TargetValue}";
        }

        private int GetCurrentValue()
        {
            return CheckType switch
            {
                StateCheckType.PlayerLevel => GetPlayerLevel(),
                StateCheckType.HasItem => GetItemCount(),
                StateCheckType.CurrencyAmount => GetCurrencyAmount(),
                StateCheckType.StatValue => GetStatValue(),
                StateCheckType.Custom => GetCustomValue(),
                _ => 0
            };
        }

        private int GetPlayerLevel()
        {
            // 需要外部系统支持
            return 1;
        }

        private int GetItemCount()
        {
            if (Item == null) return 0;
            var inventory = Inventory.InventoryManager.Instance;
            return inventory?.GetItemCount(Item) ?? 0;
        }

        private int GetCurrencyAmount()
        {
            // 需要外部货币系统支持
            return 0;
        }

        private int GetStatValue()
        {
            // 需要外部属性系统支持
            return 0;
        }

        private int GetCustomValue()
        {
            if (string.IsNullOrEmpty(CustomCheckId)) return 0;

            if (_customChecks.TryGetValue(CustomCheckId, out var check))
            {
                return check();
            }
            return 0;
        }

        // 自定义检查注册
        private static readonly System.Collections.Generic.Dictionary<string, Func<int>> _customChecks =
            new System.Collections.Generic.Dictionary<string, Func<int>>();

        public static void RegisterCustomCheck(string id, Func<int> check)
        {
            _customChecks[id] = check;
        }

        public static void UnregisterCustomCheck(string id)
        {
            _customChecks.Remove(id);
        }
    }
}
