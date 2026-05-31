using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringDetailPipelineTests
    {
        [Test]
        public void Profile_OrdersPreviewProvidersAndDetailSections()
        {
            var firstPreview = new TestPreviewProvider(10, "late");
            var secondPreview = new TestPreviewProvider(0, "early");
            var firstSection = new TestDetailSection(20, "stats");
            var secondSection = new TestDetailSection(5, "header");

            var profile = new DataAuthoringProfile(
                "TEST_PROFILE",
                "Test Profile",
                new IDataAuthoringAssetAdapter[] { new TestAdapter() },
                "test",
                importAdapters: null,
                previewProviders: new IDataAuthoringPreviewProvider[] { firstPreview, secondPreview },
                detailSections: new IDataAuthoringDetailSection[] { firstSection, secondSection });

            Assert.That(profile.PreviewProviders.Select(provider => provider.ProviderId), Is.EqualTo(new[] { "early", "late" }));
            Assert.That(profile.DetailSections.Select(section => section.SectionId), Is.EqualTo(new[] { "header", "stats" }));
        }

        [Test]
        public void PreviewContext_PreservesSelectedAssetAndIssues()
        {
            var asset = ScriptableObject.CreateInstance<ScriptableObject>();
            var adapter = new TestAdapter();
            var record = new DataAuthoringAssetRecord(asset, "Assets/Test.asset", "test_id", "Test", "Subtitle", null);
            var issue = DataAuthoringIssue.Warning("Assets/Test.asset", "ScriptableObject", "test_id", "field", "warning");
            var profile = new DataAuthoringProfile("TEST_PROFILE", "Test Profile", new[] { adapter });

            var context = new DataAuthoringPreviewContext(profile, adapter, record, new[] { issue });

            Assert.AreSame(profile, context.Profile);
            Assert.AreSame(adapter, context.Adapter);
            Assert.AreSame(record, context.Record);
            Assert.AreSame(asset, context.Asset);
            Assert.That(context.Issues.Single().Message, Is.EqualTo("warning"));
        }

        [Test]
        public void DataAuthoringWindow_UsesInspectorHostForModernProfiles()
        {
            var source = ReadEditorScriptSource("ZGS.DataToolkit.Editor.DataAuthoringWindow");
            var hostSource = ReadEditorScriptSource(
                "ZGS.DataToolkit.Editor.DataAuthoringInspectorHost",
                "DataAuthoringInspectorHost");

            StringAssert.Contains("DataAuthoringInspectorHost", source);
            StringAssert.Contains("_inspectorHost.UsesModernPipeline", source);
            StringAssert.Contains("_inspectorHost.Draw", source);
            StringAssert.Contains("CompositeAssetInspector", hostSource);
            StringAssert.Contains("DrawPreviewProviders", hostSource);
            StringAssert.Contains("DrawDetailSections", hostSource);
            StringAssert.Contains("DrawDefaultInspector", hostSource);
        }

        [Test]
        public void DataAuthoringWindow_DelegatesModernInspectorPipelineToInspectorHost()
        {
            var hostType = Type.GetType("ZGS.DataToolkit.Editor.DataAuthoringInspectorHost, ZGS.DataToolkit.Editor");
            Assert.NotNull(hostType);

            var source = ReadEditorScriptSource("ZGS.DataToolkit.Editor.DataAuthoringWindow");
            StringAssert.Contains("DataAuthoringInspectorHost", source);
            StringAssert.Contains("_inspectorHost.Draw", source);
        }

        [Test]
        public void DataAuthoringWindow_SourceKeepsImportPreviewInBottomDrawerTab()
        {
            var source = ReadEditorScriptSource("ZGS.DataToolkit.Editor.DataAuthoringWindow");

            StringAssert.Contains("SetIssues(", source);
            StringAssert.Contains("DrawBottomDrawer", source);
            StringAssert.Contains("DrawDrawerTabs", source);
            StringAssert.Contains("DataAuthoringDrawerTab.ImportPreview", source);
            StringAssert.Contains("DrawImportPreviewControls();", source);
            StringAssert.Contains("_drawerTab = DataAuthoringDrawerTab.ImportPreview;", source);
        }

        [Test]
        public void DataAuthoringWindow_SourceUsesBottomDrawerInsteadOfProblemColumn()
        {
            var source = ReadEditorScriptSource("ZGS.DataToolkit.Editor.DataAuthoringWindow");

            StringAssert.Contains("DrawMainWorkspace();", source);
            StringAssert.Contains("DrawInspectorColumn();", source);
            StringAssert.Contains("DrawBottomDrawer();", source);
            StringAssert.DoesNotContain("DrawIssueColumn", source);
            StringAssert.DoesNotContain("GetIssueColumnWidth", source);

            var workspaceStart = source.IndexOf("private void DrawMainWorkspace()", StringComparison.Ordinal);
            var inspectorStart = source.IndexOf("private void DrawInspectorColumn()", StringComparison.Ordinal);
            var drawerStart = source.IndexOf("private void DrawBottomDrawer()", StringComparison.Ordinal);
            Assert.That(workspaceStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(inspectorStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(drawerStart, Is.GreaterThan(inspectorStart));

            var workspaceMethod = source.Substring(workspaceStart, inspectorStart - workspaceStart);
            Assert.Less(
                workspaceMethod.IndexOf("DrawInspectorColumn();", StringComparison.Ordinal),
                workspaceMethod.IndexOf("DrawBottomDrawer();", StringComparison.Ordinal));

            var inspectorMethod = source.Substring(inspectorStart, drawerStart - inspectorStart);
            StringAssert.DoesNotContain("DrawIssues", inspectorMethod);
            StringAssert.DoesNotContain("DrawBottomDrawer", inspectorMethod);
        }

        [Test]
        public void DataAuthoringWindow_SourceStopsDrawingImportPreviewAfterApplyOrClear()
        {
            var source = ReadEditorScriptSource("ZGS.DataToolkit.Editor.DataAuthoringWindow");
            var importPreviewStart = source.IndexOf("private void DrawImportPreviewControls()", StringComparison.Ordinal);
            var nextMethodStart = source.IndexOf("private static string DrawSearchField", StringComparison.Ordinal);
            Assert.That(importPreviewStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextMethodStart, Is.GreaterThan(importPreviewStart));

            var importPreviewMethod = source.Substring(importPreviewStart, nextMethodStart - importPreviewStart)
                .Replace("\r\n", "\n");
            StringAssert.Contains(
                "ApplyImportPreview();\n                    if (_importPreview == null)\n                    {\n                        return;\n                    }",
                importPreviewMethod);
            StringAssert.Contains(
                "_drawerTab = DataAuthoringDrawerTab.Problems;\n                    return;",
                importPreviewMethod);
        }

        [Test]
        public void DataAuthoringWindow_SourceUsesUnifiedToolsMenuInToolbar()
        {
            var source = ReadEditorScriptSource("ZGS.DataToolkit.Editor.DataAuthoringWindow");
            var toolbarStart = source.IndexOf("private void DrawToolbar()", StringComparison.Ordinal);
            var toolsMenuStart = source.IndexOf("private void DrawToolsMenu()", StringComparison.Ordinal);
            Assert.That(toolbarStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(toolsMenuStart, Is.GreaterThan(toolbarStart));

            var toolbarMethod = source.Substring(toolbarStart, toolsMenuStart - toolbarStart);
            StringAssert.Contains("DrawToolsMenu();", toolbarMethod);
            StringAssert.DoesNotContain("GUILayout.Button(labels.ValidateSelected", toolbarMethod);
            StringAssert.DoesNotContain("GUILayout.Button(labels.ValidateGroup", toolbarMethod);
            StringAssert.DoesNotContain("GUILayout.Button(labels.ValidateAll", toolbarMethod);
            StringAssert.DoesNotContain("GUILayout.Button(labels.ExportCsv", toolbarMethod);
            StringAssert.DoesNotContain("GUILayout.Button(labels.ImportPreview", toolbarMethod);
            StringAssert.DoesNotContain("GUILayout.Button(labels.Refresh", toolbarMethod);

            var nextMethodStart = source.IndexOf("private void DrawGroups()", StringComparison.Ordinal);
            Assert.That(nextMethodStart, Is.GreaterThan(toolsMenuStart));
            var toolsMenuMethod = source.Substring(toolsMenuStart, nextMethodStart - toolsMenuStart);
            StringAssert.Contains("new GenericMenu", toolsMenuMethod);
            StringAssert.Contains("new GUIContent(labels.ValidateSelected)", toolsMenuMethod);
            StringAssert.Contains("new GUIContent(labels.ValidateGroup)", toolsMenuMethod);
            StringAssert.Contains("new GUIContent(labels.ValidateAll)", toolsMenuMethod);
            StringAssert.Contains("new GUIContent(labels.ExportCsv)", toolsMenuMethod);
            StringAssert.Contains("new GUIContent(labels.ImportPreview)", toolsMenuMethod);
            StringAssert.Contains("new GUIContent(labels.Refresh)", toolsMenuMethod);
        }

        private static string ReadEditorScriptSource(string fullName, string searchName = "DataAuthoringWindow")
        {
            var guids = AssetDatabase.FindAssets($"{searchName} t:MonoScript");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass()?.FullName == fullName)
                {
                    return File.ReadAllText(path);
                }
            }

            Assert.Fail($"Could not find script source for {fullName}.");
            return string.Empty;
        }

        private sealed class TestPreviewProvider : IDataAuthoringPreviewProvider
        {
            public TestPreviewProvider(int order, string providerId)
            {
                Order = order;
                ProviderId = providerId;
            }

            public string ProviderId { get; }
            public int Order { get; }
            public bool CanPreview(Object asset) => asset != null;
            public void DrawPreview(DataAuthoringPreviewContext context) { }
        }

        private sealed class TestDetailSection : IDataAuthoringDetailSection
        {
            public TestDetailSection(int order, string sectionId)
            {
                Order = order;
                SectionId = sectionId;
            }

            public string SectionId { get; }
            public string Title => SectionId;
            public int Order { get; }
            public bool CanDraw(Object asset) => asset != null;
            public void DrawSection(DataAuthoringPreviewContext context) { }
        }

        private sealed class TestAdapter : IDataAuthoringAssetAdapter
        {
            public string GroupId => "Test";
            public string DisplayName => "Test";
            public int Order => 0;
            public IReadOnlyList<DataAuthoringAssetRecord> GetAssets() => new List<DataAuthoringAssetRecord>();
            public Object CreateAsset() => null;
            public Object DuplicateAsset(Object source) => null;
            public void DrawInspector(Object asset) { }
            public IReadOnlyList<DataAuthoringIssue> Validate(Object asset) => new List<DataAuthoringIssue>();
            public void AddExportSheets(TabularWorkbook workbook) { }
        }
    }
}
