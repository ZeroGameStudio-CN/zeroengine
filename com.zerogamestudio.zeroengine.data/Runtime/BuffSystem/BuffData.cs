using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.BuffSystem
{
    /// <summary>
    /// Buff 配置数据。
    /// 定义 Buff 的基本属性、行为和属性修饰效果。
    /// </summary>
    [CreateAssetMenu(fileName = "NewBuff", menuName = "ZeroEngine/Buff System/Buff Data")]
    public class BuffData : ScriptableObject
    {
        /// <summary>
        /// 唯一标识符
        /// </summary>
        public string BuffId;

        /// <summary>
        /// Buff 分类（增益/减益/中性）
        /// </summary>
        public BuffCategory Category;

        /// <summary>
        /// 显示图标
        /// </summary>
        public Sprite Icon;

        [Header("Stats")]
        /// <summary>
        /// 持续时间（秒），0 表示永久
        /// </summary>
        public float Duration = 10f;

        /// <summary>
        /// 最大堆叠层数
        /// </summary>
        public int MaxStacks = 1;

        /// <summary>
        /// Tick 间隔（秒），用于 DOT/HOT 效果
        /// </summary>
        public float TickInterval = 1f;

        [Header("Behavior")]
        /// <summary>
        /// 过期模式（移除全部层数/移除一层）
        /// </summary>
        public BuffExpireMode ExpireMode = BuffExpireMode.RemoveAllStacks;

        /// <summary>
        /// 堆叠模式（叠加/刷新/替换）(v1.2.0+)
        /// </summary>
        public BuffStackMode StackMode = BuffStackMode.Stack;

        /// <summary>
        /// 添加层数时刷新持续时间
        /// </summary>
        public bool RefreshOnAddStack = true;

        /// <summary>
        /// 移除层数时刷新持续时间
        /// </summary>
        public bool RefreshOnRemoveStack = false;

        [Header("Modifiers")]
        /// <summary>
        /// 属性修饰器配置列表
        /// </summary>
        public List<BuffStatModifierConfig> StatModifiers = new List<BuffStatModifierConfig>();
    }

    /// <summary>
    /// Buff 属性修饰器配置
    /// </summary>
    [System.Serializable]
    public class BuffStatModifierConfig
    {
        /// <summary>
        /// 目标属性 ID
        /// </summary>
        public StatId StatId;

        /// <summary>
        /// 修饰值
        /// </summary>
        public float Value;

        /// <summary>
        /// 修饰类型（固定值/百分比加成/百分比乘算）
        /// </summary>
        public StatModType ModType;
    }
}
