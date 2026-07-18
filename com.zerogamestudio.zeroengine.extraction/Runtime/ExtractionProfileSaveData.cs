using System;
using System.Collections.Generic;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionProfileSaveData
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion;
        public string activeRaidId;
        public ExtractionRaidSession ActiveRaid;
        public float ActiveRaidElapsedSeconds;
        public ExtractionRaidInventoryState ActiveRaidInventory;
        public ExtractionItemRegistry Items = new();
        public ExtractionItemGrid Stash = new(10, 6);
        [FormerlySerializedAs("Loadout")]
        public ExtractionItemGrid CarryGrid = new(6, 4);
        public ExtractionItemGrid SecureContainer = new(2, 2);
        public ExtractionItemGrid RecoveryHolding = new(10, 4);
        public ExtractionOwnershipLedger Ownership = new();
        public ExtractionRecoveryLedger Recovery = new();
        public ExtractionCorpseLootLedger CorpseLoot = new();
        public ExtractionFacilityProfile Facilities = new();
        public ExtractionMedicalTreatmentLedger MedicalTreatments = new();
        public List<string> UnlockedMapIds = new();
        public List<string> UnlockedBlueprintIds = new();
        public ExtractionDifficultySettings DifficultySettings = new();
        public ExtractionCharacterProfile Character = new();
        public ExtractionEquipmentState Equipment = new();
        public ExtractionMerchantState Merchant = new();
        public ExtractionOperationJournal OperationJournal = new();
        public List<string> ItemActionReceiptIds = new();

        // 兼容既有调用方；v2 JSON 只序列化 CarryGrid。旧 Unity 序列化数据由
        // FormerlySerializedAs 兼容，旧 JSON 则由 ExtractionProfileSerialization 的
        // v1 envelope 确定性读入同一网格，不保留第二份位置状态。
        public ExtractionItemGrid Loadout
        {
            get => CarryGrid;
            set => CarryGrid = value;
        }

        public static ExtractionProfileSaveData CreateEmpty()
        {
            var profile = new ExtractionProfileSaveData();
            profile.EnsureInitialized();
            return profile;
        }

        public void EnsureInitialized()
        {
            ExtractionProfileMigration.MigrateToCurrent(this);
        }
    }
}
