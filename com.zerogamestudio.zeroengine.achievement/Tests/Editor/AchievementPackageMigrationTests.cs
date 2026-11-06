using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Achievement;

namespace ZeroEngine.Tests.Achievement
{
    public sealed class AchievementPackageMigrationTests
    {
        private const string Fixture = "Packages/com.zerogamestudio.zeroengine.achievement/Tests/Editor/Fixtures/LegacyNarrativeAchievement.asset";

        [Test]
        public void LegacyManagedConditionLoadsInStandalonePackage()
        {
            var definition = AssetDatabase.LoadAssetAtPath<AchievementSO>(Fixture);
            Assert.That(definition, Is.Not.Null, "The old script GUID must still resolve.");
            Assert.That(definition.AchievementId, Is.EqualTo("legacy.relic.collect"));
            Assert.That(definition.Conditions.Single(), Is.TypeOf<CounterCondition>());
            var condition = (CounterCondition)definition.Conditions[0];
            Assert.That(condition.EventId, Is.EqualTo("collect"));
            Assert.That(condition.TargetCount, Is.EqualTo(70));
            Assert.That(definition.Rewards.Single(), Is.TypeOf<AchievementPointReward>());
        }

        [Test]
        public void SavedCounterContinuesWithoutCreatingAnExternalProvider()
        {
            var definition = AssetDatabase.LoadAssetAtPath<AchievementSO>(Fixture);
            Assert.That(definition, Is.Not.Null);
            var root = new GameObject("Achievement package migration");
            try
            {
                var manager = root.AddComponent<AchievementManager>();
                manager.RegisterAchievement(definition);
                manager.TriggerEvent("collect", 69);
                Assert.That(manager.IsCompleted(definition), Is.False);
                var save = (AchievementSaveData)manager.ExportSaveData();
                Assert.That(save.ProgressData[definition.AchievementId].ConditionProgress[0], Is.EqualTo(69));

                manager.ResetToDefault();
                manager.ImportSaveData(save);
                var provider = new RecordingProvider();
                manager.RegisterProvider(provider);
                manager.RegisterProvider(provider);
                manager.TriggerEvent("collect");
                manager.TriggerEvent("collect");

                Assert.That(manager.IsCompleted(definition), Is.True);
                Assert.That(provider.InitializeCount, Is.EqualTo(1));
                Assert.That(provider.UnlockCount, Is.EqualTo(1));
                Assert.That(provider.UnlockedId, Is.EqualTo(definition.AchievementId));
                Assert.That(typeof(AchievementManager).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                    Does.Not.Contain("ZeroEngine.Narrative").And.Not.Contain("ZeroEngine.Persistence")
                        .And.Not.Contain("ZeroEngine.Economy").And.Not.Contain("com.rlabrecque.steamworks.net"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private sealed class RecordingProvider : IAchievementProvider
        {
            public int InitializeCount;
            public int UnlockCount;
            public string UnlockedId;
            public bool Initialize() { InitializeCount++; return true; }
            public void Unlock(string id) { UnlockCount++; UnlockedId = id; }
            public void SetProgress(string id, float progress) { }
        }
    }
}
