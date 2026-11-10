using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public sealed class ExtractionContainerInventoryTests
    {
        private bool enabled;
        [SetUp] public void Before() { enabled = ExtractionFeatureSwitch.Enabled; ExtractionFeatureSwitch.SetEnabledForTests(true); }
        [TearDown] public void After() => ExtractionFeatureSwitch.SetEnabledForTests(enabled);

        private static ExtractionProfileSaveData Create(out ExtractionPlayableConfig config, out ExtractionRaidContainerManifest container)
        {
            config = new ExtractionPlayableConfig(4, 3, 2, 2);
            var medicine = new ExtractionItemDefinition("medicine", 1, 1, false, 1)
            { MaxDurability = 3, DestroyOnZeroDurability = false };
            medicine.ActionPolicy.CanUse = true;
            medicine.ActionPolicy.ConsumptionType = ExtractionItemConsumptionType.Durability;
            medicine.ActionPolicy.UseActionId = "test.heal";
            config.ItemDefinitions.Add(medicine);
            var gear = new ExtractionItemDefinition("gear", 2, 1, true, 1) { MaxDurability = 8 };
            gear.ActionPolicy.CanEquip = true; gear.ActionPolicy.EquipmentSlotType = ExtractionEquipmentSlotType.Head;
            gear.ActionPolicy.EffectAdapterId = "test.armor";
            config.ItemDefinitions.Add(gear);
            config.ItemDefinitions.Add(new ExtractionItemDefinition("cargo", 2, 1, true, 1));
            config.ItemDefinitions.Add(new ExtractionItemDefinition("secret", 1, 2, false, 1));
            var profile = ExtractionProfileSaveData.CreateEmpty();
            profile.ActiveRaid = new ExtractionRaidSession(new ExtractionMapDefinition("map", "room", 300, 1, true),
                new ExtractionRaidStartRequest("raid", 1, 100));
            profile.activeRaidId = "raid";
            profile.ActiveRaidInventory = new ExtractionRaidInventoryState(4, 3, 2, 2);
            profile.ActiveRaid.Content.LootManifest = new ExtractionRaidLootManifest();
            container = new ExtractionRaidContainerManifest("box", "region", "type", 12, 3, 3, 1f)
            { Opened = true, Columns = 4, Rows = 3, LayoutVersion = 1 };
            container.Entries.Add(new ExtractionContainerLootEntry("med-entry", "med", "medicine", 1, ExtractionItemRarity.Common, false)
            { State = ExtractionContainerLootEntryState.Revealed, RevealOrder = 0, Layout = new ExtractionItemPlacement("med-entry", 0, 0, 1, 1, false) });
            container.Entries.Add(new ExtractionContainerLootEntry("gear-entry", "armor", "gear", 1, ExtractionItemRarity.Common, false)
            { State = ExtractionContainerLootEntryState.Revealed, RevealOrder = 1, Layout = new ExtractionItemPlacement("gear-entry", 1, 0, 2, 1, false) });
            container.Entries.Add(new ExtractionContainerLootEntry("hidden-entry", "hidden", "secret", 1, ExtractionItemRarity.Common, false)
            { RevealOrder = 2, Layout = new ExtractionItemPlacement("hidden-entry", 3, 0, 1, 2, false) });
            container.SearchState.CurrentRevealOrder = 2; container.SearchState.CurrentEntryElapsedSeconds = 0.4f;
            profile.ActiveRaid.Content.LootManifest.Containers.Add(container);
            var cargo = new ExtractionItemInstance("carried", "cargo", 1);
            config.TryGetItemDefinition("cargo", out var cargoDefinition);
            ExtractionItemActionPolicyService.ApplyDefinitionPolicyToInstance(cargoDefinition, cargo);
            profile.Items.Register(cargo); profile.Ownership.Register(cargo.InstanceId, ExtractionInventoryContainerType.RaidBackpack);
            profile.ActiveRaidInventory.RaidBackpack.TryPlace(cargo, cargoDefinition, 0, 0, false);
            return profile;
        }

        [Test] public void HiddenEntry_CannotPreviewOperateOrTake_AndKeepsItsReservedSpace()
        {
            var profile = Create(out var config, out _); string before = ExtractionProfileSerialization.ToJson(profile);
            Assert.IsFalse(ExtractionContainerInventoryService.TryGetRevealedItem(profile, config, "hidden", out _, out _, out _));
            Assert.IsFalse(ExtractionContainerInventoryService.TryMaterialize(profile, config, "hidden"));
            Assert.IsFalse(ExtractionContainerInventoryService.TryMove(profile, profile.ActiveRaidInventory, config, "hidden",
                ExtractionInventoryContainerType.RaidBackpack, null, 0, 0, false, true, "hidden-take", out _));
            Assert.IsFalse(ExtractionContainerInventoryService.TryMove(profile, profile.ActiveRaidInventory, config, "carried",
                ExtractionInventoryContainerType.RaidContainer, "box", 3, 0, true, false, "overlap", out _));
            Assert.AreEqual(before, ExtractionProfileSerialization.ToJson(profile));
        }

        [Test] public void RotateMoveAndDeposit_PreservesStableIdentityAndSearchProgressAcrossReload()
        {
            var profile = Create(out var config, out var container);
            Assert.IsTrue(ExtractionContainerInventoryService.TryMove(profile, profile.ActiveRaidInventory, config, "carried",
                ExtractionInventoryContainerType.RaidBackpack, null, 2, 1, true, false, "rotate", out _));
            Assert.IsTrue(ExtractionContainerInventoryService.TryMove(profile, profile.ActiveRaidInventory, config, "carried",
                ExtractionInventoryContainerType.RaidContainer, "box", 0, 1, true, false, "deposit", out _));
            Assert.AreEqual(0.4f, container.SearchState.CurrentEntryElapsedSeconds);
            profile = ExtractionProfileSerialization.FromJson(ExtractionProfileSerialization.ToJson(profile));
            Assert.IsTrue(ExtractionContainerInventoryService.TryGetRevealedItem(profile, config, "carried", out var item, out _, out var entry));
            Assert.AreEqual("carried", item.InstanceId); Assert.IsTrue(entry.Layout.Rotated);
            Assert.AreEqual(0, entry.Layout.X); Assert.AreEqual(1, entry.Layout.Y);
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);
            Assert.IsTrue(ExtractionContainerTransferService.TryTransfer(profile, profile.ActiveRaidInventory, config,
                "box", entry.EntryId, ExtractionInventoryContainerType.RaidBackpack, "take-back", out var taken, out _));
            Assert.AreEqual("carried", taken);
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out issue), issue);
        }

        [Test] public void UseFromContainer_ConsumesOneCharge_TransfersWithoutResettingDurability()
        {
            var profile = Create(out var config, out var container);
            Assert.IsTrue(ExtractionContainerInventoryService.TryMaterialize(profile, config, "med"));
            Assert.IsTrue(ExtractionItemUseService.TryConsumeForUse(profile, profile.ActiveRaidInventory, config,
                "med", "dose", out _, out _));
            profile.Items.TryGet("med", out var item); Assert.AreEqual(2, item.CurrentDurability); Assert.AreEqual(1, item.Quantity);
            Assert.AreEqual(ExtractionContainerLootEntryState.Revealed, container.Entries[0].State);
            Assert.IsTrue(ExtractionContainerTransferService.TryTransfer(profile, profile.ActiveRaidInventory, config,
                "box", "med-entry", ExtractionInventoryContainerType.RaidBackpack, "take", out _, out _));
            Assert.IsTrue(ExtractionContainerInventoryService.TryMove(profile, profile.ActiveRaidInventory, config, "med",
                ExtractionInventoryContainerType.RaidContainer, "box", 0, 0, false, false, "return", out _));
            profile = ExtractionProfileSerialization.FromJson(ExtractionProfileSerialization.ToJson(profile));
            profile.Items.TryGet("med", out item); Assert.AreEqual(2, item.CurrentDurability);
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);
        }

        [Test] public void EquipFromContainer_ReplacesGearIntoSameContainer_WithoutBackpackSpace()
        {
            var profile = Create(out var config, out _);
            config.TryGetItemDefinition("gear", out var definition);
            var old = new ExtractionItemInstance("old-armor", "gear", 1);
            ExtractionItemActionPolicyService.ApplyDefinitionPolicyToInstance(definition, old);
            profile.Items.Register(old);
            profile.Ownership.Register(old.InstanceId, ExtractionInventoryContainerType.EquipmentSlot,
                ExtractionItemLocationService.RaidEquipmentLocationSubtype, "head-1");
            profile.ActiveRaidInventory.Equipment.Slots.Add(new ExtractionEquipmentSlotState("head-1", old.InstanceId));
            var fillerDefinition = new ExtractionItemDefinition("filler", 1, 1, false, 1);
            config.ItemDefinitions.Add(fillerDefinition);
            var backpack = profile.ActiveRaidInventory.RaidBackpack;
            int count = 0;
            while (backpack.TryFindFreeSlot(1, 1, out int fx, out int fy))
            {
                var filler = new ExtractionItemInstance("filler-" + count++, "filler", 1);
                profile.Items.Register(filler); profile.Ownership.Register(filler.InstanceId, ExtractionInventoryContainerType.RaidBackpack);
                Assert.IsTrue(backpack.TryPlace(filler, fillerDefinition, fx, fy, false));
            }
            Assert.IsTrue(ExtractionContainerInventoryService.TryEquip(profile, profile.ActiveRaidInventory, config, "armor",
                "head-1", ExtractionEquipmentSlotType.Head, "equip", out var displaced, out var result), result.ToString());
            Assert.AreEqual(old.InstanceId, displaced);
            Assert.IsTrue(ExtractionContainerInventoryService.TryGetRevealedItem(profile, config, old.InstanceId, out _, out _, out _));
            Assert.IsTrue(profile.ActiveRaidInventory.Equipment.TryGetItem("head-1", out var equipped)); Assert.AreEqual("armor", equipped);
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);
        }

        [Test] public void QuantityUse_UpdatesRevealedCount_ThenRemovesOnlyExhaustedEntry()
        {
            var profile = Create(out var config, out var container);
            config.TryGetItemDefinition("medicine", out var definition);
            definition.ActionPolicy.ConsumptionType = ExtractionItemConsumptionType.Quantity;
            container.Entries[0].Quantity = 2;
            Assert.IsTrue(ExtractionContainerInventoryService.TryMaterialize(profile, config, "med"));
            Assert.IsTrue(ExtractionItemUseService.TryConsumeForUse(profile, profile.ActiveRaidInventory, config, "med", "q1", out _, out _));
            Assert.AreEqual(1, container.Entries[0].Quantity);
            Assert.AreEqual(ExtractionContainerLootEntryState.Revealed, container.Entries[0].State);
            Assert.IsTrue(ExtractionItemUseService.TryConsumeForUse(profile, profile.ActiveRaidInventory, config, "med", "q2", out _, out _));
            Assert.AreEqual(ExtractionContainerLootEntryState.Transferred, container.Entries[0].State);
            Assert.AreEqual(ExtractionInventoryContainerType.Consumed, profile.Ownership.GetRequiredContainer("med"));
            Assert.AreEqual(ExtractionContainerLootEntryState.CommittedHidden, container.Entries[2].State);
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);
        }

        [Test] public void V3Profile_LazyContainerMigrationPreservesHiddenLootAndExistingIds()
        {
            var profile = Create(out var config, out _); profile.SchemaVersion = 3;
            var loaded = ExtractionProfileSerialization.FromJson(ExtractionProfileSerialization.ToJson(profile));
            Assert.AreEqual(ExtractionProfileSaveData.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.IsFalse(loaded.Items.TryGet("med", out _)); Assert.IsFalse(loaded.Items.TryGet("hidden", out _));
            Assert.IsTrue(ExtractionContainerInventoryService.TryGetRevealedItem(loaded, config, "med", out var item, out _, out var entry));
            Assert.AreEqual("med", item.InstanceId); Assert.AreEqual("med-entry", entry.EntryId);
            Assert.IsFalse(loaded.Items.TryGet("med", out _));
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(loaded, out var issue), issue);
        }

        [Test] public void DropAndSettlement_KeepOneOwner_AndRetireOnlyUnclaimedContainerItems()
        {
            var profile = Create(out var config, out _);
            Assert.IsTrue(ExtractionContainerInventoryService.TryMaterialize(profile, config, "med"));
            Assert.IsTrue(ExtractionItemLifecycleService.TryDropInRaid(profile, profile.ActiveRaidInventory, config,
                "med", "world-drop", out _));
            Assert.AreEqual(ExtractionInventoryContainerType.WorldPickup, profile.Ownership.GetRequiredContainer("med"));
            Assert.IsTrue(ExtractionContainerInventoryService.TryMaterialize(profile, config, "armor"));
            Assert.AreEqual(0, ExtractionRaidWorldItemService.ExpireWorldItems(profile, true));
            Assert.AreEqual(2, ExtractionRaidWorldItemService.ExpireWorldItems(profile, false));
            Assert.AreEqual(ExtractionInventoryContainerType.RaidBackpack, profile.Ownership.GetRequiredContainer("carried"));
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);
        }
    }
}
