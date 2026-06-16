using System;
using System.Collections.Generic;
using ZeroEngine.StatSystem.Formula;

namespace ZeroEngine.StatSystem
{
    [Serializable]
    public class Stats
    {
        private static readonly object NullSourceKey = new();

        private readonly Dictionary<StatId, Stat> _stats = new();
        private readonly Dictionary<object, Dictionary<StatId, Dictionary<StatModifier, List<StatModifier>>>> _runtimeModifiers = new();

        public int Count => _stats.Count;

        public Stat GetStat(StatId id)
        {
            return _stats.TryGetValue(id, out var stat) ? stat : null;
        }

        public T GetStat<T>(StatId id) where T : Stat
        {
            return _stats.TryGetValue(id, out var stat) ? stat as T : null;
        }

        public T GetOrCreateAndInit<T>(StatId id, Action<T> initAction) where T : Stat, new()
        {
            if (id.IsEmpty)
            {
                return null;
            }

            if (!_stats.TryGetValue(id, out var stat))
            {
                stat = new T();
                _stats[id] = stat;
            }

            if (stat is T typedStat)
            {
                initAction?.Invoke(typedStat);
                return typedStat;
            }

            return null;
        }

        public T AddStat<T>(StatId id) where T : Stat, new()
        {
            if (id.IsEmpty)
            {
                return null;
            }

            var stat = new T();
            _stats[id] = stat;
            return stat;
        }

        public void SetStat(StatId id, Stat stat)
        {
            if (id.IsEmpty || stat == null)
            {
                return;
            }

            _stats[id] = stat;
        }

        public bool TryGetStatValue(StatId id, out float value)
        {
            if (_stats.TryGetValue(id, out var stat))
            {
                value = stat.Value;
                return true;
            }

            value = 0f;
            return false;
        }

        public float GetStatValue(StatId id, float fallback = 0f)
        {
            return TryGetStatValue(id, out var value) ? value : fallback;
        }

        public Dictionary<StatId, float> GetValuesSnapshot()
        {
            var snapshot = new Dictionary<StatId, float>();
            foreach (var kvp in _stats)
            {
                snapshot[kvp.Key] = kvp.Value.Value;
            }

            return snapshot;
        }

        public StatBlock ToStatBlock()
        {
            var block = new StatBlock();
            foreach (var kvp in _stats)
            {
                block.Set(kvp.Key, kvp.Value.Value);
            }

            return block;
        }

        public void AddStatModifier(
            Dictionary<StatId, List<StatModifier>> data,
            object source,
            Func<StatId, Stat> forceAddFactory = null,
            MathContext ctx = null,
            Func<StatId, IncreaseType> increaseTypeResolver = null)
        {
            if (data == null)
            {
                return;
            }

            foreach (var kvp in data)
            {
                var statId = kvp.Key;
                var modifiers = kvp.Value;
                if (statId.IsEmpty || modifiers == null || modifiers.Count == 0)
                {
                    continue;
                }

                if (!_stats.TryGetValue(statId, out var stat))
                {
                    stat = forceAddFactory?.Invoke(statId);
                    if (stat == null)
                    {
                        continue;
                    }

                    _stats[statId] = stat;
                }

                for (var i = 0; i < modifiers.Count; i++)
                {
                    var originalModifier = modifiers[i];
                    if (originalModifier == null)
                    {
                        continue;
                    }

                    var runtimeModifier = originalModifier.NeedsRuntimeValue
                        ? originalModifier.CreateRuntime(ctx)
                        : originalModifier;

                    runtimeModifier.Source = source;

                    if (stat is CurrentStat currentStat)
                    {
                        var increaseType = increaseTypeResolver?.Invoke(statId) ?? IncreaseType.Flat;
                        currentStat.AddModifier(runtimeModifier, source, increaseType);
                    }
                    else
                    {
                        stat.AddModifier(runtimeModifier, source);
                    }

                    RegisterRuntimeModifier(source, statId, originalModifier, runtimeModifier);
                }
            }
        }

