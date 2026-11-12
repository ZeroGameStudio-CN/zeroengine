// ============================================================================
// MartialArtInstance.cs
// 运行时武学实例
// 创建于: 2026-01-09
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Character.MartialArts
{
    /// <summary>
    /// 运行时武学实例
    /// 记录武学的修炼进度 (层数、熟练度等)
    /// </summary>
    [Serializable]
    public class MartialArtInstance
    {
        /// <summary>武学 ID</summary>
        public string ArtId { get; private set; }

        /// <summary>武学数据引用</summary>
        public MartialArtDataSO Data { get; private set; }

        /// <summary>当前层数 (1-10)</summary>
        public int CurrentLevel { get; private set; }

        /// <summary>当前层经验</summary>
        public int CurrentExp { get; private set; }

        /// <summary>总修炼时间 (秒)</summary>
        public float TotalCultivationTime { get; private set; }

        /// <summary>是否已圆满 (满级)</summary>
        public bool IsMastered => CurrentLevel >= (Data?.maxLevel ?? 10);

        /// <summary>是否已突破 (超越满级)</summary>
        public bool IsTranscendent { get; private set; }

        /// <summary>修炼状态</summary>
        public MartialArtStatus Status
        {
            get
            {
                if (IsTranscendent) return MartialArtStatus.Transcendent;
                if (IsMastered) return MartialArtStatus.Mastered;
                if (CurrentLevel >= 7) return MartialArtStatus.Advanced;
                if (CurrentLevel >= 4) return MartialArtStatus.Intermediate;
                if (CurrentLevel >= 1) return MartialArtStatus.Beginner;
                return MartialArtStatus.NotLearned;
            }
        }

        /// <summary>已解锁的招式 ID 列表</summary>
        public List<string> UnlockedSkills { get; private set; } = new List<string>();

        // ===== 事件 =====

        /// <summary>升级事件</summary>
        public event Action<int, int> OnLevelUp;

        /// <summary>经验变化事件</summary>
        public event Action<int, int> OnExpChanged;

        /// <summary>招式解锁事件</summary>
        public event Action<string> OnSkillUnlocked;

        /// <summary>圆满事件</summary>
        public event Action OnMastered;

        /// <summary>突破事件</summary>
        public event Action OnTranscended;

        // ===== 构造 =====

        public MartialArtInstance(MartialArtDataSO data)
        {
            Data = data;
            ArtId = data.artId;
            CurrentLevel = 1;
            CurrentExp = 0;
            TotalCultivationTime = 0;
            IsTranscendent = false;

            // 解锁初始招式
            RefreshUnlockedSkills();
        }

        /// <summary>
        /// 反序列化构造
        /// </summary>
        public MartialArtInstance(MartialArtSaveData saveData, MartialArtDataSO data)
        {
            Data = data;
            ArtId = saveData.artId;
            CurrentLevel = saveData.level;
            CurrentExp = saveData.exp;
            TotalCultivationTime = saveData.totalTime;
            IsTranscendent = saveData.isTranscendent;
            UnlockedSkills = new List<string>(saveData.unlockedSkills);
        }

        // ===== 经验与升级 =====

        /// <summary>
        /// 增加修炼经验
        /// </summary>
        public void AddExp(int amount)
        {
            if (amount <= 0 || Data == null) return;

            // 应用修炼速度倍率
            amount = Mathf.RoundToInt(amount * Data.cultivationSpeedMultiplier);

            int oldExp = CurrentExp;
            CurrentExp += amount;

            OnExpChanged?.Invoke(oldExp, CurrentExp);

            // 检查升级
            CheckLevelUp();
        }

        /// <summary>
        /// 增加修炼时间
        /// </summary>
        public void AddCultivationTime(float seconds)
        {
            TotalCultivationTime += seconds;
        }

        /// <summary>
        /// 检查并执行升级
        /// </summary>
        private void CheckLevelUp()
        {
            if (Data == null) return;

            while (CurrentLevel < Data.maxLevel)
            {
                int expNeeded = Data.GetExpForLevel(CurrentLevel + 1);
                if (CurrentExp >= expNeeded)
                {
                    CurrentExp -= expNeeded;
                    LevelUp();
                }
                else
                {
                    break;
                }
            }

            // 满级后多余经验清零
            if (IsMastered && !IsTranscendent)
            {
                CurrentExp = 0;
            }
        }

        /// <summary>
        /// 升级
        /// </summary>
        private void LevelUp()
        {
            int oldLevel = CurrentLevel;
            CurrentLevel++;

            // 刷新解锁招式
            RefreshUnlockedSkills();

            OnLevelUp?.Invoke(oldLevel, CurrentLevel);

            // 检查圆满
            if (IsMastered && oldLevel < Data.maxLevel)
            {
                OnMastered?.Invoke();
            }
        }

        /// <summary>
        /// 强制设置层数 (GM/调试用)
        /// </summary>
        public void SetLevel(int level)
        {
            if (Data == null) return;

            int oldLevel = CurrentLevel;
            CurrentLevel = Mathf.Clamp(level, 1, Data.maxLevel);
            CurrentExp = 0;

            RefreshUnlockedSkills();

            if (CurrentLevel != oldLevel)
            {
                OnLevelUp?.Invoke(oldLevel, CurrentLevel);
            }
        }

        // ===== 突破 =====

        /// <summary>
        /// 尝试突破 (超越满级)
        /// </summary>
        public bool TryTranscend()
        {
            if (!IsMastered || IsTranscendent) return false;

            // 突破条件由外部检查
            IsTranscendent = true;
            OnTranscended?.Invoke();

            return true;
        }

        // ===== 招式 =====

        /// <summary>
        /// 刷新已解锁招式
        /// </summary>
        private void RefreshUnlockedSkills()
        {
            if (Data == null) return;

            // SetLevel also supports lowering a level; the snapshot must describe its current unlocks.
            for (var i = UnlockedSkills.Count - 1; i >= 0; i--)
            {
                bool eligible = false;
                foreach (var entry in Data.skills)
                {
                    if (entry.unlockLevel <= CurrentLevel && entry.skillId == UnlockedSkills[i])
                    {
                        eligible = true;
                        break;
                    }
                }
                if (!eligible) UnlockedSkills.RemoveAt(i);
            }

            foreach (var skillEntry in Data.skills)
            {
                if (skillEntry.unlockLevel <= CurrentLevel &&
                    !UnlockedSkills.Contains(skillEntry.skillId))
                {
                    UnlockedSkills.Add(skillEntry.skillId);
                    OnSkillUnlocked?.Invoke(skillEntry.skillId);
                }
            }
        }

        /// <summary>
        /// 检查招式是否已解锁
        /// </summary>
        public bool IsSkillUnlocked(string skillId)
        {
            return UnlockedSkills.Contains(skillId);
        }

        // ===== 属性 =====

        /// <summary>
        /// 获取当前属性加成
        /// </summary>
        public MartialArtStatBonus GetCurrentStatBonus()
        {
            return Data?.GetStatBonusAtLevel(CurrentLevel) ?? new MartialArtStatBonus();
        }

        /// <summary>
        /// 获取升级进度 (0-1)
        /// </summary>
        public float GetLevelProgress()
        {
            if (Data == null || IsMastered) return 1f;

            int expNeeded = Data.GetExpForLevel(CurrentLevel + 1);
            if (expNeeded <= 0) return 1f;

            return (float)CurrentExp / expNeeded;
        }

        // ===== 序列化 =====

        /// <summary>
        /// 转换为存档数据
        /// </summary>
        public MartialArtSaveData ToSaveData()
        {
            return new MartialArtSaveData
            {
                artId = ArtId,
                level = CurrentLevel,
                exp = CurrentExp,
                totalTime = TotalCultivationTime,
                isTranscendent = IsTranscendent,
                unlockedSkills = UnlockedSkills.ToArray()
            };
        }
    }

    /// <summary>
    /// 武学存档数据
    /// </summary>
    [Serializable]
    public class MartialArtSaveData
    {
        public string artId;
        public int level;
        public int exp;
        public float totalTime;
        public bool isTranscendent;
        public string[] unlockedSkills;
    }
}
