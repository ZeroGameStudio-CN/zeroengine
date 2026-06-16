using System;
using UnityEngine;
using UnityEngine.AI;

namespace ZeroEngine.AI.UtilityAI
{
    /// <summary>
    /// 空闲行动 - 什么都不做
    /// </summary>
    [Serializable]
    public class IdleAction : UtilityAction
    {
        [SerializeField] private float _idleDuration = 2f;

        public IdleAction()
        {
            _name = "Idle";
            _basePriority = 0.1f;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (context.ActionDuration >= _idleDuration)
            {
                return AIActionResult.Success();
            }
            return AIActionResult.Running();
        }
    }

    /// <summary>
    /// 移动到目标行动
    /// </summary>
    [Serializable]
    public class MoveToTargetAction : UtilityAction
    {
        [SerializeField] private float _stoppingDistance = 1f;
        [SerializeField] private float _timeout = 30f;
        [SerializeField] private bool _useNavMesh = true;

        private NavMeshAgent _navAgent;

        public float StoppingDistance
        {
            get => _stoppingDistance;
            set => _stoppingDistance = Mathf.Max(0.1f, value);
        }

        public MoveToTargetAction()
        {
            _name = "MoveToTarget";
        }

        protected override void OnStart(AIContext context)
        {
            if (_useNavMesh)
            {
                _navAgent = context.GetCachedComponent<NavMeshAgent>();
                if (_navAgent != null)
                {
                    Vector3 destination = context.CurrentTarget != null ?
                        context.CurrentTarget.position : context.TargetPosition;
                    _navAgent.SetDestination(destination);
                    _navAgent.stoppingDistance = _stoppingDistance;
                }
            }

            context.IsMoving = true;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            // 检查超时
            if (_timeout > 0f && context.ActionDuration >= _timeout)
            {
                return AIActionResult.Failed("Timeout");
            }

            // 检查是否到达
            float distance = context.DistanceToTarget;
            if (distance <= _stoppingDistance)
            {
                return AIActionResult.Success();
            }

            // 更新 NavMesh 目标
            if (_useNavMesh && _navAgent != null)
            {
                Vector3 destination = context.CurrentTarget != null ?
                    context.CurrentTarget.position : context.TargetPosition;

                if (Vector3.Distance(_navAgent.destination, destination) > 1f)
                {
                    _navAgent.SetDestination(destination);
                }

                // 检查路径是否有效
                if (_navAgent.pathStatus == NavMeshPathStatus.PathInvalid)
                {
                    return AIActionResult.Failed("Invalid path");
                }
            }

            return AIActionResult.Running();
        }

        protected override void OnEnd(AIContext context, AIActionResult result)
        {
            context.IsMoving = false;

            if (_navAgent != null)
            {
                _navAgent.ResetPath();
            }
        }
    }

    /// <summary>
    /// 移动到位置行动
    /// </summary>
    [Serializable]
    public class MoveToPositionAction : UtilityAction
    {
        [SerializeField] private string _positionBlackboardKey = "";
        [SerializeField] private float _stoppingDistance = 0.5f;
        [SerializeField] private float _timeout = 30f;

        private NavMeshAgent _navAgent;
        private Vector3 _targetPosition;

        public MoveToPositionAction()
        {
            _name = "MoveToPosition";
        }

        public MoveToPositionAction(string positionKey)
        {
            _positionBlackboardKey = positionKey;
            _name = $"MoveTo:{positionKey}";
        }

        protected override void OnStart(AIContext context)
        {
            // 获取目标位置
            if (!string.IsNullOrEmpty(_positionBlackboardKey))
            {
                _targetPosition = context.Blackboard.GetVector3(_positionBlackboardKey);
            }
            else
            {
                _targetPosition = context.TargetPosition;
            }

            _navAgent = context.GetCachedComponent<NavMeshAgent>();
            if (_navAgent != null)
            {
                _navAgent.SetDestination(_targetPosition);
                _navAgent.stoppingDistance = _stoppingDistance;
            }

            context.IsMoving = true;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (_timeout > 0f && context.ActionDuration >= _timeout)
            {
                return AIActionResult.Failed("Timeout");
            }

            float distance = Vector3.Distance(context.Transform.position, _targetPosition);
            if (distance <= _stoppingDistance)
            {
                return AIActionResult.Success();
            }

            return AIActionResult.Running();
        }

        protected override void OnEnd(AIContext context, AIActionResult result)
        {
            context.IsMoving = false;
            _navAgent?.ResetPath();
        }
    }

