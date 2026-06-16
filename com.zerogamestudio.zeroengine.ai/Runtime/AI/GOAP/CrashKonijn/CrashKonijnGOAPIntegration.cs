using CrashKonijn.Goap.Behaviours;
using CrashKonijn.Goap.Classes;
using CrashKonijn.Goap.Interfaces;
using UnityEngine;
using UnityEngine.AI;

namespace ZeroEngine.AI.GOAP
{
    /// <summary>
    /// ZeroEngine GOAP Agent 包装器
    /// 将 crashkonijn/GOAP 集成到 ZeroEngine AI 系统
    /// </summary>
    public class ZeroGOAPAgent : MonoBehaviour, IAIBrain
    {
        [Header("GOAP Settings")]
        [SerializeField] private AgentBehaviour _goapAgent;
        [SerializeField] private bool _autoInitialize = true;

        [Header("ZeroEngine Integration")]
        [SerializeField] private bool _syncBlackboard = true;
        [SerializeField] private float _blackboardSyncInterval = 0.1f;

        private AIContext _context;
        private float _syncTimer;
        private bool _isActive;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                if (_goapAgent != null)
                {
                    _goapAgent.enabled = value;
                }
            }
        }

        public string CurrentActionName
        {
            get
            {
                if (_goapAgent?.CurrentAction != null)
                {
                    return _goapAgent.CurrentAction.GetType().Name;
                }
                return "None";
            }
        }

        public AgentBehaviour GOAPAgent => _goapAgent;

        private void Awake()
        {
            if (_goapAgent == null)
            {
                _goapAgent = GetComponent<AgentBehaviour>();
            }
        }

        public void Initialize(AIContext context)
        {
            _context = context;

            if (_autoInitialize && _goapAgent != null)
            {
                // GOAP Agent 初始化由 GoapRunnerBehaviour 处理
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_isActive || _context == null) return;

            if (_syncBlackboard)
            {
                _syncTimer -= deltaTime;
                if (_syncTimer <= 0f)
                {
                    _syncTimer = _blackboardSyncInterval;
                    SyncBlackboard();
                }
            }
        }

        public void ForceReevaluate()
        {
            // crashkonijn/GOAP 会自动重新评估
        }

        public void StopCurrentAction()
        {
            // 由 GOAP 系统管理
        }

        public void Reset()
        {
            _syncTimer = 0f;
        }

        private void SyncBlackboard()
        {
            if (_context?.Blackboard == null) return;

            // 同步常用值到 GOAP WorldData，具体实现取决于项目需求。
        }

        public void SetWorldStateFromBlackboard(string blackboardKey, string worldKey)
        {
            if (_context?.Blackboard == null) return;

            // 将黑板值传递给 GOAP，具体映射由项目实现。
        }
    }

    /// <summary>
    /// GOAP 目标基类 - 与 ZeroEngine 集成
    /// </summary>
    public abstract class ZeroGOAPGoal : GoalBase
    {
        protected AIContext Context { get; private set; }

        public void SetContext(AIContext context)
        {
            Context = context;
        }
    }

    /// <summary>
    /// GOAP 行动基类 - 与 ZeroEngine 集成
    /// </summary>
    public abstract class ZeroGOAPAction : ActionBase
    {
        protected AIContext Context { get; private set; }

        public void SetContext(AIContext context)
        {
            Context = context;
        }

        protected T GetBlackboardValue<T>(string key, T defaultValue = default)
        {
            return Context?.Blackboard?.Get(key, defaultValue) ?? defaultValue;
        }

        protected void SetBlackboardValue<T>(string key, T value)
        {
            Context?.Blackboard?.Set(key, value);
        }
    }

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

            if (_navAgent != null)
            {
                _navAgent.SetDestination(Context.CurrentTarget.position);
            }

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

            if (Context.DistanceToTarget > _attackRange)
            {
                return ActionRunState.Stop;
            }

            if (Time.time - _lastAttackTime < _attackCooldown)
            {
                return ActionRunState.Continue;
            }

            PerformAttack();
            _lastAttackTime = Time.time;

            bool targetAlive = GetBlackboardValue("TargetAlive", true);
            if (!targetAlive)
            {
                return ActionRunState.Stop;
            }

            return ActionRunState.Continue;
        }

        private void PerformAttack()
        {
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
            int healItems = GetBlackboardValue("HealItemCount", 0);
            if (healItems <= 0)
            {
                return ActionRunState.Stop;
            }

            if (Time.time - _startTime >= _healDuration)
            {
                PerformHeal();
                return ActionRunState.Stop;
            }

            return ActionRunState.Continue;
        }

        private void PerformHeal()
        {
            int healItems = GetBlackboardValue("HealItemCount", 0);
            SetBlackboardValue("HealItemCount", healItems - 1);

#if ZEROENGINE_COMBAT
            // 使用 HealthComponent 治疗
            // var healAmount = GetBlackboardValue("HealItemAmount", 50f);
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
                Vector3 fleeDirection = -Context.DirectionToTarget;
                _fleeTarget = Context.Transform.position + fleeDirection * _fleeDistance;

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

            if (Context.DistanceToTarget >= _safeDistance)
            {
                return ActionRunState.Stop;
            }

            if (_navAgent != null && !_navAgent.pathPending &&
                _navAgent.remainingDistance <= _navAgent.stoppingDistance)
            {
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

            Collider[] colliders = Physics.OverlapSphere(
                Context.Transform.position,
                _searchRadius,
                _targetLayers);

            Transform nearestTarget = null;
            float nearestDistance = float.MaxValue;

            foreach (var collider in colliders)
            {
                if (collider.gameObject == Context.Owner) continue;

                bool isHostile = GetBlackboardValue($"IsHostile_{collider.gameObject.GetInstanceID()}", false);
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

    /// <summary>
    /// 生存目标 - 保持存活
    /// </summary>
    public class SurviveGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context?.Blackboard != null)
            {
                float healthPercent = Context.Blackboard.GetFloat(BlackboardKeys.HealthPercent, 1f);
                return healthPercent;
            }
            return 0.5f;
        }
    }

    /// <summary>
    /// 攻击敌人目标
    /// </summary>
    public class AttackEnemyGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 1f;

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
    public class HealGoal : ZeroGOAPGoal
    {
        [SerializeField] private float _healthThreshold = 0.5f;

        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context?.Blackboard == null) return 1f;

            float healthPercent = Context.Blackboard.GetFloat(BlackboardKeys.HealthPercent, 1f);

            if (healthPercent < _healthThreshold)
            {
                return 0.2f;
            }

            return 1f;
        }
    }

    /// <summary>
    /// 巡逻目标
    /// </summary>
    public class PatrolGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 0.8f;

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
    public class FleeGoal : ZeroGOAPGoal
    {
        [SerializeField] private float _fleeHealthThreshold = 0.2f;

        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context?.Blackboard == null) return 1f;

            float healthPercent = Context.Blackboard.GetFloat(BlackboardKeys.HealthPercent, 1f);

            if (healthPercent < _fleeHealthThreshold && Context.IsInCombat)
            {
                return 0.1f;
            }

            return 1f;
        }
    }

    /// <summary>
    /// 空闲目标
    /// </summary>
    public class IdleGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            return 0.9f;
        }
    }

    /// <summary>
    /// 遵循日程目标 - 与 NPCSchedule 集成
    /// </summary>
    public class FollowScheduleGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 1f;

            if (!Context.IsInCombat && Context.Blackboard.Contains(BlackboardKeys.CurrentSchedule))
            {
                return 0.3f;
            }

            return 1f;
        }
    }

    /// <summary>
    /// 返回家目标
    /// </summary>
    public class ReturnHomeGoal : ZeroGOAPGoal
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            if (Context == null) return 1f;

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
