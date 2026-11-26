using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AI.NPCSchedule
{
    /// <summary>
    /// NPC 日程 ScriptableObject
    /// 定义 NPC 的完整时间表
    /// </summary>
    [CreateAssetMenu(fileName = "NPCSchedule", menuName = "ZeroEngine/AI and Behavior/Schedules/NPC Schedule")]
    public class NPCScheduleSO : ScriptableObject
    {
        #region Serialized Fields

        [Header("Info")]
        [SerializeField] private string _scheduleId = "";
        [SerializeField] private string _displayName = "";
        [SerializeField] [TextArea(2, 4)]
        private string _description = "";

        [Header("Entries")]
        [SerializeField] private List<ScheduleEntry> _entries = new();

        [Header("Default Behavior")]
        [SerializeField] private ScheduleEntry _defaultEntry;

        [Header("Presets")]
        [SerializeField] private SchedulePresetSO[] _presets;

        #endregion

        #region Properties

        /// <summary>日程 ID</summary>
        public string ScheduleId => _scheduleId;

        /// <summary>显示名称</summary>
        public string DisplayName => _displayName;

        /// <summary>描述</summary>
        public string Description => _description;

        /// <summary>所有条目</summary>
        public IReadOnlyList<ScheduleEntry> Entries => _entries;

        /// <summary>默认条目 (无匹配时使用)</summary>
        public ScheduleEntry DefaultEntry => _defaultEntry;

        #endregion

        #region Methods

        /// <summary>
        /// 通过 ID 获取条目
        /// </summary>
        public ScheduleEntry GetEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return null;

            foreach (var entry in _entries)
            {
                if (entry?.EntryId == entryId)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// 添加条目
        /// </summary>
        public void AddEntry(ScheduleEntry entry)
        {
            if (entry != null && !_entries.Contains(entry))
            {
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// 移除条目
        /// </summary>
        public bool RemoveEntry(ScheduleEntry entry)
        {
            return _entries.Remove(entry);
        }

        /// <summary>
        /// 通过 ID 移除条目
        /// </summary>
        public bool RemoveEntry(string entryId)
        {
            var entry = GetEntry(entryId);
            return entry != null && _entries.Remove(entry);
        }

        /// <summary>
        /// 获取指定时间的活跃条目
        /// </summary>
        public List<ScheduleEntry> GetActiveEntries(float hour, DayOfWeek day, Season season)
        {
            var active = new List<ScheduleEntry>();

            foreach (var entry in _entries)
            {
                if (entry == null) continue;
                if (!entry.IsActiveAtTime(hour)) continue;
                if (!entry.IsActiveOnDay(day)) continue;
                if (!entry.IsActiveInSeason(season)) continue;

                active.Add(entry);
            }

            // 按优先级排序 (高到低)
            active.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            return active;
        }

        /// <summary>
        /// 重置所有行动状态
        /// </summary>
        public void ResetAllActions()
        {
            foreach (var entry in _entries)
            {
                entry?.Action?.Reset();
            }

            _defaultEntry?.Action?.Reset();
        }

        /// <summary>
        /// 应用预设
        /// </summary>
        public void ApplyPreset(SchedulePresetSO preset)
        {
            if (preset == null) return;

            foreach (var entry in preset.Entries)
            {
                if (entry != null)
                {
                    AddEntry(entry);
                }
            }
        }

        /// <summary>
        /// 验证日程数据
        /// </summary>
        public List<string> Validate()
        {
            var issues = new List<string>();

            // 检查 ID
            if (string.IsNullOrEmpty(_scheduleId))
            {
                issues.Add("Schedule ID is empty");
            }

            // 检查条目
            var usedIds = new HashSet<string>();
            foreach (var entry in _entries)
            {
                if (entry == null)
                {
                    issues.Add("Null entry found");
                    continue;
                }

                if (string.IsNullOrEmpty(entry.EntryId))
                {
                    issues.Add("Entry with empty ID found");
                }
                else if (usedIds.Contains(entry.EntryId))
                {
                    issues.Add($"Duplicate entry ID: {entry.EntryId}");
                }
                else
                {
                    usedIds.Add(entry.EntryId);
                }

                // 检查时间
                if (entry.StartHour < 0 || entry.StartHour > 24)
                {
                    issues.Add($"Entry {entry.EntryId}: Invalid start hour ({entry.StartHour})");
                }

                if (entry.EndHour < 0 || entry.EndHour > 24)
                {
                    issues.Add($"Entry {entry.EntryId}: Invalid end hour ({entry.EndHour})");
                }

                // 检查行动
                if (entry.Action == null)
                {
                    issues.Add($"Entry {entry.EntryId}: No action defined");
                }
            }

            return issues;
        }

        #endregion

        #region Editor

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 自动生成 ID
            if (string.IsNullOrEmpty(_scheduleId))
            {
                _scheduleId = name;
            }
        }
#endif

        #endregion
    }

    /// <summary>
    /// 日程预设 ScriptableObject
    /// 可重用的日程模板
    /// </summary>
    [CreateAssetMenu(fileName = "SchedulePreset", menuName = "ZeroEngine/AI and Behavior/Schedules/Schedule Preset")]
    public class SchedulePresetSO : ScriptableObject
    {
        #region Serialized Fields

        [Header("Info")]
        [SerializeField] private string _presetId = "";
        [SerializeField] private string _displayName = "";
        [SerializeField] [TextArea(2, 4)]
        private string _description = "";

        [Header("Category")]
        [SerializeField] private SchedulePresetCategory _category = SchedulePresetCategory.General;

        [Header("Entries")]
        [SerializeField] private List<ScheduleEntry> _entries = new();

        #endregion

        #region Properties

        public string PresetId => _presetId;
        public string DisplayName => _displayName;
        public string Description => _description;
        public SchedulePresetCategory Category => _category;
        public IReadOnlyList<ScheduleEntry> Entries => _entries;

        #endregion

        #region Editor

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_presetId))
            {
                _presetId = name;
            }
        }
#endif

        #endregion
    }

    /// <summary>
    /// 预设分类
    /// </summary>
    public enum SchedulePresetCategory
    {
        General,
        Shopkeeper,
        Farmer,
        Guard,
        Noble,
        Villager,
        Adventurer,
        Custom
    }
}
