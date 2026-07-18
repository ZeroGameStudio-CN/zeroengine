using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionLootContentConfigValidatorTests
    {
        [Test]
        public void Validate_CompleteContentConfig_PassesWithoutWarnings()
        {
            var report = ExtractionLootContentConfigValidator.Validate(CreateValidConfig());

            Assert.IsTrue(report.IsValid, report.FirstError);
            Assert.IsEmpty(report.Warnings);
        }

        [Test]
        public void Validate_DuplicateRegionId_ReturnsChineseError()
        {
            var config = CreateValidConfig();
            config.LootRegions.Add(CreateRegion("region-a"));

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("区域 ID", report.FirstError);
            StringAssert.Contains("重复", report.FirstError);
        }

        [Test]
        public void Validate_ContainerMaximumExceedsCapacity_ReturnsError()
        {
            var config = CreateValidConfig();
            config.ContainerDefinitions[0].MaximumContentCount = 3;

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("容量", report.FirstError);
        }

        [Test]
        public void Validate_SpawnCandidateReferencesMissingContainer_ReturnsError()
        {
            var config = CreateValidConfig();
            config.ContainerSpawns[0].Candidates[0].ContainerTypeId = "missing-container";

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("不存在的容器类型", report.FirstError);
        }

        [Test]
        public void Validate_SpawnEnablesAlwaysAndChancePerRaid_ReturnsError()
        {
            var config = CreateValidConfig();
            config.ContainerSpawns[0].ChancePerRaid = true;

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("Always 与 ChancePerRaid", report.FirstError);
        }

        [Test]
        public void Validate_ChancePerRaidOutsideUnitInterval_ReturnsError()
        {
            var config = CreateValidConfig();
            var spawn = config.ContainerSpawns[0];
            spawn.Always = false;
            spawn.ChancePerRaid = true;
            spawn.Chance = 0f;

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("出现率", report.FirstError);
        }

        [Test]
        public void Validate_RareLootDisabledLeavesNoCandidate_ReturnsError()
        {
            var config = CreateValidConfig();
            config.ContainerDefinitions[0].LootTableIds.Remove("common-table");

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("关闭珍品", report.FirstError);
        }

        [Test]
        public void Validate_RevealTimesNotStrictlyIncreasing_ReturnsError()
        {
            var config = CreateValidConfig();
            config.LootProfiles[0].RevealTimes.BaseRevealSeconds.Epic = 1.2f;

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("严格递增", report.FirstError);
        }

        [Test]
        public void Validate_MinimumGeneratedDropsExceedGuaranteedCapacity_ReturnsError()
        {
            var config = CreateValidConfig();
            config.LootProfiles[0].MinimumGeneratedDropsByRarity.Common = 2;
            config.LootProfiles[0].MinimumGeneratedDropsByRarity.Rare = 1;

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("最低生成数", report.FirstError);
            StringAssert.Contains("固定容器容量", report.FirstError);
        }

        [Test]
        public void Validate_MinimumRarityHasNoReachableItem_ReturnsError()
        {
            var config = CreateValidConfig();
            config.LootProfiles[0].MinimumGeneratedDropsByRarity.Epic = 1;

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("Epic", report.FirstError);
            StringAssert.Contains("合法物品", report.FirstError);
        }

        [Test]
        public void Validate_MinimumRarityOnlyExistsInOneWeightedCandidate_ReturnsError()
        {
            var config = CreateValidConfig();
            var commonOnly = new ExtractionContainerDefinition("common-only", 2, 1, 2, 1f);
            commonOnly.LootTableIds.Add("common-table");
            config.ContainerDefinitions.Add(commonOnly);
            config.LootRegions[0].AllowedContainerTypeIds.Add("common-only");
            config.ContainerSpawns[0].Candidates.Add(
                new ExtractionWeightedContainerCandidate("common-only", 1));

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("Rare", report.FirstError);
            StringAssert.Contains("所有候选结果", report.FirstError);
            StringAssert.Contains("只能承载 0 个", report.FirstError);
        }

        [Test]
        public void Validate_UniformRarityCurve_ProducesIneffectiveWarning()
        {
            var config = CreateValidConfig();
            config.ContentTiers[0].RarityWeightMultipliers =
                new ExtractionRarityFloatValues(2f, 2f, 2f, 2f, 2f, 2f);

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsTrue(report.IsValid, report.FirstError);
            Assert.That(report.Warnings, Has.Count.EqualTo(1));
            StringAssert.Contains("统一倍率", report.Warnings[0]);
            StringAssert.Contains("不会改变", report.Warnings[0]);
            StringAssert.Contains("掉落概率", report.Warnings[0]);
        }

        [Test]
        public void ContentPolicy_OnlyRareLootDisabledChangesRewards()
        {
            var profile = CreateValidConfig().LootProfiles[0];
            var lenient = new ExtractionDifficultySettings
            {
                CorpseLossTier = ExtractionCorpseLossTier.Lenient,
                EnemyDamageTier = ExtractionEnemyDamageTier.Half
            };
            var hard = new ExtractionDifficultySettings
            {
                CorpseLossTier = ExtractionCorpseLossTier.Default,
                EnemyDamageTier = ExtractionEnemyDamageTier.Hard
            };

            Assert.AreEqual(
                ExtractionLootContentPolicy.GetMinimumGeneratedDrops(
                    profile,
                    ExtractionItemRarity.Rare,
                    lenient.RareLootDisabled),
                ExtractionLootContentPolicy.GetMinimumGeneratedDrops(
                    profile,
                    ExtractionItemRarity.Rare,
                    hard.RareLootDisabled));

            hard.RareLootDisabled = true;
            Assert.IsFalse(ExtractionLootContentPolicy.IsLootTableEnabled(
                CreateTable("rare-only", "rare-item", true),
                hard.RareLootDisabled));
            Assert.AreEqual(
                0,
                ExtractionLootContentPolicy.GetMinimumGeneratedDrops(
                    profile,
                    ExtractionItemRarity.Rare,
                    hard.RareLootDisabled));
            Assert.AreEqual(
                1,
                ExtractionLootContentPolicy.GetMinimumGeneratedDrops(
                    profile,
                    ExtractionItemRarity.Common,
                    hard.RareLootDisabled));
            Assert.IsFalse(ExtractionLootContentPolicy.IsPityEnabled(profile, hard.RareLootDisabled));
        }

        private static ExtractionPlayableConfig CreateValidConfig()
        {
            var config = new ExtractionPlayableConfig(6, 4, 2, 2);
            var map = new ExtractionMapDefinition("map-a", "Room", 900, 1, true)
            {
                ContentTierId = "tier-a",
                LootProfileId = "profile-a"
            };
            config.Maps.Add(map);

            config.ItemDefinitions.Add(CreateItem("common-item", ExtractionItemRarity.Common));
            config.ItemDefinitions.Add(CreateItem("rare-item", ExtractionItemRarity.Rare));
            config.LootTables.Add(CreateTable("common-table", "common-item", false));
            config.LootTables.Add(CreateTable("rare-table", "rare-item", true));

            config.ContentTiers.Add(
                new ExtractionContentTierDefinition(
                    "tier-a",
                    new ExtractionRarityFloatValues(1f, 1.05f, 1.15f, 1.3f, 1.5f, 1.8f)));

            var profile = new ExtractionLootProfileDefinition("profile-a", "tier-a")
            {
                MinimumGeneratedDropsByRarity =
                    new ExtractionRarityIntValues(1, 0, 1, 0, 0, 0),
                Pity = new ExtractionLootPityDefinition(ExtractionItemRarity.Rare, 0.25f, 3f),
                RevealTimes = new ExtractionRevealTimeDefinition(
                    new ExtractionRarityFloatValues(0.5f, 0.8f, 1.2f, 1.8f, 2.6f, 3.6f))
            };
            profile.RegionIds.Add("region-a");
            config.LootProfiles.Add(profile);
            config.LootRegions.Add(CreateRegion("region-a"));

            var container = new ExtractionContainerDefinition("supply", 2, 1, 2, 1f);
            container.LootTableIds.Add("common-table");
            container.LootTableIds.Add("rare-table");
            config.ContainerDefinitions.Add(container);

            var spawn = new ExtractionContainerSpawnDefinition("spawn-a", "region-a", true, false, 1f);
            spawn.Candidates.Add(new ExtractionWeightedContainerCandidate("supply", 1));
            config.ContainerSpawns.Add(spawn);
            return config;
        }

        private static ExtractionLootRegionDefinition CreateRegion(string regionId)
        {
            var region = new ExtractionLootRegionDefinition(
                regionId,
                new ExtractionRarityFloatValues(1f, 1.02f, 1.08f, 1.16f, 1.28f, 1.45f));
            region.AllowedContainerTypeIds.Add("supply");
            region.ContainerSpawnIds.Add("spawn-a");
            return region;
        }

        private static ExtractionItemDefinition CreateItem(string id, ExtractionItemRarity rarity)
        {
            return new ExtractionItemDefinition(id, 1, 1, false, 1) { Rarity = rarity };
        }

        private static ExtractionLootTableDefinition CreateTable(string id, string itemId, bool isRare)
        {
            var table = new ExtractionLootTableDefinition(id) { IsRare = isRare };
            table.Entries.Add(new ExtractionLootTableEntry(itemId, 1, 1, false));
            return table;
        }
    }
}
