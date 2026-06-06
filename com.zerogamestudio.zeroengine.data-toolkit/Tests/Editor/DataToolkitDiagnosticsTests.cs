using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor.Tests
{
    [TestFixture]
    public sealed class DataToolkitDiagnosticsTests
    {
        private const string TestRoot = "Packages/com.zerogamestudio.zeroengine.data-toolkit/Tests/Editor/__DataToolkitDiagnosticsTests";
        private const string AssetPath = TestRoot + "/Selected.asset";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Packages/com.zerogamestudio.zeroengine.data-toolkit/Tests/Editor", "__DataToolkitDiagnosticsTests");
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.SaveAssets();
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
        }

        [Test]
        public void BuildReport_WithCustomInspectorProvider_ReportsFirstClassCoverage()
        {
            CreateTestAsset(AssetPath);
            var context = new DataToolkitContext(CreateSettings(DataToolkitDefaultInspectorMode.LazyPreview));
            var report = DataToolkitDiagnosticsService.BuildReport(
                context,
                new IDataToolkitAssetInspectorProvider[] { new SelectedDataInspectorProvider() });

            var info = report.Types.Single(type => type.Type == typeof(SelectedToolkitTestData));

            Assert.AreEqual("DataToolkitDiagnosticsTests", report.ProjectId);
            Assert.AreEqual(1, info.AssetCount);
            Assert.AreEqual(AssetPath, info.SampleAssetPath);
            Assert.AreEqual(DataToolkitInspectorCoverageLevel.FirstClass, info.CoverageLevel);
            StringAssert.Contains("SelectedDataInspectorProvider", info.Reason);
        }

        [Test]
        public void BuildReport_WithoutCustomProvider_UsesDefaultSafePreviewCoverage()
        {
            CreateTestAsset(AssetPath);
            var context = new DataToolkitContext(CreateSettings(DataToolkitDefaultInspectorMode.LazyPreview));

            var report = DataToolkitDiagnosticsService.BuildReport(context, Array.Empty<IDataToolkitAssetInspectorProvider>());
            var info = report.Types.Single(type => type.Type == typeof(SelectedToolkitTestData));

            Assert.GreaterOrEqual(report.TypeCount, 1);
            Assert.GreaterOrEqual(report.AssetCount, 1);
            Assert.GreaterOrEqual(report.SafePreviewCount, 1);
            Assert.AreEqual(DataToolkitInspectorCoverageLevel.SafePreview, info.CoverageLevel);
        }

        [Test]
        public void BuildReport_WithNoAssets_ReportsNoAssetsCoverage()
        {
            var context = new DataToolkitContext(CreateSettings(DataToolkitDefaultInspectorMode.LazyPreview));

            var report = DataToolkitDiagnosticsService.BuildReport(context, Array.Empty<IDataToolkitAssetInspectorProvider>());
            var info = report.Types.Single(type => type.Type == typeof(SelectedToolkitTestData));

            Assert.AreEqual(0, info.AssetCount);
            Assert.AreEqual(DataToolkitInspectorCoverageLevel.NoAssets, info.CoverageLevel);
            StringAssert.Contains("No asset", info.Reason);
        }

        [Test]
        public void BuildReport_WithSubAssetSample_UsesSubAssetForCoverage()
        {
            CreateSubAsset(AssetPath);
            var context = new DataToolkitContext(CreateSettings(DataToolkitDefaultInspectorMode.LazyPreview));

            var report = DataToolkitDiagnosticsService.BuildReport(context, Array.Empty<IDataToolkitAssetInspectorProvider>());
            var info = report.Types.Single(type => type.Type == typeof(SelectedToolkitTestData));

            Assert.AreEqual(1, info.AssetCount);
            Assert.AreEqual(AssetPath, info.SampleAssetPath);
            Assert.AreEqual(DataToolkitInspectorCoverageLevel.SafePreview, info.CoverageLevel);
        }

        [Test]
        public void Discovery_IncludesPackageNativeManageableDataAttribute()
        {
            var types = ManageableDataTypeDiscovery.GetManageableScriptableObjectTypes();

            CollectionAssert.Contains(types.ToArray(), typeof(ToolkitAttributedTestData));
        }

        [Test]
        public void LoadFirstAssetOfType_UsesMainAssetBeforeSubAssetFallback()
        {
            var source = File.ReadAllText("Packages/com.zerogamestudio.zeroengine.data-toolkit/Editor/Discovery/AssetDiscoveryService.cs");
            var mainLoadIndex = source.IndexOf("var mainAsset = AssetDatabase.LoadAssetAtPath(path, type);", StringComparison.Ordinal);
            var mainReturnIndex = source.IndexOf("if (mainAsset != null)", StringComparison.Ordinal);
            var allAssetsIndex = source.IndexOf("AssetDatabase.LoadAllAssetsAtPath(path)", StringComparison.Ordinal);

            Assert.GreaterOrEqual(mainLoadIndex, 0);
            Assert.Greater(mainReturnIndex, mainLoadIndex);
            Assert.Greater(allAssetsIndex, mainLoadIndex);
        }

        private static DataToolkitProjectSettings CreateSettings(DataToolkitDefaultInspectorMode inspectorMode)
        {
            return new DataToolkitProjectSettings(
                projectId: "DataToolkitDiagnosticsTests",
                windowTitle: "Data Toolkit Diagnostics Tests",
                menuPath: "Window/Data Toolkit Diagnostics Tests",
                editorPrefsPrefix: "ZGS_DataToolkitDiagnosticsTests",
                searchRoots: new[] { TestRoot },
                excludedPaths: Array.Empty<string>(),
                defaultInspectorMode: inspectorMode);
        }

        private static void CreateTestAsset(string path)
        {
            var asset = ScriptableObject.CreateInstance<SelectedToolkitTestData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
        }

        private static void CreateSubAsset(string path)
        {
            var host = ScriptableObject.CreateInstance<ToolkitHostTestData>();
            AssetDatabase.CreateAsset(host, path);
            var child = ScriptableObject.CreateInstance<SelectedToolkitTestData>();
            child.name = "SelectedSubAsset";
            AssetDatabase.AddObjectToAsset(child, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
        }

        private sealed class SelectedDataInspectorProvider : IDataToolkitAssetInspectorProvider
        {
            public int Order => 0;

            public bool CanInspect(DataToolkitContext context, UnityEngine.Object asset)
            {
                return asset is SelectedToolkitTestData;
            }

            public IAssetInspector CreateInspector(DataToolkitContext context)
            {
                return new NullAssetInspector();
            }
        }

        private sealed class NullAssetInspector : IAssetInspector
        {
            public bool CanInspect(UnityEngine.Object asset)
            {
                return asset != null;
            }

            public void SetTarget(UnityEngine.Object asset)
            {
            }

            public void Draw()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
