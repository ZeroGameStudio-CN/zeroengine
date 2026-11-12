using System;
using System.Collections.Generic;

namespace ZeroEngine.StatSystem
{
    /// <summary>
    /// 属性容器，管理多个 Stat 对象
    /// 非 MonoBehaviour，可作为纯数据类使用
    /// </summary>
    [Serializable]
    public class Stats
    {
        private readonly Dictionary<StatType, Stat> _stats = new();
        private readonly Dictionary<StatId, Stat> _statsById = new();

        /// <summary>
        /// 获取指定类型的属性
        /// </summary>
        public Stat GetStat(StatType type)
        {
            if (_stats.TryGetValue(type, out var stat)) return stat;
            return _statsById.TryGetValue(ToId(type), out stat) ? stat : null;
        }

        public Stat GetStat(StatId id)
        {
            id = new StatId(id.Value);
            if (id.IsEmpty) return null;
            if (_statsById.TryGetValue(id, out var stat)) return stat;
            return StatId.TryParseStatType(id, out var type) ? GetStat(type) : null;
        }

        /// <summary>
        /// 获取指定类型的属性（泛型版本）
        /// </summary>
        public T GetStat<T>(StatType type) where T : Stat
        {
            return _stats.TryGetValue(type, out var stat) ? stat as T : null;
        }

        public T GetStat<T>(StatId id) where T : Stat
        {
            return GetStat(id) as T;
        }

        /// <summary>
        /// 获取或创建属性，并执行初始化回调
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="type">属性枚举类型</param>
        /// <param name="initAction">初始化回调</param>
        /// <returns>属性实例</returns>
        public T GetOrCreateAndInit<T>(StatType type, Action<T> initAction) where T : Stat, new()
        {
            return GetOrCreateAndInit(ToId(type), initAction, type);
        }

        public T GetOrCreateAndInit<T>(StatId id, Action<T> initAction) where T : Stat, new()
        {
            return GetOrCreateAndInit(id, initAction, StatType.None);
        }

        /// <summary>
        /// 设置属性
        /// </summary>
        public void SetStat(StatType type, Stat stat)
        {
            if (type == StatType.None || stat == null) return;
            _stats[type] = stat;
            _statsById[ToId(type)] = stat;
        }

        public void SetStat(StatId id, Stat stat)
        {
            id = new StatId(id.Value);
            if (id.IsEmpty || stat == null) return;
            _statsById[id] = stat;
            if (StatId.TryParseStatType(id, out var type)) _stats[type] = stat;
        }

        /// <summary>
        /// 移除指定来源的所有修饰器
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
            }
        }

        /// <summary>
        /// 清空所有属性
        /// </summary>
        public void Clear()
        {
            foreach (var stat in GetUniqueStats())
            {
                stat.ClearEventListeners();
            }
            _stats.Clear();
            _statsById.Clear();
        }

        /// <summary>
        /// 获取存档数据
        /// </summary>
        public Dictionary<StatType, float> GetSaveData()
        {
            var data = new Dictionary<StatType, float>();
            foreach (var kvp in _stats)
            {
                data[kvp.Key] = kvp.Value.BaseValue;
            }
            return data;
        }

        /// <summary>
        /// 从存档数据恢复
        /// </summary>
        public void LoadFromData(Dictionary<StatType, float> data)
        {
            if (data == null) return;

            foreach (var kvp in data)
            {
                if (_stats.TryGetValue(kvp.Key, out var stat))
                {
                    stat.BaseValue = kvp.Value;
                }
            }
        }

        public void LoadFromData(Dictionary<StatId, float> data)
        {
            if (data == null) return;
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

        /// <summary>
        /// 属性数量
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
        /// 是否包含指定类型的属性
        /// </summary>
        public bool ContainsStat(StatType type) => _stats.ContainsKey(type);

        public bool ContainsStat(StatId id) => GetStat(id) != null;

        private T GetOrCreateAndInit<T>(StatId id, Action<T> initAction, StatType type) where T : Stat, new()
        {
            id = new StatId(id.Value);
            if (id.IsEmpty) return null;

            var stat = GetStat(id);
            if (stat == null)
            {
                stat = new T();
                _statsById[id] = stat;
                if (type != StatType.None || StatId.TryParseStatType(id, out type)) _stats[type] = stat;
            }

            if (stat is not T typedStat) return null;
            initAction?.Invoke(typedStat);
            return typedStat;
        }

        private IEnumerable<Stat> GetUniqueStats()
        {
            var seen = new HashSet<Stat>();
            foreach (var stat in _statsById.Values)
                if (stat != null && seen.Add(stat)) yield return stat;
            foreach (var stat in _stats.Values)
                if (stat != null && seen.Add(stat)) yield return stat;
        }

        private static StatId ToId(StatType type)
        {
            return type == StatType.None ? new StatId(string.Empty) : new StatId(type.ToString());
        }
    }
}
