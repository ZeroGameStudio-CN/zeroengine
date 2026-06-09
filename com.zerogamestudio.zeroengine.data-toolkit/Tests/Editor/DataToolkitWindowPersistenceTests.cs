using System;
using System.Collections.Generic;
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

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.CreateFolder("Assets", "__ZGSDataToolkitWindowPersistenceTests");
            DeletePersistedSelection();
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<DataToolkitWindow>())
            {
                window.Close();
            }

            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.SaveAssets();
            DeletePersistedSelection();
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void ReopenedWindow_RestoresSelectedTypeAndAsset()
        {
            CreateTestAsset(OriginalAssetPath);
            var profile = CreateProfile();

            var firstWindow = DataToolkitWindow.Open(profile);
            InvokeWindowMethod(firstWindow, "SelectType", typeof(SelectedToolkitTestData));
            InvokeWindowMethod(firstWindow, "SelectAssetByPath", OriginalAssetPath);
            Assert.AreEqual(OriginalAssetPath, EditorPrefs.GetString(EditorPrefsPrefix + "_SelectedAssetPath", string.Empty));
            var selectedAssetGuid = EditorPrefs.GetString(EditorPrefsPrefix + "_SelectedAssetGuid", string.Empty);
            Assert.IsNotEmpty(selectedAssetGuid);
            firstWindow.Close();
            AssetDiscoveryService.ClearCaches();

            var reopenedWindow = DataToolkitWindow.Open(profile);

            Assert.AreEqual(typeof(SelectedToolkitTestData), GetWindowField(reopenedWindow, "selectedType"));
            Assert.AreEqual(OriginalAssetPath, GetWindowField(reopenedWindow, "selectedAssetPath"));
            Assert.AreEqual(OriginalAssetPath, AssetDatabase.GUIDToAssetPath(selectedAssetGuid));
        }

        [Test]
        public void ToolbarProviderException_IsLoggedOnceAndProviderIsDisabled()
        {
            LogAssert.ignoreFailingMessages = true;
            var provider = new ThrowingToolbarProvider();
            var profile = new DataToolkitProjectProfile(
                CreateSettings(),
                toolbarProviders: new[] { provider });
            var window = DataToolkitWindow.Open(profile);

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

        private static DataToolkitProjectSettings CreateSettings()
        {
            return new DataToolkitProjectSettings(
                projectId: "DataToolkitWindowPersistenceTests",
                windowTitle: "Data Toolkit Test Window",
                menuPath: "Window/Data Toolkit Test",
                editorPrefsPrefix: EditorPrefsPrefix,
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
