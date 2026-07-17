using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionEquipmentState
    {
        public List<ExtractionEquipmentSlotState> Slots = new();

        internal void EnsureInitialized()
        {
            Slots ??= new List<ExtractionEquipmentSlotState>();
        }
    }

    [Serializable]
    public class ExtractionEquipmentSlotState
    {
        public string SlotId;
        public string ItemInstanceId;

        public ExtractionEquipmentSlotState(string slotId, string itemInstanceId)
        {
            SlotId = slotId;
            ItemInstanceId = itemInstanceId;
        }
    }
}
