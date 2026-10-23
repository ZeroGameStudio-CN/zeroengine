using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Save;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Equipment
{
    /// <summary>
    /// 装备管理器
    /// 管理角色的装备穿戴、属性计算、套装效果
    /// </summary>
    public class EquipmentManager : MonoBehaviour, ISaveable
    {
        [Header("配置")]
        [Tooltip("可用的槽位类型列表")]
        [SerializeField] private List<EquipmentSlotType> _availableSlots = new List<EquipmentSlotType>();

        [Tooltip("关联的属性控制器")]
        [SerializeField] private StatController _statController;

        [Header("调试")]
        [SerializeField] private bool _debugMode;

        private EquipmentLoadout<EquipmentSlotType, EquipmentInstance> _loadout;

        private EquipmentLoadout<EquipmentSlotType, EquipmentInstance> Loadout => _loadout ??=
            new EquipmentLoadout<EquipmentSlotType, EquipmentInstance>(
                _availableSlots,
                item => item.Data.SlotType,
                item => item.GetStatModifiers(),
                ResolveStat,
                item => item.Data != null && item.Data.SlotType != null);

        // 当前激活的套装效果
        private readonly Dictionary<EquipmentSetSO, HashSet<int>> _activeSetEffects =
            new Dictionary<EquipmentSetSO, HashSet<int>>();

        // 套装修饰器
        private readonly Dictionary<(EquipmentSetSO, int), List<(StatType, StatModifier)>> _setModifiers =
            new Dictionary<(EquipmentSetSO, int), List<(StatType, StatModifier)>>();

        // === 性能优化：缓存集合避免 GC 分配 ===
        private readonly List<EquipmentSlotType> _tempSlotList = new List<EquipmentSlotType>(8);
        private readonly Dictionary<EquipmentSetSO, int> _tempSetCounts = new Dictionary<EquipmentSetSO, int>();
        private readonly List<(EquipmentSetSO, int)> _tempSetsToRemove = new List<(EquipmentSetSO, int)>(8);
        private readonly HashSet<int> _tempActiveThresholds = new HashSet<int>();

        #region Events

        /// <summary>装备穿戴/卸下事件</summary>
        public event Action<EquipmentEventArgs> OnEquipmentChanged;

        /// <summary>套装效果激活/取消事件</summary>
        public event Action<SetEventArgs> OnSetEffectChanged;

        /// <summary>强化事件</summary>
        public event Action<EnhanceEventArgs> OnEnhanced;

        #endregion

        #region Properties

        /// <summary>可用槽位列表</summary>
        public IReadOnlyList<EquipmentSlotType> AvailableSlots => _availableSlots;

        /// <summary>已装备数量</summary>
        public int EquippedCount => Loadout.Equipped.Count;

        /// <summary>Host-owned content lookup used when restoring saved equipment IDs.</summary>
        public Func<string, EquipmentDataSO> ResolveEquipmentData { get; set; }

        #endregion

        #region ISaveable

        public string SaveKey => "Equipment";

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
            var data = new EquipmentManagerSaveData();
            foreach (var kvp in Loadout.Equipped)
            {
                data.EquippedItems.Add(new EquippedSlotData
                {
                    SlotId = kvp.Key.SlotId,
                    Equipment = kvp.Value.ExportSaveData()
                });
            }
            return data;
        }

        public void ImportSaveData(object data)
        {
            if (data is not EquipmentManagerSaveData saveData) return;
            if (!TryRestore(saveData, ResolveEquipmentData))
            {
                LogWarning("无法解析装备存档，保留当前装备");
            }
        }

        /// <summary>Resolve every saved definition before replacing any current equipment.</summary>
        public bool TryRestore(EquipmentManagerSaveData data, Func<string, EquipmentDataSO> resolveData)
        {
            if (data?.EquippedItems == null) return false;
            var restored = new List<EquipmentInstance>(data.EquippedItems.Count);
            foreach (var slot in data.EquippedItems)
            {
                if (slot?.Equipment == null || resolveData == null) return false;
                var definition = resolveData(slot.Equipment.DataId);
                if (definition == null || definition.SlotType == null ||
                    !string.Equals(definition.Id, slot.Equipment.DataId, StringComparison.Ordinal) ||
                    !string.Equals(definition.SlotType.SlotId, slot.SlotId, StringComparison.Ordinal)) return false;
                var instance = new EquipmentInstance();
                instance.ImportSaveData(slot.Equipment, definition);
                restored.Add(instance);
            }

            var previous = new List<KeyValuePair<EquipmentSlotType, EquipmentInstance>>(Loadout.Equipped);
            if (!Loadout.TryRestore(restored)) return false;
            foreach (var pair in previous) pair.Value.IsEquipped = false;
            foreach (var item in restored) item.IsEquipped = true;
            UpdateSetEffects();
            foreach (var pair in previous)
                OnEquipmentChanged?.Invoke(EquipmentEventArgs.Unequipped(pair.Key, pair.Value));
            foreach (var item in restored)
                OnEquipmentChanged?.Invoke(EquipmentEventArgs.Equipped(item.Data.SlotType, item, null));
            return true;
        }

        public void ResetToDefault()
        {
            UnequipAll();
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_statController == null)
            {
                _statController = GetComponent<StatController>();
            }
        }

        private void Start()
        {
            Register();
        }

        private void OnDestroy()
        {
            if (_loadout != null)
            {
                foreach (var item in _loadout.Equipped.Values) item.IsEquipped = false;
                _loadout.Dispose();
            }
            _tempSetsToRemove.Clear();
            _tempSetsToRemove.AddRange(_setModifiers.Keys);
            foreach (var key in _tempSetsToRemove) RemoveSetEffectModifiers(key.Item1, key.Item2);
            _activeSetEffects.Clear();
            Unregister();
        }

        #endregion

        #region Public API

        /// <summary>
        /// 装备物品
        /// </summary>
        public bool TryEquip(EquipmentInstance equipment, out EquipmentInstance replaced)
        {
            replaced = null;
            if (equipment == null || equipment.Data == null || equipment.Data.SlotType == null)
                return false;
            if (!Loadout.TryEquip(equipment, out replaced)) return false;

            if (replaced != null) replaced.IsEquipped = false;
            equipment.IsEquipped = true;
            UpdateSetEffects();
            OnEquipmentChanged?.Invoke(EquipmentEventArgs.Equipped(equipment.Data.SlotType, equipment, replaced));
            Log($"装备 {equipment.Data.ItemName} 到 {equipment.Data.SlotType.DisplayName}");
            return true;
        }

        /// <summary>
        /// 卸下指定槽位的装备
        /// </summary>
        public EquipmentInstance Unequip(EquipmentSlotType slotType)
        {
            if (slotType == null) return null;
            var equipment = Loadout.Unequip(slotType);
            if (equipment == null) return null;

            equipment.IsEquipped = false;
            UpdateSetEffects();
            OnEquipmentChanged?.Invoke(EquipmentEventArgs.Unequipped(slotType, equipment));
            Log($"卸下 {equipment.Data.ItemName} 从 {slotType.DisplayName}");
            return equipment;
        }

        /// <summary>
        /// 卸下所有装备
        /// </summary>
        public void UnequipAll()
        {
            // 使用缓存列表避免分配
            _tempSlotList.Clear();
            foreach (var slot in Loadout.Equipped.Keys)
            {
                _tempSlotList.Add(slot);
            }

            for (int i = 0; i < _tempSlotList.Count; i++)
            {
                Unequip(_tempSlotList[i]);
            }
            _tempSlotList.Clear();
        }

        /// <summary>
        /// 获取指定槽位的装备
        /// </summary>
        public EquipmentInstance GetEquipped(EquipmentSlotType slotType)
        {
            if (slotType == null) return null;
            return Loadout.Equipped.TryGetValue(slotType, out var equipment) ? equipment : null;
        }

        /// <summary>
        /// 获取所有已装备的物品
        /// </summary>
        public IEnumerable<EquipmentInstance> GetAllEquipped()
        {
            return Loadout.Equipped.Values;
        }

        /// <summary>
        /// 检查槽位是否有装备
        /// </summary>
        public bool HasEquipped(EquipmentSlotType slotType)
        {
            return slotType != null && Loadout.Equipped.ContainsKey(slotType);
        }

        /// <summary>
        /// 获取指定套装已装备的件数
        /// </summary>
        public int GetSetPieceCount(EquipmentSetSO set)
        {
            if (set == null) return 0;

            int count = 0;
            foreach (var equipment in Loadout.Equipped.Values)
            {
                if (equipment.Data.BelongsToSet == set)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 强化装备
        /// </summary>
        public bool TryEnhance(EquipmentSlotType slotType, out EnhanceResult result)
        {
            result = EnhanceResult.Failed;

            var equipment = GetEquipped(slotType);
            if (equipment == null)
            {
                return false;
            }

            int oldLevel = equipment.EnhanceLevel;
            if (!equipment.TryEnhance(out result))
            {
                return false;
            }

            // Refresh through the same stat lifecycle as equip/restore.
            if (!Loadout.RefreshStats())
            {
                equipment.EnhanceLevel = oldLevel;
                equipment.MarkModifiersDirty();
                result = EnhanceResult.Failed;
                return false;
            }

            // 触发事件
            OnEnhanced?.Invoke(new EnhanceEventArgs(equipment, result, oldLevel, equipment.EnhanceLevel));
            OnEquipmentChanged?.Invoke(EquipmentEventArgs.Enhanced(slotType, equipment));

            return true;
        }

        /// <summary>
        /// 获取装备总评分
        /// </summary>
        public float GetTotalScore()
        {
            float total = 0;
            foreach (var equipment in Loadout.Equipped.Values)
            {
                total += equipment.GetScore();
            }
            return total;
        }

        #endregion

        #region Stat Integration

        private Stat ResolveStat(StatId statId)
        {
            return _statController == null ? null : _statController.GetStat(statId);
        }

        #endregion

        #region Set System

        private void UpdateSetEffects()
        {
            // 使用缓存字典避免分配
            _tempSetCounts.Clear();
            foreach (var equipment in Loadout.Equipped.Values)
            {
                var set = equipment.Data.BelongsToSet;
                if (set != null)
                {
                    if (!_tempSetCounts.ContainsKey(set))
                        _tempSetCounts[set] = 0;
                    _tempSetCounts[set]++;
                }
            }

            // 更新套装效果
            // 1. 移除不再激活的效果（使用缓存列表）
            _tempSetsToRemove.Clear();
            foreach (var kvp in _setModifiers)
            {
                var (set, threshold) = kvp.Key;
                if (!_tempSetCounts.TryGetValue(set, out var count) || count < threshold)
                {
                    _tempSetsToRemove.Add((set, threshold));
                }
            }

            for (int i = 0; i < _tempSetsToRemove.Count; i++)
            {
                var key = _tempSetsToRemove[i];
                RemoveSetEffectModifiers(key.Item1, key.Item2);
                OnSetEffectChanged?.Invoke(new SetEventArgs(key.Item1, key.Item2, false));
            }

            // 2. 添加新激活的效果
            foreach (var kvp in _tempSetCounts)
            {
                var set = kvp.Key;
                var count = kvp.Value;

                foreach (var effect in set.Effects)
                {
                    if (count >= effect.RequiredPieces)
                    {
                        var key = (set, effect.RequiredPieces);
                        if (!_setModifiers.ContainsKey(key))
                        {
                            ApplySetEffectModifiers(set, effect);
                            OnSetEffectChanged?.Invoke(new SetEventArgs(set, effect.RequiredPieces, true));
                        }
                    }
                }
            }

            // 更新激活记录（使用缓存 HashSet）
            _activeSetEffects.Clear();
            foreach (var kvp in _tempSetCounts)
            {
                var set = kvp.Key;
                var count = kvp.Value;
                _tempActiveThresholds.Clear();

                foreach (var effect in set.Effects)
                {
                    if (count >= effect.RequiredPieces)
                    {
                        _tempActiveThresholds.Add(effect.RequiredPieces);
                    }
                }

                if (_tempActiveThresholds.Count > 0)
                {
                    // 存储需要新建 HashSet（这是必要的存储分配）
                    _activeSetEffects[set] = new HashSet<int>(_tempActiveThresholds);
                }
            }
        }

        private void ApplySetEffectModifiers(EquipmentSetSO set, SetEffect effect)
        {
            if (_statController == null) return;

            var key = (set, effect.RequiredPieces);
            var appliedList = new List<(StatType, StatModifier)>();

            foreach (var statData in effect.StatBonuses)
            {
                var modifier = statData.CreateModifier(0, set);
                _statController.AddModifier(statData.StatType, modifier);
                appliedList.Add((statData.StatType, modifier));
            }

            _setModifiers[key] = appliedList;
            Log($"套装 {set.SetName} {effect.RequiredPieces}件效果激活");
        }

        private void RemoveSetEffectModifiers(EquipmentSetSO set, int threshold)
        {
            if (_statController == null) return;

            var key = (set, threshold);
            if (_setModifiers.TryGetValue(key, out var modifiers))
            {
                foreach (var (statType, modifier) in modifiers)
                {
                    _statController.RemoveModifier(statType, modifier);
                }
                _setModifiers.Remove(key);
                Log($"套装 {set.SetName} {threshold}件效果失效");
            }
        }

        /// <summary>
        /// 获取当前激活的套装效果
        /// </summary>
        public IEnumerable<(EquipmentSetSO Set, int Threshold)> GetActiveSetEffects()
        {
            foreach (var kvp in _activeSetEffects)
            {
                foreach (var threshold in kvp.Value)
                {
                    yield return (kvp.Key, threshold);
                }
            }
        }

        #endregion

        #region Logging

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log(string.Concat("[Equipment] ", message));
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning(string.Concat("[Equipment] ", message));
        }

        #endregion
    }

    #region Save Data

    [Serializable]
    public class EquipmentManagerSaveData
    {
        public List<EquippedSlotData> EquippedItems = new List<EquippedSlotData>();
    }

    [Serializable]
    public class EquippedSlotData
    {
        public string SlotId;
        public EquipmentSaveData Equipment;
    }

    #endregion
}
