using System.Collections.Generic;
using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionWeaponGrowthServiceTests
    {
        private ExtractionPlayableConfig config;
        private ExtractionProfileSaveData profile;
        private ExtractionWeaponGrowthDefinition growth;

        [SetUp]
        public void SetUp()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(true);
            config = new ExtractionPlayableConfig(4, 4, 2, 2);
            profile = ExtractionProfileSaveData.CreateEmpty();

            var weaponDefinition = new ExtractionItemDefinition("weapon", 1, 1, false, 1);
            weaponDefinition.ActionPolicy.CanEquip = true;
            weaponDefinition.ActionPolicy.EquipmentSlotType = ExtractionEquipmentSlotType.Weapon;
            weaponDefinition.ActionPolicy.EffectAdapterId = "weapon-adapter";
            var materialDefinition = new ExtractionItemDefinition("material", 1, 1, false, 99);
            config.ItemDefinitions.Add(weaponDefinition);
            config.ItemDefinitions.Add(materialDefinition);

            AddItem("weapon-1", weaponDefinition, 1, 0);
            AddItem("material-1", materialDefinition, 4, 1);

            growth = new ExtractionWeaponGrowthDefinition { ItemDefinitionId = "weapon" };
            var enhance = new ExtractionWeaponGrowthStepDefinition(1);
            enhance.Costs.Add(new ExtractionItemCost("material", 2));
            growth.EnhancementSteps.Add(enhance);
            var forge = new ExtractionWeaponGrowthStepDefinition(1);
            forge.Costs.Add(new ExtractionItemCost("material", 1));
            growth.ForgeSteps.Add(forge);
            var affix = new ExtractionWeaponAffixDefinition("affix-a");
            affix.Costs.Add(new ExtractionItemCost("material", 1));
            growth.Affixes.Add(affix);
        }

        [TearDown]
        public void TearDown()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(false);
        }

        [Test]
        public void EnhanceForgeAndAffix_KeepInstanceIdAndApplyReceiptsOnce()
        {
            var sources = new List<ExtractionInventoryContainerType> { ExtractionInventoryContainerType.Stash };
            Assert.IsTrue(ExtractionWeaponGrowthService.TryEnhance(
                profile, config, growth, "weapon-1", sources, "enhance-1", out var enhanceResult));
            Assert.AreEqual(ExtractionWeaponGrowthResult.Succeeded, enhanceResult);

            Assert.IsTrue(ExtractionWeaponGrowthService.TryEnhance(
                profile, config, growth, "weapon-1", sources, "enhance-1", out var replay));
            Assert.AreEqual(ExtractionWeaponGrowthResult.AlreadyApplied, replay);
            Assert.IsTrue(ExtractionWeaponGrowthService.TryForge(
                profile, config, growth, "weapon-1", sources, "forge-1", out _));
            Assert.IsTrue(ExtractionWeaponGrowthService.TryReplaceAffix(
                profile, config, growth, "weapon-1", 0, "affix-a", true, sources, "affix-1", out _));

            Assert.IsTrue(profile.Items.TryGet("weapon-1", out var weapon));
            Assert.AreEqual("weapon-1", weapon.InstanceId);
            Assert.AreEqual(1, weapon.EnhancementLevel);
            Assert.AreEqual(1, weapon.ForgeTier);
            CollectionAssert.AreEqual(new[] { "affix-a" }, weapon.AffixIds);
            Assert.IsFalse(
                profile.Items.TryGet("material-1", out _),
                "fully consumed material is removed from the registry");
        }

        [Test]
        public void Enhance_InsufficientMaterials_DoesNotChangeWeapon()
        {
            Assert.IsTrue(profile.Items.TryGet("material-1", out var material));
            material.Quantity = 1;
            var sources = new List<ExtractionInventoryContainerType> { ExtractionInventoryContainerType.Stash };

            Assert.IsFalse(ExtractionWeaponGrowthService.TryEnhance(
                profile, config, growth, "weapon-1", sources, "enhance-fail", out var result));

            Assert.AreEqual(ExtractionWeaponGrowthResult.InsufficientMaterials, result);
            Assert.IsTrue(profile.Items.TryGet("weapon-1", out var weapon));
            Assert.AreEqual(0, weapon.EnhancementLevel);
            Assert.AreEqual(1, material.Quantity);
            Assert.IsFalse(profile.ItemActionReceiptIds.Contains("enhance-fail"));
        }

        [Test]
        public void ReplaceAffix_NotConfirmed_ConsumesNothing()
        {
            Assert.IsTrue(profile.Items.TryGet("material-1", out var material));
            int before = material.Quantity;

            Assert.IsFalse(ExtractionWeaponGrowthService.TryReplaceAffix(
                profile,
                config,
                growth,
                "weapon-1",
                0,
                "affix-a",
                false,
                new List<ExtractionInventoryContainerType> { ExtractionInventoryContainerType.Stash },
                "affix-cancel",
                out var result));

            Assert.AreEqual(ExtractionWeaponGrowthResult.Cancelled, result);
            Assert.AreEqual(before, material.Quantity);
        }

        private void AddItem(
            string instanceId,
            ExtractionItemDefinition definition,
            int quantity,
            int x)
        {
            var item = new ExtractionItemInstance(instanceId, definition.DefinitionId, quantity);
            Assert.IsTrue(profile.Items.Register(item));
            Assert.IsTrue(profile.Stash.TryPlace(item, definition, x, 0, false));
            Assert.IsTrue(profile.Ownership.Register(instanceId, ExtractionInventoryContainerType.Stash));
        }
    }
}
