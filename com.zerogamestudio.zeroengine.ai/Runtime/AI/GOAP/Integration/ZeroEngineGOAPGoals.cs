using System;
using UnityEngine;
using CrashKonijn.Goap.Behaviours;
using CrashKonijn.Goap.Interfaces;
using CrashKonijn.Goap.Classes;
namespace ZeroEngine.AI.GOAP
{

    /// <summary>
    /// 生存目标 - 保持存活
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "SurviveGoal")]
    public class SurviveGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            // 血量越低，生存目标优先级越高
            if (Context?.Blackboard != null)
            {
                float healthPercent = Context.Blackboard.GetFloat(BlackboardKeys.HealthPercent, 1f);
                return healthPercent; // 血量满时成本高，血量低时成本低
            }
            return 0.5f;
        }
    }

    /// <summary>
    /// 攻击敌人目标
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "AttackEnemyGoal")]
    public class AttackEnemyGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 1f;

            // 有目标时优先攻击
            if (Context.CurrentTarget != null && Context.IsInCombat)
            {
                return 0.3f;
            }

            return 1f;
        }
    }

    /// <summary>
    /// 治疗目标
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "HealGoal")]
    public class HealGoal : ZeroGOAPGoal
    {
        [SerializeField] private float _healthThreshold = 0.5f;

        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context?.Blackboard == null) return 1f;

            float healthPercent = Context.Blackboard.GetFloat(BlackboardKeys.HealthPercent, 1f);

            if (healthPercent < _healthThreshold)
            {
                return 0.2f; // 血量低时治疗优先
            }

            return 1f;
        }
    }

    /// <summary>
    /// 巡逻目标
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "PatrolGoal")]
    public class PatrolGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 0.8f;

            // 不在战斗且没有警戒时巡逻
            if (!Context.IsInCombat && !Context.IsAlerted)
            {
                return 0.4f;
            }

            return 1f;
        }
    }

    /// <summary>
    /// 逃跑目标
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "FleeGoal")]
    public class FleeGoal : ZeroGOAPGoal
    {
        [SerializeField] private float _fleeHealthThreshold = 0.2f;

        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context?.Blackboard == null) return 1f;

            float healthPercent = Context.Blackboard.GetFloat(BlackboardKeys.HealthPercent, 1f);

            // 血量极低且在战斗中时逃跑
            if (healthPercent < _fleeHealthThreshold && Context.IsInCombat)
            {
                return 0.1f; // 最高优先级
            }

            return 1f;
        }
    }

    /// <summary>
    /// 空闲目标
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "IdleGoal")]
    public class IdleGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            // 空闲是默认行为，成本适中
            return 0.9f;
        }
    }

    /// <summary>
    /// 遵循日程目标 - 与 NPCSchedule 集成
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "FollowScheduleGoal")]
    public class FollowScheduleGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 1f;

            // 不在战斗中时遵循日程
            if (!Context.IsInCombat)
            {
                // 检查是否有当前日程
                if (Context.Blackboard.Contains(BlackboardKeys.CurrentSchedule))
                {
                    return 0.3f;
                }
            }

            return 1f;
        }
    }

    /// <summary>
    /// 返回家目标
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "ReturnHomeGoal")]
    public class ReturnHomeGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 1f;

            // 不在战斗中且离家远时返回
            if (!Context.IsInCombat && !Context.IsAlerted)
            {
                Vector3 homePos = Context.Blackboard.GetVector3(BlackboardKeys.HomePosition);
                float distanceToHome = Vector3.Distance(Context.Transform.position, homePos);

                if (distanceToHome > 10f)
                {
                    return 0.5f;
                }
            }

            return 1f;
        }
    }

}
