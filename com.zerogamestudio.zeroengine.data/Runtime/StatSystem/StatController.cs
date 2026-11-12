using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.BuffSystem;

namespace ZeroEngine.StatSystem
{
    /// <summary>
    /// Event args for controller-level stat changes.
    /// </summary>
    public struct StatControllerChangedEventArgs
    {
        public StatType StatType;
        public float OldValue;
        public float NewValue;
        public float Delta => NewValue - OldValue;

        public StatControllerChangedEventArgs(StatType type, float oldValue, float newValue)
        {
            StatType = type;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    public class StatController : MonoBehaviour, IStatProvider, IBuffStatTarget
    {
        [SerializeField] private Dictionary<StatType, Stat> _stats = new Dictionary<StatType, Stat>();

        /// <summary>
        /// Event fired when any stat on this controller changes.
        /// </summary>
        public event Action<StatControllerChangedEventArgs> OnAnyStatChanged;

        /// <summary>
        /// Initialize a stat with base value and subscribe to its changes.
        /// </summary>
        public void InitStat(StatType type, float baseValue)
        {
            if (_stats.ContainsKey(type))
            {
                _stats[type].BaseValue = baseValue;
            }
            else
            {
                var stat = new Stat(baseValue);
                _stats[type] = stat;
                SubscribeToStat(type, stat);
            }
        }

        /// <summary>
        /// Initialize a stat with base value, min and max constraints.
        /// </summary>
        public void InitStat(StatType type, float baseValue, float minValue, float maxValue)
        {
            if (_stats.ContainsKey(type))
            {
                var existing = _stats[type];
                existing.BaseValue = baseValue;
                existing.MinValue = minValue;
                existing.MaxValue = maxValue;
            }
            else
            {
                var stat = new Stat(baseValue, minValue, maxValue);
                _stats[type] = stat;
                SubscribeToStat(type, stat);
            }
        }

        private void SubscribeToStat(StatType type, Stat stat)
        {
            stat.OnValueChanged += args =>
            {
                OnAnyStatChanged?.Invoke(new StatControllerChangedEventArgs(type, args.OldValue, args.NewValue));
            };
        }

        public Stat GetStat(StatType type)
        {
            if (_stats.TryGetValue(type, out var stat)) return stat;
            return null;
        }

        public Stat GetStat(StatId statId)
        {
            return StatId.TryParseStatType(statId, out var statType) ? GetStat(statType) : null;
        }

        public float GetStatValue(StatType type)
        {
            if (_stats.TryGetValue(type, out var stat)) return stat.Value;
            return 0;
        }

        public float GetStatValue(StatId statId)
        {
            return StatId.TryParseStatType(statId, out var statType) ? GetStatValue(statType) : 0f;
        }

        public void AddModifier(StatType type, StatModifier mod)
        {
            if (!_stats.ContainsKey(type))
            {
                var stat = new Stat(0);
                _stats[type] = stat;
                SubscribeToStat(type, stat);
            }
            _stats[type].AddModifier(mod);
        }

        public void AddModifier(StatId statId, StatModifier mod)
        {
            if (StatId.TryParseStatType(statId, out var statType))
            {
                AddModifier(statType, mod);
            }
        }

        public void RemoveModifier(StatType type, StatModifier mod)
        {
            if (_stats.TryGetValue(type, out var stat))
            {
                stat.RemoveModifier(mod);
            }
        }

        public void RemoveModifier(StatId statId, StatModifier mod)
        {
            if (StatId.TryParseStatType(statId, out var statType))
            {
                RemoveModifier(statType, mod);
            }
        }

        /// <summary>
        /// Force recalculate all stats and fire change events.
        /// </summary>
        public void RefreshAllStats()
        {
            foreach (var stat in _stats.Values)
            {
                stat.ForceRecalculate();
            }
        }

        #region Save/Load Support

        /// <summary>
        /// 导出存档数据
        /// </summary>
        public StatControllerSaveData ExportSaveData()
        {
            var data = new StatControllerSaveData();
            foreach (var kvp in _stats)
            {
                data.Stats.Add(new StatSaveData
                {
                    Type = kvp.Key,
                    BaseValue = kvp.Value.BaseValue,
                    MinValue = kvp.Value.MinValue,
                    MaxValue = kvp.Value.MaxValue
                });
            }
            return data;
        }

        /// <summary>
        /// 导入存档数据
        /// </summary>
        public void ImportSaveData(StatControllerSaveData data)
        {
            if (data?.Stats == null) return;

            foreach (var statData in data.Stats)
            {
                if (_stats.TryGetValue(statData.Type, out var existingStat))
                {
                    existingStat.BaseValue = statData.BaseValue;
                    existingStat.MinValue = statData.MinValue;
                    existingStat.MaxValue = statData.MaxValue;
                }
                else
                {
                    InitStat(statData.Type, statData.BaseValue, statData.MinValue, statData.MaxValue);
                }
            }
        }

        /// <summary>
        /// 重置为初始状态
        /// </summary>
        public void ResetStats()
        {
            _stats.Clear();
        }

        #endregion

        private void OnDestroy()
        {
            // Clean up event subscriptions
            foreach (var stat in _stats.Values)
            {
                stat.ClearEventListeners();
            }
            OnAnyStatChanged = null;
        }
    }

    /// <summary>
    /// StatController 存档数据
    /// </summary>
    [Serializable]
    public class StatControllerSaveData
    {
        public List<StatSaveData> Stats = new List<StatSaveData>();
    }

    /// <summary>
    /// 单个 Stat 存档数据
    /// </summary>
    [Serializable]
    public class StatSaveData
    {
        public StatType Type;
        public float BaseValue;
        public float MinValue = float.MinValue;
        public float MaxValue = float.MaxValue;
    }
}
