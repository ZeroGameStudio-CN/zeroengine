using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZGS.DataToolkit.Editor.Tests
{
    [TestFixture]
    public sealed class DataToolkitWindowPersistenceTests
    {
        private const string TestRoot = "Assets/__ZGSDataToolkitWindowPersistenceTests";
        private const string OriginalAssetPath = TestRoot + "/Selected.asset";
        private const string EditorPrefsPrefix = "ZGS_DataToolkitWindowPersistenceTests";

        private readonly List<DataToolkitWindow> testWindows = new();

        [SetUp]
        public void SetUp()
        {
            testWindows.Clear();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__ZGSDataToolkitWindowPersistenceTests");
            DeletePersistedSelection();
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var window in testWindows)
            {
                if (window != null)
                {
                    window.Close();
                }
            }

            testWindows.Clear();
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.SaveAssets();
            DeletePersistedSelection();
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void ProjectSettings_UseConfiguredUiText()
        {
            var uiText = new DataToolkitUiText(
                dataTypes: "数据类型",
                assets: "资产",
                browse: "浏览数据",
                inspector: "资产详情",
                selectAssetPrompt: "从中栏选择一个数据资产。",
                assetSummaryFormat: "{0} 类 / {1}{2} 项资产");
            var settings = new DataToolkitProjectSettings(
                projectId: "LocalizedDataToolkit",
                windowTitle: "数据管理",
                menuPath: "工具/数据管理",
                editorPrefsPrefix: "LocalizedDataToolkit",
                searchRoots: new[] { TestRoot },
                excludedPaths: Array.Empty<string>(),
                uiText: uiText);

            Assert.AreSame(uiText, settings.UiText);
            Assert.AreEqual("数据类型", settings.UiText.DataTypes);
            Assert.AreEqual("资产", settings.UiText.Assets);
            Assert.AreEqual("浏览数据", settings.UiText.Browse);
            Assert.AreEqual("资产详情", settings.UiText.Inspector);
            Assert.AreEqual("从中栏选择一个数据资产。", settings.UiText.SelectAssetPrompt);
            Assert.AreEqual("2 类 / 3+ 项资产", string.Format(settings.UiText.AssetSummaryFormat, 2, 3, "+"));
        }

        [Test]
        public void ReopenedWindow_RestoresSelectedTypeAndAsset()
        {
            CreateTestAsset(OriginalAssetPath);
            var profile = CreateProfile();

            var firstWindow = Track(DataToolkitWindow.Open(profile));
            InvokeWindowMethod(firstWindow, "SelectType", typeof(SelectedToolkitTestData));
            InvokeWindowMethod(firstWindow, "SelectAssetByPath", OriginalAssetPath);
            SetWindowField(firstWindow, "typeColumnScroll", new Vector2(1f, 2f));
            SetWindowField(firstWindow, "assetColumnScroll", new Vector2(3f, 4f));
            SetWindowField(firstWindow, "inspectorScroll", new Vector2(5f, 6f));
            Assert.AreEqual(OriginalAssetPath, EditorPrefs.GetString(EditorPrefsPrefix + "_SelectedAssetPath", string.Empty));
            var selectedAssetGuid = EditorPrefs.GetString(EditorPrefsPrefix + "_SelectedAssetGuid", string.Empty);
            Assert.IsNotEmpty(selectedAssetGuid);
            firstWindow.Close();
            testWindows.Remove(firstWindow);
            AssetDiscoveryService.ClearCaches();

            var reopenedWindow = Track(DataToolkitWindow.Open(profile));

            Assert.AreEqual(typeof(SelectedToolkitTestData), GetWindowField(reopenedWindow, "selectedType"));
            Assert.AreEqual(OriginalAssetPath, GetWindowField(reopenedWindow, "selectedAssetPath"));
            Assert.AreEqual(OriginalAssetPath, AssetDatabase.GUIDToAssetPath(selectedAssetGuid));
            Assert.AreEqual(new Vector2(1f, 2f), GetWindowField(reopenedWindow, "typeColumnScroll"));
            Assert.AreEqual(new Vector2(3f, 4f), GetWindowField(reopenedWindow, "assetColumnScroll"));
            Assert.AreEqual(new Vector2(5f, 6f), GetWindowField(reopenedWindow, "inspectorScroll"));
        }

        [Test]
        public void CompactView_SwitchesToInspectorWhenAssetIsSelectedAndPersistsChoice()
        {
            CreateTestAsset(OriginalAssetPath);
            var window = Track(DataToolkitWindow.Open(CreateProfile()));

            InvokeWindowMethod(window, "SelectType", typeof(SelectedToolkitTestData));
            InvokeWindowMethod(window, "SelectAssetByPath", OriginalAssetPath);

            Assert.AreEqual(1, GetWindowField(window, "compactBodyView"));
            Assert.AreEqual(1, EditorPrefs.GetInt(EditorPrefsPrefix + "_CompactView", -1));

            InvokeWindowMethod(window, "SetCompactBodyView", 0);

            Assert.AreEqual(0, GetWindowField(window, "compactBodyView"));
            Assert.AreEqual(0, EditorPrefs.GetInt(EditorPrefsPrefix + "_CompactView", -1));
        }

        [Test]
        public void EmbeddedView_IsHiddenAndWorkspacePanelIsPublic()
        {
            var createEmbedded = typeof(DataToolkitWindow).GetMethod(
                "CreateEmbedded",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(createEmbedded);
            var embedded = (DataToolkitWindow)createEmbedded.Invoke(
                null,
                new object[] { CreateProfile(), (Action)(() => { }) });
            try
            {
                Assert.AreEqual(HideFlags.HideAndDontSave, embedded.hideFlags);
                Assert.AreEqual(true, GetWindowField(embedded, "embeddedHost"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(embedded);
            }

            var panelType = typeof(DataToolkitWindow).Assembly.GetType("ZGS.DataToolkit.Editor.DataToolkitWorkspacePanel");
            Assert.NotNull(panelType);
            Assert.IsTrue(panelType.IsPublic);
            Assert.IsTrue(panelType.GetInterfaces().Any(type => type.FullName == "ZeroEngine.EditorUI.IEditorWorkspacePanel"));
            Assert.IsTrue(panelType.GetInterfaces().Any(type => type.FullName == "ZeroEngine.EditorUI.IEditorWorkspaceFullWidthPanel"));
            Assert.NotNull(panelType.GetConstructor(new[] { typeof(Func<DataToolkitProjectProfile>) }));
        }

        [Test]
        public void LayoutRestoredWindow_RebindsSerializedProjectIdWhenProfileRegistersLater()
        {
            var projectId = "LateRegisteredDataToolkitProject_" + Guid.NewGuid().ToString("N");
            var window = Track(ScriptableObject.CreateInstance<DataToolkitWindow>());
            try
            {
                SetWindowField(window, "serializedProjectId", projectId);

                InvokeWindowMethod(window, "InitializeFromSerializedProjectId");

                Assert.AreNotEqual(projectId, GetWindowContext(window).Settings.ProjectId);
                Assert.AreEqual(projectId, GetWindowField(window, "serializedProjectId"));

                DataToolkitProjectRegistry.Register(
                    projectId,
                    () => new DataToolkitProjectProfile(CreateSettings(
                        projectId,
                        "Late Registered Data Manager",
                        "ZGS_LateRegisteredDataToolkitProject")));

                InvokeWindowMethod(window, "RestoreRegisteredProfileForSerializedProjectId");

                Assert.AreEqual(projectId, GetWindowContext(window).Settings.ProjectId);
                Assert.AreEqual(projectId, GetWindowField(window, "serializedProjectId"));
            }
            finally
            {
                DestroyTrackedWindow(window);
            }
        }

        [Test]
        public void ToolbarProviderException_IsLoggedOnceAndProviderIsDisabled()
        {
            LogAssert.ignoreFailingMessages = true;
            var provider = new ThrowingToolbarProvider();
            var profile = new DataToolkitProjectProfile(
                CreateSettings(),
                toolbarProviders: new[] { provider });
            var window = Track(DataToolkitWindow.Open(profile));

            var messages = new List<string>();
            void HandleLog(string condition, string stackTrace, LogType type)
            {
                if (condition.Contains(ThrowingToolbarProvider.ExceptionMessage))
                {
                    messages.Add(condition);
                }
            }

            Application.logMessageReceived += HandleLog;
            try
            {
                InvokeWindowMethod(window, "GetVisibleToolbarProviders");
                InvokeWindowMethod(window, "GetVisibleToolbarProviders");
            }
            finally
            {
                Application.logMessageReceived -= HandleLog;
            }

            Assert.AreEqual(1, messages.Count);
        }

        [Test]
        public void SelectableRowsWithoutCountText_UseAvailableTitleWidth()
        {
            var method = typeof(DataToolkitWindow).GetMethod("BuildSelectableRowTitleRect", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method, "Selectable rows should calculate title width from whether count text is visible.");

            var rowRect = new Rect(10f, 20f, 240f, 24f);
            var titleRectWithoutCount = (Rect)method.Invoke(null, new object[] { rowRect, false });
            var titleRectWithCount = (Rect)method.Invoke(null, new object[] { rowRect, true });

            Assert.AreEqual(rowRect.x + 6f, titleRectWithoutCount.x);
            Assert.AreEqual(rowRect.width - 12f, titleRectWithoutCount.width);
            Assert.AreEqual(rowRect.width - 56f, titleRectWithCount.width);
            Assert.Greater(titleRectWithoutCount.width, titleRectWithCount.width);
        }

        private static DataToolkitProjectProfile CreateProfile()
        {
            return new DataToolkitProjectProfile(CreateSettings());
        }

        private DataToolkitWindow Track(DataToolkitWindow window)
        {
            testWindows.Add(window);
            return window;
        }

        private void DestroyTrackedWindow(DataToolkitWindow window)
        {
            testWindows.Remove(window);
            if (window != null)
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static DataToolkitProjectSettings CreateSettings()
        {
            return CreateSettings(
                "DataToolkitWindowPersistenceTests",
                "Data Toolkit Test Window",
                EditorPrefsPrefix);
        }

        private static DataToolkitProjectSettings CreateSettings(string projectId, string windowTitle, string editorPrefsPrefix)
        {
            return new DataToolkitProjectSettings(
                projectId: projectId,
                windowTitle: windowTitle,
                menuPath: "Window/Data Toolkit Test",
                editorPrefsPrefix: editorPrefsPrefix,
                searchRoots: new[] { TestRoot },
                excludedPaths: Array.Empty<string>());
        }

        private static void CreateTestAsset(string path)
        {
            var asset = ScriptableObject.CreateInstance<SelectedToolkitTestData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
        }

        private static void DeletePersistedSelection()
        {
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_SelectedType");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_SelectedAssetGuid");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_SelectedAssetPath");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_TypeSearch");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_AssetSearch");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_TypeScrollX");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_TypeScrollY");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_AssetScrollX");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_AssetScrollY");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_InspectorScrollX");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_InspectorScrollY");
            EditorPrefs.DeleteKey(EditorPrefsPrefix + "_CompactView");
        }

        private static object InvokeWindowMethod(DataToolkitWindow window, string methodName, params object[] arguments)
        {
            var method = typeof(DataToolkitWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, $"{methodName} should exist on DataToolkitWindow.");
            return method.Invoke(window, arguments);
        }

        private static object GetWindowField(DataToolkitWindow window, string fieldName)
        {
            var field = typeof(DataToolkitWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"{fieldName} should exist on DataToolkitWindow.");
            return field.GetValue(window);
        }

        private static void SetWindowField(DataToolkitWindow window, string fieldName, object value)
        {
            var field = typeof(DataToolkitWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"{fieldName} should exist on DataToolkitWindow.");
            field.SetValue(window, value);
        }

        private static DataToolkitContext GetWindowContext(DataToolkitWindow window)
        {
            return (DataToolkitContext)GetWindowField(window, "context");
        }

        private sealed class ThrowingToolbarProvider : IDataToolkitToolbarProvider
        {
            public const string ExceptionMessage = "Toolbar failure for persistence tests";

            public int Order => 0;

            public bool IsVisible(DataToolkitContext context)
            {
                throw new InvalidOperationException(ExceptionMessage);
            }

            public void DrawToolbar(DataToolkitContext context)
            {
            }
        }
    }

}
