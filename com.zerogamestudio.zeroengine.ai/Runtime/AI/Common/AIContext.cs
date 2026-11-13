using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AI
{
    /// <summary>
    /// AI 执行上下文
    /// 提供 AI 决策所需的环境信息和共享数据
    /// </summary>
    [Serializable]
    public class AIContext
    {
        #region Core References

        /// <summary>AI 所属的 GameObject</summary>
        public GameObject Owner { get; private set; }

        /// <summary>AI 所属的 Transform</summary>
        public Transform Transform { get; private set; }

        /// <summary>AI Agent 组件</summary>
        public AIAgent Agent { get; private set; }

        /// <summary>黑板数据</summary>
        public AIBlackboard Blackboard { get; private set; }

        #endregion

        #region Cached Components

        private readonly Dictionary<Type, Component> _componentCache = new();

        #endregion

        #region Target Info

        /// <summary>当前目标</summary>
        public Transform CurrentTarget { get; set; }

        /// <summary>目标位置</summary>
        public Vector3 TargetPosition { get; set; }

        /// <summary>到目标的距离</summary>
        public float DistanceToTarget => CurrentTarget != null
            ? Vector3.Distance(Transform.position, CurrentTarget.position)
            : Vector3.Distance(Transform.position, TargetPosition);

        /// <summary>到目标的方向</summary>
        public Vector3 DirectionToTarget => CurrentTarget != null
            ? (CurrentTarget.position - Transform.position).normalized
            : (TargetPosition - Transform.position).normalized;

        #endregion

        /// <summary>Optional actor-local monotonic clock. The host retains tick scheduling and pause policy.</summary>
        public Func<float> TimeProvider { get; set; }
        public float Now => TimeProvider != null ? TimeProvider() : UnityEngine.Time.time;

        #region Time Info

        /// <summary>上次决策时间</summary>
        public float LastDecisionTime { get; set; }

        /// <summary>自上次决策经过的时间</summary>
        public float TimeSinceLastDecision => Now - LastDecisionTime;

        /// <summary>当前行动开始时间</summary>
        public float ActionStartTime { get; set; }

        /// <summary>当前行动持续时间</summary>
        public float ActionDuration => Now - ActionStartTime;

        #endregion

        #region State Flags

        /// <summary>是否在战斗中</summary>
        public bool IsInCombat { get; set; }

        /// <summary>是否在移动</summary>
        public bool IsMoving { get; set; }

        /// <summary>是否被攻击</summary>
        public bool IsUnderAttack { get; set; }

        /// <summary>是否警戒状态</summary>
        public bool IsAlerted { get; set; }

        #endregion

        #region Constructor

        public AIContext(AIAgent agent)
        {
            Agent = agent;
            Owner = agent.gameObject;
            Transform = agent.transform;
            Blackboard = new AIBlackboard();
        }

        /// <summary>
        /// 用于无 Agent 场景的构造函数
        /// </summary>
        public AIContext(GameObject owner)
        {
            Owner = owner;
            Transform = owner.transform;
            Blackboard = new AIBlackboard();
        }

        #endregion

        #region Component Access

        /// <summary>
        /// 获取缓存的组件 (避免重复 GetComponent)
        /// </summary>
        public T GetCachedComponent<T>() where T : Component
        {
            var type = typeof(T);

            if (_componentCache.TryGetValue(type, out var cached))
            {
                return cached as T;
            }

            var component = Owner.GetComponent<T>();
            if (component != null)
            {
                _componentCache[type] = component;
            }

            return component;
        }

        /// <summary>
        /// 清除组件缓存
        /// </summary>
        public void ClearComponentCache()
        {
            _componentCache.Clear();
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 检查目标是否在指定范围内
        /// </summary>
        public bool IsTargetInRange(float range)
        {
            return DistanceToTarget <= range;
        }

        /// <summary>
        /// 检查目标是否在视野角度内
        /// </summary>
        public bool IsTargetInFieldOfView(float fovAngle)
        {
            if (CurrentTarget == null) return false;

            var directionToTarget = DirectionToTarget;
            var angle = Vector3.Angle(Transform.forward, directionToTarget);
            return angle <= fovAngle * 0.5f;
        }

        /// <summary>
        /// 检查是否有视线到目标
        /// </summary>
        public bool HasLineOfSightToTarget(LayerMask obstacleMask)
        {
            if (CurrentTarget == null) return false;

            var direction = CurrentTarget.position - Transform.position;
            var distance = direction.magnitude;

            return !Physics.Raycast(Transform.position, direction.normalized, distance, obstacleMask);
        }

        /// <summary>
        /// 重置上下文状态
        /// </summary>
        public void Reset()
        {
            CurrentTarget = null;
            TargetPosition = Vector3.zero;
            IsInCombat = false;
            IsMoving = false;
            IsUnderAttack = false;
            IsAlerted = false;
            LastDecisionTime = 0f;
            ActionStartTime = 0f;
            Blackboard.Clear();
        }

        #endregion
    }
}
