using NUnit.Framework;
using UnityEngine;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionProfileMigrationTests
    {
        [TestCase(0)]
        [TestCase(1)]
        public void EnsureInitialized_LegacyProfile_MigratesDeterministicallyToSchemaV2(int schemaVersion)
        {
            string json =
                $"{{\"SchemaVersion\":{schemaVersion}," +
                "\"Items\":{\"Entries\":[{\"InstanceId\":\"legacy-item\",\"DefinitionId\":\"legacy-cargo\",\"Quantity\":1}]} ," +
                "\"Loadout\":{\"Width\":6,\"Height\":4,\"Placements\":[{\"ItemInstanceId\":\"legacy-item\",\"X\":1,\"Y\":2,\"Width\":1,\"Height\":1,\"Rotated\":false}]}," +
                "\"Ownership\":{\"Entries\":[{\"ItemInstanceId\":\"legacy-item\",\"Container\":1}]}}";
            var profile = ExtractionProfileSerialization.FromJson(json);

            Assert.AreEqual(2, profile.SchemaVersion);
            Assert.IsTrue(profile.CarryGrid.TryGetPlacement("legacy-item", out _));
            Assert.AreEqual(0, profile.Equipment.Slots.Count, "旧 Loadout 不能在迁移时被猜测为已装备。");
            Assert.IsNotNull(profile.Character);
            Assert.IsNotNull(profile.Merchant);
            Assert.IsNotNull(profile.OperationJournal);

            Assert.IsTrue(profile.Items.TryGet("legacy-item", out var item));
            Assert.IsTrue(item.HasFlag(ExtractionItemInstanceFlags.CanDrop));
            Assert.IsTrue(item.HasFlag(ExtractionItemInstanceFlags.CanSell));
            Assert.IsTrue(item.HasFlag(ExtractionItemInstanceFlags.DropOnDeath));
            Assert.IsNotNull(item.AffixIds);
            Assert.AreEqual(0, item.CurrentDurability);

            var ownershipEntry = profile.Ownership.Entries[0];
            Assert.AreEqual(ExtractionProfileMigration.CarryGridLocationSubtype, ownershipEntry.LocationSubtype);

            string once = JsonUtility.ToJson(profile);
            profile.EnsureInitialized();
            Assert.AreEqual(once, JsonUtility.ToJson(profile), "迁移重复执行必须保持字节级稳定。");
        }

        [Test]
        public void EnsureInitialized_SchemaV2ExplicitPolicyAndInstanceState_PreservesValues()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            var item = new ExtractionItemInstance("task-item", "task-definition", 1)
            {
                CurrentDurability = 4,
                EnhancementLevel = 2,
                ForgeTier = 1,
                Flags = ExtractionItemInstanceFlags.PolicyInitialized | ExtractionItemInstanceFlags.RaidBound
            };
            item.AffixIds.Add("affix.stable");

            Assert.IsTrue(profile.Items.Register(item));
            profile.EnsureInitialized();
            Assert.IsTrue(profile.Items.TryGet(item.InstanceId, out var stored));

            Assert.IsFalse(stored.HasFlag(ExtractionItemInstanceFlags.CanDrop));
            Assert.IsFalse(stored.HasFlag(ExtractionItemInstanceFlags.CanSell));
            Assert.IsFalse(stored.HasFlag(ExtractionItemInstanceFlags.DropOnDeath));
            Assert.IsTrue(stored.HasFlag(ExtractionItemInstanceFlags.RaidBound));
            Assert.AreEqual(4, stored.CurrentDurability);
            Assert.AreEqual(2, stored.EnhancementLevel);
            Assert.AreEqual(1, stored.ForgeTier);
            CollectionAssert.AreEqual(new[] { "affix.stable" }, stored.AffixIds);
        }

        [Test]
        public void CreateEmpty_SchemaV2_InitializesNewAuthorityRoots()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();

            Assert.AreEqual(2, profile.SchemaVersion);
            Assert.IsNotNull(profile.CarryGrid);
            Assert.IsNotNull(profile.Character);
            Assert.AreEqual(1f, profile.Character.SearchSpeedMultiplier);
            Assert.IsNotNull(profile.Equipment);
            Assert.IsNotNull(profile.Merchant);
            Assert.IsNotNull(profile.OperationJournal);
        }

        [Test]
        public void FromJson_LegacyActiveRaid_InitializesContentAndEquipmentRoots()
        {
            const string json =
                "{\"SchemaVersion\":1,\"activeRaidId\":\"raid-1\"," +
                "\"ActiveRaid\":{\"RaidId\":\"raid-1\",\"MapId\":\"map-1\"}," +
                "\"ActiveRaidInventory\":{" +
                "\"RaidBackpack\":{\"Width\":6,\"Height\":4,\"Placements\":[]}," +
                "\"SecureContainer\":{\"Width\":2,\"Height\":2,\"Placements\":[]}}}";

            var profile = ExtractionProfileSerialization.FromJson(json);

            Assert.IsNotNull(profile.ActiveRaid.Content);
            Assert.IsNotNull(profile.ActiveRaidInventory.Equipment);
            Assert.IsEmpty(profile.ActiveRaid.Content.AppliedReceiptIds);
            Assert.IsEmpty(profile.ActiveRaidInventory.Equipment.Slots);
        }

        [Test]
        public void FromJson_FutureSchema_DoesNotApplyLegacyLoadoutMigration()
        {
            const string json =
                "{\"SchemaVersion\":99,\"activeRaidId\":\"future\"," +
                "\"Loadout\":{\"Width\":6,\"Height\":4,\"Placements\":[{" +
                "\"ItemInstanceId\":\"future-item\",\"X\":0,\"Y\":0,\"Width\":1,\"Height\":1}]}}";

            var profile = ExtractionProfileSerialization.FromJson(json);

            Assert.AreEqual(99, profile.SchemaVersion);
            Assert.AreEqual("future", profile.activeRaidId);
            Assert.IsFalse(profile.CarryGrid?.TryGetPlacement("future-item", out _) ?? false);
        }

        [Test]
        public void InventoryContainerType_SerializedValues_KeepLegacyValuesAndAppendV2States()
        {
            Assert.AreEqual(0, (int)ExtractionInventoryContainerType.Stash);
            Assert.AreEqual(1, (int)ExtractionInventoryContainerType.Loadout);
            Assert.AreEqual(9, (int)ExtractionInventoryContainerType.Buyback);
            Assert.AreEqual(10, (int)ExtractionInventoryContainerType.EquipmentSlot);
            Assert.AreEqual(11, (int)ExtractionInventoryContainerType.WorldPickup);
            Assert.AreEqual(12, (int)ExtractionInventoryContainerType.Destroyed);
            Assert.AreEqual(13, (int)ExtractionInventoryContainerType.Consumed);
            Assert.AreEqual(14, (int)ExtractionInventoryContainerType.Sold);
            Assert.AreEqual(15, (int)ExtractionInventoryContainerType.DestroyedByUse);
        }
    }
}
