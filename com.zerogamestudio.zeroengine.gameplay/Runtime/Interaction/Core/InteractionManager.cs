using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Interaction
{
    /// <summary>
    /// 交互管理器 (v1.14.0+)
    /// 管理所有可交互对象的注册、检测和交互逻辑
    /// </summary>
    public class InteractionManager : MonoSingleton<InteractionManager>
    {
        #region Static Cache (GC 优化)

        // 用于 GetNearbyInteractables 的复用列表
        private static readonly List<IInteractable> TempInteractableList = new(16);

        // 用于排序的比较器
        private static InteractableDistanceComparer _distanceComparer;

        private class InteractableDistanceComparer : IComparer<IInteractable>
        {
            public Vector3 ReferencePosition;

            public int Compare(IInteractable a, IInteractable b)
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;

                float distA = Vector3.SqrMagnitude(a.GetInteractionPosition() - ReferencePosition);
                float distB = Vector3.SqrMagnitude(b.GetInteractionPosition() - ReferencePosition);

                // 先按优先级排序 (高优先级在前)
                int priorityCompare = ((int)b.Priority).CompareTo((int)a.Priority);
                if (priorityCompare != 0) return priorityCompare;

                // 同优先级按距离排序
                return distA.CompareTo(distB);
            }
        }

        #endregion

        #region Serialized Fields

        [Header("Settings")]
        [SerializeField]
        [Tooltip("默认交互检测距离")]
        private float _defaultDetectionRadius = 5f;

        [SerializeField]
        [Tooltip("默认交互距离")]
        private float _defaultInteractionDistance = 2f;

        [SerializeField]
        [Tooltip("启用调试日志")]
        private bool _debugMode = false;

        #endregion

        #region Private Fields

        // 已注册的可交互对象
        private readonly HashSet<IInteractable> _registeredInteractables = new();

        // 按类型索引
        private readonly Dictionary<InteractionType, HashSet<IInteractable>> _interactablesByType = new();

        // 当前焦点目标
        private IInteractable _currentFocusTarget;

        #endregion

        #region Properties

        /// <summary>默认检测距离</summary>
        public float DefaultDetectionRadius
        {
            get => _defaultDetectionRadius;
            set => _defaultDetectionRadius = Mathf.Max(0, value);
        }

        /// <summary>默认交互距离</summary>
        public float DefaultInteractionDistance
        {
            get => _defaultInteractionDistance;
            set => _defaultInteractionDistance = Mathf.Max(0, value);
        }

        /// <summary>当前焦点目标</summary>
        public IInteractable CurrentFocusTarget => _currentFocusTarget;

        /// <summary>已注册的可交互对象数量</summary>
        public int RegisteredCount => _registeredInteractables.Count;

        #endregion

        #region Events

        /// <summary>可交互对象进入范围</summary>
        public event Action<IInteractable> OnInteractableEnterRange;

        /// <summary>可交互对象离开范围</summary>
        public event Action<IInteractable> OnInteractableExitRange;

        /// <summary>交互开始</summary>
        public event Action<IInteractable> OnInteractionStarted;

        /// <summary>交互完成</summary>
        public event Action<InteractionEventArgs> OnInteractionCompleted;

        /// <summary>交互失败</summary>
        public event Action<IInteractable, string> OnInteractionFailed;

        /// <summary>焦点目标变更</summary>
        public event Action<IInteractable, IInteractable> OnFocusTargetChanged;

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();

            // 初始化类型索引
            foreach (InteractionType type in Enum.GetValues(typeof(InteractionType)))
            {
                _interactablesByType[type] = new HashSet<IInteractable>();
            }

            // 初始化比较器
            _distanceComparer = new InteractableDistanceComparer();
        }

        #endregion

        #region Registration API

        /// <summary>
        /// 注册可交互对象
        /// </summary>
        public void RegisterInteractable(IInteractable interactable)
        {
            if (interactable == null) return;

            if (_registeredInteractables.Add(interactable))
            {
                // 添加到类型索引
                if (_interactablesByType.TryGetValue(interactable.Type, out var typeSet))
                {
                    typeSet.Add(interactable);
                }

                LogDebug($"Registered: {interactable.DisplayName} ({interactable.InteractableId})");
            }
        }

        /// <summary>
        /// 注销可交互对象
        /// </summary>
        public void UnregisterInteractable(IInteractable interactable)
        {
            if (interactable == null) return;

            if (_registeredInteractables.Remove(interactable))
            {
                // 从类型索引移除
                if (_interactablesByType.TryGetValue(interactable.Type, out var typeSet))
                {
                    typeSet.Remove(interactable);
                }

                // 如果是当前焦点，清除焦点
                if (_currentFocusTarget == interactable)
                {
                    SetFocusTarget(null);
                }

                LogDebug($"Unregistered: {interactable.DisplayName}");
            }
        }

        /// <summary>
        /// 检查对象是否已注册
        /// </summary>
        public bool IsRegistered(IInteractable interactable)
        {
            return interactable != null && _registeredInteractables.Contains(interactable);
        }

        #endregion

        #region Query API

        /// <summary>
        /// 获取指定位置附近的可交互对象 (按距离和优先级排序)
        /// </summary>
        /// <param name="position">检测中心位置</param>
        /// <param name="radius">检测半径 (0 使用默认值)</param>
        /// <param name="results">结果列表 (复用以减少 GC)</param>
        public void GetNearbyInteractables(Vector3 position, float radius, List<IInteractable> results)
        {
            results.Clear();

            float effectiveRadius = radius > 0 ? radius : _defaultDetectionRadius;
            float sqrRadius = effectiveRadius * effectiveRadius;

            foreach (var interactable in _registeredInteractables)
            {
                if (interactable == null || !interactable.IsEnabled) continue;

                float sqrDist = Vector3.SqrMagnitude(interactable.GetInteractionPosition() - position);
                if (sqrDist <= sqrRadius)
                {
                    results.Add(interactable);
                }
            }

            // 排序
            if (results.Count > 1)
            {
                _distanceComparer.ReferencePosition = position;
                results.Sort(_distanceComparer);
            }
        }

        /// <summary>
        /// 获取指定位置附近的可交互对象 (返回新列表)
        /// </summary>
        public List<IInteractable> GetNearbyInteractables(Vector3 position, float radius = 0)
        {
            var results = new List<IInteractable>();
            GetNearbyInteractables(position, radius, results);
            return results;
        }

        /// <summary>
        /// 获取最近的可交互对象
        /// </summary>
        public IInteractable GetNearestInteractable(Vector3 position, float radius = 0)
        {
            TempInteractableList.Clear();
            GetNearbyInteractables(position, radius, TempInteractableList);
            return TempInteractableList.Count > 0 ? TempInteractableList[0] : null;
        }

        /// <summary>
        /// 获取指定类型的所有可交互对象
        /// </summary>
        public IReadOnlyCollection<IInteractable> GetInteractablesByType(InteractionType type)
        {
            if (_interactablesByType.TryGetValue(type, out var set))
            {
                return set;
            }
            return Array.Empty<IInteractable>();
        }

        /// <summary>
        /// 通过 ID 查找可交互对象
        /// </summary>
        public IInteractable FindById(string interactableId)
        {
            if (string.IsNullOrEmpty(interactableId)) return null;

            foreach (var interactable in _registeredInteractables)
            {
                if (interactable.InteractableId == interactableId)
                {
                    return interactable;
                }
            }
            return null;
        }

        #endregion

        #region Interaction API

        /// <summary>
        /// 尝试与目标交互
        /// </summary>
        /// <param name="target">交互目标</param>
        /// <param name="interactor">交互者 GameObject</param>
        /// <returns>交互结果</returns>
        public InteractionResult TryInteract(IInteractable target, GameObject interactor)
        {
            if (target == null)
            {
                return InteractionResult.Failed(null, "No target");
            }

            if (!target.IsEnabled)
            {
                return InteractionResult.Failed(target, "Target is disabled");
            }

            // 创建交互上下文
            Vector3 interactorPos = interactor != null ? interactor.transform.position : Vector3.zero;
            float distance = Vector3.Distance(interactorPos, target.GetInteractionPosition());
            var ctx = new InteractionContext(interactor, interactorPos, distance);

            // 检查是否可交互
            if (!target.CanInteract(ctx))
            {
                string reason = target.GetCannotInteractReason(ctx);
                LogDebug($"Cannot interact with {target.DisplayName}: {reason}");
                OnInteractionFailed?.Invoke(target, reason);
                return InteractionResult.Failed(target, reason);
            }

            // 触发交互开始事件
            OnInteractionStarted?.Invoke(target);
            LogDebug($"Interaction started: {target.DisplayName}");

            // 执行交互
            var result = target.OnInteract(ctx);

            // 触发完成事件
            var eventArgs = new InteractionEventArgs(target, ctx, result);
            OnInteractionCompleted?.Invoke(eventArgs);

            // 触发任务系统事件 (如果集成)
            TriggerQuestEvent(target);

            if (result.Success)
            {
                LogDebug($"Interaction completed: {target.DisplayName}");
            }
            else
            {
                LogDebug($"Interaction failed: {target.DisplayName} - {result.FailureReason}");
                OnInteractionFailed?.Invoke(target, result.FailureReason);
            }

            return result;
        }

        /// <summary>
        /// 尝试与当前焦点目标交互
        /// </summary>
        public InteractionResult TryInteractWithFocus(GameObject interactor)
        {
            return TryInteract(_currentFocusTarget, interactor);
        }

        #endregion

        #region Focus API

        /// <summary>
        /// 设置焦点目标
        /// </summary>
        public void SetFocusTarget(IInteractable target)
        {
            if (_currentFocusTarget == target) return;

            var previous = _currentFocusTarget;

            // 通知旧目标失去焦点
            previous?.OnUnfocus();

            _currentFocusTarget = target;

            // 通知新目标获得焦点
            target?.OnFocus();

            // 触发事件
            OnFocusTargetChanged?.Invoke(previous, target);

            LogDebug($"Focus changed: {previous?.DisplayName ?? "null"} -> {target?.DisplayName ?? "null"}");
        }

        /// <summary>
        /// 更新焦点目标 (通常由 InteractionDetector 调用)
        /// </summary>
        public void UpdateFocusTarget(Vector3 position, float radius = 0)
        {
            var nearest = GetNearestInteractable(position, radius);
            SetFocusTarget(nearest);
        }

        /// <summary>
        /// 清除焦点
        /// </summary>
        public void ClearFocus()
        {
            SetFocusTarget(null);
        }

        /// <summary>
        /// 通知可交互对象进入检测范围。
        /// </summary>
        public void NotifyInteractableEnteredRange(IInteractable interactable)
        {
            if (interactable == null) return;

            OnInteractableEnterRange?.Invoke(interactable);
        }

        /// <summary>
        /// 通知可交互对象离开检测范围。
        /// </summary>
        public void NotifyInteractableExitedRange(IInteractable interactable)
        {
            if (interactable == null) return;

            OnInteractableExitRange?.Invoke(interactable);

            if (_currentFocusTarget == interactable)
            {
                SetFocusTarget(null);
            }
        }

        #endregion

        #region Quest Integration

        private void TriggerQuestEvent(IInteractable target)
        {
            // 与 Quest 系统集成
#if ZEROENGINE_QUEST
            try
            {
                var questManager = ZeroEngine.Quest.QuestManager.Instance;
                if (questManager != null)
                {
                    questManager.ProcessConditionEvent(
                        ZeroEngine.Quest.QuestEvents.Interacted,
                        new ZeroEngine.Quest.ConditionEventData
                        {
                            TargetId = target.InteractableId,
                            Amount = 1
                        }
                    );
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeroEngine.Interaction] Quest integration error: {e.Message}");
            }
#endif
        }

        #endregion

        #region Debug

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogDebug(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[ZeroEngine.Interaction] {message}");
            }
        }

        #endregion
    }
}
