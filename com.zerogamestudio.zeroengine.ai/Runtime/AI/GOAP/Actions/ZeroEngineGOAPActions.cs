using UnityEngine;

namespace ZeroEngine.AI.GOAP
{
    /// <summary>
    /// 预设 GOAP 行动集合
    /// 需要安装 crashkonijn/GOAP 包才能使用
    /// </summary>
    public static class ZeroEngineGOAPActions
    {
        // 行动键常量
        public const string ACTION_MOVE_TO_TARGET = "MoveToTarget";
        public const string ACTION_ATTACK = "Attack";
        public const string ACTION_USE_HEAL_ITEM = "UseHealItem";
        public const string ACTION_FLEE = "Flee";
        public const string ACTION_PATROL = "Patrol";
        public const string ACTION_WAIT = "Wait";
        public const string ACTION_FIND_TARGET = "FindTarget";
        public const string ACTION_RETURN_HOME = "ReturnHome";
    }
}
