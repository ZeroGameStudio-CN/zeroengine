using UnityEngine;

namespace ZeroEngine.AI.GOAP
{
    /// <summary>
    /// 预设 GOAP 目标集合
    /// 需要安装 crashkonijn/GOAP 包才能使用
    /// </summary>
    /// <remarks>
    /// 使用方式:
    /// 1. 安装 crashkonijn/GOAP 包
    /// 2. 添加 CRASHKONIJN_GOAP 编译符号
    /// 3. 在 GoapSetConfig 中配置目标
    /// </remarks>
    public static class ZeroEngineGOAPGoals
    {
        // 目标键常量
        public const string GOAL_SURVIVE = "Survive";
        public const string GOAL_ATTACK_ENEMY = "AttackEnemy";
        public const string GOAL_HEAL = "Heal";
        public const string GOAL_PATROL = "Patrol";
        public const string GOAL_FLEE = "Flee";
        public const string GOAL_IDLE = "Idle";
        public const string GOAL_FOLLOW_SCHEDULE = "FollowSchedule";
        public const string GOAL_GATHER_RESOURCE = "GatherResource";
        public const string GOAL_RETURN_HOME = "ReturnHome";
    }
}
