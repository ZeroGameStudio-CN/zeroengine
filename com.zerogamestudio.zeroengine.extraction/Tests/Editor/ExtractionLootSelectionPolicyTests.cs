using NUnit.Framework;
using UnityEngine;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public sealed class ExtractionLootSelectionPolicyTests
    {
        [SetUp] public void Enable() => ExtractionFeatureSwitch.SetEnabledForTests(true);
        [TearDown] public void Disable() => ExtractionFeatureSwitch.SetEnabledForTests(false);

        [Test]
        public void TwoStage_RarityDoesNotDependOnItemCount()
        {
            var original = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var expanded = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            for (int i = 0; i < 20; i++)
            {
                string id = "extra-common-" + i;
                expanded.ItemDefinitions.Add(new ExtractionItemDefinition(id, 1, 1, false, 1) { Rarity = ExtractionItemRarity.Common });
                expanded.LootTables[0].Entries.Add(new ExtractionLootTableEntry(id, 100, 1, true));
            }
            for (int seed = 0; seed < 64; seed++)
            {
                var first = Open(original, seed, new ExtractionLootSelectionPolicy());
                var second = Open(expanded, seed, new ExtractionLootSelectionPolicy());
                Assert.AreEqual(first.Entries.Count, second.Entries.Count);
                for (int i = 0; i < first.Entries.Count; i++) Assert.AreEqual(first.Entries[i].Rarity, second.Entries[i].Rarity);
            }
        }

        [Test]
        public void Luck_ChangesRarityWhileKeepingSeedDeterministic()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            int low = 0, high = 0;
            for (int seed = 0; seed < 64; seed++)
            {
                foreach (var entry in Open(config, seed, new ExtractionLootSelectionPolicy { Luck = -10000 }).Entries) low += (int)entry.Rarity;
                var first = Open(config, seed, new ExtractionLootSelectionPolicy { Luck = 10000 });
                foreach (var entry in first.Entries) high += (int)entry.Rarity;
                Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(Open(config, seed, new ExtractionLootSelectionPolicy { Luck = 10000 })));
            }
            Assert.Greater(high, low);
        }

        [Test]
        public void NewPolicy_IsCopiedAndSurvivesSave_OpenedContainerNeverRerolls()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var policy = new ExtractionLootSelectionPolicy { Luck = 300 };
            var profile = Profile(config, 77, policy);
            policy.Luck = -1000;
            policy.RarityWeights[0] = 0;
            profile = JsonUtility.FromJson<ExtractionProfileSaveData>(JsonUtility.ToJson(profile));
            Assert.AreEqual(300, profile.ActiveRaid.Content.LootManifest.SelectionPolicy.Luck);
            Assert.AreEqual(1, profile.ActiveRaid.Content.LootManifest.SelectionPolicy.RarityWeights[0]);
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, "spawn-a", out var first, out _));
            string before = JsonUtility.ToJson(first);
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, "spawn-a", out var second, out var result));
            Assert.AreEqual(ExtractionContainerOpenResult.AlreadyOpened, result);
            Assert.AreEqual(before, JsonUtility.ToJson(second));
        }

        [Test]
        public void LegacyManifest_MissingPolicyRemainsLegacyAfterSave()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = Profile(config, 77, null);
            var restored = JsonUtility.FromJson<ExtractionProfileSaveData>(JsonUtility.ToJson(profile));
            Assert.AreEqual(0, restored.ActiveRaid.Content.LootManifest.LootSelectionVersion);
            // Unity JSON may instantiate nested objects; these must not upgrade a legacy raid.
            restored.ActiveRaid.Content.LootManifest.SelectionPolicy = new ExtractionLootSelectionPolicy { Luck = 10000 };
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, "spawn-a", out var first, out _));
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(restored, config, "spawn-a", out var second, out _));
            Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(second));
        }

        [Test]
        public void UnknownVersion_PreservesUnopenedContainerWithoutFallback()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = Profile(config, 77, new ExtractionLootSelectionPolicy());
            var manifest = profile.ActiveRaid.Content.LootManifest;
            manifest.LootSelectionVersion = 99;
            string before = JsonUtility.ToJson(manifest);
            Assert.IsFalse(ExtractionContainerLootService.TryOpen(profile, config, "spawn-a", out _, out var result));
            Assert.AreEqual(ExtractionContainerOpenResult.MissingConfiguration, result);
            Assert.AreEqual(before, JsonUtility.ToJson(manifest));
        }

        [Test]
        public void DisabledAndMissingRaritiesCannotBeSelected()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var policy = new ExtractionLootSelectionPolicy { RarityWeights = new float[] { 0, 1, 0, 99, 99, 99 } };
            foreach (var entry in Open(config, 77, policy).Entries) Assert.AreEqual(ExtractionItemRarity.Uncommon, entry.Rarity);
        }

        private static ExtractionProfileSaveData Profile(ExtractionPlayableConfig config, int seed, ExtractionLootSelectionPolicy policy)
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            var request = new ExtractionRaidStartRequest("raid-" + seed, seed, 1000) { LootSelectionPolicy = policy };
            Assert.IsTrue(ExtractionRaidSessionFactory.TryCreate(profile, config, config.Maps[0], request, false, out _, out var failure), failure.ToString());
            return profile;
        }

        private static ExtractionRaidContainerManifest Open(ExtractionPlayableConfig config, int seed, ExtractionLootSelectionPolicy policy)
        {
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(Profile(config, seed, policy), config, "spawn-a", out var container, out var result), result.ToString());
            return container;
        }
    }
}
