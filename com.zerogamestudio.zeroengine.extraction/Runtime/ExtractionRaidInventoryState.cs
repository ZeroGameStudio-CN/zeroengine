using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionRaidInventoryState
    {
        public ExtractionItemGrid RaidBackpack;
        public ExtractionItemGrid SecureContainer;
        public ExtractionEquipmentState Equipment = new();

        public ExtractionRaidInventoryState(
            int raidBackpackWidth,
            int raidBackpackHeight,
            int secureWidth,
            int secureHeight)
        {
            RaidBackpack = new ExtractionItemGrid(raidBackpackWidth, raidBackpackHeight);
            SecureContainer = new ExtractionItemGrid(secureWidth, secureHeight);
        }

        internal void EnsureInitialized()
        {
            Equipment ??= new ExtractionEquipmentState();
            Equipment.EnsureInitialized();
        }
    }
}
