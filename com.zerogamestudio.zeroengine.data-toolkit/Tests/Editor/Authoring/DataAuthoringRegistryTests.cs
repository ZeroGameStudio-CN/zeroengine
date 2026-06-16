using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            DataAuthoringRegistry.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DataAuthoringRegistry.ClearForTests();
        }

        [Test]
        public void RefreshFromProviders_CanIncludeTestOnlyProviderWhenRequested()
        {
            DataAuthoringRegistry.RefreshFromProviders(includeTestProviders: true);

            var profile = DataAuthoringRegistry.GetProfile("ZE_TEST_DATA_AUTHORING");

            Assert.NotNull(profile);
            Assert.AreEqual("测试数据工作台", profile.Title);
            Assert.That(profile.Adapters.Select(adapter => adapter.GroupId), Does.Contain("TestAssets"));
        }

        [Test]
        public void GetProfiles_WhenEmpty_DoesNotExposeTestOnlyProviders()
        {
            var profiles = DataAuthoringRegistry.GetProfiles();

            Assert.That(profiles.Select(profile => profile.ProfileId), Does.Not.Contain("ZE_TEST_DATA_AUTHORING"));
        }

        [Test]
        public void TestAdapter_CreatesSearchableRecordsAndValidationIssues()
        {
            var adapter = new TestAuthoringAdapter();
            var record = adapter.GetAssets().Single();

            Assert.True(record.MatchesSearch("test"));
            Assert.True(record.MatchesSearch("测试"));
            Assert.That(adapter.Validate(record.Asset).Single().Severity, Is.EqualTo(DataAuthoringIssueSeverity.Warning));
        }

        [Test]
        public void Profile_DefaultLabelsAndActionsAreSafe()
        {
            var profile = new DataAuthoringProfile(
                "TEST_PROFILE",
                "Test Profile",
                new[] { new TestAuthoringAdapter() });

            Assert.NotNull(profile.Labels);
            Assert.NotNull(profile.Actions);
            Assert.AreEqual("Groups", profile.Labels.Groups);
            Assert.AreEqual("Problems", profile.Labels.Problems);
            Assert.AreEqual("Tools", profile.Labels.Tools);
            Assert.AreEqual("Validate Selected", profile.Labels.ValidateSelected);
            Assert.IsNull(profile.Actions.OpenIssueDashboard);
            Assert.IsEmpty(profile.Actions.OpenIssueDashboardLabel);
        }

        [Test]
        public void Profile_AcceptsCustomLabelsAndActions()
        {
            var invoked = false;
            var labels = new DataAuthoringWindowLabels
            {
                Groups = "分类",
                Problems = "问题",
                Tools = "工具",
                ValidateSelected = "校验当前"
            };
            var actions = new DataAuthoringWindowActions("打开体检", () => invoked = true);

            var profile = new DataAuthoringProfile(
                "TEST_PROFILE",
                "Test Profile",
                new[] { new TestAuthoringAdapter() },
                labels: labels,
                actions: actions);

            Assert.AreEqual("分类", profile.Labels.Groups);
            Assert.AreEqual("问题", profile.Labels.Problems);
            Assert.AreEqual("工具", profile.Labels.Tools);
            Assert.AreEqual("校验当前", profile.Labels.ValidateSelected);

            profile.Actions.OpenIssueDashboard();
            Assert.True(invoked);
        }

        private sealed class TestAsset : ScriptableObject
        {
            public string id = "test_asset";
            public string displayName = "测试资产";
        }

        private sealed class TestAuthoringAdapter : IDataAuthoringAssetAdapter
        {
            private readonly TestAsset _asset;

            public TestAuthoringAdapter()
            {
                _asset = ScriptableObject.CreateInstance<TestAsset>();
            }

            public string GroupId => "TestAssets";
            public string DisplayName => "测试资产";
            public int Order => 0;

            public IReadOnlyList<DataAuthoringAssetRecord> GetAssets()
            {
                return new[]
                {
                    new DataAuthoringAssetRecord(_asset, "Assets/Tests/Test.asset", _asset.id, _asset.displayName, "测试", null)
                };
            }

            public Object CreateAsset()
            {
                return ScriptableObject.CreateInstance<TestAsset>();
            }

            public Object DuplicateAsset(Object source)
            {
                return Object.Instantiate(source);
            }

            public void DrawInspector(Object asset)
            {
            }

            public IReadOnlyList<DataAuthoringIssue> Validate(Object asset)
            {
                return new[]
                {
                    DataAuthoringIssue.Warning("Assets/Tests/Test.asset", "TestAsset", "test_asset", "id", "test warning")
                };
            }

            public void AddExportSheets(TabularWorkbook workbook)
            {
            }
        }

        private static class TestProvider
        {
            [DataAuthoringProvider(testOnly: true)]
            public static DataAuthoringProfile CreateProfile()
            {
                return new DataAuthoringProfile(
                    "ZE_TEST_DATA_AUTHORING",
                    "测试数据工作台",
                    new IDataAuthoringAssetAdapter[] { new TestAuthoringAdapter() });
            }
        }
    }
}
