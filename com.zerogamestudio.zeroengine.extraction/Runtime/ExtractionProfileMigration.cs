using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionProfileMigration
    {
        public const string CarryGridLocationSubtype = "carry-grid";

        public static void MigrateToCurrent(ExtractionProfileSaveData profile)
        {
            if (profile == null || profile.SchemaVersion > ExtractionProfileSaveData.CurrentSchemaVersion)
                return;

            EnsureRoots(profile);
            EnsureItemState(profile);
            EnsureOwnershipLocations(profile);
            profile.ActiveRaid?.EnsureInitialized();
            profile.ActiveRaidInventory?.EnsureInitialized();

            if (string.IsNullOrEmpty(profile.activeRaidId))
            {
                profile.ActiveRaid = null;
                profile.ActiveRaidElapsedSeconds = 0f;
                profile.ActiveRaidInventory = null;
            }

            profile.SchemaVersion = ExtractionProfileSaveData.CurrentSchemaVersion;
        }

        private static void EnsureRoots(ExtractionProfileSaveData profile)
        {
            profile.Items ??= new ExtractionItemRegistry();
            profile.Items.Entries ??= new List<ExtractionItemInstance>();
            profile.Stash ??= new ExtractionItemGrid(10, 6);
            profile.CarryGrid ??= new ExtractionItemGrid(6, 4);
            profile.SecureContainer ??= new ExtractionItemGrid(2, 2);
            profile.RecoveryHolding ??= new ExtractionItemGrid(10, 4);
            profile.Ownership ??= new ExtractionOwnershipLedger();
            profile.Ownership.Entries ??= new List<ExtractionOwnershipEntry>();
            profile.Recovery ??= new ExtractionRecoveryLedger();
            profile.CorpseLoot ??= new ExtractionCorpseLootLedger();
            profile.Facilities ??= new ExtractionFacilityProfile();
            profile.MedicalTreatments ??= new ExtractionMedicalTreatmentLedger();
            profile.UnlockedMapIds ??= new List<string>();
            profile.UnlockedBlueprintIds ??= new List<string>();
            profile.DifficultySettings ??= new ExtractionDifficultySettings();
            profile.Character ??= new ExtractionCharacterProfile();
            profile.Character.EnsureInitialized();
            profile.Equipment ??= new ExtractionEquipmentState();
            profile.Equipment.EnsureInitialized();
            profile.Merchant ??= new ExtractionMerchantState();
            profile.Merchant.EnsureInitialized();
            profile.OperationJournal ??= new ExtractionOperationJournal();
            profile.OperationJournal.EnsureInitialized();
            profile.ItemActionReceiptIds ??= new List<string>();
        }

        private static void EnsureItemState(ExtractionProfileSaveData profile)
        {
            foreach (var item in profile.Items.Entries)
                item?.EnsureInitialized();
        }

        private static void EnsureOwnershipLocations(ExtractionProfileSaveData profile)
        {
            foreach (var entry in profile.Ownership.Entries)
            {
                if (entry == null
                    || entry.Container != ExtractionInventoryContainerType.Loadout
                    || !string.IsNullOrEmpty(entry.LocationSubtype))
                {
                    continue;
                }

                // v0/v1 的 Loadout 语义无法判断具体装备位；只标记为安全携行网格，
                // Equipment 保持空，后续 C3 只能通过显式装备事务安装效果。
                entry.LocationSubtype = CarryGridLocationSubtype;
                entry.LocationId = null;
            }
        }
    }
}
