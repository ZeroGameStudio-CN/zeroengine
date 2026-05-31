using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 成就管理器
    /// 负责成就的解锁、进度追踪和奖励发放
    /// 支持外部成就提供者（Steam 等）
    /// </summary>
    public class AchievementManager : MonoSingleton<AchievementManager>, ISaveable
    {
        [Header("配置")]
        [Tooltip("所有成就定义")]
        [SerializeField] private List<AchievementSO> _allAchievements = new List<AchievementSO>();

        [Tooltip("成就组")]
        [SerializeField] private List<AchievementGroupSO> _achievementGroups = new List<AchievementGroupSO>();

#if STEAMWORKS_NET
        [SerializeField] private bool _autoInitializeSteam = true;
#endif

        [Header("调试")]
        [SerializeField] private bool _debugMode;

        // 进度数据
        private readonly Dictionary<string, AchievementProgress> _progressData =
            new Dictionary<string, AchievementProgress>();

        // 成就点数
        private int _totalPoints;

        // 外部成就提供者
        private readonly List<IAchievementProvider> _providers = new List<IAchievementProvider>();

        // 缓存：ID -> AchievementSO
        private readonly Dictionary<string, AchievementSO> _achievementLookup =
            new Dictionary<string, AchievementSO>();

        // 缓存：分类 -> 成就列表
        private readonly Dictionary<AchievementCategory, List<AchievementSO>> _categoryCache =
            new Dictionary<AchievementCategory, List<AchievementSO>>();

        // 事件缓冲（避免事件处理中的重入问题）- 使用Queue避免O(n)移除
        private readonly Queue<(string eventId, object data)> _eventBuffer =
            new Queue<(string, object)>(16);
        private bool _processingEvents;

        // 临时列表（零分配）
        private readonly List<AchievementSO> _tempAchievementList = new List<AchievementSO>(32);

        #region Events

        /// <summary>成就事件（新版）</summary>
        public event Action<AchievementEventArgs> OnAchievementEvent;

        /// <summary>成就解锁（兼容旧版）</summary>
        public event Action<AchievementSO> OnAchievementUnlocked;

        /// <summary>成就进度（兼容旧版）</summary>
        public event Action<AchievementSO, float> OnAchievementProgress;

        #endregion

        #region Properties

        /// <summary>总成就点数</summary>
        public int TotalPoints => _totalPoints;

        /// <summary>已完成成就数量</summary>
        public int CompletedCount
        {
            get
            {
                int count = 0;
                foreach (var progress in _progressData.Values)
                {
                    if (progress.State == AchievementState.Completed ||
                        progress.State == AchievementState.Claimed)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>总成就数量</summary>
        public int TotalCount => _allAchievements.Count;

        #endregion

        #region ISaveable

        public string SaveKey => "AchievementManager";

        public void Register()
        {
            SaveSlotManager.Instance?.Register(this);
        }

        public void Unregister()
        {
            SaveSlotManager.Instance?.Unregister(this);
        }

        public object ExportSaveData()
        {
            return new AchievementSaveData
            {
                TotalPoints = _totalPoints,
                ProgressData = new Dictionary<string, AchievementProgress>(_progressData)
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is not AchievementSaveData saveData) return;

            _totalPoints = saveData.TotalPoints;
            _progressData.Clear();

            if (saveData.ProgressData != null)
            {
                foreach (var kvp in saveData.ProgressData)
                {
                    _progressData[kvp.Key] = kvp.Value;
                }
            }
        }

        public void ResetToDefault()
        {
            _totalPoints = 0;
            _progressData.Clear();
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            BuildCaches();
            InitializeProviders();
        }

        private void Start()
        {
            Register();
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Provider Management

        private void InitializeProviders()
        {
#if STEAMWORKS_NET
            if (_autoInitializeSteam)
            {
                var steamProvider = new Providers.SteamAchievementProvider();
                if (steamProvider.Initialize())
                {
                    _providers.Add(steamProvider);
                    Log("Steam Provider initialized.");
                }
            }
#endif
        }

        /// <summary>
        /// 注册外部成就提供者
        /// </summary>
        public void RegisterProvider(IAchievementProvider provider)
        {
            if (provider != null && provider.Initialize())
            {
                _providers.Add(provider);
            }
        }

        private void SyncProviderUnlock(string achievementId)
        {
            foreach (var provider in _providers)
            {
                try
                {
                    provider.Unlock(achievementId);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Achievement] Provider Unlock Error: {e.Message}");
                }
            }
        }

        private void SyncProviderProgress(string achievementId, float progress)
        {
            foreach (var provider in _providers)
            {
                try
                {
                    provider.SetProgress(achievementId, progress);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Achievement] Provider SetProgress Error: {e.Message}");
                }
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 注册成就（运行时添加）
        /// </summary>
        public void RegisterAchievement(AchievementSO achievement)
        {
            if (achievement == null) return;
            if (_achievementLookup.ContainsKey(achievement.AchievementId)) return;

            _allAchievements.Add(achievement);
            _achievementLookup[achievement.AchievementId] = achievement;

            // 更新分类缓存
            if (!_categoryCache.TryGetValue(achievement.Category, out var list))
            {
                list = new List<AchievementSO>();
                _categoryCache[achievement.Category] = list;
            }
            list.Add(achievement);
        }

        /// <summary>
        /// 触发事件（用于成就追踪）
        /// </summary>
        public void TriggerEvent(string eventId, object data = null)
        {
            if (string.IsNullOrEmpty(eventId)) return;

            // 如果正在处理事件，加入缓冲区
            if (_processingEvents)
            {
                _eventBuffer.Enqueue((eventId, data));
                return;
            }

            ProcessEventInternal(eventId, data);

            // 处理缓冲区中的事件（O(1)出队）
            while (_eventBuffer.Count > 0)
            {
                var buffered = _eventBuffer.Dequeue();
                ProcessEventInternal(buffered.eventId, buffered.data);
            }
        }

        private void ProcessEventInternal(string eventId, object data)
        {
            _processingEvents = true;

            try
            {
                foreach (var achievement in _allAchievements)
                {
                    if (achievement == null) continue;

                    var progress = GetOrCreateProgress(achievement);

                    // 跳过已完成/已领取的成就（除非可重复）
                    if (!achievement.Repeatable &&
                        (progress.State == AchievementState.Completed ||
                         progress.State == AchievementState.Claimed))
                    {
                        continue;
                    }

                    // 跳过锁定的成就
                    if (progress.State == AchievementState.Locked)
                    {
                        // 检查是否满足前置条件
                        if (!achievement.CheckPrerequisites(IsCompleted, GetCharacterLevel()))
                        {
                            continue;
                        }
                        // 解锁
                        progress.State = AchievementState.InProgress;
                        OnAchievementEvent?.Invoke(AchievementEventArgs.Unlocked(achievement));
                        Log($"成就解锁: {achievement.DisplayName}");
                    }

                    // 处理事件
                    float oldProgress = achievement.GetOverallProgress(progress);
                    achievement.ProcessEvent(eventId, data, progress);
                    float newProgress = achievement.GetOverallProgress(progress);

                    // 进度变化事件
                    if (Math.Abs(newProgress - oldProgress) > 0.001f)
                    {
                        OnAchievementEvent?.Invoke(
                            AchievementEventArgs.ProgressUpdated(achievement, newProgress));
                        OnAchievementProgress?.Invoke(achievement, newProgress);
                        SyncProviderProgress(achievement.AchievementId, newProgress);
                        Log($"成就进度: {achievement.DisplayName} {oldProgress:P0} -> {newProgress:P0}");
                    }

                    // 检查是否完成
                    if (progress.State == AchievementState.InProgress &&
                        achievement.CheckAllConditions(progress))
                    {
                        CompleteAchievement(achievement, progress);
                    }
                }
            }
            finally
            {
                _processingEvents = false;
            }
        }

        /// <summary>
        /// 直接解锁成就（旧版 API 兼容）
        /// </summary>
        public void Unlock(string id)
        {
            if (!_achievementLookup.TryGetValue(id, out var achievement))
            {
                Debug.LogWarning($"[Achievement] Unknown Achievement ID: {id}");
                return;
            }

            var progress = GetOrCreateProgress(achievement);
            if (progress.State == AchievementState.Completed ||
                progress.State == AchievementState.Claimed)
            {
                return;
            }

            // 强制完成所有条件
            for (int i = 0; i < (achievement.Conditions?.Count ?? 0); i++)
            {
                progress.ConditionProgress[i] = int.MaxValue;
            }

            CompleteAchievement(achievement, progress);
        }

        /// <summary>
        /// 设置进度（旧版 API 兼容）
        /// </summary>
        public void SetProgress(string id, float progressValue)
        {
            if (!_achievementLookup.TryGetValue(id, out var achievement))
            {
                return;
            }

            var progress = GetOrCreateProgress(achievement);
            if (progress.State == AchievementState.Completed ||
                progress.State == AchievementState.Claimed)
            {
                return;
            }

            progressValue = Mathf.Clamp01(progressValue);

            if (progressValue >= 1.0f)
            {
                Unlock(id);
            }
            else
            {
                OnAchievementProgress?.Invoke(achievement, progressValue);
                SyncProviderProgress(id, progressValue);
            }
        }

        /// <summary>
        /// 获取成就状态
        /// </summary>
        public AchievementState GetState(AchievementSO achievement)
        {
            if (achievement == null) return AchievementState.Locked;
            return GetOrCreateProgress(achievement).State;
        }

        /// <summary>
        /// 获取成就状态（通过ID）
        /// </summary>
        public AchievementState GetState(string achievementId)
        {
            if (!_achievementLookup.TryGetValue(achievementId, out var achievement))
                return AchievementState.Locked;
            return GetState(achievement);
        }

        /// <summary>
        /// 获取成就进度 (0-1)
        /// </summary>
        public float GetProgress(AchievementSO achievement)
        {
            if (achievement == null) return 0f;
            var progress = GetOrCreateProgress(achievement);
            return achievement.GetOverallProgress(progress);
        }

        /// <summary>
        /// 获取成就进度（通过ID）
        /// </summary>
        public float GetProgress(string id)
        {
            if (!_achievementLookup.TryGetValue(id, out var achievement))
                return 0f;
            return GetProgress(achievement);
        }

        /// <summary>
        /// 检查成就是否已完成
        /// </summary>
        public bool IsCompleted(AchievementSO achievement)
        {
            var state = GetState(achievement);
            return state == AchievementState.Completed || state == AchievementState.Claimed;
        }

        /// <summary>
        /// 检查成就是否已解锁（旧版 API 兼容）
        /// </summary>
        public bool IsUnlocked(string id)
        {
            if (!_achievementLookup.TryGetValue(id, out var achievement))
                return false;
            return IsCompleted(achievement);
        }

        /// <summary>
        /// 检查成就是否已领取奖励
        /// </summary>
        public bool IsClaimed(AchievementSO achievement)
        {
            return GetState(achievement) == AchievementState.Claimed;
        }

        /// <summary>
        /// 领取成就奖励
        /// </summary>
        public bool ClaimReward(AchievementSO achievement)
        {
            if (achievement == null) return false;

            var progress = GetOrCreateProgress(achievement);
            if (progress.State != AchievementState.Completed)
            {
                Log($"无法领取奖励: {achievement.DisplayName} (状态: {progress.State})");
                return false;
            }

            // 发放奖励
            achievement.GrantRewards();

            // 更新状态
            progress.State = AchievementState.Claimed;
            progress.ClaimTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // 触发事件
            OnAchievementEvent?.Invoke(AchievementEventArgs.RewardClaimed(achievement));
            Log($"领取奖励: {achievement.DisplayName}");

            return true;
        }

        /// <summary>
        /// 领取所有已完成但未领取的奖励
        /// </summary>
        public int ClaimAllRewards()
        {
            int claimed = 0;

            foreach (var achievement in _allAchievements)
            {
                if (achievement != null && GetState(achievement) == AchievementState.Completed)
                {
                    if (ClaimReward(achievement))
                    {
                        claimed++;
                    }
                }
            }

            return claimed;
        }

        /// <summary>
        /// 添加成就点数
        /// </summary>
        public void AddAchievementPoints(int points)
        {
            if (points <= 0) return;
            _totalPoints += points;
        }

        /// <summary>
        /// 获取指定分类的成就列表（零分配版本）
        /// </summary>
        public void GetAchievementsByCategory(AchievementCategory category, List<AchievementSO> results)
        {
            results.Clear();
            if (_categoryCache.TryGetValue(category, out var cached))
            {
                results.AddRange(cached);
            }
        }

        /// <summary>
        /// 获取所有成就（只读）
        /// </summary>
        public IReadOnlyList<AchievementSO> GetAllAchievements() => _allAchievements;

        /// <summary>
        /// 获取成就组列表（只读）
        /// </summary>
        public IReadOnlyList<AchievementGroupSO> GetAchievementGroups() => _achievementGroups;

        /// <summary>
        /// 通过ID获取成就
        /// </summary>
        public AchievementSO GetAchievement(string achievementId)
        {
            _achievementLookup.TryGetValue(achievementId, out var achievement);
            return achievement;
        }

        /// <summary>
        /// 获取成就数据（旧版 API 兼容）
        /// </summary>
        public AchievementSO GetAchievementData(string id) => GetAchievement(id);

        /// <summary>
        /// 获取可领取奖励的成就列表
        /// </summary>
        public void GetClaimableAchievements(List<AchievementSO> results)
        {
            results.Clear();
            foreach (var achievement in _allAchievements)
            {
                if (achievement != null && GetState(achievement) == AchievementState.Completed)
                {
                    results.Add(achievement);
                }
            }
        }

        /// <summary>
        /// 强制完成成就（调试用）
        /// </summary>
        public void ForceComplete(AchievementSO achievement)
        {
            if (achievement == null) return;

            var progress = GetOrCreateProgress(achievement);
            if (progress.State == AchievementState.Completed ||
                progress.State == AchievementState.Claimed)
            {
                return;
            }

            CompleteAchievement(achievement, progress);
        }

        /// <summary>
        /// 重置成就进度（调试用）
        /// </summary>
        public void ResetAchievement(AchievementSO achievement)
        {
            if (achievement == null) return;

            if (_progressData.ContainsKey(achievement.AchievementId))
            {
                _progressData.Remove(achievement.AchievementId);
                Log($"重置成就: {achievement.DisplayName}");
            }
        }

        #endregion

        #region Internal

        private void BuildCaches()
        {
            _achievementLookup.Clear();
            _categoryCache.Clear();

            foreach (var achievement in _allAchievements)
            {
                if (achievement == null) continue;

                _achievementLookup[achievement.AchievementId] = achievement;

                if (!_categoryCache.TryGetValue(achievement.Category, out var list))
                {
                    list = new List<AchievementSO>();
                    _categoryCache[achievement.Category] = list;
                }
                list.Add(achievement);
            }
        }

        private AchievementProgress GetOrCreateProgress(AchievementSO achievement)
        {
            if (!_progressData.TryGetValue(achievement.AchievementId, out var progress))
            {
                progress = new AchievementProgress
                {
                    AchievementId = achievement.AchievementId,
                    State = AchievementState.Locked
                };

                // 检查前置条件，决定初始状态
                if (achievement.CheckPrerequisites(IsCompleted, GetCharacterLevel()))
                {
                    progress.State = AchievementState.InProgress;
                }

                _progressData[achievement.AchievementId] = progress;
            }
            return progress;
        }

        private void CompleteAchievement(AchievementSO achievement, AchievementProgress progress)
        {
            progress.State = AchievementState.Completed;
            progress.UnlockTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // 添加成就点数
            if (achievement.Points > 0)
            {
                _totalPoints += achievement.Points;
            }

            // 触发事件
            OnAchievementEvent?.Invoke(AchievementEventArgs.Unlocked(achievement));
            OnAchievementUnlocked?.Invoke(achievement);
            SyncProviderUnlock(achievement.AchievementId);

            Log($"成就完成: {achievement.DisplayName} (+{achievement.Points}点)");
        }

        private int GetCharacterLevel()
        {
            // 需要外部系统支持
            return 1;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[Achievement] {message}");
            }
        }

        #endregion
    }

    #region Save Data

    [Serializable]
    public class AchievementSaveData
    {
        public int TotalPoints;
        public Dictionary<string, AchievementProgress> ProgressData;
    }

    #endregion
}