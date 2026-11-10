using System;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程分类 (v1.14.0+)
    /// </summary>
    public enum TutorialCategory
    {
        Onboarding,     // 新手引导
        System,         // 系统教程
        Combat,         // 战斗教程
        Crafting,       // 制作教程
        Social,         // 社交教程
        Advanced        // 进阶技巧
    }

    /// <summary>
    /// 教程状态 (v1.14.0+)
    /// </summary>
    public enum TutorialState
    {
        NotStarted,     // 未开始
        InProgress,     // 进行中
        Completed,      // 已完成
        Skipped         // 已跳过
    }

    /// <summary>
    /// 步骤状态 (v1.14.0+)
    /// </summary>
    public enum StepState
    {
        Pending,        // 等待执行
        Active,         // 正在执行
        Completed,      // 已完成
        Skipped         // 已跳过
    }

    /// <summary>
    /// 高亮类型 (v1.14.0+)
    /// </summary>
    public enum HighlightType
    {
        Circle,         // 圆形高亮
        Rectangle,      // 矩形高亮
        Finger,         // 手指指示
        Arrow,          // 箭头指向
        Pulse,          // 脉冲效果
        None            // 无高亮
    }

    /// <summary>
    /// 教程事件参数 (v1.14.0+)
    /// </summary>
    public readonly struct TutorialEventArgs
    {
        public readonly TutorialSequenceSO Sequence;
        public readonly TutorialStep Step;
        public readonly int StepIndex;
        public readonly TutorialState State;

        public TutorialEventArgs(TutorialSequenceSO sequence, TutorialStep step, int stepIndex, TutorialState state)
        {
            Sequence = sequence;
            Step = step;
            StepIndex = stepIndex;
            State = state;
        }
    }

    /// <summary>
    /// 步骤事件参数 (v1.14.0+)
    /// </summary>
    public readonly struct StepEventArgs
    {
        public readonly TutorialStep Step;
        public readonly int StepIndex;
        public readonly StepState State;

        public StepEventArgs(TutorialStep step, int stepIndex, StepState state)
        {
            Step = step;
            StepIndex = stepIndex;
            State = state;
        }
    }

    /// <summary>
    /// 教程进度数据 (用于存档) (v1.14.0+)
    /// </summary>
    [Serializable]
    public class TutorialSaveData
    {
        /// <summary>已完成的教程序列 ID</summary>
        public string[] CompletedSequences = Array.Empty<string>();

        /// <summary>已跳过的教程序列 ID</summary>
        public string[] SkippedSequences = Array.Empty<string>();
        public string[] TriggeredMarkers = Array.Empty<string>();

        /// <summary>当前进行中的教程 ID (为空表示无)</summary>
        public string CurrentSequenceId;

        /// <summary>当前步骤索引</summary>
        public int CurrentStepIndex;
        public string CurrentStepId;

        /// <summary>全局变量 (键值对)</summary>
        public TutorialVariable[] GlobalVariables = Array.Empty<TutorialVariable>();
    }

    /// <summary>
    /// 教程变量 (v1.14.0+)
    /// </summary>
    [Serializable]
    public struct TutorialVariable
    {
        public string Key;
        public string Value;
        public TutorialVariableType Type;
    }

    /// <summary>
    /// 变量类型 (v1.14.0+)
    /// </summary>
    public enum TutorialVariableType
    {
        String,
        Int,
        Float,
        Bool
    }
}
