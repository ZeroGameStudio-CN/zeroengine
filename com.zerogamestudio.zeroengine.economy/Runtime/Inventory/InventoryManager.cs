using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Economy;
using ZeroEngine.Save;
using ZeroEngine.Utils;

namespace ZeroEngine.Inventory
{
    public class InventoryManager : Singleton<InventoryManager>, ISaveable, IInventoryProvider
    {
        public const int MaxSlots = 30;
        private List<InventorySlot> _slots = new List<InventorySlot>();

        public IReadOnlyList<InventorySlot> Slots => _slots;

        // Database of all items to re-link on load
        private Dictionary<string, InventoryItemSO> _itemDatabase = new Dictionary<string, InventoryItemSO>();

        #region Events (v1.2.0+)

        /// <summary>
        /// 槽位变化事件 (v1.2.0+)
        /// </summary>
        public event Action<SlotChangedEventArgs> OnSlotChanged;

        /// <summary>
        /// 物品添加事件 (v1.2.0+)
        /// </summary>
        public event Action<InventoryItemSO, int> OnItemAdded;

        /// <summary>
        /// 物品移除事件 (v1.2.0+)
        /// </summary>
        public event Action<InventoryItemSO, int> OnItemRemoved;

        /// <summary>
        /// 背包已满事件 (v1.2.0+)
        /// </summary>
        public event Action<InventoryItemSO, int> OnInventoryFull;

        #endregion

        private void Start()
        {
            // 注册到存档系统
            SaveSlotManager.Instance?.Register(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveSlotManager.Instance?.Unregister(this);
        }

        #region ISaveable Implementation

        /// <summary>
        /// ISaveable: 存档键名
        /// </summary>
        public string SaveKey => "Inventory";

        /// <summary>
        /// ISaveable: 导出存档数据
        /// </summary>
        public object ExportSaveData()
        {
            return _slots;
        }

        /// <summary>
        /// ISaveable: 导入存档数据
        /// </summary>
        public void ImportSaveData(object data)
        {
            if (data is List<InventorySlot> slots)
            {
                _slots = slots;
            }
            else
            {
                _slots = new List<InventorySlot>();
            }

            // 确保最少槽位数
            while (_slots.Count < 10)
            {
                _slots.Add(new InventorySlot { SlotIndex = _slots.Count });
            }

            // 重新链接 SO 引用
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.SlotIndex = i;

                if (!string.IsNullOrEmpty(slot.ItemId))
                {
                    if (_itemDatabase.TryGetValue(slot.ItemId, out var so))
                    {
                        slot.ItemData = so;
                    }
                    else
                    {
                        ZeroLog.Warning(ZeroLog.Modules.Inventory, $"Item ID {slot.ItemId} not found in database.");
                    }
                }
            }

            TriggerInventoryUpdated();
        }

        /// <summary>
        /// ISaveable: 重置为初始状态
        /// </summary>
        public void ResetToDefault()
        {
            _slots.Clear();
            for (int i = 0; i < 10; i++)
            {
                _slots.Add(new InventorySlot { SlotIndex = i });
            }
            TriggerInventoryUpdated();
        }

        #endregion

        /// <summary>
        /// 初始化物品数据库
        /// </summary>
        public void InitializeDatabase(List<InventoryItemSO> allItems)
        {
            _itemDatabase.Clear();
            foreach (var item in allItems)
            {
                if (item != null && !string.IsNullOrEmpty(item.Id) && !_itemDatabase.ContainsKey(item.Id))
                {
                    _itemDatabase.Add(item.Id, item);
                }
            }
        }

        /// <summary>
        /// 注册单个物品到数据库 (v1.2.0+)
        /// </summary>
        public void RegisterItem(InventoryItemSO item)
        {
            if (item != null && !string.IsNullOrEmpty(item.Id))
            {
                _itemDatabase[item.Id] = item;
            }
        }

        /// <summary>
        /// 从数据库获取物品 (v1.2.0+)
        /// </summary>
        public InventoryItemSO GetItemData(string itemId)
        {
            return _itemDatabase.TryGetValue(itemId, out var item) ? item : null;
        }

        #region Add/Remove Items

