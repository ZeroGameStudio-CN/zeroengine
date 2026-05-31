using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
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

        // 已装备的装备（槽位 -> 装备实例）
        private readonly Dictionary<EquipmentSlotType, EquipmentInstance> _equippedItems =
            new Dictionary<EquipmentSlotType, EquipmentInstance>();

        // 当前激活的套装效果
        private readonly Dictionary<EquipmentSetSO, HashSet<int>> _activeSetEffects =
            new Dictionary<EquipmentSetSO, HashSet<int>>();

        // 当前应用的修饰器（用于移除）
        private readonly Dictionary<EquipmentInstance, List<EquipmentStatModifier>> _appliedModifiers =
            new Dictionary<EquipmentInstance, List<EquipmentStatModifier>>();

        // 套装修饰器
        private readonly Dictionary<(EquipmentSetSO, int), List<EquipmentStatModifier>> _setModifiers =
            new Dictionary<(EquipmentSetSO, int), List<EquipmentStatModifier>>();

        // === 性能优化：缓存集合避免 GC 分配 ===
        private readonly List<EquipmentSlotType> _tempSlotList = new List<EquipmentSlotType>(8);
        private readonly Dictionary<EquipmentSetSO, int> _tempSetCounts = new Dictionary<EquipmentSetSO, int>();
        private readonly List<(EquipmentSetSO, int)> _tempSetsToRemove = new List<(EquipmentSetSO, int)>(8);
        private readonly HashSet<int> _tempActiveThresholds = new HashSet<int>();

        // 修饰器列表对象池
        private static class ModifierListPool
        {
            private static readonly Stack<List<EquipmentStatModifier>> _pool =
                new Stack<List<EquipmentStatModifier>>(16);

            public static List<EquipmentStatModifier> Get()
            {
                return _pool.Count > 0 ? _pool.Pop() : new List<EquipmentStatModifier>(16);
            }

            public static void Return(List<EquipmentStatModifier> list)
            {
                if (list == null) return;
                list.Clear();
                if (_pool.Count < 32)
                {
                    _pool.Push(list);
                }
            }
        }

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
        public int EquippedCount => _equippedItems.Count;

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
            foreach (var kvp in _equippedItems)
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

            // 清空当前装备
            UnequipAll();

            // 恢复装备（需要外部提供装备数据查找）
            // 这里只是框架，实际需要 EquipmentDatabase 或类似机制
            foreach (var slotData in saveData.EquippedItems)
            {
                // TODO: 通过 slotData.Equipment.DataId 查找 EquipmentDataSO
                // 然后恢复 EquipmentInstance 并装备
            }
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

            if (equipment == null || equipment.Data == null)
            {
                LogWarning("尝试装备空装备");
                return false;
            }

            var slotType = equipment.Data.SlotType;
            if (slotType == null)
            {
                LogWarning($"装备 {equipment.Data.name} 没有指定槽位类型");
                return false;
            }

            if (!_availableSlots.Contains(slotType))
            {
                LogWarning($"槽位类型 {slotType.DisplayName} 不可用");
                return false;
            }

            // 检查是否有已装备的物品
            if (_equippedItems.TryGetValue(slotType, out var existing))
            {
                replaced = existing;
                RemoveEquipmentStats(existing);
                existing.IsEquipped = false;
            }

            // 装备新物品
            _equippedItems[slotType] = equipment;
            equipment.IsEquipped = true;
            ApplyEquipmentStats(equipment);

            // 更新套装效果
            UpdateSetEffects();

            // 触发事件
            OnEquipmentChanged?.Invoke(EquipmentEventArgs.Equipped(slotType, equipment, replaced));

            Log($"装备 {equipment.Data.ItemName} 到 {slotType.DisplayName}");
            return true;
        }

        /// <summary>
        /// 卸下指定槽位的装备
        /// </summary>
        public EquipmentInstance Unequip(EquipmentSlotType slotType)
        {
            if (slotType == null) return null;

            if (!_equippedItems.TryGetValue(slotType, out var equipment))
            {
                return null;
            }

            _equippedItems.Remove(slotType);
            equipment.IsEquipped = false;
            RemoveEquipmentStats(equipment);

            // 更新套装效果
            UpdateSetEffects();

            // 触发事件
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
            foreach (var slot in _equippedItems.Keys)
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
            return _equippedItems.TryGetValue(slotType, out var equipment) ? equipment : null;
        }

        /// <summary>
        /// 获取所有已装备的物品
        /// </summary>
        public IEnumerable<EquipmentInstance> GetAllEquipped()
        {
            return _equippedItems.Values;
        }

        /// <summary>
        /// 检查槽位是否有装备
        /// </summary>
        public bool HasEquipped(EquipmentSlotType slotType)
        {
            return slotType != null && _equippedItems.ContainsKey(slotType);
        }

        /// <summary>
        /// 获取指定套装已装备的件数
        /// </summary>
        public int GetSetPieceCount(EquipmentSetSO set)
        {
            if (set == null) return 0;

            int count = 0;
            foreach (var equipment in _equippedItems.Values)
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

            // 重新应用属性
            RemoveEquipmentStats(equipment);
            ApplyEquipmentStats(equipment);

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
            foreach (var equipment in _equippedItems.Values)
            {
                total += equipment.GetScore();
            }
            return total;
        }

        #endregion

        #region Stat Integration

        private void ApplyEquipmentStats(EquipmentInstance equipment)
        {
            if (_statController == null || equipment == null) return;

            var modifiers = equipment.GetCalculatedModifiers();
            var appliedList = ModifierListPool.Get(); // 使用对象池

            foreach (var mod in modifiers)
            {
                _statController.AddModifier(mod.StatId, mod.Modifier);
                appliedList.Add(mod);
            }

            _appliedModifiers[equipment] = appliedList;
        }

        private void RemoveEquipmentStats(EquipmentInstance equipment)
        {
            if (_statController == null || equipment == null) return;

            if (_appliedModifiers.TryGetValue(equipment, out var modifiers))
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    var modifier = modifiers[i];
                    _statController.RemoveModifier(modifier.StatId, modifier.Modifier);
                }
                ModifierListPool.Return(modifiers); // 归还对象池
                _appliedModifiers.Remove(equipment);
            }
        }

        #endregion

        #region Set System

        private void UpdateSetEffects()
        {
            // 使用缓存字典避免分配
            _tempSetCounts.Clear();
            foreach (var equipment in _equippedItems.Values)
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
            var appliedList = new List<EquipmentStatModifier>();

            foreach (var statData in effect.StatBonuses)
            {
                var modifier = statData.CreateModifier(0, set);
                _statController.AddModifier(statData.StatId, modifier);
                appliedList.Add(new EquipmentStatModifier(statData.StatId, modifier));
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
                foreach (var modifier in modifiers)
                {
                    _statController.RemoveModifier(modifier.StatId, modifier.Modifier);
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
