// ============================================================================
// MartialArtManager.cs
// 武学管理器 (MonoSingleton + ISaveable)
// 创建于: 2026-01-09
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Character.MartialArts
{
    /// <summary>
    /// 武学管理器
    /// 管理角色武学学习、修炼、装备
    /// </summary>
    public class MartialArtManager : MonoSingleton<MartialArtManager>
    {
        [Header("配置")]
        [Tooltip("武学数据库")]
        [SerializeField]
        private MartialArtDatabaseSO _database;

        [Tooltip("最大可学习武学数量 (0 = 无限)")]
        [SerializeField]
        private int _maxLearnedArts = 0;

        [Header("运行时数据")]
        [SerializeField]
        private string _characterId = "player";

        // 已学习武学
        private Dictionary<string, MartialArtInstance> _learnedArts = new Dictionary<string, MartialArtInstance>();

        // 装备槽位
        private Dictionary<MartialArtSlotType, string> _equippedSlots = new Dictionary<MartialArtSlotType, string>();

        // ===== 事件 =====

        /// <summary>学习武学事件</summary>
        public event Action<MartialArtLearnedEventArgs> OnMartialArtLearned;

        /// <summary>武学升级事件</summary>
        public event Action<MartialArtLevelUpEventArgs> OnMartialArtLevelUp;

        /// <summary>招式解锁事件</summary>
        public event Action<SkillUnlockedEventArgs> OnSkillUnlocked;

        /// <summary>装备武学事件</summary>
        public event Action<MartialArtEquippedEventArgs> OnMartialArtEquipped;

        /// <summary>修炼经验获得事件</summary>
        public event Action<CultivationExpGainedEventArgs> OnCultivationExpGained;

        // ===== 属性 =====

        /// <summary>武学数据库</summary>
        public MartialArtDatabaseSO Database => _database;

        /// <summary>已学习武学数量</summary>
        public int LearnedArtCount => _learnedArts.Count;

        /// <summary>已学习武学列表</summary>
        public IReadOnlyCollection<MartialArtInstance> LearnedArts => _learnedArts.Values;

        /// <summary>当前主修内功</summary>
        public MartialArtInstance ActiveInnerArt => GetEquippedArt(MartialArtSlotType.PrimaryInner);

        /// <summary>当前主修外功</summary>
        public MartialArtInstance ActiveOuterArt => GetEquippedArt(MartialArtSlotType.PrimaryOuter);

        /// <summary>当前轻功</summary>
        public MartialArtInstance ActiveLightness => GetEquippedArt(MartialArtSlotType.Lightness);

        // ===== 学习武学 =====

        /// <summary>
        /// 学习武学
        /// </summary>
        public bool LearnMartialArt(string artId)
        {
            if (string.IsNullOrEmpty(artId)) return false;

            // 获取武学数据
            var artData = _database?.GetMartialArt(artId);
            if (artData == null)
            {
                Debug.LogError($"[MartialArtManager] 找不到武学数据: {artId}");
                return false;
            }

            return LearnMartialArtInternal(artData);
        }

        /// <summary>
        /// 学习武学 (通过数据)
        /// </summary>
        public bool LearnMartialArt(MartialArtDataSO artData)
        {
            if (artData == null) return false;
            if (string.IsNullOrEmpty(artData.artId)) return false;

            return LearnMartialArtInternal(artData);
        }

        private bool LearnMartialArtInternal(MartialArtDataSO artData)
        {
            var artId = artData.artId;

            // 检查是否已学习
            if (_learnedArts.ContainsKey(artId))
            {
                Debug.LogWarning($"[MartialArtManager] 已学习武学: {artId}");
                return false;
            }

            // 检查数量限制
            if (_maxLearnedArts > 0 && _learnedArts.Count >= _maxLearnedArts)
            {
                Debug.LogWarning($"[MartialArtManager] 已达到武学数量上限: {_maxLearnedArts}");
                return false;
            }

            // 创建实例
            var instance = new MartialArtInstance(artData);
            _learnedArts[artId] = instance;

            // 绑定事件
            BindArtEvents(instance);

            // 触发事件
            var eventArgs = new MartialArtLearnedEventArgs
            {
                CharacterId = _characterId,
                ArtId = artId,
                ArtName = artData.artName,
                ArtType = artData.artType,
                Grade = artData.grade
            };

            OnMartialArtLearned?.Invoke(eventArgs);
            EventManager.Trigger(MartialArtEvents.OnMartialArtLearned, eventArgs);

            PublishCurrentUnlockedSkills(instance);

            Debug.Log($"[MartialArtManager] {_characterId} 学习武学: {artData.artName}");
            return true;
        }

        private void PublishCurrentUnlockedSkills(MartialArtInstance instance)
        {
            if (instance == null) return;

            foreach (var skillId in instance.UnlockedSkills)
            {
                HandleSkillUnlocked(instance, skillId);
            }
        }

        /// <summary>
        /// 遗忘武学
        /// </summary>
        public bool ForgetMartialArt(string artId)
        {
            if (!_learnedArts.TryGetValue(artId, out var instance))
                return false;

            // 先卸下装备
            foreach (var slot in _equippedSlots)
            {
                if (slot.Value == artId)
                {
                    UnequipMartialArt(slot.Key);
                }
            }

            // 解绑事件
            UnbindArtEvents(instance);

            // 移除
            _learnedArts.Remove(artId);

            Debug.Log($"[MartialArtManager] {_characterId} 遗忘武学: {artId}");
            return true;
        }

        // ===== 查询 =====

        /// <summary>
        /// 检查是否已学习
        /// </summary>
        public bool HasLearnedMartialArt(string artId)
        {
            return _learnedArts.ContainsKey(artId);
        }

        /// <summary>
        /// 获取武学实例
        /// </summary>
        public MartialArtInstance GetMartialArt(string artId)
        {
            _learnedArts.TryGetValue(artId, out var instance);
            return instance;
        }

        /// <summary>
        /// 按类型获取已学武学
        /// </summary>
        public List<MartialArtInstance> GetMartialArtsByType(MartialArtType type)
        {
            var result = new List<MartialArtInstance>();
            foreach (var art in _learnedArts.Values)
            {
                if (art.Data?.artType == type)
                    result.Add(art);
            }
            return result;
        }

        /// <summary>
        /// 按品级获取已学武学
        /// </summary>
        public List<MartialArtInstance> GetMartialArtsByGrade(MartialArtGrade grade)
        {
            var result = new List<MartialArtInstance>();
            foreach (var art in _learnedArts.Values)
            {
                if (art.Data?.grade == grade)
                    result.Add(art);
            }
            return result;
        }

        // ===== 装备 =====

        /// <summary>
        /// 装备武学到槽位
        /// </summary>
        public bool EquipMartialArt(string artId, MartialArtSlotType slot)
        {
            if (!_learnedArts.TryGetValue(artId, out var instance))
            {
                Debug.LogWarning($"[MartialArtManager] 未学习武学: {artId}");
                return false;
            }

            // 检查类型是否匹配槽位
            if (!IsSlotCompatible(instance.Data?.artType ?? MartialArtType.None, slot))
            {
                Debug.LogWarning($"[MartialArtManager] 武学类型与槽位不匹配");
                return false;
            }

            // 获取之前装备的武学
            _equippedSlots.TryGetValue(slot, out var previousArtId);

            // 装备
            _equippedSlots[slot] = artId;

            // 触发事件
            var eventArgs = new MartialArtEquippedEventArgs
            {
                CharacterId = _characterId,
                ArtId = artId,
                Slot = slot,
                PreviousArtId = previousArtId
            };

            OnMartialArtEquipped?.Invoke(eventArgs);
            EventManager.Trigger(MartialArtEvents.OnMartialArtEquipped, eventArgs);

            Debug.Log($"[MartialArtManager] 装备武学 {artId} 到槽位 {slot}");
            return true;
        }

        /// <summary>
        /// 卸下槽位武学
        /// </summary>
        public bool UnequipMartialArt(MartialArtSlotType slot)
        {
            if (!_equippedSlots.TryGetValue(slot, out var artId))
                return false;

            _equippedSlots.Remove(slot);

            EventManager.Trigger(MartialArtEvents.OnMartialArtUnequipped, new
            {
                CharacterId = _characterId,
                ArtId = artId,
                Slot = slot
            });

            return true;
        }

        /// <summary>
        /// 获取槽位装备的武学
        /// </summary>
        public MartialArtInstance GetEquippedArt(MartialArtSlotType slot)
        {
            if (_equippedSlots.TryGetValue(slot, out var artId))
            {
                return GetMartialArt(artId);
            }
            return null;
        }

        /// <summary>
        /// 检查武学类型是否与槽位兼容
        /// </summary>
        private bool IsSlotCompatible(MartialArtType artType, MartialArtSlotType slot)
        {
            return slot switch
            {
                MartialArtSlotType.PrimaryInner or MartialArtSlotType.SecondaryInner
                    => artType == MartialArtType.InnerArt,

                MartialArtSlotType.PrimaryOuter or MartialArtSlotType.SecondaryOuter
                    => artType != MartialArtType.InnerArt && artType != MartialArtType.Lightness,

                MartialArtSlotType.Lightness
                    => artType == MartialArtType.Lightness,

                MartialArtSlotType.Ultimate
                    => artType == MartialArtType.Ultimate || artType == MartialArtType.Forbidden,

                _ => false
            };
        }

        // ===== 修炼 =====

        /// <summary>
        /// 增加武学经验
        /// </summary>
        public void AddMartialArtExp(string artId, int amount, CultivationExpSource source = CultivationExpSource.Other)
        {
            if (!_learnedArts.TryGetValue(artId, out var instance))
                return;

            instance.AddExp(amount);

            var eventArgs = new CultivationExpGainedEventArgs
            {
                CharacterId = _characterId,
                ArtId = artId,
                ExpGained = amount,
                CurrentExp = instance.CurrentExp,
                CurrentLevel = instance.CurrentLevel,
                Source = source
            };

            OnCultivationExpGained?.Invoke(eventArgs);
            EventManager.Trigger(MartialArtEvents.OnCultivationExpGained, eventArgs);
        }

        /// <summary>
        /// 给所有已装备武学增加经验
        /// </summary>
        public void AddExpToEquippedArts(int amount, CultivationExpSource source = CultivationExpSource.Combat)
        {
            foreach (var slot in _equippedSlots)
            {
                AddMartialArtExp(slot.Value, amount, source);
            }
        }

        // ===== 属性计算 =====

        /// <summary>
        /// 获取所有已装备武学的属性加成总和
        /// </summary>
        public MartialArtStatBonus GetTotalEquippedStatBonus()
        {
            var total = new MartialArtStatBonus();

            foreach (var slot in _equippedSlots)
            {
                var art = GetMartialArt(slot.Value);
                if (art == null) continue;

                var bonus = art.GetCurrentStatBonus();
                total.health += bonus.health;
                total.energy += bonus.energy;
                total.attack += bonus.attack;
                total.defense += bonus.defense;
                total.speed += bonus.speed;
                total.innerDamage += bonus.innerDamage;
                total.outerDamage += bonus.outerDamage;
            }

            return total;
        }

        // ===== 事件绑定 =====

        private void BindArtEvents(MartialArtInstance art)
        {
            if (art == null) return;

            art.OnLevelUp += (oldLv, newLv) => HandleArtLevelUp(art, oldLv, newLv);
            art.OnSkillUnlocked += (skillId) => HandleSkillUnlocked(art, skillId);
            art.OnMastered += () => HandleArtMastered(art);
            art.OnTranscended += () => HandleArtTranscended(art);
        }

        private void UnbindArtEvents(MartialArtInstance art)
        {
            // 由于使用 lambda，无法精确解绑，但实例被移除后不会再触发
        }

        private void HandleArtLevelUp(MartialArtInstance art, int oldLevel, int newLevel)
        {
            var eventArgs = new MartialArtLevelUpEventArgs
            {
                CharacterId = _characterId,
                ArtId = art.ArtId,
                ArtName = art.Data?.artName ?? "",
                OldLevel = oldLevel,
                NewLevel = newLevel,
                IsMastered = art.IsMastered
            };

            OnMartialArtLevelUp?.Invoke(eventArgs);
            EventManager.Trigger(MartialArtEvents.OnMartialArtLevelUp, eventArgs);
        }

        private void HandleSkillUnlocked(MartialArtInstance art, string skillId)
        {
            var skillEntry = art.Data?.skills.Find(s => s.skillId == skillId);

            var eventArgs = new SkillUnlockedEventArgs
            {
                CharacterId = _characterId,
                ArtId = art.ArtId,
                SkillId = skillId,
                SkillName = skillEntry?.skillName ?? "",
                IsUltimate = skillEntry?.isUltimate ?? false
            };

            OnSkillUnlocked?.Invoke(eventArgs);
            EventManager.Trigger(MartialArtEvents.OnSkillUnlocked, eventArgs);
        }

        private void HandleArtMastered(MartialArtInstance art)
        {
            EventManager.Trigger(MartialArtEvents.OnMartialArtMastered, new
            {
                CharacterId = _characterId,
                ArtId = art.ArtId,
                ArtName = art.Data?.artName ?? ""
            });
        }

        private void HandleArtTranscended(MartialArtInstance art)
        {
            EventManager.Trigger(MartialArtEvents.OnMartialArtTranscended, new
            {
                CharacterId = _characterId,
                ArtId = art.ArtId,
                ArtName = art.Data?.artName ?? ""
            });
        }

        // ===== 存档 =====

        /// <summary>
        /// 获取存档数据
        /// </summary>
        public MartialArtManagerSaveData GetSaveData()
        {
            var artsSaveData = new List<MartialArtSaveData>();
            foreach (var art in _learnedArts.Values)
            {
                artsSaveData.Add(art.ToSaveData());
            }

            var slotsSaveData = new List<EquippedSlotSaveData>();
            foreach (var slot in _equippedSlots)
            {
                slotsSaveData.Add(new EquippedSlotSaveData
                {
                    slot = slot.Key,
                    artId = slot.Value
                });
            }

            return new MartialArtManagerSaveData
            {
                characterId = _characterId,
                learnedArts = artsSaveData.ToArray(),
                equippedSlots = slotsSaveData.ToArray()
            };
        }

        /// <summary>
        /// 加载存档数据
        /// </summary>
        public void LoadSaveData(MartialArtManagerSaveData saveData)
        {
            if (saveData == null) return;

            _characterId = saveData.characterId;

            // 恢复已学武学
            _learnedArts.Clear();
            foreach (var artSave in saveData.learnedArts)
            {
                var artData = _database?.GetMartialArt(artSave.artId);
                if (artData != null)
                {
                    var instance = new MartialArtInstance(artSave, artData);
                    _learnedArts[artSave.artId] = instance;
                    BindArtEvents(instance);
                }
            }

            // 恢复装备槽位
            _equippedSlots.Clear();
            foreach (var slotSave in saveData.equippedSlots)
            {
                if (_learnedArts.ContainsKey(slotSave.artId))
                {
                    _equippedSlots[slotSave.slot] = slotSave.artId;
                }
            }
        }
    }

    /// <summary>
    /// 武学管理器存档数据
    /// </summary>
    [Serializable]
    public class MartialArtManagerSaveData
    {
        public string characterId;
        public MartialArtSaveData[] learnedArts;
        public EquippedSlotSaveData[] equippedSlots;
    }

    /// <summary>
    /// 装备槽位存档数据
    /// </summary>
    [Serializable]
    public class EquippedSlotSaveData
    {
        public MartialArtSlotType slot;
        public string artId;
    }
}
