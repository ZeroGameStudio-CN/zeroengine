using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilityAuthoringWorkbenchTests
    {
        [SetUp]
        public void SetUp()
        {
            AbilityAuthoringRegistry.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AbilityAuthoringRegistry.ClearForTests();
        }

        [Test]
        public void RefreshFromProviders_CanIncludeTestOnlyProviderWhenRequested()
        {
            AbilityAuthoringRegistry.RefreshFromProviders(includeTestProviders: true);

            var profile = AbilityAuthoringRegistry.GetProfile("ZE_TEST_ABILITY");
            Assert.NotNull(profile);
            Assert.AreEqual("测试 Ability 工作台", profile.Title);
            Assert.That(profile.Adapter.GetAssets().Select(record => record.Id), Does.Contain("test-ability"));
        }

        [Test]
        public void GetProfiles_WhenEmpty_DoesNotExposeTestOnlyProviders()
        {
            var profiles = AbilityAuthoringRegistry.GetProfiles();

            Assert.That(profiles.Select(profile => profile.ProfileId), Does.Not.Contain("ZE_TEST_ABILITY"));
        }

        [Test]
        public void TestAdapter_CreatesSearchableValidationRecords()
        {
            var adapter = new TestAbilityAuthoringAdapter();

            var record = adapter.GetAssets().Single();

            Assert.True(adapter.MatchesSearch(record, "test"));
            Assert.True(adapter.MatchesSearch(record, "测试"));
            Assert.True(adapter.ValidateAsset(record.Asset).Succeeded);
            Assert.That(adapter.GetCreateAssetPath(), Is.EqualTo("Assets/Tests/NewAbility.asset"));
        }

        private sealed class TestAbilityAsset : ScriptableObject
        {
            public AbilityDefinition ability = new()
            {
                AbilityId = "test-ability",
                DisplayName = "测试能力"
            };
        }

        private sealed class TestAbilityAuthoringAdapter : IAbilityAuthoringAssetAdapter
        {
            private readonly TestAbilityAsset _asset;

            public TestAbilityAuthoringAdapter()
            {
                _asset = ScriptableObject.CreateInstance<TestAbilityAsset>();
            }

            public string DefaultAssetFolder => "Assets/Tests";

            public string GetCreateAssetPath()
            {
                return "Assets/Tests/NewAbility.asset";
            }

            public System.Collections.Generic.IReadOnlyList<AbilityAuthoringAssetRecord> GetAssets()
            {
                return new[]
                {
                    new AbilityAuthoringAssetRecord(_asset, "Assets/Tests/TestAbility.asset", "test-ability", "测试能力", "测试", null)
                };
            }

            public bool MatchesSearch(AbilityAuthoringAssetRecord record, string search)
            {
                return record.MatchesSearch(search);
            }

            public Object CreateAsset()
            {
                return ScriptableObject.CreateInstance<TestAbilityAsset>();
            }

            public Object DuplicateAsset(Object source)
            {
                return Object.Instantiate(source);
            }

            public void PrepareAsset(Object asset)
            {
            }

            public SerializedProperty FindAbilityProperty(SerializedObject serializedObject)
            {
                return serializedObject.FindProperty("ability");
            }

            public void DrawProjectSections(SerializedObject serializedObject, Object asset)
            {
            }

            public AbilityAuthoringValidationResult ValidateAsset(Object asset)
            {
                return AbilityAuthoringValidationResult.Success("ok");
            }
        }

        private static class TestAbilityAuthoringProvider
        {
            [AbilityAuthoringProvider(testOnly: true)]
            public static AbilityAuthoringProfile CreateProfile()
            {
                return new AbilityAuthoringProfile(
                    "ZE_TEST_ABILITY",
                    "测试 Ability 工作台",
                    new TestAbilityAuthoringAdapter());
            }
        }
    }
}
