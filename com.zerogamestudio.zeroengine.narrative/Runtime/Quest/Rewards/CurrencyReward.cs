using System;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Utils;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 货币奖励 (v1.2.0+)
    /// </summary>
    [Serializable]
    public class CurrencyReward : QuestReward
    {
        [Tooltip("货币类型")]
        public CurrencyType CurrencyType = CurrencyType.Gold;

        [Tooltip("货币数量")]
        public int Amount;

        public override string RewardType => "Currency";

        public override bool Grant()
        {
            if (Amount <= 0) return false;

            // 通过事件通知货币系统
            EventManager.Trigger(GameEvents.CurrencyGained, CurrencyType.ToString(), Amount);
            ZeroLog.Info(ZeroLog.Modules.Quest, $"Granted {Amount} {CurrencyType}");
            return true;
        }

        public override string GetPreviewText()
        {
            return $"{GetCurrencyName()} +{Amount}";
        }

        private string GetCurrencyName()
        {
            return CurrencyType switch
            {
                CurrencyType.Gold => "金币",
                CurrencyType.Gem => "钻石",
                CurrencyType.Token => "代币",
                _ => CurrencyType.ToString()
            };
        }
    }

    /// <summary>
    /// 货币类型 (v1.2.0+)
    /// </summary>
    public enum CurrencyType
    {
        Gold,
        Gem,
        Token,
        Custom
    }
}