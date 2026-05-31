using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Interaction
{
    /// <summary>
    /// 交互检测器 (v1.14.0+)
    /// 挂载在玩家角色上，检测并管理附近的可交互对象
    /// </summary>
    public class InteractionDetector : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Detection Settings")]
        [SerializeField]
        [Tooltip("检测半径")]
        private float _detectionRadius = 5f;

        [SerializeField]
        [Tooltip("最大交互距离")]
        private float _interactionDistance = 2f;

        [SerializeField]
        [Tooltip("检测频率 (每秒次数)")]
        [Range(1, 60)]
        private int _detectionRate = 10;

        [SerializeField]
        [Tooltip("检测中心偏移")]
        private Vector3 _detectionOffset = Vector3.zero;

        [Header("Input Settings")]
        [SerializeField]
        [Tooltip("交互按键")]
        private KeyCode _interactKey = KeyCode.E;

        [SerializeField]
        [Tooltip("使用 Input System (新输入系统)")]
        private bool _useNewInputSystem = false;

        [SerializeField]
        [Tooltip("Input Action 名称 (新输入系统)")]
        private string _interactActionName = "Interact";

        [Header("Visual Feedback")]
        [SerializeField]
        [Tooltip("启用轮廓高亮")]
        private bool _enableOutline = true;

        [SerializeField]
        [Tooltip("高亮颜色")]
        private Color _outlineColor = Color.yellow;

        [Header("Debug")]
        [SerializeField]
        private bool _showDebugGizmos = true;

        #endregion

        #region Private Fields

        private float _detectionInterval;
        private float _lastDetectionTime;

        // 复用列表避免 GC
        private readonly List<IInteractable> _nearbyInteractables = new(8);
        private readonly List<IInteractable> _previousNearby = new(8);

        private IInteractable _currentTarget;
        private bool _isInteracting;

        #endregion

        #region Properties

        /// <summary>检测半径</summary>
        public float DetectionRadius
        {
            get => _detectionRadius;
            set => _detectionRadius = Mathf.Max(0, value);
        }

        /// <summary>交互距离</summary>
        public float InteractionDistance
        {
            get => _interactionDistance;
            set => _interactionDistance = Mathf.Max(0, value);
        }

        /// <summary>当前目标</summary>
        public IInteractable CurrentTarget => _currentTarget;

        /// <summary>是否正在交互</summary>
        public bool IsInteracting => _isInteracting;

        /// <summary>附近可交互对象列表 (只读)</summary>
        public IReadOnlyList<IInteractable> NearbyInteractables => _nearbyInteractables;

        /// <summary>检测中心位置</summary>
        public Vector3 DetectionCenter => transform.position + transform.TransformDirection(_detectionOffset);

        #endregion

        #region Events

        /// <summary>当前目标变更</summary>
        public event Action<IInteractable> OnTargetChanged;

        /// <summary>可交互对象进入范围</summary>
        public event Action<IInteractable> OnInteractableEnterRange;

        /// <summary>可交互对象离开范围</summary>
        public event Action<IInteractable> OnInteractableExitRange;

        /// <summary>交互请求</summary>
        public event Action<IInteractable> OnInteractRequested;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            _detectionInterval = 1f / _detectionRate;
        }

        private void Update()
        {
            // 检测周期
            if (Time.time - _lastDetectionTime >= _detectionInterval)
            {
                _lastDetectionTime = Time.time;
                DetectInteractables();
            }

            // 处理输入
            HandleInput();
        }

        #endregion

        #region Detection

        private void DetectInteractables()
        {
            if (InteractionManager.Instance == null) return;

            // 保存上一帧的结果
            _previousNearby.Clear();
            _previousNearby.AddRange(_nearbyInteractables);

            // 获取新的检测结果
            InteractionManager.Instance.GetNearbyInteractables(DetectionCenter, _detectionRadius, _nearbyInteractables);

            // 检测进入/离开事件
            CheckEnterExitEvents();

            // 更新当前目标
            UpdateCurrentTarget();
        }

        private void CheckEnterExitEvents()
        {
            var interactionManager = InteractionManager.Instance;

            // 检测新进入的对象
            foreach (var interactable in _nearbyInteractables)
            {
                if (!_previousNearby.Contains(interactable))
                {
                    OnInteractableEnterRange?.Invoke(interactable);
                    interactionManager?.NotifyInteractableEnteredRange(interactable);
                }
            }

            // 检测离开的对象
            foreach (var interactable in _previousNearby)
            {
                if (!_nearbyInteractables.Contains(interactable))
                {
                    OnInteractableExitRange?.Invoke(interactable);
                    interactionManager?.NotifyInteractableExitedRange(interactable);
                }
            }
        }

        private void UpdateCurrentTarget()
        {
            IInteractable newTarget = null;

            // 在交互距离内找到最佳目标
            foreach (var interactable in _nearbyInteractables)
            {
                float distance = Vector3.Distance(DetectionCenter, interactable.GetInteractionPosition());
                if (distance <= _interactionDistance && interactable.IsEnabled)
                {
                    newTarget = interactable;
                    break; // 已按优先级和距离排序，取第一个
                }
            }

            SetCurrentTarget(newTarget);
        }

        private void SetCurrentTarget(IInteractable target)
        {
            if (_currentTarget == target) return;

            var previous = _currentTarget;
            _currentTarget = target;

            // 更新 InteractionManager 的焦点
            InteractionManager.Instance?.SetFocusTarget(target);

            // 触发事件
            OnTargetChanged?.Invoke(target);

            // 视觉反馈
            if (_enableOutline)
            {
                UpdateOutline(previous, target);
            }
        }

        private void UpdateOutline(IInteractable previous, IInteractable current)
        {
            // TODO: 实现轮廓高亮效果
            // 可以使用 Quick Outline 等插件，或自定义 Shader
        }

        #endregion

        #region Input

        private void HandleInput()
        {
            if (_isInteracting) return;

            bool interactPressed = false;

            if (_useNewInputSystem)
            {
#if ENABLE_INPUT_SYSTEM
                // 使用新输入系统
                var inputAction = UnityEngine.InputSystem.InputSystem.actions?.FindAction(_interactActionName);
                if (inputAction != null)
                {
                    interactPressed = inputAction.WasPressedThisFrame();
                }
#endif
            }
            else
            {
                // 使用旧输入系统
                interactPressed = Input.GetKeyDown(_interactKey);
            }

            if (interactPressed)
            {
                TryInteract();
            }
        }

        /// <summary>
        /// 尝试与当前目标交互
        /// </summary>
        public void TryInteract()
        {
            if (_currentTarget == null) return;
            if (_isInteracting) return;

            OnInteractRequested?.Invoke(_currentTarget);

            var result = InteractionManager.Instance?.TryInteract(_currentTarget, gameObject);
            if (result.HasValue && result.Value.Success)
            {
                // 可以在这里处理交互成功后的逻辑
            }
        }

        /// <summary>
        /// 强制与指定目标交互 (忽略距离检测)
        /// </summary>
        public InteractionResult ForceInteract(IInteractable target)
        {
            if (target == null)
            {
                return InteractionResult.Failed(null, "No target");
            }

            return InteractionManager.Instance?.TryInteract(target, gameObject)
                ?? InteractionResult.Failed(target, "InteractionManager not available");
        }

        #endregion

        #region Public API

        /// <summary>
        /// 手动刷新检测
        /// </summary>
        public void RefreshDetection()
        {
            DetectInteractables();
        }

        /// <summary>
        /// 设置检测参数
        /// </summary>
        public void SetDetectionParams(float radius, float interactionDistance)
        {
            _detectionRadius = Mathf.Max(0, radius);
            _interactionDistance = Mathf.Max(0, interactionDistance);
        }

        /// <summary>
        /// 启用/禁用检测
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                SetCurrentTarget(null);
                _nearbyInteractables.Clear();
            }
        }

        /// <summary>
        /// 获取与目标的距离
        /// </summary>
        public float GetDistanceTo(IInteractable target)
        {
            if (target == null) return float.MaxValue;
            return Vector3.Distance(DetectionCenter, target.GetInteractionPosition());
        }

        /// <summary>
        /// 检查目标是否在交互范围内
        /// </summary>
        public bool IsInInteractionRange(IInteractable target)
        {
            return GetDistanceTo(target) <= _interactionDistance;
        }

        #endregion

        #region Editor

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_showDebugGizmos) return;

            Vector3 center = Application.isPlaying ? DetectionCenter : transform.position + _detectionOffset;

            // 检测范围
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawWireSphere(center, _detectionRadius);

            // 交互范围
            Gizmos.color = new Color(1, 1, 0, 0.3f);
            Gizmos.DrawWireSphere(center, _interactionDistance);

            // 当前目标连线
            if (Application.isPlaying && _currentTarget != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(center, _currentTarget.GetInteractionPosition());
            }
        }
#endif

        #endregion
    }
}
