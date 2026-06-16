using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem.Formula;

namespace ZeroEngine.StatSystem
{
    /// <summary>
    /// 属性修饰器类型
    /// </summary>
    /// <remarks>
    /// 计算公式: (Base + Flat) * (1 + PercentAdd) * PercentMult
    /// </remarks>
    public enum StatModType
    {
        /// <summary>固定值加成</summary>
        Flat = 100,
        /// <summary>百分比加成（加法叠加）</summary>
        PercentAdd = 200,
        /// <summary>百分比乘算（乘法叠加）</summary>
        PercentMult = 300
    }

    /// <summary>
    /// 属性值变化事件参数
    /// </summary>
    public struct StatChangedEventArgs
    {
        /// <summary>变化前的值</summary>
        public float OldValue;
        /// <summary>变化后的值</summary>
        public float NewValue;
        /// <summary>变化量</summary>
        public float Delta => NewValue - OldValue;

        public StatChangedEventArgs(float oldValue, float newValue)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    /// <summary>
    /// Runtime stat contract shared by data, combat and editor tooling.
    /// </summary>
    public interface IStat
    {
        float Value { get; }
        int MaxValueInt { get; }
        IReadOnlyList<StatModifier> GetModifiers();
        void InitBase(float value, bool round = true);
        void AddModifier(StatModifier mod);
        void AddModifier(StatModifier mod, object source);
        bool RemoveModifier(StatModifier mod);
        bool RemoveModifier(StatModifier mod, object source);
        bool RemoveAllModifiersFromSource(object source);
        void RemoveAllModifiers();
    }

    /// <summary>
    /// 属性修饰器，用于修改属性的最终值。
    /// 可由 Buff、装备、技能等系统添加。
    /// </summary>
    [Serializable]
    public class StatModifier
    {
        /// <summary>修饰值</summary>
        public float Value;
        /// <summary>修饰类型</summary>
        public StatModType ModType;
        /// <summary>计算顺序</summary>
        public int Order;
        /// <summary>来源（用于调试和追踪）</summary>
        public object Source;

        [Tooltip("Optional Formula")]
        public MathFormula Formula;

        /// <summary>
        /// 创建属性修饰器
        /// </summary>
        /// <param name="value">修饰值</param>
        /// <param name="type">修饰类型</param>
        /// <param name="order">计算顺序</param>
        /// <param name="source">来源对象</param>
        public StatModifier(float value, StatModType type, int order, object source = null)
        {
            Value = value;
            ModType = type;
            Order = order;
            Source = source;
        }

        public StatModifier(float value, StatModType type) : this(value, type, (int)type) { }

        /// <summary>
        /// 获取修饰值（支持公式计算）
        /// </summary>
        public float GetValue(MathContext ctx = null)
        {
            if (Formula != null) return Formula.Evaluate(ctx);
            return Value;
        }

        public bool NeedsRuntimeValue => Formula != null;

        public StatModifier Clone()
        {
            return new StatModifier(Value, ModType, Order, Source)
            {
                Formula = Formula
            };
        }

        public StatModifier CreateRuntime(MathContext ctx)
        {
            var modifier = Clone();
            if (Formula != null)
            {
                modifier.Value = Formula.Evaluate(ctx);
                modifier.Formula = null;
            }

            return modifier;
        }
    }

    /// <summary>
    /// 游戏属性类，支持修饰器叠加、值限制和事件通知。
    /// </summary>
    /// <remarks>
    /// 计算公式: (BaseValue + Flat) * (1 + PercentAdd) * PercentMult
    /// 结果会被 MinValue 和 MaxValue 限制。
    /// </remarks>
    [Serializable]
    public class Stat : IStat
    {
        /// <summary>基础值</summary>
        public float BaseValue;

        /// <summary>
        /// Minimum allowed final value. Set to float.MinValue to disable.
        /// </summary>
        [Tooltip("Minimum allowed final value")]
        public float MinValue = float.MinValue;

        /// <summary>
        /// Maximum allowed final value. Set to float.MaxValue to disable.
        /// </summary>
        [Tooltip("Maximum allowed final value")]
        public float MaxValue = float.MaxValue;

        protected bool _isDirty = true;
        protected float _cachedValue;

        protected readonly List<StatModifier> _modifiers = new List<StatModifier>();

        public Dictionary<object, List<StatModifier>> StatModifiersBySource { get; } = new Dictionary<object, List<StatModifier>>();

