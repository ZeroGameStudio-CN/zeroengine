using System;
using System.Collections.Generic;

namespace ZeroEngine.StatSystem
{
    /// <summary>
    /// 属性容器，管理多个 Stat 对象。
    /// </summary>
    [Serializable]
    public class Stats
    {
        private readonly Dictionary<StatId, Stat> _statsById = new();
        private readonly Dictionary<StatType, Stat> _statsByType = new();

        /// <summary>
        /// 获取指定类型的属性。
        /// </summary>
        public Stat GetStat(StatType type)
        {
            if (_statsByType.TryGetValue(type, out var stat))
            {
                return stat;
            }

            return _statsById.TryGetValue(ToId(type), out stat) ? stat : null;
        }

        public Stat GetStat(StatId id)
        {
            id = new StatId(id.Value);
            if (id.IsEmpty)
            {
                return null;
            }

            if (_statsById.TryGetValue(id, out var stat))
            {
                return stat;
            }

            return StatId.TryParseStatType(id, out var type) ? GetStat(type) : null;
        }

        /// <summary>
        /// 获取指定类型的属性（泛型版本）。
        /// </summary>
        public T GetStat<T>(StatType type) where T : Stat
        {
            return GetStat(type) as T;
        }

        public T GetStat<T>(StatId id) where T : Stat
        {
            return GetStat(id) as T;
        }

        /// <summary>
        /// 获取或创建属性，并执行初始化回调。
        /// </summary>
        public T GetOrCreateAndInit<T>(StatType type, Action<T> initAction) where T : Stat, new()
        {
            return GetOrCreateAndInit(ToId(type), initAction, type);
        }

        public T GetOrCreateAndInit<T>(StatId id, Action<T> initAction) where T : Stat, new()
        {
            return GetOrCreateAndInit(id, initAction, StatType.None);
        }

        /// <summary>
        /// 设置属性。
        /// </summary>
        public void SetStat(StatType type, Stat stat)
        {
            if (type == StatType.None || stat == null)
            {
                return;
            }

            _statsByType[type] = stat;
            _statsById[ToId(type)] = stat;
        }

        public void SetStat(StatId id, Stat stat)
        {
            id = new StatId(id.Value);
            if (id.IsEmpty || stat == null)
            {
                return;
            }

            _statsById[id] = stat;
            if (StatId.TryParseStatType(id, out var type))
            {
                _statsByType[type] = stat;
            }
        }

        /// <summary>
        /// 移除指定来源的所有修饰器。
        /// </summary>
        public void RemoveAllModifiersFromSource(object source)
        {
            RemoveAllModifiersFromSource(source, preserveCurrentPercent: false);
        }

        public void RemoveAllModifiersFromSource(object source, bool preserveCurrentPercent)
        {
            foreach (var stat in GetUniqueStats())
            {
                if (stat is CurrentStat currentStat && preserveCurrentPercent)
                {
                    var percent = currentStat.Percent;
                    stat.RemoveAllModifiersFromSource(source);
                    currentStat.SetCurrent(currentStat.MaxValue * percent);
                    continue;
                }

                stat.RemoveAllModifiersFromSource(source);
                if (stat is CurrentStat current)
                {
                    current.SetCurrent(current.CurrentValue);
                }
            }
        }

        /// <summary>
        /// 清空所有属性。
        /// </summary>
        public void Clear()
        {
            foreach (var stat in GetUniqueStats())
            {
                stat.ClearEventListeners();
            }

            _statsById.Clear();
            _statsByType.Clear();
        }

        /// <summary>
        /// 获取存档数据。
        /// </summary>
        public Dictionary<StatId, float> GetSaveData()
        {
            var data = new Dictionary<StatId, float>();
            foreach (var kvp in _statsById)
            {
                if (!kvp.Key.IsEmpty)
                {
                    data[kvp.Key] = kvp.Value.BaseValue;
                }
            }

            foreach (var kvp in _statsByType)
            {
                var id = ToId(kvp.Key);
                if (!id.IsEmpty && !data.ContainsKey(id))
                {
                    data[id] = kvp.Value.BaseValue;
                }
            }

            return data;
        }

        /// <summary>
        /// 从存档数据恢复。
        /// </summary>
        public void LoadFromData(Dictionary<StatId, float> data)
        {
            if (data == null)
            {
                return;
            }

            foreach (var kvp in data)
            {
                var stat = GetStat(kvp.Key);
                if (stat == null)
                {
                    stat = new Stat();
                    SetStat(kvp.Key, stat);
                }

                stat.BaseValue = kvp.Value;
            }
        }

        public void LoadFromData(Dictionary<StatType, float> data)
        {
            if (data == null)
            {
                return;
            }

            foreach (var kvp in data)
            {
                var stat = GetStat(kvp.Key);
                if (stat == null)
                {
                    stat = new Stat();
                    SetStat(kvp.Key, stat);
                }

                stat.BaseValue = kvp.Value;
            }
        }

        public bool TryGetStatValue(StatId id, out float value)
        {
            var stat = GetStat(id);
            if (stat == null)
            {
                value = 0f;
                return false;
            }

            value = stat.Value;
            return true;
        }

        public StatBlock ToStatBlock()
        {
            var block = new StatBlock();
            var seenStats = new HashSet<Stat>();
            foreach (var kvp in _statsById)
            {
                if (!kvp.Key.IsEmpty)
                {
                    block.Set(kvp.Key, kvp.Value.Value);
                    seenStats.Add(kvp.Value);
                }
            }

            foreach (var kvp in _statsByType)
            {
                var id = ToId(kvp.Key);
                if (!id.IsEmpty && seenStats.Add(kvp.Value))
                {
                    block.Set(id, kvp.Value.Value);
                }
            }

            return block;
        }

        /// <summary>
        /// 属性数量。
        /// </summary>
        public int Count
        {
            get
            {
                var count = 0;
                foreach (var _ in GetUniqueStats())
                {
                    count++;
                }

                return count;
            }
        }

        /// <summary>
        /// 是否包含指定类型的属性。
        /// </summary>
        public bool ContainsStat(StatType type) => GetStat(type) != null;

        public bool ContainsStat(StatId id) => GetStat(id) != null;

        private T GetOrCreateAndInit<T>(StatId id, Action<T> initAction, StatType type) where T : Stat, new()
        {
            id = new StatId(id.Value);
            if (id.IsEmpty)
            {
                return null;
            }

            var stat = GetStat(id);
            if (stat == null)
            {
                stat = new T();
                _statsById[id] = stat;
                if (type != StatType.None || StatId.TryParseStatType(id, out type))
                {
                    _statsByType[type] = stat;
                }
            }

            if (stat is T typedStat)
            {
                initAction?.Invoke(typedStat);
                return typedStat;
            }

            return null;
        }

        private IEnumerable<Stat> GetUniqueStats()
        {
            var seen = new HashSet<Stat>();
            foreach (var stat in _statsById.Values)
            {
                if (stat != null && seen.Add(stat))
                {
                    yield return stat;
                }
            }

            foreach (var stat in _statsByType.Values)
            {
                if (stat != null && seen.Add(stat))
                {
                    yield return stat;
                }
            }
        }

        private static StatId ToId(StatType type)
        {
            return type == StatType.None ? new StatId(string.Empty) : new StatId(type.ToString());
        }
    }
}
