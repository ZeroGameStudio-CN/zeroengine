using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    // Protect the additive save schema and the shared draft/revision transaction boundary.
    public class ExtractionTalentProgressionPersistenceTests
    {
        [Test]
        public void FromJson_V5WithoutGrowth_PreservesLegacyStateAndCreatesEmptyProgression()
        {
            const string json =
                "{\"SchemaVersion\":5,\"activeRaidId\":\"old-raid\"," +
                "\"ActiveRaid\":{\"RaidId\":\"old-raid\",\"MapId\":\"old-map\"}," +
                "\"ActiveRaidElapsedSeconds\":31," +
                "\"Character\":{\"AttributeValues\":[{\"AttributeId\":\"health-flat\",\"Value\":12}]," +
                "\"UnlockedTalentIds\":[\"double-jump\"],\"SearchSpeedMultiplier\":1.25}," +
                "\"Items\":{\"Entries\":[{\"InstanceId\":\"old-material\",\"DefinitionId\":\"scrap\",\"Quantity\":7}]}," +
                "\"CarryGrid\":{\"Width\":6,\"Height\":5,\"Placements\":[{" +
                "\"ItemInstanceId\":\"old-material\",\"X\":2,\"Y\":4,\"Width\":1,\"Height\":1}]}," +
                "\"Ownership\":{\"Entries\":[{\"ItemInstanceId\":\"old-material\",\"Container\":1}]}," +
                "\"Facilities\":{\"Entries\":[{\"FacilityType\":4,\"Level\":3}]}," +
                "\"UnlockedMapIds\":[\"old-map\"],\"ItemActionReceiptIds\":[\"old-receipt\"]}";

            var profile = ExtractionProfileSerialization.FromJson(json);

            Assert.AreEqual(ExtractionProfileSaveData.CurrentSchemaVersion, profile.SchemaVersion);
            AssertEmptyGrowth(profile);
            CollectionAssert.AreEqual(new[] { "double-jump" }, profile.Character.UnlockedTalentIds);
            Assert.AreEqual("health-flat", profile.Character.AttributeValues[0].AttributeId);
            Assert.AreEqual(12f, profile.Character.AttributeValues[0].Value);
            Assert.AreEqual(1.25f, profile.Character.SearchSpeedMultiplier);
            Assert.AreEqual("old-raid", profile.ActiveRaid.RaidId);
            Assert.AreEqual(31f, profile.ActiveRaidElapsedSeconds);
            Assert.AreEqual(5, profile.CarryGrid.Height);
            Assert.IsTrue(profile.CarryGrid.TryGetPlacement("old-material", out var placement));
            Assert.AreEqual(2, placement.X);
            Assert.AreEqual(4, placement.Y);
            Assert.IsTrue(profile.Items.TryGet("old-material", out var item));
            Assert.AreEqual(7, item.Quantity);
            Assert.IsTrue(profile.Ownership.TryGetContainer("old-material", out var container));
            Assert.AreEqual(ExtractionInventoryContainerType.Loadout, container);
            Assert.AreEqual(3, profile.Facilities.GetLevel(ExtractionFacilityType.Merchant));
            CollectionAssert.AreEqual(new[] { "old-map" }, profile.UnlockedMapIds);
            CollectionAssert.AreEqual(new[] { "old-receipt" }, profile.ItemActionReceiptIds);

            string migrated = ExtractionProfileSerialization.ToJson(profile);
            profile.EnsureInitialized();
            Assert.AreEqual(migrated, ExtractionProfileSerialization.ToJson(profile));
        }

        [Test]
        public void EnsureInitialized_NullGrowthCollections_CreatesEmptyStateWithoutGrantingLevels()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            profile.Growth = null;
            profile.EnsureInitialized();
            AssertEmptyGrowth(profile);

            profile.Growth.NodeLevels = null;
            profile.Growth.PurchaseReceipts = null;
            profile.Growth.TargetNodeId = null;
            profile.EnsureInitialized();
            AssertEmptyGrowth(profile);
        }

        [Test]
        public void Serialization_RoundTrip_PreservesLevelsTargetAndExactPurchaseReceipt()
        {
            var profile = CreateProfile();
            ApplyPurchase(profile);

            var restored = ExtractionProfileSerialization.FromJson(ExtractionProfileSerialization.ToJson(profile));

            AssertPurchased(restored);
            CollectionAssert.AreEqual(new[] { "double-jump" }, restored.Character.UnlockedTalentIds);
        }

        [Test]
        public void Load_ChangingSnapshotOrDraft_DoesNotMutateStoredProgression()
        {
            var profile = CreateProfile();
            ApplyPurchase(profile);
            var store = new ExtractionInMemoryProfileStore(profile);
            var loaded = store.Load();
            var snapshot = loaded.Profile;
            snapshot.Growth.NodeLevels[0].Level = 9;
            snapshot.Growth.PurchaseReceipts[0].ConfigVersion = "changed-snapshot";
            snapshot.Growth.TargetNodeId = "changed-snapshot";
            Assert.IsTrue(loaded.TryCreateDraft(out var draft));
            draft.Profile.Growth.NodeLevels[0].Level = 10;
            draft.Profile.Growth.PurchaseReceipts[0].OperationId = "changed-draft";
            draft.Profile.Growth.TargetNodeId = "changed-draft";

            AssertPurchased(store.Load().Profile);
            Assert.AreEqual(loaded.Revision, store.Load().Revision);
        }

        [Test]
        public void Commit_MaterialsAndProgressionTogether_AdvancesRevisionAndKeepsDetachedState()
        {
            var store = new ExtractionInMemoryProfileStore(CreateProfile());
            var loaded = store.Load();
            Assert.IsTrue(loaded.TryCreateDraft(out var draft));
            ApplyPurchase(draft.Profile);

            var result = store.Commit(draft);

            Assert.IsTrue(result.Success);
            Assert.AreNotEqual(loaded.Revision, result.Revision);
            AssertPurchased(store.Load().Profile);
            draft.Profile.Growth.NodeLevels[0].Level = 99;
            draft.Profile.Growth.PurchaseReceipts.Clear();
            AssertPurchased(store.Load().Profile);
        }

        [TestCase(ExtractionProfileInMemoryCommitFault.Prepare)]
        [TestCase(ExtractionProfileInMemoryCommitFault.Commit)]
        [TestCase(ExtractionProfileInMemoryCommitFault.Readback)]
        public void Commit_InjectedFault_KeepsMaterialsProgressionAndRevision(
            ExtractionProfileInMemoryCommitFault fault)
        {
            var store = new ExtractionInMemoryProfileStore(CreateProfile());
            var loaded = store.Load();
            Assert.IsTrue(loaded.TryCreateDraft(out var draft));
            ApplyPurchase(draft.Profile);
            store.NextCommitFault = fault;

            Assert.IsFalse(store.Commit(draft).Success);

            var unchanged = store.Load();
            Assert.AreEqual(loaded.Revision, unchanged.Revision);
            AssertEmptyGrowth(unchanged.Profile);
            Assert.IsTrue(unchanged.Profile.Items.TryGet("material-1", out var material));
            Assert.AreEqual(7, material.Quantity);
        }

        [Test]
        public void Commit_StaleGrowthDraft_KeepsFirstPurchaseAndInventory()
        {
            var store = new ExtractionInMemoryProfileStore(CreateProfile());
            var loaded = store.Load();
            Assert.IsTrue(loaded.TryCreateDraft(out var first));
            Assert.IsTrue(loaded.TryCreateDraft(out var stale));
            ApplyPurchase(first.Profile);
            ApplyPurchase(stale.Profile);
            stale.Profile.Growth.NodeLevels[0].Level = 2;
            stale.Profile.Growth.TargetNodeId = "different-target";
            Assert.IsTrue(stale.Profile.Items.TryGet("material-1", out var staleMaterial));
            staleMaterial.Quantity = 0;
            Assert.IsTrue(store.Commit(first).Success);
            string committedRevision = store.Load().Revision;

            var result = store.Commit(stale);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ExtractionProfileCommitFailure.RevisionConflict, result.Failure);
            Assert.AreEqual(committedRevision, store.Load().Revision);
            AssertPurchased(store.Load().Profile);
        }

        private static ExtractionProfileSaveData CreateProfile()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            profile.Character.UnlockedTalentIds.Add("double-jump");
            var definition = new ExtractionItemDefinition("scrap", 1, 1, false, 99);
            var material = new ExtractionItemInstance("material-1", "scrap", 7);
            Assert.IsTrue(profile.Items.Register(material));
            Assert.IsTrue(profile.CarryGrid.TryPlace(material, definition, 0, 0, false));
            Assert.IsTrue(profile.Ownership.Register(material.InstanceId, ExtractionInventoryContainerType.Loadout));
            return profile;
        }

        private static void ApplyPurchase(ExtractionProfileSaveData profile)
        {
            Assert.IsTrue(profile.Items.TryGet("material-1", out var material));
            material.Quantity = 2;
            profile.Growth.NodeLevels.Add(new ExtractionTalentNodeLevel { NodeId = "growth.health", Level = 1 });
            profile.Growth.TargetNodeId = "growth.health";
            profile.Growth.PurchaseReceipts.Add(new ExtractionTalentPurchaseReceipt
            {
                OperationId = "purchase-1",
                NodeId = "growth.health",
                ExpectedLevel = 0,
                TargetLevel = 1,
                ConfigVersion = "costs-v1"
            });
        }

        private static void AssertEmptyGrowth(ExtractionProfileSaveData profile)
        {
            Assert.IsNotNull(profile.Growth);
            Assert.IsEmpty(profile.Growth.NodeLevels);
            Assert.IsEmpty(profile.Growth.PurchaseReceipts);
            Assert.AreEqual(string.Empty, profile.Growth.TargetNodeId);
        }

        private static void AssertPurchased(ExtractionProfileSaveData profile)
        {
            Assert.AreEqual(1, profile.Growth.NodeLevels.Count);
            Assert.AreEqual("growth.health", profile.Growth.NodeLevels[0].NodeId);
            Assert.AreEqual(1, profile.Growth.NodeLevels[0].Level);
            Assert.AreEqual("growth.health", profile.Growth.TargetNodeId);
            Assert.AreEqual(1, profile.Growth.PurchaseReceipts.Count);
            var receipt = profile.Growth.PurchaseReceipts[0];
            Assert.AreEqual("purchase-1", receipt.OperationId);
            Assert.AreEqual("growth.health", receipt.NodeId);
            Assert.AreEqual(0, receipt.ExpectedLevel);
            Assert.AreEqual(1, receipt.TargetLevel);
            Assert.AreEqual("costs-v1", receipt.ConfigVersion);
            Assert.IsTrue(profile.Items.TryGet("material-1", out var material));
            Assert.AreEqual(2, material.Quantity);
        }
    }
}
