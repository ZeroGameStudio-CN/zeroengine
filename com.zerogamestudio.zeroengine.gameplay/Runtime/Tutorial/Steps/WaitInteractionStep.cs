using System;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 等待交互步骤 (v1.14.0+)
    /// 等待玩家与指定对象交互
    /// </summary>
    [Serializable]
    public class WaitInteractionStep : TutorialStep
    {
        public override bool SupportsAutomaticCompletion => false;
        [Header("Interaction Settings")]
        [Tooltip("目标交互对象 ID")]
        public string InteractableId;

        [Tooltip("要求的交互类型 (Any 表示任意类型)")]
        public InteractionRequirement InteractionRequirement = InteractionRequirement.Any;

        [Tooltip("提示文本")]
        [TextArea(1, 2)]
        public string PromptText;

        [Tooltip("超时时间 (秒，0 无限等待)")]
        public float Timeout = 0f;

        [Tooltip("高亮交互对象")]
        public bool HighlightTarget = true;

        [Tooltip("显示路径引导")]
        public bool ShowPathGuide = false;

        // 运行时状态
        [NonSerialized]
        private bool _interactionCompleted;
        [NonSerialized]
        private Interaction.IInteractable _targetInteractable;

        public override string StepType => "WaitInteraction";

        public override void OnEnter(TutorialContext ctx)
        {
            _interactionCompleted = false;

            // 查找交互对象
            _targetInteractable = FindInteractable();

            // 注册事件监听
#if ZEROENGINE_INTERACTION
            if (Interaction.InteractionManager.Instance != null)
            {
                Interaction.InteractionManager.Instance.OnInteractionCompleted += OnInteraction;
            }
#endif

            // 显示提示
            if (!string.IsNullOrEmpty(PromptText))
            {
                TutorialUIManager.Instance?.ShowPrompt(PromptText, null);
            }

            // 高亮目标
            if (HighlightTarget && _targetInteractable != null)
            {
                // 获取目标位置并显示箭头
                var position = _targetInteractable.GetInteractionPosition();
                TutorialUIManager.Instance?.ShowWorldArrow(position);
            }
        }

        public override void OnUpdate(TutorialContext ctx)
        {
            if (_interactionCompleted) return;

            // 超时检查
            if (Timeout > 0 && ctx.StepElapsedTime >= Timeout)
            {
                ctx.Fail("The required interaction timed out.");
                return;
            }

            // 如果目标丢失，尝试重新查找
            if (_targetInteractable == null || _targetInteractable.GameObject == null)
            {
                _targetInteractable = FindInteractable();
            }

            // 更新箭头位置
            if (HighlightTarget && _targetInteractable != null)
            {
                var position = _targetInteractable.GetInteractionPosition();
                TutorialUIManager.Instance?.UpdateWorldArrow(position);
            }
        }

        public override void OnExit(TutorialContext ctx)
        {
            // 移除事件监听
#if ZEROENGINE_INTERACTION
            if (Interaction.InteractionManager.Instance != null)
            {
                Interaction.InteractionManager.Instance.OnInteractionCompleted -= OnInteraction;
            }
#endif

            TutorialUIManager.Instance?.HidePrompt();
            TutorialUIManager.Instance?.HideWorldArrow();
        }

        public override bool IsCompleted(TutorialContext ctx)
        {
            return _interactionCompleted;
        }

        private void OnInteraction(Interaction.InteractionEventArgs args)
        {
            var interactable = args.Interactable;
            if (interactable == null) return;

            // 检查 ID 匹配
            if (!string.IsNullOrEmpty(InteractableId) && interactable.InteractableId != InteractableId)
            {
                return;
            }

            // 检查交互类型
            if (InteractionRequirement != InteractionRequirement.Any)
            {
                var requiredType = (Interaction.InteractionType)(int)InteractionRequirement;
                if (interactable.Type != requiredType)
                {
                    return;
                }
            }

            _interactionCompleted = true;
        }

        private Interaction.IInteractable FindInteractable()
        {
#if ZEROENGINE_INTERACTION
            if (!string.IsNullOrEmpty(InteractableId))
            {
                return Interaction.InteractionManager.Instance?.FindById(InteractableId);
            }
#endif
            return null;
        }

        public override string GetDisplayText()
        {
            if (!string.IsNullOrEmpty(PromptText))
            {
                return PromptText;
            }

            return $"Interact with {InteractableId}";
        }

        public override bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(InteractableId) && InteractionRequirement == InteractionRequirement.Any)
            {
                error = "Either InteractableId or specific InteractionRequirement is required";
                return false;
            }
            error = null;
            return true;
        }
    }

    /// <summary>
    /// 交互要求类型
    /// </summary>
    public enum InteractionRequirement
    {
        Any = -1,
        Pickup = 0,
        Talk = 1,
        Open = 2,
        Use = 3,
        Examine = 4,
        Activate = 5,
        Enter = 6,
        Craft = 7
    }
}
