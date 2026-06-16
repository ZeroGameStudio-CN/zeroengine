using System;
using UnityEngine;

namespace ZeroEngine.StatSystem
{
    /// <summary>
    /// 当前值增加类型（用于 CurrentStat）
    /// </summary>
    public enum IncreaseType
    {
        /// <summary>只重新夹取当前值，不主动增加或保持百分比</summary>
        None,
        /// <summary>按固定值增加</summary>
        Flat,
        /// <summary>按修饰器原始值增加当前值</summary>
        Increase,
        /// <summary>按百分比增加</summary>
        Percent,
        /// <summary>只影响当前值，不影响最大值</summary>
        CurrentOnly
    }

    /// <summary>
    /// 带当前值的属性（如 HP、MP）
    /// MaxValue 由修饰器计算得出，CurrentValue 可独立变化
    /// </summary>
    [Serializable]
    public class CurrentStat : Stat
    {
        [SerializeField]
        private float _currentValue;

        private bool _roundValues = true;

        /// <summary>
        /// 当前值变化时触发 (currentValue, maxValue)
        /// </summary>
        public event Action<float, float> OnValueChange;

        /// <summary>
        /// 当前值归零时触发
        /// </summary>
        public event Action OnCurrentValueZero;

        /// <summary>
        /// 当前值（整数）
        /// </summary>
        public int CurrentValueInt => _roundValues ? RoundToIntSaturated(_currentValue) : (int)_currentValue;

        /// <summary>
        /// 当前值（浮点）
        /// </summary>
        public float CurrentValue => _currentValue;

        /// <summary>
        /// 最大值（整数）- 覆盖基类
        /// </summary>
        public new int MaxValueInt => _roundValues ? RoundToIntSaturated(Value) : (int)Value;

        /// <summary>
        /// 最大值（浮点，来自 Stat.Value）
        /// </summary>
        public new float MaxValue => base.Value;

        /// <summary>
        /// 当前值百分比 (0-1)
        /// </summary>
        public float Percent => MaxValue > 0 ? _currentValue / MaxValue : 0f;

        public CurrentStat() { }

        public CurrentStat(float maxValue, float currentValue = -1, bool round = true)
        {
            BaseValue = maxValue;
            _currentValue = currentValue < 0 ? maxValue : currentValue;
            _roundValues = round;
        }

        /// <summary>
        /// 初始化当前值属性
        /// </summary>
        /// <param name="maxValue">最大值</param>
        /// <param name="currentValue">当前值（-1 表示满值）</param>
        /// <param name="round">是否取整</param>
        public void InitCurrent(float maxValue, float currentValue = -1, bool round = true)
        {
            BaseValue = maxValue;
            _currentValue = currentValue < 0 ? maxValue : Mathf.Clamp(currentValue, 0, maxValue);
            _roundValues = round;
            _isDirty = true;
        }

        /// <summary>
        /// 设置当前值
        /// </summary>
        public void SetCurrent(float value)
        {
            float oldValue = _currentValue;
            _currentValue = Mathf.Clamp(value, 0, MaxValue);

            if (Math.Abs(oldValue - _currentValue) > 0.0001f)
            {
                OnValueChange?.Invoke(_currentValue, MaxValue);

                if (_currentValue <= 0 && oldValue > 0)
                {
                    OnCurrentValueZero?.Invoke();
                }
            }
        }

        /// <summary>
        /// 增加当前值（负数表示减少）
        /// </summary>
        public void IncreaseCurrent(float delta)
        {
            SetCurrent(_currentValue + delta);
        }

        /// <summary>
        /// 添加修饰器（可选择是否同步调整当前值）
        /// </summary>
        /// <param name="modifier">修饰器</param>
        /// <param name="source">来源</param>
        /// <param name="increaseType">当前值调整方式</param>
        public void AddModifier(StatModifier modifier, object source, IncreaseType increaseType = IncreaseType.Flat)
        {
            if (modifier == null) return;

            if (increaseType == IncreaseType.CurrentOnly)
            {
                IncreaseCurrent(modifier.GetValue());
                return;
            }

            float oldMax = MaxValue;
            modifier.Source = source;
            base.AddModifier(modifier);
            float newMax = MaxValue;

            // 根据增加类型调整当前值
            if (increaseType == IncreaseType.Percent && oldMax > 0)
            {
                // 保持百分比
                float percent = _currentValue / oldMax;
                _currentValue = percent * newMax;
            }
            else if (increaseType == IncreaseType.Increase)
            {
                _currentValue += modifier.GetValue();
            }
            else if (increaseType == IncreaseType.None)
            {
                // Do not actively change current value.
            }
            else
            {
                // 按差值调整
                float delta = newMax - oldMax;
                if (delta > 0)
                {
                    _currentValue += delta;
                }
            }

            _currentValue = Mathf.Clamp(_currentValue, 0, newMax);
            OnValueChange?.Invoke(_currentValue, newMax);
        }

        /// <summary>
        /// 移除修饰器
        /// </summary>
        public override bool RemoveModifier(StatModifier mod)
        {
            bool removed = base.RemoveModifier(mod);

            if (removed)
            {
                ClampCurrentAfterMaxChange(false, 0f);
            }

            return removed;
        }

        public override bool RemoveModifier(StatModifier mod, object source)
        {
            return RemoveModifier(mod, source, false);
        }

        public bool RemoveModifier(StatModifier mod, object source, bool preservePercent)
        {
            float oldPercent = Percent;
            bool removed = base.RemoveModifier(mod, source);
            if (removed)
            {
                ClampCurrentAfterMaxChange(preservePercent, oldPercent);
            }

            return removed;
        }

        public override bool RemoveAllModifiersFromSource(object source)
        {
            return RemoveAllModifiersFromSource(source, false);
        }

        public bool RemoveAllModifiersFromSource(object source, bool preservePercent)
        {
            float oldPercent = Percent;
            bool removed = base.RemoveAllModifiersFromSource(source);
            if (removed)
            {
                ClampCurrentAfterMaxChange(preservePercent, oldPercent);
            }

            return removed;
        }

        public override void RemoveAllModifiers()
        {
            if (ModifierCount == 0) return;

            base.RemoveAllModifiers();
            ClampCurrentAfterMaxChange(false, 0f);
        }

        /// <summary>
        /// 恢复到满值
        /// </summary>
        public void RestoreToFull()
        {
            SetCurrent(MaxValue);
        }

        private void ClampCurrentAfterMaxChange(bool preservePercent, float oldPercent)
        {
            float newMax = MaxValue;
            _currentValue = preservePercent ? oldPercent * newMax : Mathf.Clamp(_currentValue, 0, newMax);
            OnValueChange?.Invoke(_currentValue, newMax);
        }

        /// <summary>
        /// 清除事件监听
        /// </summary>
        public new void ClearEventListeners()
        {
            base.ClearEventListeners();
            OnValueChange = null;
            OnCurrentValueZero = null;
        }
    }
}
