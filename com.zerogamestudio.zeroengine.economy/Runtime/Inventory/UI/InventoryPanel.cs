using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Inventory.UI
{
    public class InventoryPanel : MonoBehaviour
    {
        [Header("UI References")]
        public Transform SlotGridParent;
        public InventorySlotUI SlotPrefab;
        public GameObject PanelRoot;
        
        private List<InventorySlotUI> _uiSlots = new List<InventorySlotUI>();

        private void Start()
        {
            // Init Slots
            InitializeUI();
            
            // Subscribe to update event
            // Assuming InventoryManager triggers an event via EventManager or C# Action
            // For now, let's poll or use Manager event if available
            // InventoryManager should have an event. Let's add it there or just listen to global.
            // "GameEvents.InventoryUpdated" (String based)
            EventManager.Subscribe(GameEvents.InventoryUpdated, Refresh);
            
            Refresh();
        }

        private void OnDestroy()
        {
            EventManager.Unsubscribe(GameEvents.InventoryUpdated, Refresh);
            for (int i = 0; i < _uiSlots.Count; i++)
            {
                if (_uiSlots[i] != null)
                {
                    _uiSlots[i].OnSlotDropRequested -= HandleSlotDropRequested;
                }
            }
        }

        private void InitializeUI()
        {
            // Clear existing
            foreach(Transform child in SlotGridParent) Destroy(child.gameObject);
            _uiSlots.Clear();

            // Create based on Manager max slots
            int max = InventoryManager.MaxSlots; 
            for (int i = 0; i < max; i++)
            {
                var ui = Instantiate(SlotPrefab, SlotGridParent);
                ui.Setup(i);
                ui.OnSlotDropRequested += HandleSlotDropRequested;
                _uiSlots.Add(ui);
            }
        }

        public void Refresh()
        {
            if (InventoryManager.Instance == null) return;
            
            var dataSlots = InventoryManager.Instance.Slots;
            for (int i = 0; i < _uiSlots.Count; i++)
            {
                if (i < dataSlots.Count)
                {
                    _uiSlots[i].Bind(dataSlots[i]);
                }
                else
                {
                    _uiSlots[i].Bind(null);
                }
            }
        }
        
        public void Toggle()
        {
            if (PanelRoot != null)
                PanelRoot.SetActive(!PanelRoot.activeSelf);
        }

        private void HandleSlotDropRequested(int sourceSlotIndex, int targetSlotIndex)
        {
            InventoryManager.Instance?.SwapSlots(sourceSlotIndex, targetSlotIndex);
        }
    }
}

