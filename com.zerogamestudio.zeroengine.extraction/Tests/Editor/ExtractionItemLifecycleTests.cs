using System.Collections.Generic;
using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionItemLifecycleTests
    {
        private ExtractionPlayableConfig config;
        private ExtractionProfileSaveData profile;

        [SetUp]
        public void SetUp()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(true);
            config = new ExtractionPlayableConfig(4, 4, 2, 2);
            profile = ExtractionProfileSaveData.CreateEmpty();
        }

        [TearDown]
        public void TearDown()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(false);
        }

        [Test]
        public void EquipAndUnequip_KeepSingleLocation_EquipmentCountsWeightWithoutGridSpace()
        {
            var weapon = AddDefinition("weapon", 2, 1, 7, ExtractionEquipmentSlotType.Weapon);
            AddItemToGrid("weapon-1", weapon, profile.CarryGrid, ExtractionInventoryContainerType.Loadout, 0, 0);

            Assert.IsTrue(ExtractionEquipmentTransactionService.TryEquip(
                profile,
                null,
                config,
                "weapon-1",
                ExtractionInventoryContainerType.Loadout,
                ExtractionItemLocationService.BaseEquipmentLocationSubtype,
                "weapon-primary",
                ExtractionEquipmentSlotType.Weapon,
                "equip-1",
                out var displaced,
                out var equipResult));

            Assert.AreEqual(ExtractionEquipmentTransactionResult.Succeeded, equipResult);
            Assert.IsNull(displaced);
            Assert.IsFalse(profile.CarryGrid.TryGetPlacement("weapon-1", out _));
            Assert.AreEqual(
                ExtractionInventoryContainerType.EquipmentSlot,
                profile.Ownership.GetRequiredContainer("weapon-1"));
            Assert.AreEqual(
                7,
                ExtractionItemLocationService.CalculateWeight(
                    profile,
                    config,
                    ExtractionInventoryContainerType.EquipmentSlot));
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);

            Assert.IsTrue(ExtractionEquipmentTransactionService.TryUnequip(
                profile,
                null,
                config,
                ExtractionItemLocationService.BaseEquipmentLocationSubtype,
                "weapon-primary",
                ExtractionInventoryContainerType.Loadout,
                "unequip-1",
                out var itemId,
                out var unequipResult));
            Assert.AreEqual("weapon-1", itemId);
            Assert.AreEqual(ExtractionEquipmentTransactionResult.Succeeded, unequipResult);
            Assert.IsTrue(profile.CarryGrid.TryGetPlacement("weapon-1", out _));
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out issue), issue);
        }

        [Test]
        public void Equip_SwapHasNoRoom_FailsWithoutChangingEitherItem()
        {
            profile.CarryGrid = new ExtractionItemGrid(1, 1);
            var small = AddDefinition("small", 1, 1, 1, ExtractionEquipmentSlotType.Weapon);
            var large = AddDefinition("large", 2, 1, 1, ExtractionEquipmentSlotType.Weapon);
            AddItemToGrid("small-1", small, profile.CarryGrid, ExtractionInventoryContainerType.Loadout, 0, 0);
            var equipped = new ExtractionItemInstance("large-1", large.DefinitionId, 1);
            Assert.IsTrue(profile.Items.Register(equipped));
            Assert.IsTrue(profile.Ownership.Register(
                equipped.InstanceId,
                ExtractionInventoryContainerType.EquipmentSlot,
                ExtractionItemLocationService.BaseEquipmentLocationSubtype,
                "weapon-primary"));
            profile.Equipment.Slots.Add(new ExtractionEquipmentSlotState("weapon-primary", equipped.InstanceId));

            Assert.IsFalse(ExtractionEquipmentTransactionService.TryEquip(
                profile,
                null,
                config,
                "small-1",
                ExtractionInventoryContainerType.Loadout,
                ExtractionItemLocationService.BaseEquipmentLocationSubtype,
                "weapon-primary",
                ExtractionEquipmentSlotType.Weapon,
                "swap-no-space",
                out _,
                out var result));

            Assert.AreEqual(ExtractionEquipmentTransactionResult.NoSpace, result);
            Assert.IsTrue(profile.CarryGrid.TryGetPlacement("small-1", out _));
            Assert.IsTrue(profile.Equipment.TryGetItem("weapon-primary", out var equippedId));
            Assert.AreEqual("large-1", equippedId);
        }

        [Test]
        public void Equip_DuplicatePhysicalLocation_IsRejectedBeforeMutation()
        {
            var weapon = AddDefinition("weapon", 1, 1, 1, ExtractionEquipmentSlotType.Weapon);
            AddItemToGrid(
                "weapon-duplicate",
                weapon,
                profile.CarryGrid,
                ExtractionInventoryContainerType.Loadout,
                0,
                0);
            profile.Equipment.Slots.Add(
                new ExtractionEquipmentSlotState("weapon-secondary", "weapon-duplicate"));

            Assert.IsFalse(ExtractionEquipmentTransactionService.TryEquip(
                profile,
                null,
                config,
                "weapon-duplicate",
                ExtractionInventoryContainerType.Loadout,
                ExtractionItemLocationService.BaseEquipmentLocationSubtype,
                "weapon-primary",
                ExtractionEquipmentSlotType.Weapon,
                "equip-conflict",
                out _,
                out var result));

            Assert.AreEqual(ExtractionEquipmentTransactionResult.LocationConflict, result);
            Assert.IsTrue(profile.CarryGrid.TryGetPlacement("weapon-duplicate", out _));
            Assert.AreEqual(
                ExtractionInventoryContainerType.Loadout,
                profile.Ownership.GetRequiredContainer("weapon-duplicate"));
        }

        [Test]
        public void DropThenPickup_PreservesSameInstanceAndGrowthState()
        {
            var weapon = AddDefinition("weapon", 1, 1, 2, ExtractionEquipmentSlotType.Weapon);
            profile.ActiveRaid = CreateRaid();
            profile.ActiveRaidInventory = new ExtractionRaidInventoryState(4, 4, 2, 2);
            var item = AddItemToGrid(
                "weapon-persist",
                weapon,
                profile.ActiveRaidInventory.RaidBackpack,
                ExtractionInventoryContainerType.RaidBackpack,
                0,
                0);
            item.EnhancementLevel = 3;
            item.ForgeTier = 2;
            item.AffixIds.Add("affix-a");

            Assert.IsTrue(ExtractionItemLifecycleService.TryDropInRaid(
                profile,
                profile.ActiveRaidInventory,
                config,
                item.InstanceId,
                "world-1",
                out var dropResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, dropResult);
            Assert.AreEqual(
                ExtractionInventoryContainerType.WorldPickup,
                profile.Ownership.GetRequiredContainer(item.InstanceId));

            Assert.IsTrue(ExtractionItemLifecycleService.TryPickupWorldItem(
                profile,
                profile.ActiveRaidInventory,
                config,
                item.InstanceId,
                ExtractionInventoryContainerType.RaidBackpack,
                out var pickupResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, pickupResult);
            Assert.IsTrue(profile.Items.TryGet(item.InstanceId, out var restored));
            Assert.AreEqual(3, restored.EnhancementLevel);
            Assert.AreEqual(2, restored.ForgeTier);
            CollectionAssert.AreEqual(new[] { "affix-a" }, restored.AffixIds);
        }

        [Test]
        public void RaidBackpackAndSecure_MovePreservesSingleLocationAndPolicy()
        {
            profile.ActiveRaid = CreateRaid();
            profile.ActiveRaidInventory = new ExtractionRaidInventoryState(4, 4, 2, 2);
            var allowed = AddDefinition("secure-allowed", 1, 1, 1);
            var denied = AddDefinition("secure-denied", 1, 1, 1);
            denied.ActionPolicy.CanPlaceInSecure = false;
            AddItemToGrid(
                "allowed-1",
                allowed,
                profile.ActiveRaidInventory.RaidBackpack,
                ExtractionInventoryContainerType.RaidBackpack,
                0,
                0);
            AddItemToGrid(
                "denied-1",
                denied,
                profile.ActiveRaidInventory.RaidBackpack,
                ExtractionInventoryContainerType.RaidBackpack,
                1,
                0);

            Assert.IsTrue(ExtractionItemLifecycleService.TryMoveRaidCarriedItem(
                profile,
                profile.ActiveRaidInventory,
                config,
                "allowed-1",
                ExtractionInventoryContainerType.InSecureContainer,
                out var secureResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, secureResult);
            Assert.IsFalse(profile.ActiveRaidInventory.RaidBackpack.TryGetPlacement("allowed-1", out _));
            Assert.IsTrue(profile.ActiveRaidInventory.SecureContainer.TryGetPlacement("allowed-1", out _));
            Assert.AreEqual(
                ExtractionInventoryContainerType.InSecureContainer,
                profile.Ownership.GetRequiredContainer("allowed-1"));
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);

            Assert.IsFalse(ExtractionItemLifecycleService.TryMoveRaidCarriedItem(
                profile,
                profile.ActiveRaidInventory,
                config,
                "denied-1",
                ExtractionInventoryContainerType.InSecureContainer,
                out var deniedResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.PolicyDenied, deniedResult);
            Assert.IsTrue(profile.ActiveRaidInventory.RaidBackpack.TryGetPlacement("denied-1", out _));

            Assert.IsTrue(ExtractionItemLifecycleService.TryMoveRaidCarriedItem(
                profile,
                profile.ActiveRaidInventory,
                config,
                "allowed-1",
                ExtractionInventoryContainerType.RaidBackpack,
                out var backpackResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, backpackResult);
            Assert.IsTrue(profile.ActiveRaidInventory.RaidBackpack.TryGetPlacement("allowed-1", out _));
            Assert.IsFalse(profile.ActiveRaidInventory.SecureContainer.TryGetPlacement("allowed-1", out _));
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out issue), issue);
        }

        [Test]
        public void BaseStashCarryAndSecure_MovePreservesSingleLocationAndPolicy()
        {
            var allowed = AddDefinition("base-allowed", 1, 1, 1);
            var denied = AddDefinition("base-denied", 1, 1, 1);
            denied.ActionPolicy.CanPlaceInSecure = false;
            AddItemToGrid(
                "base-allowed-1",
                allowed,
                profile.Stash,
                ExtractionInventoryContainerType.Stash,
                0,
                0);
            AddItemToGrid(
                "base-denied-1",
                denied,
                profile.Stash,
                ExtractionInventoryContainerType.Stash,
                1,
                0);

            Assert.IsTrue(ExtractionItemLifecycleService.TryMoveBaseStoredItem(
                profile,
                config,
                "base-allowed-1",
                ExtractionInventoryContainerType.Loadout,
                out var carryResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, carryResult);
            Assert.IsTrue(profile.CarryGrid.TryGetPlacement("base-allowed-1", out _));

            Assert.IsTrue(ExtractionItemLifecycleService.TryMoveBaseStoredItem(
                profile,
                config,
                "base-allowed-1",
                ExtractionInventoryContainerType.SecureContainer,
                out var secureResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, secureResult);
            Assert.IsTrue(profile.SecureContainer.TryGetPlacement("base-allowed-1", out _));
            Assert.IsFalse(profile.CarryGrid.TryGetPlacement("base-allowed-1", out _));

            Assert.IsFalse(ExtractionItemLifecycleService.TryMoveBaseStoredItem(
                profile,
                config,
                "base-denied-1",
                ExtractionInventoryContainerType.SecureContainer,
                out var deniedResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.PolicyDenied, deniedResult);
            Assert.IsTrue(profile.Stash.TryGetPlacement("base-denied-1", out _));
            Assert.IsTrue(ExtractionItemLocationService.TryValidate(profile, out var issue), issue);
        }

        [Test]
        public void QuestItem_CannotDropOrDestroy_ButAuthorizedTaskSubmissionConsumesIt()
        {
            var quest = AddDefinition("quest", 1, 1, 1);
            ExtractionItemActionPresetService.TryApply(quest, ExtractionItemActionPreset.QuestKeepOnDeath);
            AddItemToGrid("quest-1", quest, profile.Stash, ExtractionInventoryContainerType.Stash, 0, 0);

            Assert.IsFalse(ExtractionItemLifecycleService.TryDestroyAtBase(
                profile, config, "quest-1", true, out var destroyResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.PolicyDenied, destroyResult);
            Assert.IsTrue(ExtractionItemLifecycleService.TrySubmitToTask(
                profile, null, config, "quest-1", "task-a", out var submitResult));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, submitResult);
            Assert.AreEqual(
                ExtractionInventoryContainerType.Consumed,
                profile.Ownership.GetRequiredContainer("quest-1"));
        }

        [Test]
        public void Use_QuantityReceiptAppliesOnce_AndLastUseMovesToConsumed()
        {
            var consumable = AddDefinition("med", 1, 1, 1);
            consumable.ActionPolicy = ExtractionItemActionPolicy.CreateDefaultLoot();
            consumable.ActionPolicy.CanUse = true;
            consumable.ActionPolicy.UseActionId = "heal";
            consumable.ActionPolicy.ConsumptionType = ExtractionItemConsumptionType.Quantity;
            var item = AddItemToGrid("med-1", consumable, profile.Stash, ExtractionInventoryContainerType.Stash, 0, 0);
            item.Quantity = 1;

            Assert.IsTrue(ExtractionItemUseService.TryConsumeForUse(
                profile, null, config, item.InstanceId, "use-1", out var actionId, out var first));
            Assert.AreEqual("heal", actionId);
            Assert.AreEqual(ExtractionItemUseResult.Succeeded, first);
            Assert.AreEqual(
                ExtractionInventoryContainerType.Consumed,
                profile.Ownership.GetRequiredContainer(item.InstanceId));

            Assert.IsTrue(ExtractionItemUseService.TryConsumeForUse(
                profile, null, config, item.InstanceId, "use-1", out _, out var replay));
            Assert.AreEqual(ExtractionItemUseResult.AlreadyApplied, replay);
            Assert.AreEqual(1, profile.ItemActionReceiptIds.Count);
        }

        [Test]
        public void Use_WorldLocation_IsRejectedWithoutConsumption()
        {
            var consumable = AddDefinition("consumable-location", 1, 1, 1);
            consumable.ActionPolicy.CanUse = true;
            consumable.ActionPolicy.UseActionId = "test.use";
            consumable.ActionPolicy.ConsumptionType = ExtractionItemConsumptionType.Quantity;
            var item = new ExtractionItemInstance("consumable-world", consumable.DefinitionId, 2);
            Assert.IsTrue(profile.Items.Register(item));
            Assert.IsTrue(profile.Ownership.Register(
                item.InstanceId,
                ExtractionInventoryContainerType.WorldPickup,
                "world-pickup",
                "pickup-1"));

            Assert.IsFalse(ExtractionItemUseService.TryConsumeForUse(
                profile,
                null,
                config,
                item.InstanceId,
                "use-world",
                out _,
                out var result));

            Assert.AreEqual(ExtractionItemUseResult.LocationConflict, result);
            Assert.AreEqual(2, item.Quantity);
            Assert.IsFalse(profile.ItemActionReceiptIds.Contains("use-world"));
        }

        [Test]
        public void DeathPolicy_SecureAndKeepItemsSurvive_DropItemMovesToCorpse_RaidBoundDestroys()
        {
            profile.ActiveRaid = CreateRaid();
            profile.ActiveRaidInventory = new ExtractionRaidInventoryState(5, 5, 2, 2);
            var drop = AddDefinition("drop", 1, 1, 1);
            var keep = AddDefinition("keep", 1, 1, 1);
            keep.ActionPolicy.DropOnDeath = false;
            var secure = AddDefinition("secure", 1, 1, 1);
            var bound = AddDefinition("bound", 1, 1, 1);
            bound.ActionPolicy.RaidBound = true;
            AddItemToGrid("drop-1", drop, profile.ActiveRaidInventory.RaidBackpack, ExtractionInventoryContainerType.RaidBackpack, 0, 0);
            AddItemToGrid("keep-1", keep, profile.ActiveRaidInventory.RaidBackpack, ExtractionInventoryContainerType.RaidBackpack, 1, 0);
            AddItemToGrid("secure-1", secure, profile.ActiveRaidInventory.SecureContainer, ExtractionInventoryContainerType.InSecureContainer, 0, 0);
            AddItemToGrid("bound-1", bound, profile.ActiveRaidInventory.RaidBackpack, ExtractionInventoryContainerType.RaidBackpack, 2, 0);

            Assert.IsTrue(ExtractionItemLifecycleService.TryApplyDeathPolicy(
                profile, profile.ActiveRaidInventory, config, "corpse-a", out var result));
            Assert.AreEqual(ExtractionItemLifecycleResult.Succeeded, result);
            Assert.AreEqual(ExtractionInventoryContainerType.Corpse, profile.Ownership.GetRequiredContainer("drop-1"));
            Assert.AreEqual(ExtractionInventoryContainerType.Holding, profile.Ownership.GetRequiredContainer("keep-1"));
            Assert.AreEqual(ExtractionInventoryContainerType.Holding, profile.Ownership.GetRequiredContainer("secure-1"));
            Assert.AreEqual(ExtractionInventoryContainerType.Destroyed, profile.Ownership.GetRequiredContainer("bound-1"));
            Assert.IsTrue(profile.RecoveryHolding.TryGetPlacement("keep-1", out _));
            Assert.IsTrue(profile.RecoveryHolding.TryGetPlacement("secure-1", out _));
        }

        private ExtractionItemDefinition AddDefinition(
            string id,
            int width,
            int height,
            int weight,
            ExtractionEquipmentSlotType slot = ExtractionEquipmentSlotType.None)
        {
            var definition = new ExtractionItemDefinition(id, width, height, true, 1)
            {
                Weight = weight
            };
            if (slot != ExtractionEquipmentSlotType.None)
            {
                definition.ActionPolicy.CanEquip = true;
                definition.ActionPolicy.EquipmentSlotType = slot;
                definition.ActionPolicy.EffectAdapterId = "adapter." + id;
            }
            config.ItemDefinitions.Add(definition);
            return definition;
        }

        private ExtractionItemInstance AddItemToGrid(
            string instanceId,
            ExtractionItemDefinition definition,
            ExtractionItemGrid grid,
            ExtractionInventoryContainerType container,
            int x,
            int y)
        {
            var item = new ExtractionItemInstance(instanceId, definition.DefinitionId, 1);
            Assert.IsTrue(profile.Items.Register(item));
            Assert.IsTrue(grid.TryPlace(item, definition, x, y, false));
            Assert.IsTrue(profile.Ownership.Register(instanceId, container));
            Assert.IsTrue(profile.Items.TryGet(instanceId, out var stored));
            return stored;
        }

        private static ExtractionRaidSession CreateRaid()
        {
            var map = new ExtractionMapDefinition("map", "room", 300, 1, true);
            return new ExtractionRaidSession(map, new ExtractionRaidStartRequest("raid", 1, 1));
        }
    }
}
