using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionProfileSaveData
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;
        public string activeRaidId;
        public ExtractionRaidSession ActiveRaid;
        public float ActiveRaidElapsedSeconds;
        public ExtractionRaidInventoryState ActiveRaidInventory;
        public ExtractionItemRegistry Items = new();
        public ExtractionItemGrid Stash = new(10, 6);
        public ExtractionItemGrid Loadout = new(6, 4);
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

        public static ExtractionProfileSaveData CreateEmpty()
        {
            var profile = new ExtractionProfileSaveData();
            profile.EnsureInitialized();
            return profile;
        }

        public void EnsureInitialized()
        {
            Items ??= new ExtractionItemRegistry();
            Stash ??= new ExtractionItemGrid(10, 6);
            Loadout ??= new ExtractionItemGrid(6, 4);
            SecureContainer ??= new ExtractionItemGrid(2, 2);
            RecoveryHolding ??= new ExtractionItemGrid(10, 4);
            Ownership ??= new ExtractionOwnershipLedger();
            Recovery ??= new ExtractionRecoveryLedger();
            CorpseLoot ??= new ExtractionCorpseLootLedger();
            Facilities ??= new ExtractionFacilityProfile();
            MedicalTreatments ??= new ExtractionMedicalTreatmentLedger();
            UnlockedMapIds ??= new List<string>();
            UnlockedBlueprintIds ??= new List<string>();
            DifficultySettings ??= new ExtractionDifficultySettings();
            if (string.IsNullOrEmpty(activeRaidId))
            {
                ActiveRaid = null;
                ActiveRaidElapsedSeconds = 0f;
                ActiveRaidInventory = null;
            }

            Migrate();
        }

        private void Migrate()
        {
            // v0 → v1：引入 SchemaVersion 标记 + ActiveRaid 运行时字段；
            // 旧存档无需数据转换，仅补盖版本号。新版存档（version 更高）原样保留，
            // 避免被旧客户端误改写。
            if (SchemaVersion < CurrentSchemaVersion)
                SchemaVersion = CurrentSchemaVersion;
        }
    }
}
