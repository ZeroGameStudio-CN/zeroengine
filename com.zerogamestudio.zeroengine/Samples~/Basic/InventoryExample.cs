using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Inventory;

namespace ZeroEngine.Samples
{
    /// <summary>
    /// Inventory 基础示例
    /// 演示物品添加、移除、查询和事件监听
    /// </summary>
    public class InventoryExample : MonoBehaviour
    {
        [Header("Test Items")]
        [SerializeField] private InventoryItemSO testItem;
        [SerializeField] private int maxSlots = 20;

        private void Start()
        {
            // 初始化背包
            InventoryManager.Instance.Initialize(maxSlots);

            // 监听事件
            InventoryManager.Instance.OnItemAdded += OnItemAdded;
            InventoryManager.Instance.OnItemRemoved += OnItemRemoved;
            InventoryManager.Instance.OnSlotChanged += OnSlotChanged;
            InventoryManager.Instance.OnInventoryFull += OnInventoryFull;

            if (testItem != null)
            {
                TestInventoryOperations();
            }
            else
            {
                Debug.Log("[InventoryExample] No test item assigned.");
            }
        }

        private void TestInventoryOperations()
        {
            Debug.Log("[InventoryExample] Testing inventory operations...");

            // 添加物品
            bool added = InventoryManager.Instance.AddItem(testItem, 5);
            Debug.Log($"[InventoryExample] Added {testItem.ItemName} x5: {added}");

            // 查询物品数量
            int count = InventoryManager.Instance.GetItemCount(testItem.ItemId);
            Debug.Log($"[InventoryExample] Item count: {count}");

            // 检查是否拥有物品
            bool hasItem = InventoryManager.Instance.HasItem(testItem.ItemId, 3);
            Debug.Log($"[InventoryExample] Has 3+ items: {hasItem}");

            // 查找物品所在槽位
            var slot = InventoryManager.Instance.FindSlot(testItem.ItemId);
            if (slot != null)
            {
                Debug.Log($"[InventoryExample] Item found in slot {slot.SlotIndex}");
            }

            // 获取空槽位数量
            int emptySlots = InventoryManager.Instance.GetEmptySlotCount();
            Debug.Log($"[InventoryExample] Empty slots: {emptySlots}");

            var queryResults = new List<InventorySlot>();

            // 按类型查询
            InventoryManager.Instance.GetItemsByType(testItem.Type, queryResults);
            foreach (var itemSlot in queryResults)
            {
                Debug.Log($"[InventoryExample] Found item by type: {itemSlot.ItemData.ItemName}");
            }

            // 按稀有度查询
            InventoryManager.Instance.GetItemsByRarity(testItem.Rarity, queryResults);
            foreach (var itemSlot in queryResults)
            {
                Debug.Log($"[InventoryExample] Found item by rarity: {itemSlot.ItemData.ItemName}");
            }
        }

        private void OnItemAdded(InventoryItemSO item, int amount)
        {
            Debug.Log($"[InventoryExample] Item added: {item.ItemName} x{amount}");
        }

        private void OnItemRemoved(InventoryItemSO item, int amount)
        {
            Debug.Log($"[InventoryExample] Item removed: {item.ItemName} x{amount}");
        }

        private void OnSlotChanged(SlotChangedEventArgs args)
        {
            Debug.Log($"[InventoryExample] Slot {args.SlotIndex} changed: {args.EventType}");
        }

        private void OnInventoryFull()
        {
            Debug.LogWarning("[InventoryExample] Inventory is full!");
        }

        private void Update()
        {
            if (testItem == null) return;

            // 按 A 键添加物品
            if (Input.GetKeyDown(KeyCode.A))
            {
                InventoryManager.Instance.AddItem(testItem, 1);
            }

            // 按 D 键移除物品
            if (Input.GetKeyDown(KeyCode.D))
            {
                InventoryManager.Instance.RemoveItem(testItem, 1);
            }

            // 按 S 键排序背包
            if (Input.GetKeyDown(KeyCode.S))
            {
                InventoryManager.Instance.Sort();
                Debug.Log("[InventoryExample] Inventory sorted.");
            }

            // 按 C 键清空背包
            if (Input.GetKeyDown(KeyCode.C))
            {
                InventoryManager.Instance.ClearAll();
                Debug.Log("[InventoryExample] Inventory cleared.");
            }
        }

        private void OnDestroy()
        {
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.OnItemAdded -= OnItemAdded;
                InventoryManager.Instance.OnItemRemoved -= OnItemRemoved;
                InventoryManager.Instance.OnSlotChanged -= OnSlotChanged;
                InventoryManager.Instance.OnInventoryFull -= OnInventoryFull;
            }
        }
    }
}