    /// <summary>
    /// 巡逻行动
    /// </summary>
    [Serializable]
    public class PatrolAction : UtilityAction
    {
        [SerializeField] private string _patrolPointsKey = "PatrolPoints";
        [SerializeField] private float _waitTime = 2f;
        [SerializeField] private float _stoppingDistance = 0.5f;
        [SerializeField] private bool _loop = true;

        private NavMeshAgent _navAgent;
        private Vector3[] _configuredPatrolPoints;
        private Vector3[] _activePatrolPoints;
        private int _currentIndex;
        private bool _isWaiting;
        private float _waitTimer;

        public PatrolAction()
        {
            _name = "Patrol";
            _basePriority = 0.3f;
        }

        protected override void OnStart(AIContext context)
        {
            _navAgent = context.GetCachedComponent<NavMeshAgent>();

            // 从黑板获取巡逻点
            _currentIndex = context.Blackboard.GetInt(BlackboardKeys.PatrolIndex, 0);
            _activePatrolPoints = _configuredPatrolPoints;
            if (!string.IsNullOrEmpty(_patrolPointsKey) &&
                context.Blackboard.TryGet<Vector3[]>(_patrolPointsKey, out var patrolPoints))
            {
                _activePatrolPoints = patrolPoints;
            }

            context.IsMoving = true;

            MoveToCurrentPoint(context);
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (_activePatrolPoints == null || _activePatrolPoints.Length == 0)
            {
                return AIActionResult.Failed("No patrol points");
            }

            if (_isWaiting)
            {
                _waitTimer -= deltaTime;
                if (_waitTimer <= 0f)
                {
                    _isWaiting = false;
                    NextPoint(context);
                    MoveToCurrentPoint(context);
                }
                return AIActionResult.Running();
            }

            // 检查是否到达当前点
            if (_navAgent != null && !_navAgent.pathPending)
            {
                if (_navAgent.remainingDistance <= _stoppingDistance)
                {
                    _isWaiting = true;
                    _waitTimer = _waitTime;
                }
            }

            return AIActionResult.Running();
        }

        private void MoveToCurrentPoint(AIContext context)
        {
            if (_navAgent != null && _activePatrolPoints != null && _currentIndex < _activePatrolPoints.Length)
            {
                _navAgent.SetDestination(_activePatrolPoints[_currentIndex]);
            }
        }

        private void NextPoint(AIContext context)
        {
            _currentIndex++;

            if (_currentIndex >= _activePatrolPoints.Length)
            {
                if (_loop)
                {
                    _currentIndex = 0;
                }
                else
                {
                    _currentIndex = _activePatrolPoints.Length - 1;
                }
            }

            context.Blackboard.Set(BlackboardKeys.PatrolIndex, _currentIndex);
        }

        protected override void OnEnd(AIContext context, AIActionResult result)
        {
            context.IsMoving = false;
        }

        /// <summary>
        /// 设置巡逻点
        /// </summary>
        public void SetPatrolPoints(Vector3[] points)
        {
            _configuredPatrolPoints = points;
            _activePatrolPoints = points;
            _currentIndex = 0;
        }
    }

    /// <summary>
    /// 逃跑行动
    /// </summary>
    [Serializable]
    public class FleeAction : UtilityAction
    {
        [SerializeField] private float _fleeDistance = 15f;
        [SerializeField] private float _safeDistance = 20f;
        [SerializeField] private float _timeout = 10f;

        private NavMeshAgent _navAgent;
        private Vector3 _fleeTarget;

        public FleeAction()
        {
            _name = "Flee";
            // 血量低时优先逃跑
            AddConsideration(new HealthConsideration(true, true) { Weight = 2f });
            AddConsideration(new CombatStateConsideration(CombatStateConsideration.CombatStateType.InCombat));
        }

        protected override void OnStart(AIContext context)
        {
            _navAgent = context.GetCachedComponent<NavMeshAgent>();

            // 计算逃跑方向 (远离目标)
            Vector3 fleeDirection = -context.DirectionToTarget;
            _fleeTarget = context.Transform.position + fleeDirection * _fleeDistance;

            // 确保逃跑点在 NavMesh 上
            if (NavMesh.SamplePosition(_fleeTarget, out NavMeshHit hit, _fleeDistance, NavMesh.AllAreas))
            {
                _fleeTarget = hit.position;
            }

            if (_navAgent != null)
            {
                _navAgent.SetDestination(_fleeTarget);
            }

            context.IsMoving = true;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (_timeout > 0f && context.ActionDuration >= _timeout)
            {
                return AIActionResult.Failed("Timeout");
            }

            // 检查是否已经安全
            if (context.DistanceToTarget >= _safeDistance)
            {
                return AIActionResult.Success();
            }

            // 检查是否到达逃跑点
            if (_navAgent != null && !_navAgent.pathPending &&
                _navAgent.remainingDistance <= _navAgent.stoppingDistance)
            {
                // 如果还不够安全，继续逃跑
                if (context.DistanceToTarget < _safeDistance)
                {
                    OnStart(context); // 重新计算逃跑点
                }
                else
                {
                    return AIActionResult.Success();
                }
            }

            return AIActionResult.Running();
        }