        /// <summary>
        /// Event fired when the stat value changes after recalculation.
        /// </summary>
        public event Action<StatChangedEventArgs> OnValueChanged;

        /// <summary>修饰器数量</summary>
        public int ModifierCount => _modifiers.Count;

        /// <summary>
        /// 获取所有修饰器（只读）
        /// </summary>
        public IReadOnlyList<StatModifier> GetModifiers() => _modifiers;

        public virtual float Value
        {
            get
            {
                if (_isDirty)
                {
                    float oldValue = _cachedValue;
                    _cachedValue = CalculateFinalValue();
                    _isDirty = false;

                    // Fire event if value actually changed
                    if (Math.Abs(oldValue - _cachedValue) > 0.0001f)
                    {
                        OnValueChanged?.Invoke(new StatChangedEventArgs(oldValue, _cachedValue));
                    }
                }
                return _cachedValue;
            }
        }

        public Stat() { }

        public Stat(float baseValue)
        {
            BaseValue = baseValue;
        }

        public Stat(float baseValue, float minValue, float maxValue)
        {
            BaseValue = baseValue;
            MinValue = minValue;
            MaxValue = maxValue;
        }

        /// <summary>
        /// 初始化基础值
        /// </summary>
        /// <param name="value">基础值</param>
        /// <param name="round">是否四舍五入为整数</param>
        public virtual void InitBase(float value, bool round = true)
        {
            BaseValue = round ? Mathf.Round(value) : value;
            _isDirty = true;
        }

        /// <summary>
        /// 获取计算后的值（整数版本）
        /// </summary>
        public int MaxValueInt => RoundToIntSaturated(Value);

        /// <summary>
        /// Forces recalculation and event firing even if not dirty.
        /// Useful for UI initialization or manual refresh.
        /// </summary>
        public void ForceRecalculate()
        {
            float oldValue = _cachedValue;
            _cachedValue = CalculateFinalValue();
            _isDirty = false;

            if (Math.Abs(oldValue - _cachedValue) > 0.0001f)
            {
                OnValueChanged?.Invoke(new StatChangedEventArgs(oldValue, _cachedValue));
            }
        }

        /// <summary>
        /// Clears all event subscribers. Call when disposing the stat owner.
        /// </summary>
        public void ClearEventListeners()
        {
            OnValueChanged = null;
        }

        /// <summary>
        /// 添加修饰器
        /// </summary>
        /// <param name="mod">要添加的修饰器</param>
        public virtual void AddModifier(StatModifier mod)
        {
            if (mod == null) return;

            _isDirty = true;
            _modifiers.Add(mod);
            TrackModifierSource(mod.Source, mod);
            _modifiers.Sort(CompareModifierOrder);
        }

        /// <summary>
        /// 添加修饰器并设置来源
        /// </summary>
        public virtual void AddModifier(StatModifier mod, object source)
        {
            if (mod == null) return;

            mod.Source = source;
            AddModifier(mod);
        }

        public virtual void AddModifiersBatch(IReadOnlyList<StatModifier> modifiers, object source)
        {
            if (modifiers == null) return;

            for (int i = 0; i < modifiers.Count; i++)
            {
                AddModifier(modifiers[i], source);
            }
        }

        /// <summary>
        /// 移除修饰器
        /// </summary>
        /// <param name="mod">要移除的修饰器</param>
        /// <returns>是否成功移除</returns>
        public virtual bool RemoveModifier(StatModifier mod)
        {
            int index = FindModifierIndex(mod);
            if (index >= 0)
            {
                var removed = _modifiers[index];
                _modifiers.RemoveAt(index);
                UntrackModifierSource(removed.Source, removed);
                _isDirty = true;
                return true;
            }
            return false;
        }

        public virtual bool RemoveModifier(StatModifier mod, object source)
        {
            int index = FindModifierIndex(mod, source);
            if (index < 0) return false;

            var removed = _modifiers[index];
            _modifiers.RemoveAt(index);
            UntrackModifierSource(removed.Source, removed);
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// 移除来自指定来源的所有修饰器
        /// </summary>
        /// <param name="source">来源对象</param>
        public virtual bool RemoveAllModifiersFromSource(object source)
        {
            // Manual loop to avoid Lambda closure GC allocation
            bool removed = false;
            for (int i = _modifiers.Count - 1; i >= 0; i--)
            {
                if (_modifiers[i].Source == source)
                {
                    var modifier = _modifiers[i];
                    _modifiers.RemoveAt(i);
                    UntrackModifierSource(modifier.Source, modifier);
                    removed = true;
                }
            }
            if (removed) _isDirty = true;
            return removed;
        }

        public virtual void RemoveAllModifiers()
        {
            if (_modifiers.Count == 0) return;

            _modifiers.Clear();
            StatModifiersBySource.Clear();
            _isDirty = true;
        }

        protected virtual int FindModifierIndex(StatModifier mod, object source = null)
        {
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (IsMatchingModifier(_modifiers[i], mod, source))
                {
                    return i;
                }
            }

            return -1;
        }

