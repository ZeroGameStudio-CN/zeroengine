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

        /// <summary>
        /// 获取指定类型的属性
        /// </summary>
        public Stat GetStat(StatType type)
        {
            return _stats.TryGetValue(type, out var stat) ? stat : null;
        }

        /// <summary>
        /// 获取指定类型的属性（泛型版本）
        /// </summary>
        public T GetStat<T>(StatType type) where T : Stat
        {
            return _stats.TryGetValue(type, out var stat) ? stat as T : null;
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
            if (!_stats.TryGetValue(type, out var stat))
            {
                stat = new T();
                _stats[type] = stat;
            }

            if (stat is T typedStat)
            {
                initAction?.Invoke(typedStat);
                return typedStat;
            }

            return null;
        }

        /// <summary>
        /// 设置属性
        /// </summary>
        public void SetStat(StatType type, Stat stat)
        {
            _stats[type] = stat;
        }

        /// <summary>
        /// 移除指定来源的所有修饰器
        /// </summary>
        public void RemoveAllModifiersFromSource(object source)
        {
            foreach (var stat in _stats.Values)
            {
                stat.RemoveAllModifiersFromSource(source);
            }
        }

        /// <summary>
        /// 清空所有属性
        /// </summary>
        public void Clear()
        {
            foreach (var stat in _stats.Values)
            {
                stat.ClearEventListeners();
            }
            _stats.Clear();
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
                    stat.InitBase(kvp.Value, round: false);
                }
            }
        }

        /// <summary>
        /// 属性数量
        /// </summary>
        public int Count => _stats.Count;

        /// <summary>
        /// 是否包含指定类型的属性
        /// </summary>
        public bool ContainsStat(StatType type) => _stats.ContainsKey(type);
    }
}
