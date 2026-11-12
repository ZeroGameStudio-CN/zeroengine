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

#if CRASHKONIJN_GOAP
    using CrashKonijn.Goap.Behaviours;
    using CrashKonijn.Goap.Interfaces;
    using CrashKonijn.Goap.Classes;
    using UnityEngine.AI;

    /// <summary>
    /// 移动到目标行动
    /// </summary>
    public class GOAPMoveToTargetAction : ZeroGOAPAction
    {
        private NavMeshAgent _navAgent;
        private float _stoppingDistance = 2f;

        public override void Start(IMonoAgent agent, IActionData data)
        {
            base.Start(agent, data);

            _navAgent = agent.GetComponent<NavMeshAgent>();
            if (_navAgent != null && Context?.CurrentTarget != null)
            {
                _navAgent.SetDestination(Context.CurrentTarget.position);
                _navAgent.stoppingDistance = _stoppingDistance;
            }

            if (Context != null)
            {
                Context.IsMoving = true;
            }
        }

        public override ActionRunState Perform(IMonoAgent agent, IActionData data, ActionContext context)
        {
            if (Context?.CurrentTarget == null)
            {
                return ActionRunState.Stop;
            }

            // 更新目标位置
            if (_navAgent != null)
            {
                _navAgent.SetDestination(Context.CurrentTarget.position);
            }

            // 检查是否到达
            float distance = Context.DistanceToTarget;
            if (distance <= _stoppingDistance)
            {
                return ActionRunState.Stop;
            }

            return ActionRunState.Continue;
        }

        public override void End(IMonoAgent agent, IActionData data)
        {
            base.End(agent, data);

            if (Context != null)
            {
                Context.IsMoving = false;
            }
        }
    }

    /// <summary>
    /// 攻击行动
    /// </summary>
    public class GOAPAttackAction : ZeroGOAPAction
    {
        private float _attackRange = 2f;
        private float _attackCooldown = 1f;
        private float _lastAttackTime;

        public override ActionRunState Perform(IMonoAgent agent, IActionData data, ActionContext context)
        {
            if (Context?.CurrentTarget == null)
            {
                return ActionRunState.Stop;
            }

            // 检查攻击范围
            if (Context.DistanceToTarget > _attackRange)
            {
                return ActionRunState.Stop; // 需要移动到目标
            }

            // 检查冷却
            if (Time.time - _lastAttackTime < _attackCooldown)
            {
                return ActionRunState.Continue;
            }

            // 执行攻击
            PerformAttack();
            _lastAttackTime = Time.time;

            // 检查目标是否死亡
            bool targetAlive = GetBlackboardValue("TargetAlive", true);
            if (!targetAlive)
            {
                return ActionRunState.Stop;
            }

            return ActionRunState.Continue;
        }

        private void PerformAttack()
        {
            // 实际攻击逻辑由游戏实现
            // 这里只设置黑板标志
            SetBlackboardValue("LastAttackTime", Time.time);

#if ZEROENGINE_COMBAT
            // 如果有 Combat 系统，使用 CombatManager
            // CombatManager.Instance.DealDamage(...);
#endif
        }
    }

    /// <summary>
    /// 使用治疗物品行动
    /// </summary>
    public class GOAPUseHealItemAction : ZeroGOAPAction
    {
        private float _healDuration = 1f;
        private float _startTime;

        public override void Start(IMonoAgent agent, IActionData data)
        {
            base.Start(agent, data);
            _startTime = Time.time;
        }

        public override ActionRunState Perform(IMonoAgent agent, IActionData data, ActionContext context)
        {
            // 检查是否有治疗物品
            int healItems = GetBlackboardValue("HealItemCount", 0);
            if (healItems <= 0)
            {
                return ActionRunState.Stop;
            }

            // 等待治疗动画/时间
            if (Time.time - _startTime >= _healDuration)
            {
                // 执行治疗
                PerformHeal();
                return ActionRunState.Stop;
            }

            return ActionRunState.Continue;
        }

        private void PerformHeal()
        {
            // 减少物品数量
            int healItems = GetBlackboardValue("HealItemCount", 0);
            SetBlackboardValue("HealItemCount", healItems - 1);

#if ZEROENGINE_COMBAT
            // 使用 HealthComponent 治疗
            var health = Context?.Owner?.GetComponent<Combat.HealthComponent>();
            if (health != null)
            {
                float healAmount = GetBlackboardValue("HealItemAmount", 50f);
                health.Heal(healAmount);
            }
#endif
        }
    }

    /// <summary>
    /// 逃跑行动
    /// </summary>
    public class GOAPFleeAction : ZeroGOAPAction
    {
        private NavMeshAgent _navAgent;
        private float _fleeDistance = 15f;
        private float _safeDistance = 20f;
        private Vector3 _fleeTarget;

        public override void Start(IMonoAgent agent, IActionData data)
        {
            base.Start(agent, data);

            _navAgent = agent.GetComponent<NavMeshAgent>();

            if (Context != null)
            {
                // 计算逃跑方向
                Vector3 fleeDirection = -Context.DirectionToTarget;
                _fleeTarget = Context.Transform.position + fleeDirection * _fleeDistance;

                // 确保在 NavMesh 上
                if (NavMesh.SamplePosition(_fleeTarget, out NavMeshHit hit, _fleeDistance, NavMesh.AllAreas))
                {
                    _fleeTarget = hit.position;
                }

                if (_navAgent != null)
                {
                    _navAgent.SetDestination(_fleeTarget);
                }

                Context.IsMoving = true;
            }
        }

        public override ActionRunState Perform(IMonoAgent agent, IActionData data, ActionContext context)
        {
            if (Context == null)
            {
                return ActionRunState.Stop;
            }

            // 检查是否安全
            if (Context.DistanceToTarget >= _safeDistance)
            {
                return ActionRunState.Stop;
            }

            // 检查是否到达逃跑点
            if (_navAgent != null && !_navAgent.pathPending &&
                _navAgent.remainingDistance <= _navAgent.stoppingDistance)
            {
                // 继续逃跑
                Vector3 fleeDirection = -Context.DirectionToTarget;
                _fleeTarget = Context.Transform.position + fleeDirection * _fleeDistance;

                if (NavMesh.SamplePosition(_fleeTarget, out NavMeshHit hit, _fleeDistance, NavMesh.AllAreas))
                {
                    _navAgent.SetDestination(hit.position);
                }
            }

            return ActionRunState.Continue;
        }

        public override void End(IMonoAgent agent, IActionData data)
        {
            base.End(agent, data);

            if (Context != null)
            {
                Context.IsMoving = false;
            }
        }
    }

    /// <summary>
    /// 寻找目标行动
    /// </summary>
    public class GOAPFindTargetAction : ZeroGOAPAction
    {
        private float _searchRadius = 15f;
        private LayerMask _targetLayers = -1;

        public override ActionRunState Perform(IMonoAgent agent, IActionData data, ActionContext context)
        {
            if (Context == null)
            {
                return ActionRunState.Stop;
            }

            // 搜索附近目标
            Collider[] colliders = Physics.OverlapSphere(
                Context.Transform.position,
                _searchRadius,
                _targetLayers
            );

            Transform nearestTarget = null;
            float nearestDistance = float.MaxValue;

            foreach (var collider in colliders)
            {
                if (collider.gameObject == Context.Owner) continue;

                // 检查是否是敌对目标
#if UNITY_6000_6_OR_NEWER
                bool isHostile = GetBlackboardValue($"IsHostile_{EntityId.ToULong(collider.gameObject.GetEntityId())}", false);
#else
                bool isHostile = GetBlackboardValue($"IsHostile_{collider.gameObject.GetInstanceID()}", false);
#endif
                if (!isHostile) continue;

                float distance = Vector3.Distance(Context.Transform.position, collider.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestTarget = collider.transform;
                }
            }

            if (nearestTarget != null)
            {
                Context.CurrentTarget = nearestTarget;
                SetBlackboardValue(BlackboardKeys.Target, nearestTarget.gameObject);
                SetBlackboardValue(BlackboardKeys.TargetDistance, nearestDistance);
            }

            return ActionRunState.Stop;
        }
    }

    /// <summary>
    /// 等待行动
    /// </summary>
    public class GOAPWaitAction : ZeroGOAPAction
    {
        private float _waitDuration = 2f;
        private float _startTime;

        public override void Start(IMonoAgent agent, IActionData data)
        {
            base.Start(agent, data);
            _startTime = Time.time;
        }

        public override ActionRunState Perform(IMonoAgent agent, IActionData data, ActionContext context)
        {
            if (Time.time - _startTime >= _waitDuration)
            {
                return ActionRunState.Stop;
            }

            return ActionRunState.Continue;
        }
    }

    /// <summary>
    /// 返回家行动
    /// </summary>
    public class GOAPReturnHomeAction : ZeroGOAPAction
    {
        private NavMeshAgent _navAgent;
        private float _stoppingDistance = 1f;

        public override void Start(IMonoAgent agent, IActionData data)
        {
            base.Start(agent, data);

            _navAgent = agent.GetComponent<NavMeshAgent>();

            if (Context != null && _navAgent != null)
            {
                Vector3 homePos = GetBlackboardValue(BlackboardKeys.HomePosition, Context.Transform.position);
                _navAgent.SetDestination(homePos);
                _navAgent.stoppingDistance = _stoppingDistance;
                Context.IsMoving = true;
            }
        }

        public override ActionRunState Perform(IMonoAgent agent, IActionData data, ActionContext context)
        {
            if (_navAgent == null)
            {
                return ActionRunState.Stop;
            }

            if (!_navAgent.pathPending && _navAgent.remainingDistance <= _stoppingDistance)
            {
                return ActionRunState.Stop;
            }

            return ActionRunState.Continue;
        }

        public override void End(IMonoAgent agent, IActionData data)
        {
            base.End(agent, data);

            if (Context != null)
            {
                Context.IsMoving = false;
            }
        }
    }
#endif
}
