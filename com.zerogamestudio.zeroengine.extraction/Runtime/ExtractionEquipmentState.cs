using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionEquipmentState
    {
        public List<ExtractionEquipmentSlotState> Slots = new();
        public List<string> AppliedReceiptIds = new();

        internal void EnsureInitialized()
        {
            Slots ??= new List<ExtractionEquipmentSlotState>();
            AppliedReceiptIds ??= new List<string>();
        }

        public bool TryGetItem(string slotId, out string itemInstanceId)
        {
            itemInstanceId = null;
            if (string.IsNullOrEmpty(slotId) || Slots == null) return false;

            foreach (var slot in Slots)
            {
                if (slot == null || slot.SlotId != slotId) continue;
                itemInstanceId = slot.ItemInstanceId;
                return !string.IsNullOrEmpty(itemInstanceId);
            }

            return false;
        }

        public bool TryGetSlotForItem(string itemInstanceId, out string slotId)
        {
            slotId = null;
            if (string.IsNullOrEmpty(itemInstanceId) || Slots == null) return false;

            foreach (var slot in Slots)
            {
                if (slot == null || slot.ItemInstanceId != itemInstanceId) continue;
                slotId = slot.SlotId;
                return !string.IsNullOrEmpty(slotId);
            }

            return false;
        }

        internal bool TrySet(string slotId, string itemInstanceId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(slotId) || string.IsNullOrEmpty(itemInstanceId)) return false;
            if (TryGetSlotForItem(itemInstanceId, out var existingSlot) && existingSlot != slotId)
                return false;

            foreach (var slot in Slots)
            {
                if (slot == null || slot.SlotId != slotId) continue;
                slot.ItemInstanceId = itemInstanceId;
                return true;
            }

            Slots.Add(new ExtractionEquipmentSlotState(slotId, itemInstanceId));
            return true;
        }

        internal bool TryClear(string slotId, string expectedItemInstanceId)
        {
            if (string.IsNullOrEmpty(slotId) || string.IsNullOrEmpty(expectedItemInstanceId) || Slots == null)
                return false;

            for (int i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                if (slot == null || slot.SlotId != slotId || slot.ItemInstanceId != expectedItemInstanceId)
                    continue;

                Slots.RemoveAt(i);
                return true;
            }

            return false;
        }
    }

    [Serializable]
    public class ExtractionEquipmentSlotState
    {
        public string SlotId;
        public string ItemInstanceId;
        public string EffectReceiptId;

        public ExtractionEquipmentSlotState(string slotId, string itemInstanceId)
            : this(slotId, itemInstanceId, null)
        {
        }

        public ExtractionEquipmentSlotState(
            string slotId,
            string itemInstanceId,
            string effectReceiptId)
        {
            SlotId = slotId;
            ItemInstanceId = itemInstanceId;
            EffectReceiptId = effectReceiptId;
        }
    }
}