        protected virtual bool IsMatchingModifier(StatModifier current, StatModifier expected, object source)
        {
            if (current == null || expected == null) return false;
            if (source != null && current.Source != source) return false;
            if (ReferenceEquals(current, expected)) return true;

            return Math.Abs(current.Value - expected.Value) <= 0.0001f
                && current.ModType == expected.ModType
                && current.Order == expected.Order
                && (source != null || current.Source == expected.Source);
        }

        protected void TrackModifierSource(object source, StatModifier modifier)
        {
            if (source == null || modifier == null) return;

            if (!StatModifiersBySource.TryGetValue(source, out var modifiers))
            {
                modifiers = new List<StatModifier>();
                StatModifiersBySource[source] = modifiers;
            }

            modifiers.Add(modifier);
        }

        protected void UntrackModifierSource(object source, StatModifier modifier)
        {
            if (source == null || modifier == null) return;

            if (!StatModifiersBySource.TryGetValue(source, out var modifiers)) return;

            modifiers.Remove(modifier);
            if (modifiers.Count == 0)
            {
                StatModifiersBySource.Remove(source);
            }
        }

        protected virtual int CompareModifierOrder(StatModifier a, StatModifier b)
        {
            if (a.Order < b.Order) return -1;
            if (a.Order > b.Order) return 1;
            return 0;
        }

        protected virtual float CalculateFinalValue()
        {
            float finalValue = BaseValue;


            // Simplified calculation loop assuming generic order
            // Flat -> PercentAdd (Sum) -> PercentMult (Mult)
            // But strict order calculation handles mixed types correctly if Order is correct.
            // LLS legacy had Flat=1, PercentAdd=101, PercentMult=201.
            
            // Standard approach:
            // 1. Base
            // 2. Add all Flats (Order < 100 usually)
            // 3. Add all PercentAdds (Order ~ 200, applies to Base + Flats) or Base? LLS: "PercentAdd". usually (Base + Flat) * (1 + Sum%)
            
            // We follow the sorted modifiers.
            // If strictly following order:
            // But specialized logic for PercentAdd is often (Base + Flat) * (1 + sum(PercentAdd))
            // While PercentMult is (Current) * PercentMult
            
            // Let's iterate and group by type for the standard RPG formula:
            // (Base + Sum_Flat) * (1 + Sum_PercentAdd) * (Product_PercentMult)
            
            float flatSum = 0;
            float percentAddSum = 0;
            float percentMultProduct = 1;

            for (int i = 0; i < _modifiers.Count; i++)
            {
                var mod = _modifiers[i];
                float val = SanitizeFinite(mod.GetValue()); // No context here? For context we need CurrentStat logic or pass context.
                                            // Base Stat usually doesn't have dynamic context unless passed.
                                            // Assuming simple static value here.

                if (mod.ModType == StatModType.Flat) flatSum += val;
                else if (mod.ModType == StatModType.PercentAdd) percentAddSum += val;
                else if (mod.ModType == StatModType.PercentMult) percentMultProduct *= val;
            }

            finalValue += flatSum;
            finalValue *= (1 + percentAddSum);
            finalValue *= percentMultProduct;

            // Apply min/max clamp to prevent overflow
            finalValue = SanitizeFinite(finalValue);
            finalValue = Mathf.Clamp(finalValue, MinValue, MaxValue);
            finalValue = SanitizeFinite(finalValue);

            return (float)Math.Round(finalValue, 4);
        }

        protected static float SanitizeFinite(float value)
        {
            if (float.IsNaN(value)) return 0f;
            if (float.IsPositiveInfinity(value)) return float.MaxValue;
            if (float.IsNegativeInfinity(value)) return float.MinValue;
            return value;
        }

        protected static int RoundToIntSaturated(float value)
        {
            value = SanitizeFinite(value);
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            return Mathf.RoundToInt(value);
        }
    }
}
