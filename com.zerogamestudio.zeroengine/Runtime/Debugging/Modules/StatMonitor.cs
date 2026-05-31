using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Debugging
{
    /// <summary>
    /// Stat 系统监控模块
    /// </summary>
    public class StatMonitor : IDebugModule
    {
        public const string MODULE_NAME = "StatSystem";

        private readonly List<StatController> _trackedControllers = new List<StatController>(8);
        private readonly List<StatDebugData> _statData = new List<StatDebugData>(32);
        private readonly List<string> _changeLog = new List<string>(MAX_LOG);
        private const int MAX_LOG = 50;

        private bool _isEnabled = true;

        public string ModuleName => MODULE_NAME;
        public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }

        /// <summary>Stat 变化事件</summary>
        public event Action<StatDebugData, float, float> OnStatChanged;

        /// <summary>
        /// 追踪 StatController
        /// </summary>
        public void TrackController(StatController controller)
        {
            if (controller == null || _trackedControllers.Contains(controller)) return;

            _trackedControllers.Add(controller);
            controller.OnAnyStatChanged += OnStatChangedHandler;
        }

        /// <summary>
        /// 取消追踪 StatController
        /// </summary>
        public void UntrackController(StatController controller)
        {
            if (controller == null) return;

            if (_trackedControllers.Remove(controller))
            {
                controller.OnAnyStatChanged -= OnStatChangedHandler;
            }
        }

        private void OnStatChangedHandler(StatControllerChangedEventArgs args)
        {
            var data = new StatDebugData
            {
                StatName = args.StatId.ToString(),
                BaseValue = 0, // 无法从事件获取
                CurrentValue = args.NewValue,
                ModifierCount = 0
            };

            string delta = args.Delta >= 0 ? $"+{args.Delta:F1}" : $"{args.Delta:F1}";
            AddLog($"{args.StatId}: {args.OldValue:F1} → {args.NewValue:F1} ({delta})");

            OnStatChanged?.Invoke(data, args.OldValue, args.NewValue);
        }

        private void AddLog(string message)
        {
            if (_changeLog.Count >= MAX_LOG)
            {
                _changeLog.RemoveAt(0);
            }
            _changeLog.Add($"[{Time.unscaledTime:F2}] {message}");
        }

        public void Update()
        {
            _statData.Clear();

            foreach (var controller in _trackedControllers)
            {
                if (controller == null) continue;

                var saveData = controller.ExportSaveData();
                if (saveData?.Stats == null)
                {
                    continue;
                }

                for (int s = 0; s < saveData.Stats.Count; s++)
                {
                    var id = saveData.Stats[s].Id;
                    var stat = controller.GetStat(id);
                    if (stat == null) continue;

                    var data = new StatDebugData
                    {
                        StatName = id.ToString(),
                        BaseValue = stat.BaseValue,
                        CurrentValue = stat.Value,
                        MinValue = stat.MinValue,
                        MaxValue = stat.MaxValue,
                        ModifierCount = stat.ModifierCount,
                        Modifiers = new List<StatModifierDebugData>()
                    };

                    // 收集修饰器信息
                    var modifiers = stat.GetModifiers();
                    for (int j = 0; j < modifiers.Count; j++)
                    {
                        var mod = modifiers[j];
                        data.Modifiers.Add(new StatModifierDebugData
                        {
                            Source = mod.Source?.ToString() ?? "Unknown",
                            ModType = mod.ModType.ToString(),
                            Value = mod.Value
                        });
                    }

                    _statData.Add(data);
                }
            }
        }

        public string GetSummary()
        {
            int totalStats = _statData.Count;
            int modifiedStats = 0;

            foreach (var data in _statData)
            {
                if (data.ModifierCount > 0)
                    modifiedStats++;
            }

            return $"Controllers: {_trackedControllers.Count}, Stats: {totalStats}, Modified: {modifiedStats}";
        }

        public IEnumerable<DebugEntry> GetEntries()
        {
            foreach (var data in _statData)
            {
                var type = data.ModifierCount > 0 ? DebugEntryType.Warning : DebugEntryType.Info;
                yield return new DebugEntry(data.StatName, data.ToString(), type);
            }
        }

        /// <summary>
        /// 获取 Stat 数据列表
        /// </summary>
        public IReadOnlyList<StatDebugData> GetStatData() => _statData;

        /// <summary>
        /// 获取变化日志
        /// </summary>
        public IReadOnlyList<string> GetChangeLog() => _changeLog;

        public void Clear()
        {
            _statData.Clear();
            _changeLog.Clear();
        }
    }
}
