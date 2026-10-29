using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程序列 ScriptableObject (v1.14.0+)
    /// </summary>
    [CreateAssetMenu(fileName = "NewTutorial", menuName = "ZeroEngine/Tutorial/Tutorial Sequence")]
    public class TutorialSequenceSO : ScriptableObject
    {
        #region Serialized Fields

        [Header("Basic Info")]
        [Tooltip("教程序列 ID (全局唯一)")]
        public string SequenceId;

        [Tooltip("显示名称")]
        public string DisplayName;

        [Tooltip("描述")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("教程分类")]
        public TutorialCategory Category = TutorialCategory.Onboarding;

        [Tooltip("教程图标")]
        public Sprite Icon;

        [Header("Behavior")]
        [Tooltip("自动开始 (当条件满足时)")]
        public bool AutoStart = true;

        [Tooltip("可跳过整个教程")]
        public bool Skippable = true;

        [Tooltip("完成后可重播")]
        public bool Replayable = false;

        [Tooltip("优先级 (高优先级优先触发)")]
        public int Priority = 0;

        [Header("Conditions")]
        [Tooltip("开始条件列表")]
        [SerializeReference]
        public List<TutorialCondition> StartConditions = new();

        [Header("Steps")]
        [Tooltip("教程步骤列表")]
        [SerializeReference]
        public List<TutorialStep> Steps = new();

        [Header("Rewards")]
        [Tooltip("完成奖励")]
        [SerializeReference]
        public List<TutorialReward> CompletionRewards = new();

        [Header("Advanced")]
        [Tooltip("前置教程 ID 列表")]
        public string[] Prerequisites = Array.Empty<string>();

        [Tooltip("互斥教程 ID 列表 (完成一个则跳过其他)")]
        public string[] MutuallyExclusive = Array.Empty<string>();

        [Tooltip("完成后自动触发的教程 ID")]
        public string NextSequenceId;

        #endregion

        #region Properties

        /// <summary>步骤数量</summary>
        public int StepCount => Steps?.Count ?? 0;

        /// <summary>是否有效</summary>
        public bool IsValid => !string.IsNullOrEmpty(SequenceId) && Steps != null && Steps.Count > 0;

        #endregion

        #region Methods

        /// <summary>
        /// 检查开始条件是否满足
        /// </summary>
        public bool CanStart(TutorialContext ctx = null)
        {
            if (StartConditions == null || StartConditions.Count == 0)
            {
                return true;
            }

            foreach (var condition in StartConditions)
            {
                if (condition != null && !condition.IsSatisfied(ctx))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取步骤
        /// </summary>
        public TutorialStep GetStep(int index)
        {
            if (Steps == null || index < 0 || index >= Steps.Count)
            {
                return null;
            }
            return Steps[index];
        }

        /// <summary>
        /// 通过 ID 查找步骤
        /// </summary>
        public TutorialStep FindStepById(string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || Steps == null)
            {
                return null;
            }

            foreach (var step in Steps)
            {
                if (step != null && step.StepId == stepId)
                {
                    return step;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取步骤索引
        /// </summary>
        public int GetStepIndex(TutorialStep step)
        {
            if (step == null || Steps == null)
            {
                return -1;
            }
            return Steps.IndexOf(step);
        }

        /// <summary>
        /// 通过 ID 获取步骤索引
        /// </summary>
        public int GetStepIndex(string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || Steps == null)
            {
                return -1;
            }

            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i] != null && Steps[i].StepId == stepId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 验证教程配置
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrEmpty(SequenceId))
            {
                errors.Add("Sequence ID is required");
            }

            if (Steps == null || Steps.Count == 0)
            {
                errors.Add("At least one step is required");
            }
            else
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    var step = Steps[i];
                    if (step == null)
                    {
                        errors.Add($"Step {i} is null");
                    }
                    else if (!step.Validate(out string stepError))
                    {
                        errors.Add($"Step {i} ({step.StepType}): {stepError}");
                    }
                    else if (!step.SupportsAutomaticCompletion && step.AutoCompleteDelay > 0)
                    {
                        errors.Add($"Step {i}: an operation cannot be completed by an automatic delay.");
                    }
                }
            }

            return errors.Count == 0;
        }

        #endregion

        #region Editor

        private void OnValidate()
        {
            // 自动生成 ID
            if (string.IsNullOrEmpty(SequenceId))
            {
                SequenceId = name;
            }

            if (string.IsNullOrEmpty(DisplayName))
            {
                DisplayName = name;
            }
        }

        #endregion
    }

    /// <summary>
    /// 教程奖励基类 (v1.14.0+)
    /// </summary>
    [Serializable]
    public abstract class TutorialReward
    {
        public abstract string RewardType { get; }
        public abstract void Grant();
        public abstract string GetDescription();
    }

    /// <summary>
    /// 物品奖励 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class ItemTutorialReward : TutorialReward
    {
        public string ItemId;
        public int Amount = 1;

        public override string RewardType => "Item";

        public override void Grant()
        {
            throw new InvalidOperationException("Item tutorial rewards are granted by the project's ITutorialRewardService.");
        }

        public override string GetDescription()
        {
            return $"{ItemId} x{Amount}";
        }
    }

    /// <summary>
    /// 成就奖励 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class AchievementTutorialReward : TutorialReward
    {
        public string AchievementId;

        public override string RewardType => "Achievement";

        public override void Grant()
        {
            throw new InvalidOperationException("Achievement tutorial rewards are granted by the project's ITutorialRewardService.");
        }

        public override string GetDescription()
        {
            return $"Achievement: {AchievementId}";
        }
    }
}