        public bool AddItem(InventoryItemSO item, int amount = 1)
        {
            if (item == null || amount <= 0) return false;

            int originalAmount = amount;
            _affectedSlotsBuffer.Clear();

            // 1. Try to stack on existing slots
            if (item.IsStackable)
            {
                foreach (var slot in _slots)
                {
                    if (!slot.IsEmpty && slot.ItemId == item.Id && !slot.IsFull)
                    {
                        int oldAmount = slot.Amount;
                        int space = slot.AvailableSpace;
                        int toAdd = Mathf.Min(space, amount);

                        slot.AddAmount(toAdd);
                        amount -= toAdd;

                        _affectedSlotsBuffer.Add((slot, oldAmount));

                        if (amount <= 0) break;
                    }
                }
            }

            // 2. Add to empty slots
            while (amount > 0)
            {
                var emptySlot = GetEmptySlot();
                if (emptySlot != null)
                {
                    int toAdd = Mathf.Min(item.MaxStack, amount);
                    emptySlot.Set(item, toAdd);
                    amount -= toAdd;

                    _affectedSlotsBuffer.Add((emptySlot, 0));
                }
                else
                {
                    // Inventory full
                    int addedAmount = originalAmount - amount;
                    if (addedAmount > 0)
                    {
                        FireSlotEvents(_affectedSlotsBuffer, item, InventoryEventType.ItemAdded);
                        OnItemAdded?.Invoke(item, addedAmount);
                    }

                    OnInventoryFull?.Invoke(item, amount);
                    ZeroLog.Warning(ZeroLog.Modules.Inventory, $"Full! Could not add {amount}x {item.ItemName}");

                    SaveInventory();
                    TriggerInventoryUpdated();
                    return false;
                }
            }

            // Fire events
            FireSlotEvents(_affectedSlotsBuffer, item, InventoryEventType.ItemAdded);
            OnItemAdded?.Invoke(item, originalAmount);

            SaveInventory();
            TriggerInventoryUpdated();
            return true;
        }

        public bool AddItem(string itemId, int amount = 1)
        {
            var item = GetItemData(itemId);
            if (item == null)
            {
                ZeroLog.Warning(ZeroLog.Modules.Inventory, $"Item ID {itemId} not found in database.");
                return false;
            }
            return AddItem(item, amount);
        }

        public void RemoveItem(string itemId, int amount = 1)
        {
            var item = GetItemData(itemId);
            int removedTotal = 0;
            _affectedSlotsBuffer.Clear();

            for (int i = 0; i < _slots.Count && amount > 0; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty && slot.ItemId == itemId)
                {
                    int oldAmount = slot.Amount;
                    int toRemove = Mathf.Min(slot.Amount, amount);

                    slot.RemoveAmount(toRemove);
                    amount -= toRemove;
                    removedTotal += toRemove;

                    _affectedSlotsBuffer.Add((slot, oldAmount));
                }
            }

            if (removedTotal > 0)
            {
                FireSlotEvents(_affectedSlotsBuffer, item, InventoryEventType.ItemRemoved);
                OnItemRemoved?.Invoke(item, removedTotal);

                SaveInventory();
                TriggerInventoryUpdated();
            }
        }

        /// <summary>
        /// 移除物品 (使用 SO 引用) (v1.2.0+)
        /// </summary>
        public void RemoveItem(InventoryItemSO item, int amount = 1)
        {
            if (item != null)
            {
                RemoveItem(item.Id, amount);
            }
        }

        /// <summary>
        /// 清空指定槽位 (v1.2.0+)
        /// </summary>
        public void ClearSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;

            var slot = _slots[slotIndex];
            if (slot.IsEmpty) return;

            var item = slot.ItemData;
            int oldAmount = slot.Amount;

            slot.Clear();

            var args = new SlotChangedEventArgs(slotIndex, slot, InventoryEventType.ItemRemoved,
                item, oldAmount, 0);
            OnSlotChanged?.Invoke(args);

            if (item != null)
            {
                OnItemRemoved?.Invoke(item, oldAmount);
            }

