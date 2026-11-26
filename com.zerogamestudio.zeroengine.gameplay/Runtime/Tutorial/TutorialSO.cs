using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程步骤定义
    /// </summary>
    [CreateAssetMenu(fileName = "New Tutorial Step", menuName = "ZeroEngine/Narrative/Tutorials/Tutorial Step")]
    public class TutorialStepSO : ScriptableObject
    {
        [Header("基础信息")]
        [Tooltip("步骤ID")]
        public string StepId;

        [Tooltip("步骤标题")]
        public string Title;

        [Tooltip("步骤说明")]
        [TextArea(2, 5)]
        public string Description;

        [Header("触发")]
        [Tooltip("触发类型")]
        public TriggerType TriggerType = TriggerType.OnClick;

        [Tooltip("触发按键 (TriggerType.OnKeyPress)")]
        public KeyCode TriggerKey = KeyCode.None;

        [Tooltip("触发事件ID (TriggerType.OnEvent)")]
        public string TriggerEventId;

        [Tooltip("触发延时 (TriggerType.OnDelay)")]
        public float TriggerDelay = 1f;

        [Header("完成条件")]
        [Tooltip("完成条件列表")]
        public List<StepCondition> CompleteConditions = new List<StepCondition>();

        [Tooltip("是否需要所有条件都满足")]
        public bool RequireAllConditions = true;

        [Header("高亮")]
        [Tooltip("高亮目标列表")]
        public List<HighlightTarget> Highlights = new List<HighlightTarget>();

        [Header("提示")]
        [Tooltip("提示框配置")]
        public TooltipConfig Tooltip = new TooltipConfig();

        [Header("动作")]
        [Tooltip("步骤开始时执行")]
        public List<StepAction> OnStartActions = new List<StepAction>();

        [Tooltip("步骤完成时执行")]
        public List<StepAction> OnCompleteActions = new List<StepAction>();

        [Header("设置")]
        [Tooltip("是否可跳过")]
        public bool Skippable = true;

        [Tooltip("是否阻止玩家输入")]
        public bool BlockInput;

        [Tooltip("是否暂停游戏")]
        public bool PauseGame;

        [Tooltip("自动完成延时 (0=不自动完成)")]
        public float AutoCompleteDelay;

        #region Methods

        public bool CheckCompleteConditions()
        {
            if (CompleteConditions.Count == 0)
                return true;

            foreach (var condition in CompleteConditions)
            {
                bool met = condition.Check();

                if (RequireAllConditions && !met)
                    return false;
                if (!RequireAllConditions && met)
                    return true;
            }

            return RequireAllConditions;
        }

        public void ExecuteStartActions()
        {
            foreach (var action in OnStartActions)
            {
                action.Execute();
            }
        }

        public void ExecuteCompleteActions()
        {
            foreach (var action in OnCompleteActions)
            {
                action.Execute();
            }
        }

        #endregion

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(StepId))
            {
                StepId = name;
            }
        }
    }

    /// <summary>
    /// 教程定义
    /// </summary>
    [CreateAssetMenu(fileName = "New Tutorial", menuName = "ZeroEngine/Narrative/Tutorials/Tutorial")]
    public class TutorialSO : ScriptableObject
    {
        [Header("基础信息")]
        [Tooltip("教程ID")]
        public string TutorialId;

        [Tooltip("教程名称")]
        public string DisplayName;

        [Tooltip("教程描述")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("教程图标")]
        public Sprite Icon;

        [Header("步骤")]
        [Tooltip("教程步骤列表")]
        public List<TutorialStepSO> Steps = new List<TutorialStepSO>();

        [Header("触发条件")]
        [Tooltip("自动触发条件")]
        public List<StepCondition> AutoTriggerConditions = new List<StepCondition>();

        [Tooltip("前置教程")]
        public List<TutorialSO> Prerequisites = new List<TutorialSO>();

        [Header("设置")]
        [Tooltip("优先级")]
        public TutorialPriority Priority = TutorialPriority.Normal;

        [Tooltip("是否可跳过整个教程")]
        public bool Skippable = true;

        [Tooltip("是否仅触发一次")]
        public bool OneTime = true;

        [Tooltip("是否在完成后自动保存")]
        public bool AutoSave = true;

        [Header("奖励")]
        [Tooltip("完成奖励ID")]
        public string RewardId;

        #region Properties

        public int StepCount => Steps.Count;

        #endregion

        #region Methods

        public TutorialStepSO GetStep(int index)
        {
            if (index >= 0 && index < Steps.Count)
                return Steps[index];
            return null;
        }

        public int GetStepIndex(TutorialStepSO step)
        {
            return Steps.IndexOf(step);
        }

        public bool CheckAutoTrigger()
        {
            if (AutoTriggerConditions.Count == 0)
                return false;

            foreach (var condition in AutoTriggerConditions)
            {
                if (!condition.Check())
                    return false;
            }
            return true;
        }

        public bool CheckPrerequisites(Func<string, bool> isCompleted)
        {
            foreach (var prereq in Prerequisites)
            {
                if (prereq != null && !isCompleted(prereq.TutorialId))
                    return false;
            }
            return true;
        }

        #endregion

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(TutorialId))
            {
                TutorialId = name;
            }
        }
    }

    /// <summary>
    /// 教程组 (用于分类)
    /// </summary>
    [CreateAssetMenu(fileName = "New Tutorial Group", menuName = "ZeroEngine/Narrative/Tutorials/Tutorial Group")]
    public class TutorialGroupSO : ScriptableObject
    {
        [Tooltip("组ID")]
        public string GroupId;

        [Tooltip("组名称")]
        public string DisplayName;

        [Tooltip("组描述")]
        [TextArea]
        public string Description;

        [Tooltip("组图标")]
        public Sprite Icon;

        [Tooltip("教程列表")]
        public List<TutorialSO> Tutorials = new List<TutorialSO>();

        [Tooltip("排序优先级")]
        public int SortOrder;
    }

    #region Support Types for TutorialStepSO

    /// <summary>
    /// 触发类型
    /// </summary>
    public enum TriggerType
    {
        OnClick,
        OnKeyPress,
        OnEvent,
        OnDelay,
        Immediate
    }

    /// <summary>
    /// 教程优先级
    /// </summary>
    public enum TutorialPriority
    {
        Low,
        Normal,
        High,
        Critical
    }

    /// <summary>
    /// 步骤条件
    /// </summary>
    [Serializable]
    public class StepCondition
    {
        public ConditionType Type;
        public string TargetId;
        public int TargetValue;

        public bool Check()
        {
            // 默认实现，具体逻辑由使用方定制
            return true;
        }
    }

    /// <summary>
    /// 条件类型
    /// </summary>
    public enum ConditionType
    {
        None,
        HasItem,
        QuestState,
        EventTriggered,
        UIOpened,
        Custom
    }

    /// <summary>
    /// 高亮目标
    /// </summary>
    [Serializable]
    public class HighlightTarget
    {
        [Tooltip("目标路径")]
        public string TargetPath;

        [Tooltip("高亮类型")]
        public HighlightType Type = HighlightType.Circle;

        [Tooltip("偏移")]
        public Vector2 Offset;

        [Tooltip("缩放")]
        public float Scale = 1f;
    }

    /// <summary>
    /// 提示框配置
    /// </summary>
    [Serializable]
    public class TooltipConfig
    {
        [Tooltip("是否显示")]
        public bool Show = true;

        [Tooltip("提示文本")]
        [TextArea]
        public string Text;

        [Tooltip("位置")]
        public TooltipPosition Position = TooltipPosition.Auto;

        [Tooltip("偏移")]
        public Vector2 Offset;
    }

    /// <summary>
    /// 提示框位置
    /// </summary>
    public enum TooltipPosition
    {
        Auto,
        Top,
        Bottom,
        Left,
        Right
    }

    /// <summary>
    /// 步骤动作
    /// </summary>
    [Serializable]
    public class StepAction
    {
        public ActionType Type;
        public string ActionId;
        public string Parameter;

        public void Execute()
        {
            // 默认实现，具体逻辑由使用方定制
        }
    }

    /// <summary>
    /// 动作类型
    /// </summary>
    public enum ActionType
    {
        None,
        PlaySound,
        ShowUI,
        HideUI,
        TriggerEvent,
        Custom
    }

    #endregion
}