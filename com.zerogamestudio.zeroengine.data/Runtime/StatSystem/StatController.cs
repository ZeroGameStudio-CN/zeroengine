using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.StatSystem
{
    public struct StatControllerChangedEventArgs
    {
        public StatId StatId;
        public float OldValue;
        public float NewValue;
        public float Delta => NewValue - OldValue;

        public StatControllerChangedEventArgs(StatId statId, float oldValue, float newValue)
        {
            StatId = statId;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    public class StatController : MonoBehaviour, IStatProvider
    {
        [SerializeField] private List<StatControllerEntry> _entries = new();

        private readonly Dictionary<StatId, Stat> _stats = new();

        public event Action<StatControllerChangedEventArgs> OnAnyStatChanged;

        public void InitStat(StatId id, float baseValue)
        {
            EnsureRuntimeMap();
            if (_stats.TryGetValue(id, out var existing))
            {
                existing.BaseValue = baseValue;
                return;
            }

            var stat = new Stat(baseValue);
            _stats[id] = stat;
            SubscribeToStat(id, stat);
            UpsertEntry(id, stat);
        }

        public void InitStat(StatId id, float baseValue, float minValue, float maxValue)
        {
            EnsureRuntimeMap();
            if (_stats.TryGetValue(id, out var existing))
            {
                existing.BaseValue = baseValue;
                existing.MinValue = minValue;
                existing.MaxValue = maxValue;
                UpsertEntry(id, existing);
                return;
            }

            var stat = new Stat(baseValue, minValue, maxValue);
            _stats[id] = stat;
            SubscribeToStat(id, stat);
            UpsertEntry(id, stat);
        }

        public Stat GetStat(StatId id)
        {
            EnsureRuntimeMap();
            return _stats.TryGetValue(id, out var stat) ? stat : null;
        }

        public float GetStatValue(StatId id)
        {
            EnsureRuntimeMap();
            return _stats.TryGetValue(id, out var stat) ? stat.Value : 0f;
        }

        public void AddModifier(StatId id, StatModifier mod)
        {
            EnsureRuntimeMap();
            if (!_stats.TryGetValue(id, out var stat))
            {
                stat = new Stat(0);
                _stats[id] = stat;
                SubscribeToStat(id, stat);
            }

            stat.AddModifier(mod);
            UpsertEntry(id, stat);
        }

        public void RemoveModifier(StatId id, StatModifier mod)
        {
            EnsureRuntimeMap();
            if (_stats.TryGetValue(id, out var stat))
            {
                stat.RemoveModifier(mod);
                UpsertEntry(id, stat);
            }
        }

        public void RefreshAllStats()
        {
            EnsureRuntimeMap();
            foreach (var stat in _stats.Values)
            {
                stat.ForceRecalculate();
            }
        }

        public StatControllerSaveData ExportSaveData()
        {
            EnsureRuntimeMap();
            var data = new StatControllerSaveData();
            foreach (var kvp in _stats)
            {
                data.Stats.Add(new StatSaveData
                {
                    Id = kvp.Key,
                    BaseValue = kvp.Value.BaseValue,
                    MinValue = kvp.Value.MinValue,
                    MaxValue = kvp.Value.MaxValue
                });
            }

            return data;
        }

        public void ImportSaveData(StatControllerSaveData data)
        {
            if (data?.Stats == null)
            {
                return;
            }

            EnsureRuntimeMap();
            foreach (var statData in data.Stats)
            {
                if (_stats.TryGetValue(statData.Id, out var existingStat))
                {
                    existingStat.BaseValue = statData.BaseValue;
                    existingStat.MinValue = statData.MinValue;
                    existingStat.MaxValue = statData.MaxValue;
                    UpsertEntry(statData.Id, existingStat);
                }
                else
                {
                    InitStat(statData.Id, statData.BaseValue, statData.MinValue, statData.MaxValue);
                }
            }
        }

        public void ResetStats()
        {
            foreach (var stat in _stats.Values)
            {
                stat.ClearEventListeners();
            }

            _stats.Clear();
            _entries.Clear();
        }

        private void Awake()
        {
            EnsureRuntimeMap();
        }

        private void OnDestroy()
        {
            foreach (var stat in _stats.Values)
            {
                stat.ClearEventListeners();
            }

            OnAnyStatChanged = null;
        }

        private void EnsureRuntimeMap()
        {
            if (_stats.Count > 0 || _entries.Count == 0)
            {
                return;
            }

            foreach (var entry in _entries)
            {
                if (entry.Id.IsEmpty)
                {
                    continue;
                }

                var stat = new Stat(entry.BaseValue, entry.MinValue, entry.MaxValue);
                _stats[entry.Id] = stat;
                SubscribeToStat(entry.Id, stat);
            }
        }

        private void SubscribeToStat(StatId id, Stat stat)
        {
            stat.OnValueChanged += args =>
            {
                OnAnyStatChanged?.Invoke(new StatControllerChangedEventArgs(id, args.OldValue, args.NewValue));
            };
        }

        private void UpsertEntry(StatId id, Stat stat)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (!_entries[i].Id.Equals(id))
                {
                    continue;
                }

                _entries[i] = new StatControllerEntry
                {
                    Id = id,
                    BaseValue = stat.BaseValue,
                    MinValue = stat.MinValue,
                    MaxValue = stat.MaxValue
                };
                return;
            }

            _entries.Add(new StatControllerEntry
            {
                Id = id,
                BaseValue = stat.BaseValue,
                MinValue = stat.MinValue,
                MaxValue = stat.MaxValue
            });
        }
    }

    [Serializable]
    public struct StatControllerEntry
    {
        public StatId Id;
        public float BaseValue;
        public float MinValue;
        public float MaxValue;
    }

    [Serializable]
    public class StatControllerSaveData
    {
        public List<StatSaveData> Stats = new();
    }

    [Serializable]
    public class StatSaveData
    {
        public StatId Id;
        public float BaseValue;
        public float MinValue = float.MinValue;
        public float MaxValue = float.MaxValue;
    }
}