            SaveInventory();
            TriggerInventoryUpdated();
        }

        /// <summary>
        /// 清空整个背包 (v1.2.0+)
        /// </summary>
        public void ClearAll()
        {
            foreach (var slot in _slots)
            {
                slot.Clear();
            }

            SaveInventory();
            TriggerInventoryUpdated();
        }

        #endregion

        #region Query Methods (v1.2.0+)

        public int GetItemCount(string itemId)
        {
            int count = 0;
            foreach (var slot in _slots)
            {
                if (!slot.IsEmpty && slot.ItemId == itemId)
                {
                    count += slot.Amount;
                }
            }
            return count;
        }

        /// <summary>
        /// 获取物品数量 (使用 SO 引用) (v1.2.0+)
        /// </summary>
        public int GetItemCount(InventoryItemSO item)
        {
            return item != null ? GetItemCount(item.Id) : 0;
        }

        /// <summary>
        /// 检查是否拥有足够数量的物品 (v1.2.0+)
        /// </summary>
        public bool HasItem(string itemId, int amount = 1)
        {
            return GetItemCount(itemId) >= amount;
        }

        /// <summary>
        /// 按类型查询物品 (v1.2.0+)
        /// </summary>
        public void GetItemsByType(InventoryItemType type, List<InventorySlot> results)
        {
            if (results == null) return;
            results.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty && slot.ItemData != null && slot.ItemData.Type == type)
                {
                    results.Add(slot);
                }
            }
        }

        /// <summary>
        /// 按分类查询物品 (v1.2.0+)
        /// </summary>
        public void GetItemsByCategory(ItemCategory category, List<InventorySlot> results)
        {
            if (results == null) return;
            results.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty && slot.ItemData != null && slot.ItemData.Category == category)
                {
                    results.Add(slot);
                }
            }
        }

        /// <summary>
        /// 按稀有度查询物品 (v1.2.0+)
        /// </summary>
        public void GetItemsByRarity(ItemRarity rarity, List<InventorySlot> results)
        {
            if (results == null) return;
            results.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty && slot.ItemData != null && slot.ItemData.Rarity == rarity)
                {
                    results.Add(slot);
                }
            }
        }

        /// <summary>
        /// 按稀有度查询物品 (最低稀有度) (v1.2.0+)
        /// </summary>
        public void GetItemsByMinRarity(ItemRarity minRarity, List<InventorySlot> results)
        {
            if (results == null) return;
            results.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty && slot.ItemData != null && slot.ItemData.Rarity >= minRarity)
                {
                    results.Add(slot);
                }
            }
        }

        /// <summary>
        /// 获取所有非空槽位 (v1.2.0+)
        /// </summary>
        public void GetAllItems(List<InventorySlot> results)
        {
            if (results == null) return;
            results.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty)
                {
                    results.Add(slot);
                }
            }
        }

        /// <summary>
        /// 获取空槽位数量 (v1.2.0+)
        /// </summary>
        public int GetEmptySlotCount()
        {
            int count = MaxSlots - _slots.Count;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsEmpty) count++;
            }
            return count;
        }

        /// <summary>
        /// 背包是否已满 (v1.2.0+)
        /// </summary>
        public bool IsFull => GetEmptySlotCount() == 0;

        /// <summary>
        /// 查找物品所在的第一个槽位 (v1.2.0+)
        /// </summary>
        public InventorySlot FindSlot(string itemId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty && slot.ItemId == itemId)
                    return slot;
            }
            return null;
        }

        /// <summary>
        /// 获取指定索引的槽位 (v1.2.0+)
        /// </summary>
        public InventorySlot GetSlot(int index)
        {
            return (index >= 0 && index < _slots.Count) ? _slots[index] : null;
        }

        #endregion

        #region Slot Operations (v1.2.0+)

        /// <summary>
        /// 交换两个槽位 (v1.2.0+)
        /// </summary>
        public void SwapSlots(int indexA, int indexB)
        {
            if (indexA < 0 || indexA >= _slots.Count) return;
            if (indexB < 0 || indexB >= _slots.Count) return;
            if (indexA == indexB) return;

            var slotA = _slots[indexA];
            var slotB = _slots[indexB];

            slotA.SwapWith(slotB);

            OnSlotChanged?.Invoke(new SlotChangedEventArgs(indexA, slotA, InventoryEventType.SlotSwapped,
                slotA.ItemData, 0, slotA.Amount));
            OnSlotChanged?.Invoke(new SlotChangedEventArgs(indexB, slotB, InventoryEventType.SlotSwapped,
                slotB.ItemData, 0, slotB.Amount));

            SaveInventory();
            TriggerInventoryUpdated();
        }

        /// <summary>
        /// 使用物品 (v1.2.0+)
        /// </summary>
        public bool UseItem(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return false;

            var slot = _slots[slotIndex];
            if (slot.IsEmpty || slot.ItemData == null) return false;

            var item = slot.ItemData;
            if (!item.Use()) return false;

            int oldAmount = slot.Amount;
            slot.RemoveAmount(1);

            OnSlotChanged?.Invoke(new SlotChangedEventArgs(slotIndex, slot, InventoryEventType.ItemUsed,
                item, oldAmount, slot.Amount));

            SaveInventory();
            TriggerInventoryUpdated();
            return true;
        }

        // Reusable lists to avoid allocation
        private readonly List<InventorySlot> _sortTempList = new List<InventorySlot>();
        private readonly List<(InventorySlot slot, int oldAmount)> _affectedSlotsBuffer = new List<(InventorySlot, int)>(8);

        /// <summary>
        /// 排序背包 (v1.2.0+)
        /// </summary>
        public void Sort(Comparison<InventorySlot> comparison = null)
        {
            comparison ??= DefaultSlotComparison;

            // Separate empty and non-empty slots using temp list
            _sortTempList.Clear();
            int emptyCount = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].IsEmpty)
                    _sortTempList.Add(_slots[i]);
                else
                    emptyCount++;
            }

            _sortTempList.Sort(comparison);

            // Rebuild slots list
            _slots.Clear();
            _slots.AddRange(_sortTempList);
            for (int i = 0; i < emptyCount; i++)
            {
                _slots.Add(new InventorySlot());
            }

            // Update slot indices
            UpdateSlotIndices();

            SaveInventory();
            TriggerInventoryUpdated();
        }

        private int DefaultSlotComparison(InventorySlot a, InventorySlot b)
        {
            if (a.ItemData == null && b.ItemData == null) return 0;
            if (a.ItemData == null) return 1;
            if (b.ItemData == null) return -1;

            // Sort by: Type > Rarity (descending) > Name
            int typeCompare = a.ItemData.Type.CompareTo(b.ItemData.Type);
            if (typeCompare != 0) return typeCompare;

            int rarityCompare = b.ItemData.Rarity.CompareTo(a.ItemData.Rarity);
            if (rarityCompare != 0) return rarityCompare;

            return string.Compare(a.ItemData.ItemName, b.ItemData.ItemName, StringComparison.Ordinal);
        }

        #endregion

        #region Private Helpers

        private InventorySlot GetEmptySlot()
        {
            foreach (var slot in _slots)
            {
                if (slot.IsEmpty) return slot;
            }

            if (_slots.Count < MaxSlots)
            {
                var newSlot = new InventorySlot { SlotIndex = _slots.Count };
                _slots.Add(newSlot);
                return newSlot;
            }
            return null;
        }

        private void UpdateSlotIndices()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].SlotIndex = i;
            }
        }

        private void FireSlotEvents(List<(InventorySlot slot, int oldAmount)> affected,
            InventoryItemSO item, InventoryEventType eventType)
        {
            foreach (var (slot, oldAmount) in affected)
            {
                var args = new SlotChangedEventArgs(slot.SlotIndex, slot, eventType,
                    item, oldAmount, slot.Amount);
                OnSlotChanged?.Invoke(args);
            }
        }

        private void TriggerInventoryUpdated()
        {
            EventManager.Trigger(GameEvents.InventoryUpdated);
        }

        /// <summary>
        /// 保存背包 (直接存储，用于即时保存)
        /// 注意: 使用 SaveSlotManager 槽位系统时，此方法用于即时保存到默认文件
        /// </summary>
        private void SaveInventory()
        {
            // 如果使用槽位系统，则跳过直接保存（由 SaveSlotManager 统一管理）
            if (SaveSlotManager.Instance != null && SaveSlotManager.Instance.HasLoadedSave)
            {
                return;
            }

            if (SaveManager.Instance == null)
            {
                return;
            }

            SaveManager.Instance.Save("PlayerInventory", _slots);
        }

        /// <summary>
        /// 加载背包 (从默认文件加载，用于非槽位模式)
        /// 注意: 使用 SaveSlotManager 时，数据通过 ImportSaveData 加载
        /// </summary>
        public void LoadInventory()
        {
            _slots = SaveManager.Instance.Load("PlayerInventory", new List<InventorySlot>());

            if (_slots.Count == 0)
            {
                for (int i = 0; i < 10; i++)
                {
                    _slots.Add(new InventorySlot { SlotIndex = i });
                }
            }

            // Re-link SO references and set indices
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.SlotIndex = i;

                if (!string.IsNullOrEmpty(slot.ItemId))
                {
                    if (_itemDatabase.TryGetValue(slot.ItemId, out var so))
                    {
                        slot.ItemData = so;
                    }
                    else
                    {
                        ZeroLog.Warning(ZeroLog.Modules.Inventory, $"Item ID {slot.ItemId} not found in database.");
                    }
                }
            }

            TriggerInventoryUpdated();
        }

        #endregion
    }
}