        protected override void OnEnd(AIContext context, AIActionResult result)
        {
            context.IsMoving = false;
            _navAgent?.ResetPath();
        }
    }

    /// <summary>
    /// 等待行动
    /// </summary>
    [Serializable]
    public class WaitAction : UtilityAction
    {
        [SerializeField] private float _duration = 1f;
        [SerializeField] private bool _randomize = false;
        [SerializeField] private float _minDuration = 0.5f;
        [SerializeField] private float _maxDuration = 2f;

        private float _actualDuration;

        public WaitAction()
        {
            _name = "Wait";
        }

        public WaitAction(float duration)
        {
            _duration = duration;
            _name = $"Wait({duration}s)";
        }

        protected override void OnStart(AIContext context)
        {
            _actualDuration = _randomize ?
                UnityEngine.Random.Range(_minDuration, _maxDuration) : _duration;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (context.ActionDuration >= _actualDuration)
            {
                return AIActionResult.Success();
            }
            return AIActionResult.Running();
        }
    }

    /// <summary>
    /// 回家/返回原点行动
    /// </summary>
    [Serializable]
    public class ReturnHomeAction : UtilityAction
    {
        [SerializeField] private float _stoppingDistance = 1f;
        [SerializeField] private float _timeout = 60f;

        private NavMeshAgent _navAgent;
        private Vector3 _homePosition;

        public ReturnHomeAction()
        {
            _name = "ReturnHome";
            // 不在战斗中时优先回家
            AddConsideration(new CombatStateConsideration(CombatStateConsideration.CombatStateType.NotInCombat));
        }

        protected override void OnStart(AIContext context)
        {
            _navAgent = context.GetCachedComponent<NavMeshAgent>();
            _homePosition = context.Blackboard.GetVector3(BlackboardKeys.HomePosition, context.Transform.position);

            if (_navAgent != null)
            {
                _navAgent.SetDestination(_homePosition);
                _navAgent.stoppingDistance = _stoppingDistance;
            }

            context.IsMoving = true;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (_timeout > 0f && context.ActionDuration >= _timeout)
            {
                return AIActionResult.Failed("Timeout");
            }

            float distance = Vector3.Distance(context.Transform.position, _homePosition);
            if (distance <= _stoppingDistance)
            {
                return AIActionResult.Success();
            }

            return AIActionResult.Running();
        }

        protected override void OnEnd(AIContext context, AIActionResult result)
        {
            context.IsMoving = false;
        }
    }

    /// <summary>
    /// 设置黑板值行动 - 用于状态变更
    /// </summary>
    [Serializable]
    public class SetBlackboardAction : UtilityAction
    {
        [SerializeField] private string _key = "";
        [SerializeField] private BlackboardValueType _valueType = BlackboardValueType.Bool;
        [SerializeField] private bool _boolValue = true;
        [SerializeField] private int _intValue = 0;
        [SerializeField] private float _floatValue = 0f;
        [SerializeField] private string _stringValue = "";

        public enum BlackboardValueType
        {
            Bool,
            Int,
            Float,
            String
        }

        public SetBlackboardAction()
        {
            _name = "SetBlackboard";
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (context?.Blackboard == null || string.IsNullOrEmpty(_key))
            {
                return AIActionResult.Failed("Invalid blackboard or key");
            }

            switch (_valueType)
            {
                case BlackboardValueType.Bool:
                    context.Blackboard.Set(_key, _boolValue);
                    break;
                case BlackboardValueType.Int:
                    context.Blackboard.Set(_key, _intValue);
                    break;
                case BlackboardValueType.Float:
                    context.Blackboard.Set(_key, _floatValue);
                    break;
                case BlackboardValueType.String:
                    context.Blackboard.Set(_key, _stringValue);
                    break;
            }

            return AIActionResult.Success();
        }
    }
}
