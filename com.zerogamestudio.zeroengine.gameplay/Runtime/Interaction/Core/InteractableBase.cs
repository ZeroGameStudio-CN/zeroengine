using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Interaction
{
    /// <summary>
    /// 可交互对象基类 (v1.14.0+)
    /// 提供 IInteractable 的默认实现
    /// </summary>
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        #region Serialized Fields

        [Header("Interaction Settings")]
        [SerializeField]
        [Tooltip("唯一标识符 (留空则自动生成)")]
        protected string _interactableId;

        [SerializeField]
        [Tooltip("显示名称")]
        protected string _displayName;

        [SerializeField]
        [Tooltip("交互类型")]
        protected InteractionType _interactionType = InteractionType.Use;

        [SerializeField]
        [Tooltip("交互优先级")]
        protected InteractionPriority _priority = InteractionPriority.Normal;

        [SerializeField]
        [Tooltip("是否启用")]
        protected bool _isEnabled = true;

        [SerializeField]
        [Tooltip("交互提示文本 (留空使用默认)")]
        protected string _interactionHint;

        [Header("Interaction Position")]
        [SerializeField]
        [Tooltip("交互位置偏移 (相对于 Transform)")]
        protected Vector3 _interactionOffset = Vector3.zero;

        [SerializeField]
        [Tooltip("使用自定义交互点")]
        protected Transform _customInteractionPoint;

        [Header("Conditions")]
        [SerializeField]
        [Tooltip("交互条件列表")]
        [SerializeReference]
        protected List<InteractionCondition> _conditions = new();

        #endregion

        #region IInteractable Implementation

        public virtual string InteractableId
        {
            get
            {
                if (string.IsNullOrEmpty(_interactableId))
                {
#if UNITY_6000_2_OR_NEWER
                    _interactableId = $"{GetType().Name}_{EntityId.ToULong(GetEntityId())}";
#else
                    _interactableId = $"{GetType().Name}_{GetInstanceID()}";
#endif
                }
                return _interactableId;
            }
        }

        public virtual string DisplayName => string.IsNullOrEmpty(_displayName) ? gameObject.name : _displayName;

        public virtual InteractionType Type => _interactionType;

        public virtual InteractionPriority Priority => _priority;

        public virtual bool IsEnabled
        {
            get => _isEnabled && isActiveAndEnabled;
            set => _isEnabled = value;
        }

        public GameObject GameObject => gameObject;

        #endregion

        #region Events

        /// <summary>获得焦点时触发</summary>
        public event Action OnFocused;

        /// <summary>失去焦点时触发</summary>
        public event Action OnUnfocused;

        /// <summary>交互完成时触发</summary>
        public event Action<InteractionResult> OnInteracted;

        #endregion

        #region Protected State

        protected bool _isFocused;

        #endregion

        #region Unity Lifecycle

        protected virtual void Awake()
        {
            // 确保有 Collider 用于检测
            if (GetComponent<Collider>() == null && GetComponent<Collider2D>() == null)
            {
                Debug.LogWarning($"[ZeroEngine.Interaction] {name} has no Collider for interaction detection.");
            }
        }

        protected virtual void OnEnable()
        {
            // 自动注册到管理器
            if (InteractionManager.Instance != null)
            {
                InteractionManager.Instance.RegisterInteractable(this);
            }
        }

        protected virtual void OnDisable()
        {
            // 自动注销
            if (InteractionManager.Instance != null)
            {
                InteractionManager.Instance.UnregisterInteractable(this);
            }
        }

        #endregion

        #region IInteractable Methods

        public virtual string GetInteractionHint()
        {
            if (!string.IsNullOrEmpty(_interactionHint))
            {
                return _interactionHint;
            }

            // 根据类型返回默认提示
            return Type switch
            {
                InteractionType.Pickup => $"[E] Pick up {DisplayName}",
                InteractionType.Talk => $"[E] Talk to {DisplayName}",
                InteractionType.Open => $"[E] Open {DisplayName}",
                InteractionType.Use => $"[E] Use {DisplayName}",
                InteractionType.Examine => $"[E] Examine {DisplayName}",
                InteractionType.Activate => $"[E] Activate {DisplayName}",
                InteractionType.Enter => $"[E] Enter {DisplayName}",
                InteractionType.Craft => $"[E] Craft at {DisplayName}",
                _ => $"[E] Interact with {DisplayName}"
            };
        }

        public virtual bool CanInteract(InteractionContext ctx)
        {
            if (!IsEnabled) return false;

            // 检查所有条件
            foreach (var condition in _conditions)
            {
                if (condition != null && !condition.IsSatisfied(ctx, this))
                {
                    return false;
                }
            }

            return true;
        }

        public virtual string GetCannotInteractReason(InteractionContext ctx)
        {
            if (!IsEnabled) return "Not available";

            // 检查条件并返回第一个不满足的原因
            foreach (var condition in _conditions)
            {
                if (condition != null && !condition.IsSatisfied(ctx, this))
                {
                    return condition.GetFailureReason(ctx, this);
                }
            }

            return null;
        }

        public virtual InteractionResult OnInteract(InteractionContext ctx)
        {
            if (!CanInteract(ctx))
            {
                var reason = GetCannotInteractReason(ctx);
                return InteractionResult.Failed(this, reason);
            }

            // 子类实现具体交互逻辑
            var result = ExecuteInteraction(ctx);

            // 触发事件
            OnInteracted?.Invoke(result);

            return result;
        }

        public virtual void OnFocus()
        {
            if (_isFocused) return;
            _isFocused = true;
            OnFocused?.Invoke();

            // 可以在这里添加视觉反馈 (高亮、轮廓等)
        }

        public virtual void OnUnfocus()
        {
            if (!_isFocused) return;
            _isFocused = false;
            OnUnfocused?.Invoke();

            // 移除视觉反馈
        }

        public virtual Vector3 GetInteractionPosition()
        {
            if (_customInteractionPoint != null)
            {
                return _customInteractionPoint.position;
            }
            return transform.position + transform.TransformDirection(_interactionOffset);
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// 执行具体的交互逻辑 (子类重写)
        /// </summary>
        protected abstract InteractionResult ExecuteInteraction(InteractionContext ctx);

        /// <summary>
        /// 添加交互条件
        /// </summary>
        public void AddCondition(InteractionCondition condition)
        {
            if (condition != null)
            {
                _conditions.Add(condition);
            }
        }

        /// <summary>
        /// 移除交互条件
        /// </summary>
        public void RemoveCondition(InteractionCondition condition)
        {
            _conditions.Remove(condition);
        }

        /// <summary>
        /// 清除所有条件
        /// </summary>
        public void ClearConditions()
        {
            _conditions.Clear();
        }

        #endregion

        #region Editor

#if UNITY_EDITOR
        protected virtual void OnDrawGizmosSelected()
        {
            // 绘制交互位置
            Gizmos.color = IsEnabled ? Color.green : Color.red;
            Gizmos.DrawWireSphere(GetInteractionPosition(), 0.2f);
        }
#endif

        #endregion
    }
}
