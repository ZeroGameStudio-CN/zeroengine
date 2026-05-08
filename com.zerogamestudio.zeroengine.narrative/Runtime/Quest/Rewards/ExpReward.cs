using System;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Utils;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 经验值奖励。
    /// </summary>
    [Serializable]
    public class ExpReward : QuestReward
    {
        [Header("Exp")]
        [Min(0)]
        [Tooltip("Experience amount to grant.")]
        public int Amount;

        public override string RewardType => "Exp";

        public override bool Grant()
        {
            if (Amount <= 0) return false;

            // 通过事件通知经验系统
            EventManager.Trigger(GameEvents.ExpGained, Amount);
            ZeroLog.Info(ZeroLog.Modules.Quest, $"Granted {Amount} EXP");
            return true;
        }

        public override string GetPreviewText()
        {
            return $"经验值 +{Amount}";
        }
    }
}
