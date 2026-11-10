using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionItemActionPolicyTests
    {
        // Core contract: serialized legacy enum values remain stable as body slots are added.
        [Test]
        public void EquipmentSlots_PreserveLegacyValues()
        {
            Assert.AreEqual(0, (int)ExtractionEquipmentSlotType.None);
            Assert.AreEqual(1, (int)ExtractionEquipmentSlotType.Weapon);
            Assert.AreEqual(2, (int)ExtractionEquipmentSlotType.Relic);
            Assert.AreEqual(3, (int)ExtractionEquipmentSlotType.Card);
        }

        [TestCase(ExtractionEquipmentSlotType.Head, "head-1")]
        [TestCase(ExtractionEquipmentSlotType.Top, "top-1")]
        [TestCase(ExtractionEquipmentSlotType.Pants, "pants-1")]
        [TestCase(ExtractionEquipmentSlotType.Shoes, "shoes-1")]
        [TestCase(ExtractionEquipmentSlotType.Underwear, "underwear-1")]
        [TestCase(ExtractionEquipmentSlotType.Gloves, "gloves-1")]
        public void BodySlots_AcceptOnlyTheCorrespondingPart(ExtractionEquipmentSlotType type, string slot)
        {
            Assert.IsTrue(ExtractionEquipmentTransactionService.SlotIdMatchesType(slot, type));
            Assert.IsFalse(ExtractionEquipmentTransactionService.SlotIdMatchesType("weapon-primary", type));
            Assert.IsFalse(ExtractionEquipmentTransactionService.SlotIdMatchesType(null, type));
        }

        [Test]
        public void DefaultPolicy_OldAndNewItemsRemainDroppableSellableAndDeathDroppable()
        {
            var definition = new ExtractionItemDefinition("cargo", 1, 1, false, 1);
            definition.ActionPolicy = new ExtractionItemActionPolicy();

            var policy = ExtractionItemActionPolicyService.GetPolicy(definition);

            Assert.IsTrue(policy.PolicyInitialized);
            Assert.IsTrue(policy.CanDrop);
            Assert.IsTrue(policy.CanSell);
            Assert.IsTrue(policy.DropOnDeath);
            Assert.IsTrue(policy.CanPlaceInSecure);
            Assert.IsFalse(policy.CanEquip);
            Assert.IsFalse(policy.CanUse);
        }

        [TestCase(ExtractionItemActionPreset.NormalLoot, true, true, true, false, false)]
        [TestCase(ExtractionItemActionPreset.Equippable, true, true, true, true, false)]
        [TestCase(ExtractionItemActionPreset.Consumable, true, true, true, false, true)]
        [TestCase(ExtractionItemActionPreset.QuestKeepOnDeath, false, false, false, false, false)]
        [TestCase(ExtractionItemActionPreset.QuestDropOnDeath, false, false, true, false, false)]
        [TestCase(ExtractionItemActionPreset.DurableTool, true, true, true, false, true)]
        public void ApplyPreset_WritesExplicitExpectedFields(
            ExtractionItemActionPreset preset,
            bool canDrop,
            bool canSell,
            bool dropOnDeath,
            bool canEquip,
            bool canUse)
        {
            var definition = new ExtractionItemDefinition("item", 1, 1, false, 1);

            Assert.IsTrue(ExtractionItemActionPresetService.TryApply(definition, preset));

            Assert.AreEqual(canDrop, definition.ActionPolicy.CanDrop);
            Assert.AreEqual(canSell, definition.ActionPolicy.CanSell);
            Assert.AreEqual(dropOnDeath, definition.ActionPolicy.DropOnDeath);
            Assert.AreEqual(canEquip, definition.ActionPolicy.CanEquip);
            Assert.AreEqual(canUse, definition.ActionPolicy.CanUse);
            Assert.IsTrue(definition.ActionPolicy.PolicyInitialized);
        }

        [Test]
        public void TaskSubmission_IsIndependentFromCanDrop()
        {
            var definition = new ExtractionItemDefinition("quest", 1, 1, false, 1);
            ExtractionItemActionPresetService.TryApply(
                definition,
                ExtractionItemActionPreset.QuestKeepOnDeath);

            Assert.IsFalse(definition.ActionPolicy.CanDrop);
            Assert.IsTrue(ExtractionItemActionPolicyService.CanSubmitToTask(definition));
        }
    }
}
