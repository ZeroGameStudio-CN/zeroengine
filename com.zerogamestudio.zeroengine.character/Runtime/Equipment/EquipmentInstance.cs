using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Equipment
{
    /// <summary>
    /// 附魔数据
    /// </summary>
    [Serializable]
    public class EnchantmentData
    {
        public string EnchantmentId;
        public StatModifierData Effect;
        public int Tier;

        public StatModifier CreateModifier(object source)
        {
            return Effect.CreateModifier(0, source);
        }
    }

    /// <summary>
    /// 宝石数据
    /// </summary>
    [Serializable]
    public class GemData
    {
        public string GemId;
        public string GemName;
        public Sprite Icon;
        public List<StatModifierData> Effects = new List<StatModifierData>();

        /// <summary>
        /// 将宝石修饰器填充到目标列表（零分配版本）
        /// </summary>
        public void GetModifiers(object source, List<StatModifier> results)
        {
            for (int i = 0; i < Effects.Count; i++)
            {
                results.Add(Effects[i].CreateModifier(0, source));
            }
        }
    }

    /// <summary>
    /// 宝石槽位
    /// </summary>
    [Serializable]
    public class GemSlot
    {
        public int SlotIndex;
        public GemSlotState State = GemSlotState.Locked;
        public GemData SocketedGem;

        public bool IsLocked => State == GemSlotState.Locked;
        public bool IsEmpty => State == GemSlotState.Empty;
        public bool HasGem => State == GemSlotState.Socketed && SocketedGem != null;
    }

    /// <summary>
    /// 装备运行时实例
    /// 包含强化、附魔、宝石等动态数据
    /// </summary>
    [Serializable]
    public class EquipmentInstance
    {
        /// <summary>装备数据定义</summary>
        [SerializeField] private EquipmentDataSO _data;
        public EquipmentDataSO Data => _data;

        /// <summary>唯一实例 ID</summary>
        public string InstanceId;

        /// <summary>强化等级</summary>
        public int EnhanceLevel;

        /// <summary>精炼等级</summary>
        public int RefineLevel;

        /// <summary>附魔列表</summary>
        public List<EnchantmentData> Enchantments = new List<EnchantmentData>();

        /// <summary>宝石槽位</summary>
        public List<GemSlot> GemSlots = new List<GemSlot>();

        /// <summary>是否已装备</summary>
        public bool IsEquipped { get; internal set; }

        /// <summary>装备评分（缓存）</summary>
        private float _cachedScore = -1;
        private bool _scoreDirty = true;

        // 缓存的修饰器列表（避免重复计算）
        private readonly List<StatModifier> _cachedModifiers = new List<StatModifier>();
        private readonly List<EquipmentStatModifier> _cachedStatModifiers = new List<EquipmentStatModifier>();
        private bool _modifiersDirty = true;

        public EquipmentInstance() { }

        public EquipmentInstance(EquipmentDataSO data)
        {
            _data = data;
            InstanceId = Guid.NewGuid().ToString();
            InitializeGemSlots();
        }

        private void InitializeGemSlots()
        {
            GemSlots.Clear();
            for (int i = 0; i < _data.GemSlotCount; i++)
            {
                GemSlots.Add(new GemSlot
                {
                    SlotIndex = i,
                    State = i == 0 ? GemSlotState.Empty : GemSlotState.Locked
                });
            }
            UpdateUnlockedGemSlots();
        }

        /// <summary>
        /// 更新已解锁的宝石槽
        /// </summary>
        public void UpdateUnlockedGemSlots()
        {
            int unlockedCount = _data.GetUnlockedGemSlots(RefineLevel);
            for (int i = 0; i < GemSlots.Count; i++)
            {
                if (GemSlots[i].State == GemSlotState.Locked && i < unlockedCount)
                {
                    GemSlots[i].State = GemSlotState.Empty;
                }
            }
        }

        /// <summary>
        /// 标记修饰器需要重新计算
        /// </summary>
        public void MarkModifiersDirty()
        {
            _modifiersDirty = true;
            _scoreDirty = true;
        }

        /// <summary>
        /// 获取所有计算后的属性修饰器
        /// </summary>
        public IReadOnlyList<StatModifier> GetCalculatedModifiers()
        {
            if (_modifiersDirty)
            {
                RecalculateModifiers();
            }
            return _cachedModifiers;
        }

        /// <summary>Calculated base, enchantment and gem modifiers with their exact target stat.</summary>
        public IReadOnlyList<EquipmentStatModifier> GetStatModifiers()
        {
            if (_modifiersDirty)
            {
                RecalculateModifiers();
            }
            return _cachedStatModifiers;
        }

        private void RecalculateModifiers()
        {
            _cachedModifiers.Clear();
            _cachedStatModifiers.Clear();

            if (_data == null)
            {
                _modifiersDirty = false;
                return;
            }

            // 1. 基础属性
            for (int i = 0; i < _data.BaseStats.Count; i++)
            {
                AddCalculatedModifier(_data.BaseStats[i], EnhanceLevel);
            }

            // 2. 附魔效果
            for (int i = 0; i < Enchantments.Count; i++)
            {
                AddCalculatedModifier(Enchantments[i]?.Effect, 0);
            }

            // 3. 宝石效果（使用零分配版本）
            for (int i = 0; i < GemSlots.Count; i++)
            {
                var slot = GemSlots[i];
                if (slot != null && slot.HasGem)
                {
                    foreach (var effect in slot.SocketedGem.Effects)
                    {
                        AddCalculatedModifier(effect, 0);
                    }
                }
            }

            _modifiersDirty = false;
        }

        private void AddCalculatedModifier(StatModifierData data, int enhanceLevel)
        {
            if (data == null) return;
            var modifier = data.CreateModifier(enhanceLevel, this);
            _cachedModifiers.Add(modifier);
            var statId = data.StatType == StatType.None ? default : new StatId(data.StatType.ToString());
            _cachedStatModifiers.Add(new EquipmentStatModifier(statId, modifier));
        }

        /// <summary>
        /// 获取装备评分
        /// </summary>
        public float GetScore()
        {
            if (_scoreDirty)
            {
                _cachedScore = CalculateScore();
                _scoreDirty = false;
            }
            return _cachedScore;
        }

        private float CalculateScore()
        {
            float score = 0;
            var modifiers = GetCalculatedModifiers();

            foreach (var mod in modifiers)
            {
                // 简单评分：Flat 按 1:1，Percent 按 100:1
                if (mod.ModType == StatModType.Flat)
                {
                    score += mod.Value;
                }
                else if (mod.ModType == StatModType.PercentAdd)
                {
                    score += mod.Value * 100;
                }
                else if (mod.ModType == StatModType.PercentMult)
                {
                    score += (mod.Value - 1) * 100;
                }
            }

            // 强化和精炼加成
            score += EnhanceLevel * 10;
            score += RefineLevel * 50;

            return score;
        }

        #region Enhancement API

        /// <summary>
        /// 尝试强化
        /// </summary>
        public bool TryEnhance(out EnhanceResult result)
        {
            if (EnhanceLevel >= _data.MaxEnhanceLevel)
            {
                result = EnhanceResult.MaxLevel;
                return false;
            }

            float successRate = _data.GetEnhanceSuccessRate(EnhanceLevel);
            bool success = UnityEngine.Random.value <= successRate;

            if (success)
            {
                EnhanceLevel++;
                MarkModifiersDirty();
                result = EnhanceResult.Success;
                return true;
            }

            result = EnhanceResult.Failed;
            return false;
        }

        /// <summary>
        /// 尝试精炼
        /// </summary>
        public bool TryRefine(out EnhanceResult result)
        {
            if (RefineLevel >= _data.MaxRefineLevel)
            {
                result = EnhanceResult.MaxLevel;
                return false;
            }

            RefineLevel++;
            UpdateUnlockedGemSlots();
            MarkModifiersDirty();
            result = EnhanceResult.Success;
            return true;
        }

        /// <summary>
        /// 添加附魔
        /// </summary>
        public bool TryAddEnchantment(EnchantmentData enchantment, int maxEnchants = 3)
        {
            if (Enchantments.Count >= maxEnchants)
                return false;

            Enchantments.Add(enchantment);
            MarkModifiersDirty();
            return true;
        }

        /// <summary>
        /// 移除附魔
        /// </summary>
        public bool RemoveEnchantment(string enchantmentId)
        {
            for (int i = Enchantments.Count - 1; i >= 0; i--)
            {
                if (Enchantments[i].EnchantmentId == enchantmentId)
                {
                    Enchantments.RemoveAt(i);
                    MarkModifiersDirty();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 镶嵌宝石
        /// </summary>
        public bool TrySocketGem(int slotIndex, GemData gem)
        {
            if (slotIndex < 0 || slotIndex >= GemSlots.Count)
                return false;

            var slot = GemSlots[slotIndex];
            if (slot.IsLocked)
                return false;

            slot.SocketedGem = gem;
            slot.State = GemSlotState.Socketed;
            MarkModifiersDirty();
            return true;
        }

        /// <summary>
        /// 移除宝石
        /// </summary>
        public GemData RemoveGem(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= GemSlots.Count)
                return null;

            var slot = GemSlots[slotIndex];
            if (!slot.HasGem)
                return null;

            var gem = slot.SocketedGem;
            slot.SocketedGem = null;
            slot.State = GemSlotState.Empty;
            MarkModifiersDirty();
            return gem;
        }

        #endregion

        #region Save/Load

        /// <summary>
        /// 导出存档数据
        /// </summary>
        public EquipmentSaveData ExportSaveData()
        {
            return new EquipmentSaveData
            {
                DataId = _data != null ? _data.Id : null,
                InstanceId = InstanceId,
                EnhanceLevel = EnhanceLevel,
                RefineLevel = RefineLevel,
                Enchantments = new List<EnchantmentData>(Enchantments),
                GemSlots = new List<GemSlot>(GemSlots)
            };
        }

        /// <summary>
        /// 从存档数据恢复（需要外部提供 DataSO 引用）
        /// </summary>
        public void ImportSaveData(EquipmentSaveData data, EquipmentDataSO dataSO)
        {
            _data = dataSO;
            InstanceId = data.InstanceId;
            EnhanceLevel = data.EnhanceLevel;
            RefineLevel = data.RefineLevel;
            Enchantments = data.Enchantments ?? new List<EnchantmentData>();
            GemSlots = data.GemSlots ?? new List<GemSlot>();
            MarkModifiersDirty();
        }

        #endregion
    }

    /// <summary>
    /// 装备实例存档数据
    /// </summary>
    [Serializable]
    public class EquipmentSaveData
    {
        public string DataId;
        public string InstanceId;
        public int EnhanceLevel;
        public int RefineLevel;
        public List<EnchantmentData> Enchantments;
        public List<GemSlot> GemSlots;
    }
}
