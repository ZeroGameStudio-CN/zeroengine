using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    public sealed class FormulaEditorProfileTests
    {
        [SetUp]
        public void SetUp()
        {
            FormulaEditorProfileRegistry.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            FormulaEditorProfileRegistry.ClearForTests();
        }

        [Test]
        public void Profile_FindProvider_ReturnsRegisteredProvider()
        {
            var providers = new[]
            {
                new FormulaProviderDescriptor(
                    "resource.coin",
                    "金币",
                    "资源",
                    "玩家当前金币数量",
                    100f,
                    Array.Empty<FormulaParameterDescriptor>()),
            };

            var previewInputs = new[]
            {
                new FormulaPreviewInputDescriptor(
                    "level",
                    "等级",
                    FormulaPreviewInputKind.Int,
                    1f,
                    "玩家等级"),
            };

            var profile = new FormulaEditorProfile(
                "test",
                "测试公式",
                "Assets/Data/Formulas",
                "ZGS/工具/公式工作台",
                "测试公式工作台",
                providers,
                previewInputs);

            Assert.IsTrue(profile.TryGetProvider("resource.coin", out var descriptor));
            Assert.AreEqual("金币", descriptor.DisplayName);
            Assert.AreEqual("玩家当前金币数量", descriptor.Description);
        }

        [Test]
        public void Registry_RegisterAndSetActive_ReturnsActiveProfile()
        {
            var profile = FormulaEditorProfile.CreateEmpty("test", "测试公式");

            FormulaEditorProfileRegistry.Register(profile);
            FormulaEditorProfileRegistry.SetActiveProfile("test");

            Assert.AreSame(profile, FormulaEditorProfileRegistry.ActiveProfile);
        }

        [Test]
        public void Registry_RegisterDuplicateId_Throws()
        {
            FormulaEditorProfileRegistry.Register(FormulaEditorProfile.CreateEmpty("test", "测试公式"));

            Assert.Throws<InvalidOperationException>(
                () => FormulaEditorProfileRegistry.Register(FormulaEditorProfile.CreateEmpty("test", "测试公式 2")));
        }

        [Test]
        public void Registry_RegisterEmptyProfileId_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => FormulaEditorProfileRegistry.Register(FormulaEditorProfile.CreateEmpty(string.Empty, "测试公式")));
        }

        [Test]
        public void Registry_SetActiveUnknownId_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => FormulaEditorProfileRegistry.SetActiveProfile("missing"));
        }

        [Test]
        public void Labels_TranslateFormulaOperations()
        {
            Assert.AreEqual("加", FormulaEditorLabels.OperationName(FormulaOperationType.Add));
            Assert.AreEqual("乘以系数", FormulaEditorLabels.OperationName(FormulaOperationType.MultiplyFactor));
            Assert.AreEqual("上下文变量", FormulaEditorLabels.SourceTypeName(FormulaValueSourceType.Provider));
        }

        [Test]
        public void Constructors_NormalizeNullStrings_ToEmpty()
        {
            var parameter = new FormulaParameterDescriptor(
                null,
                null,
                FormulaEditorParameterKind.String,
                false,
                null,
                defaultStringValue: null);
            var provider = new FormulaProviderDescriptor(null, null, null, null, 0f, null);
            var input = new FormulaPreviewInputDescriptor(null, null, FormulaPreviewInputKind.Float, 0f, null);
            var profile = new FormulaEditorProfile(null, null, null, null, null, null, null);

            Assert.AreEqual(string.Empty, parameter.Key);
            Assert.AreEqual(string.Empty, parameter.DisplayName);
            Assert.AreEqual(string.Empty, parameter.Description);
            Assert.AreEqual(string.Empty, parameter.DefaultStringValue);
            Assert.AreEqual(string.Empty, provider.Id);
            Assert.AreEqual(string.Empty, provider.DisplayName);
            Assert.AreEqual(string.Empty, provider.Category);
            Assert.AreEqual(string.Empty, provider.Description);
            Assert.AreEqual(string.Empty, input.Key);
            Assert.AreEqual(string.Empty, input.DisplayName);
            Assert.AreEqual(string.Empty, input.Description);
            Assert.AreEqual(string.Empty, profile.ProfileId);
            Assert.AreEqual(string.Empty, profile.DisplayName);
            Assert.AreEqual(string.Empty, profile.DefaultSearchRoot);
            Assert.AreEqual(string.Empty, profile.WorkbenchMenuPath);
            Assert.AreEqual(string.Empty, profile.WorkbenchTitle);
        }

        [Test]
        public void Constructor_CopiesProviderList()
        {
            var providers = new List<FormulaProviderDescriptor>
            {
                new FormulaProviderDescriptor("resource.coin", "金币", "资源", "玩家当前金币数量", 100f, null),
            };

            var profile = new FormulaEditorProfile(
                "test",
                "测试公式",
                "Assets/Data/Formulas",
                "ZGS/工具/公式工作台",
                "测试公式工作台",
                providers,
                null);

            providers.Clear();

            Assert.AreEqual(1, profile.Providers.Count);
            Assert.AreEqual("resource.coin", profile.Providers[0].Id);
        }

        [Test]
        public void Profile_StoresGovernanceSettings_AsReadOnlyCopies()
        {
            var referenceRoots = new List<string>
            {
                "Assets/Assets/_Data",
                "Assets/AddressableAssetsData",
            };
            var excludedRoots = new List<string>
            {
                "Library",
                "Temp",
            };

            var profile = new FormulaEditorProfile(
                "test",
                "测试公式",
                "Assets/Data/Formulas",
                "ZGS/工具/公式工作台",
                "测试公式工作台",
                null,
                null,
                catalogAssetPath: "Assets/Data/Formulas/FormulaCatalog.asset",
                referenceRoots: referenceRoots,
                excludedReferenceRoots: excludedRoots);

            referenceRoots.Clear();
            excludedRoots.Clear();

            Assert.AreEqual("Assets/Data/Formulas/FormulaCatalog.asset", profile.CatalogAssetPath);
            Assert.AreEqual(2, profile.ReferenceRoots.Count);
            Assert.AreEqual("Assets/Assets/_Data", profile.ReferenceRoots[0]);
            Assert.AreEqual("Assets/AddressableAssetsData", profile.ReferenceRoots[1]);
            Assert.AreEqual(2, profile.ExcludedReferenceRoots.Count);
            Assert.AreEqual("Library", profile.ExcludedReferenceRoots[0]);
            Assert.AreEqual("Temp", profile.ExcludedReferenceRoots[1]);
        }
    }
}
