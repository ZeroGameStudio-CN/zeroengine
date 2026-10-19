using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.EditorUI;
using ZGS.DataToolkit.Editor;

namespace ZGS.DataToolkit.Editor.Tests
{
    // Permanent regression: responsive embedded headers must retain one IMGUI tree per Layout.
    public sealed class DataToolkitEmbeddedLayoutTests
    {
        [UnityTest]
        public IEnumerator EmbeddedPanel_HeaderSurvivesOpenResizeAndReactivation()
        {
            if (Application.isBatchMode)
                Assert.Ignore("Native IMGUI event verification requires an interactive Editor view.");
            var provider = new LayoutHeaderActions();
            string prefsPrefix = "ZGS_DataToolkitLayoutProbe_" + Guid.NewGuid().ToString("N");
            var settings = new DataToolkitProjectSettings("ZGS", "Data Toolkit Layout Probe", "", prefsPrefix,
                new[] { "Packages/com.zerogamestudio.zeroengine.data-toolkit" }, Array.Empty<string>());
            var profile = new DataToolkitProjectProfile(settings, headerActionProviders: new[] { provider });
            var host = ScriptableObject.CreateInstance<DataToolkitEmbeddedLayoutTestWindow>();
            var errors = new List<string>();
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            void OnLog(string condition, string trace, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
                    (condition.Contains("GUILayout") || condition.Contains("Getting control") ||
                     trace.Contains("DataToolkitWindow")))
                    errors.Add(condition + "\n" + trace);
            }
            Application.logMessageReceived += OnLog;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                host.Initialize(profile);
                host.position = new Rect(80f, 100f, 1200f, 660f);
                host.ShowUtility();
                foreach (float width in new[] { 1200f, 640f, 827f, 816f, 840f, 1200f })
                {
                    int beforeLayout = host.LayoutCount;
                    int beforeRepaint = host.RepaintCount;
                    host.position = new Rect(80f, 100f, width, 660f);
                    double deadline = EditorApplication.timeSinceStartup + 8d;
                    while ((host.LayoutCount <= beforeLayout || host.RepaintCount < beforeRepaint + 3) &&
                           errors.Count == 0 && host.Exceptions.Count == 0 &&
                           EditorApplication.timeSinceStartup < deadline)
                    {
                        host.Repaint();
                        yield return null;
                    }
                    Assert.That(host.Exceptions, Is.Empty, string.Join("\n", host.Exceptions));
                    Assert.That(errors, Is.Empty, string.Join("\n", errors));
                    Assert.That(host.LayoutCount, Is.GreaterThan(beforeLayout), "No native Layout at " + width);
                    Assert.That(host.RepaintCount, Is.GreaterThanOrEqualTo(beforeRepaint + 3), "No native Repaint at " + width);
                    Assert.That(host.DisabledHeaderCount, Is.Zero, "Header was disabled at " + width);
                    TestContext.WriteLine($"width={width} layout={host.LayoutCount} repaint={host.RepaintCount} disabledHeaders={host.DisabledHeaderCount}");
                }
                host.Reactivate();
                int reactivationRepaint = host.RepaintCount;
                double reactivationDeadline = EditorApplication.timeSinceStartup + 8d;
                while (host.RepaintCount < reactivationRepaint + 3 && errors.Count == 0 &&
                       host.Exceptions.Count == 0 && EditorApplication.timeSinceStartup < reactivationDeadline)
                {
                    host.Repaint();
                    yield return null;
                }
                Assert.That(host.Exceptions, Is.Empty, string.Join("\n", host.Exceptions));
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
                Assert.That(host.RepaintCount, Is.GreaterThanOrEqualTo(reactivationRepaint + 3));
                Assert.That(host.DisabledHeaderCount, Is.Zero);
            }
            finally
            {
                host.Close();
                UnityEngine.Object.DestroyImmediate(host);
                Application.logMessageReceived -= OnLog;
                LogAssert.ignoreFailingMessages = previousIgnore;
                foreach (string suffix in new[] { "TypeColumnWidth", "AssetColumnWidth", "SelectedType",
                    "SelectedAssetGuid", "SelectedAssetPath", "TypeSearch", "AssetSearch", "TypeScrollX", "TypeScrollY",
                    "AssetScrollX", "AssetScrollY", "InspectorScrollX", "InspectorScrollY", "BodyScrollX" })
                    EditorPrefs.DeleteKey(settings.PrefKey(suffix));
            }
        }
    }

    internal sealed class LayoutHeaderActions : IDataToolkitHeaderActionProvider
    {
        public int Order => 0;
        public bool IsVisible(DataToolkitContext context) => true;
        public void DrawHeaderActions(DataToolkitContext context)
        {
            GUILayout.Button("Load All", EditorStyles.miniButton, GUILayout.Width(82f), GUILayout.Height(22f));
            GUILayout.Button("Load Demo", EditorStyles.miniButton, GUILayout.Width(92f), GUILayout.Height(22f));
        }
    }

    public sealed class DataToolkitEmbeddedLayoutTestWindow : EditorWindow
    {
        private DataToolkitWorkspacePanel panel;
        private EditorWorkspacePanelContext context;
        public readonly List<string> Exceptions = new List<string>();
        public int LayoutCount { get; private set; }
        public int RepaintCount { get; private set; }

        public int DisabledHeaderCount
        {
            get
            {
                var view = typeof(DataToolkitWorkspacePanel).GetField("embeddedView", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                var disabled = typeof(DataToolkitWindow).GetField("disabledHeaderActionProviders", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                return (int)disabled.GetType().GetProperty("Count").GetValue(disabled);
            }
        }

        public void Initialize(DataToolkitProjectProfile profile)
        {
            context = new EditorWorkspacePanelContext(this, "test.data-manager", "data-manager", (_, __) => false);
            panel = new DataToolkitWorkspacePanel(() => profile);
            panel.Activate(context);
        }

        public void Reactivate()
        {
            panel.Deactivate();
            panel.Activate(context);
            Repaint();
        }

        private void OnGUI()
        {
            if (panel == null || Exceptions.Count > 0)
                return;
            if (Event.current.type == EventType.Layout)
                LayoutCount++;
            if (Event.current.type == EventType.Repaint)
                RepaintCount++;
            context.AvailableWidth = position.width;
            try
            {
                panel.OnGUI(context);
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Exceptions.Add(exception.ToString());
            }
        }

        private void OnDisable()
        {
            panel?.Dispose();
            panel = null;
        }
    }
}
