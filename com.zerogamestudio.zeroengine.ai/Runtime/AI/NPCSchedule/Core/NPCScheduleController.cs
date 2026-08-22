using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AI.NPCSchedule
{
    /// <summary>
    /// NPC 日程控制器
    /// 管理 NPC 的时间表行为
    /// </summary>
    public class NPCScheduleController : MonoBehaviour, IAIBrain
    {
        #region Serialized Fields

        [Header("Schedule Data")]
        [SerializeField] private NPCScheduleSO _scheduleData;

        [Header("Settings")]
        [SerializeField] private bool _autoStart = true;
        [SerializeField] private float _updateInterval = 1f;
        [SerializeField] private bool _useGameTime = true;

        [Header("Override")]
        [SerializeField] private bool _allowCombatOverride = true;
        [SerializeField] private bool _allowEventOverride = true;

        [Header("Debug")]
        [SerializeField] private bool _enableDebug = false;

        #endregion

        #region Runtime State

        private AIContext _context;
        private bool _isActive;
        private float _updateTimer;

        private ScheduleEntry _currentEntry;
        private ScheduleEntry _previousEntry;

        private float _currentHour;
        private DayOfWeek _currentDay;
        private Season _currentSeason;

        private bool _isOverridden;
        private string _overrideReason;

        #endregion

        #region Properties

        /// <summary>日程数据</summary>
        public NPCScheduleSO ScheduleData
        {
            get => _scheduleData;
            set
            {
                _scheduleData = value;
                UpdateSchedule();
            }
        }

        /// <summary>是否激活</summary>
        public bool IsActive
        {
            get => _isActive;
            set => _isActive = value;
        }

        /// <summary>当前行动名称</summary>
        public string CurrentActionName =>
            _currentEntry?.Action?.ActionName ?? "None";

        /// <summary>当前日程条目</summary>
        public ScheduleEntry CurrentEntry => _currentEntry;

        /// <summary>是否被覆盖</summary>
        public bool IsOverridden => _isOverridden;

        /// <summary>覆盖原因</summary>
        public string OverrideReason => _overrideReason;

        /// <summary>当前小时</summary>
        public float CurrentHour => _currentHour;

        #endregion

        #region Events

        /// <summary>日程变更事件</summary>
        public event Action<ScheduleEntry, ScheduleEntry> OnScheduleChanged;

        /// <summary>行动开始事件</summary>
        public event Action<ScheduleAction> OnActionStarted;

        /// <summary>行动结束事件</summary>
        public event Action<ScheduleAction, bool> OnActionEnded;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (_autoStart)
            {
                _isActive = true;
            }
        }

        #endregion

        #region IAIBrain Implementation

        public void Initialize(AIContext context)
        {
            _context = context;

            // 初始化时间
            UpdateTime();
            UpdateSchedule();

            if (_enableDebug)
            {
                Debug.Log($"[NPCSchedule] Initialized with {_scheduleData?.Entries?.Count ?? 0} entries");
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_isActive || _context == null) return;
            if (_isOverridden) return;

            _updateTimer -= deltaTime;
            if (_updateTimer <= 0f)
            {
                _updateTimer = _updateInterval;
                UpdateTime();
                UpdateSchedule();
            }

            // 更新当前行动
            if (_currentEntry?.Action != null)
            {
                bool completed = _currentEntry.Action.Update(_context, deltaTime);

                if (completed)
                {
                    EndCurrentAction(false);
                }
            }
        }

        public void ForceReevaluate()
        {
            UpdateTime();
            UpdateSchedule();
        }

        public void StopCurrentAction()
        {
            EndCurrentAction(true);
        }

        public void Reset()
        {
            _currentEntry = null;
            _previousEntry = null;
            _isOverridden = false;
            _overrideReason = null;
            _updateTimer = 0f;

            _scheduleData?.ResetAllActions();
        }

        #endregion

        #region Time Management

        /// <summary>
        /// 更新时间
        /// </summary>
        private void UpdateTime()
        {
            if (_useGameTime)
            {
                // 消费项目通过黑板提供游戏时间，AI 包不反向依赖世界系统。
                GetTimeFromBlackboard();
            }
            else
            {
                // 使用真实时间
                var now = DateTime.Now;
                _currentHour = now.Hour + now.Minute / 60f;
                _currentDay = now.DayOfWeek;
            }

            // 更新黑板
            if (_context?.Blackboard != null)
            {
                _context.Blackboard.Set(BlackboardKeys.CurrentHour, _currentHour);
            }
        }

        private void GetTimeFromBlackboard()
        {
            if (_context?.Blackboard != null)
            {
                _currentHour = _context.Blackboard.GetFloat(BlackboardKeys.CurrentHour, 12f);
                _currentDay = (DayOfWeek)_context.Blackboard.GetInt("CurrentDayOfWeek", 0);
                _currentSeason = (Season)_context.Blackboard.GetInt("CurrentSeason", 0);
            }
        }

        #endregion

        #region Schedule Management

        /// <summary>
        /// 更新日程
        /// </summary>
        private void UpdateSchedule()
        {
            if (_scheduleData == null) return;

            // 查找最佳日程条目
            var bestEntry = FindBestEntry();

            // 如果日程发生变化
            if (bestEntry != _currentEntry)
            {
                SwitchEntry(bestEntry);
            }
        }

        /// <summary>
        /// 查找最佳日程条目
        /// </summary>
        private ScheduleEntry FindBestEntry()
        {
            if (_scheduleData?.Entries == null) return null;

            ScheduleEntry best = null;
            int highestPriority = int.MinValue;

            foreach (var entry in _scheduleData.Entries)
            {
                if (entry == null) continue;

                // 检查时间
                if (!entry.IsActiveAtTime(_currentHour)) continue;

                // 检查星期
                if (!entry.IsActiveOnDay(_currentDay)) continue;

                // 检查季节
                if (!entry.IsActiveInSeason(_currentSeason)) continue;

                // 检查条件
                if (!entry.CheckConditions(_context)) continue;

                // 选择优先级最高的
                if (entry.Priority > highestPriority)
                {
                    highestPriority = entry.Priority;
                    best = entry;
                }
            }

            return best;
        }

        /// <summary>
        /// 切换日程条目
        /// </summary>
        private void SwitchEntry(ScheduleEntry newEntry)
        {
            // 结束当前行动
            if (_currentEntry?.Action != null)
            {
                EndCurrentAction(true);
            }

            _previousEntry = _currentEntry;
            _currentEntry = newEntry;

            // 开始新行动
            if (_currentEntry?.Action != null)
            {
                StartCurrentAction();
            }

            // 更新黑板
            if (_context?.Blackboard != null)
            {
                _context.Blackboard.Set(BlackboardKeys.CurrentSchedule, _currentEntry?.EntryId ?? "");
            }

            OnScheduleChanged?.Invoke(_previousEntry, _currentEntry);

            if (_enableDebug)
            {
                Debug.Log($"[NPCSchedule] Switched: {_previousEntry?.EntryId ?? "None"} -> {_currentEntry?.EntryId ?? "None"}");
            }
        }

        private void StartCurrentAction()
        {
            if (_currentEntry?.Action == null) return;

            _currentEntry.Action.Start(_context, _currentEntry);
            OnActionStarted?.Invoke(_currentEntry.Action);

            if (_enableDebug)
            {
                Debug.Log($"[NPCSchedule] Action started: {_currentEntry.Action.ActionName}");
            }
        }

        private void EndCurrentAction(bool interrupted)
        {
            if (_currentEntry?.Action == null) return;

            _currentEntry.Action.End(_context, interrupted);
            OnActionEnded?.Invoke(_currentEntry.Action, interrupted);

            if (_enableDebug)
            {
                Debug.Log($"[NPCSchedule] Action ended: {_currentEntry.Action.ActionName} (interrupted: {interrupted})");
            }
        }

        #endregion

        #region Override Control

        /// <summary>
        /// 设置覆盖状态 (暂停日程)
        /// </summary>
        public void SetOverride(bool isOverridden, string reason = null)
        {
            if (_isOverridden == isOverridden) return;

            _isOverridden = isOverridden;
            _overrideReason = reason;

            if (_isOverridden)
            {
                // 暂停当前行动
                EndCurrentAction(true);

                if (_enableDebug)
                {
                    Debug.Log($"[NPCSchedule] Override started: {reason}");
                }
            }
            else
            {
                // 恢复日程
                ForceReevaluate();

                if (_enableDebug)
                {
                    Debug.Log("[NPCSchedule] Override ended");
                }
            }
        }

        /// <summary>
        /// 战斗开始时覆盖日程
        /// </summary>
        public void OnCombatStarted()
        {
            if (_allowCombatOverride)
            {
                SetOverride(true, "InCombat");
            }
        }

        /// <summary>
        /// 战斗结束时恢复日程
        /// </summary>
        public void OnCombatEnded()
        {
            if (_overrideReason == "InCombat")
            {
                SetOverride(false);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 手动设置时间
        /// </summary>
        public void SetTime(float hour, DayOfWeek day, Season season)
        {
            _currentHour = hour;
            _currentDay = day;
            _currentSeason = season;
            UpdateSchedule();
        }

        /// <summary>
        /// 获取下一个日程条目
        /// </summary>
        public ScheduleEntry GetNextEntry()
        {
            if (_scheduleData?.Entries == null) return null;

            ScheduleEntry next = null;
            float minHoursUntil = float.MaxValue;

            foreach (var entry in _scheduleData.Entries)
            {
                if (entry == null || entry == _currentEntry) continue;
                if (!entry.IsActiveOnDay(_currentDay)) continue;
                if (!entry.IsActiveInSeason(_currentSeason)) continue;

                float hoursUntil = entry.GetHoursUntilStart(_currentHour);
                if (hoursUntil < minHoursUntil)
                {
                    minHoursUntil = hoursUntil;
                    next = entry;
                }
            }

            return next;
        }

        /// <summary>
        /// 强制执行指定日程条目
        /// </summary>
        public void ForceEntry(string entryId)
        {
            var entry = _scheduleData?.GetEntry(entryId);
            if (entry != null)
            {
                SwitchEntry(entry);
            }
        }

        #endregion
    }
}