        public void RemoveStatModifier(
            Dictionary<StatId, List<StatModifier>> data,
            object source,
            bool preserveCurrentPercent = false)
        {
            if (data == null)
            {
                return;
            }

            foreach (var kvp in data)
            {
                if (!_stats.TryGetValue(kvp.Key, out var stat))
                {
                    continue;
                }

                var modifiers = kvp.Value;
                if (modifiers == null)
                {
                    continue;
                }

                for (var i = 0; i < modifiers.Count; i++)
                {
                    var originalModifier = modifiers[i];
                    if (originalModifier == null)
                    {
                        continue;
                    }

                    var runtimeModifiers = TakeRuntimeModifiers(source, kvp.Key, originalModifier);
                    if (runtimeModifiers.Count == 0)
                    {
                        RemoveSingleModifier(stat, originalModifier, source, preserveCurrentPercent);
                        continue;
                    }

                    for (var j = 0; j < runtimeModifiers.Count; j++)
                    {
                        RemoveSingleModifier(stat, runtimeModifiers[j], source, preserveCurrentPercent);
                    }
                }
            }
        }

        public void RemoveAllModifiersFromSource(object source)
        {
            RemoveAllModifiersFromSource(source, false);
        }

        public void RemoveAllModifiersFromSource(object source, bool preserveCurrentPercent)
        {
            foreach (var stat in _stats.Values)
            {
                if (stat is CurrentStat currentStat)
                {
                    currentStat.RemoveAllModifiersFromSource(source, preserveCurrentPercent);
                }
                else
                {
                    stat.RemoveAllModifiersFromSource(source);
                }
            }

            _runtimeModifiers.Remove(GetRuntimeSourceKey(source));
        }

        public void Clear()
        {
            foreach (var stat in _stats.Values)
            {
                stat.ClearEventListeners();
            }

            _stats.Clear();
            _runtimeModifiers.Clear();
        }

        public Dictionary<StatId, float> GetSaveData()
        {
            var data = new Dictionary<StatId, float>();
            foreach (var kvp in _stats)
            {
                data[kvp.Key] = kvp.Value.BaseValue;
            }

            return data;
        }

        public void LoadFromData(Dictionary<StatId, float> data)
        {
            if (data == null)
            {
                return;
            }

            foreach (var kvp in data)
            {
                if (_stats.TryGetValue(kvp.Key, out var stat))
                {
                    stat.InitBase(kvp.Value, false);
                    if (stat is CurrentStat currentStat)
                    {
                        currentStat.SetCurrent(currentStat.CurrentValue);
                    }
                }
            }
        }

        public bool ContainsStat(StatId id)
        {
            return _stats.ContainsKey(id);
        }

        private static void RemoveSingleModifier(Stat stat, StatModifier modifier, object source, bool preserveCurrentPercent)
        {
            if (stat is CurrentStat currentStat)
            {
                currentStat.RemoveModifier(modifier, source, preserveCurrentPercent);
            }
            else
            {
                stat.RemoveModifier(modifier, source);
            }
        }

        private void RegisterRuntimeModifier(object source, StatId statId, StatModifier originalModifier, StatModifier runtimeModifier)
        {
            if (originalModifier == null || runtimeModifier == null)
            {
                return;
            }

            var sourceKey = GetRuntimeSourceKey(source);
            if (!_runtimeModifiers.TryGetValue(sourceKey, out var sourceMap))
            {
                sourceMap = new Dictionary<StatId, Dictionary<StatModifier, List<StatModifier>>>();
                _runtimeModifiers[sourceKey] = sourceMap;
            }

            if (!sourceMap.TryGetValue(statId, out var statMap))
            {
                statMap = new Dictionary<StatModifier, List<StatModifier>>();
                sourceMap[statId] = statMap;
            }

            if (!statMap.TryGetValue(originalModifier, out var runtimeModifiers))
            {
                runtimeModifiers = new List<StatModifier>();
                statMap[originalModifier] = runtimeModifiers;
            }

            runtimeModifiers.Add(runtimeModifier);
        }

        private List<StatModifier> TakeRuntimeModifiers(object source, StatId statId, StatModifier originalModifier)
        {
            if (originalModifier == null)
            {
                return new List<StatModifier>();
            }

            var sourceKey = GetRuntimeSourceKey(source);
            if (!_runtimeModifiers.TryGetValue(sourceKey, out var sourceMap))
            {
                return new List<StatModifier>();
            }

            if (!sourceMap.TryGetValue(statId, out var statMap))
            {
                return new List<StatModifier>();
            }

            if (!statMap.TryGetValue(originalModifier, out var runtimeModifiers))
            {
                return new List<StatModifier>();
            }

            statMap.Remove(originalModifier);
            if (statMap.Count == 0)
            {
                sourceMap.Remove(statId);
            }

            if (sourceMap.Count == 0)
            {
                _runtimeModifiers.Remove(sourceKey);
            }

            return runtimeModifiers;
        }

        private static object GetRuntimeSourceKey(object source)
        {
            return source ?? NullSourceKey;
        }
    }
}
