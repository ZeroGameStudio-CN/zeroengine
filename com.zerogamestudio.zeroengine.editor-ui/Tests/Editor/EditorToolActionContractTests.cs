using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI.Tests.Editor
{
    public sealed class EditorToolActionContractTests
    {
        [Test]
        public void ProviderAttribute_PreservesStableId()
        {
            var attribute = new EditorToolActionProviderAttribute("zeroengine.formula");

            Assert.AreEqual("zeroengine.formula", attribute.ProviderId);
        }

        [Test]
        public void State_DisabledWithoutReason_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new EditorToolActionState(false));
        }

        [Test]
        public void State_PreservesAvailabilityCheckAndReason()
        {
            var state = new EditorToolActionState(false, true, "仅在运行模式可用。");

            Assert.IsFalse(state.Enabled);
            Assert.IsTrue(state.IsChecked);
            Assert.AreEqual("仅在运行模式可用。", state.DisabledReason);
        }

        [TestCase(EditorToolActionStatus.Succeeded)]
        [TestCase(EditorToolActionStatus.Cancelled)]
        [TestCase(EditorToolActionStatus.Failed)]
        public void Result_PreservesStatusAndSummary(EditorToolActionStatus status)
        {
            var result = new EditorToolActionResult(status, "执行结果。");

            Assert.AreEqual(status, result.Status);
            Assert.AreEqual("执行结果。", result.Message);
        }

        [Test]
        public void Result_WithoutSummary_IsRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new EditorToolActionResult(EditorToolActionStatus.Succeeded, string.Empty));
        }

        [Test]
        public void Context_ExposesStableEntryIdentity()
        {
            EditorWindow owner = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                var context = new EditorToolActionContext(owner, "com.zerogamestudio.zeroengine", "ability-editor");

                Assert.AreSame(owner, context.Owner);
                Assert.AreEqual("com.zerogamestudio.zeroengine/ability-editor", context.FullId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void WorkspaceRoute_PreservesTypedTargetAndSource()
        {
            var source = new EditorWorkspaceRouteSource(
                "zeroengine.project-atlas",
                "project-atlas",
                "character-profile",
                "角色 > 角色档案与队伍");
            var route = new EditorWorkspaceRoute(
                "sample.project.adapters",
                "configuration",
                "characters",
                source);

            Assert.That(route.FullId, Is.EqualTo("sample.project.adapters/configuration"));
            Assert.That(route.SubrouteId, Is.EqualTo("characters"));
            Assert.That(route.Source, Is.SameAs(source));
            Assert.That(route.Source.SubrouteId, Is.EqualTo("character-profile"));
        }

        [Test]
        public void WorkspaceRouteAction_UsesTypedNavigator()
        {
            var owner = ScriptableObject.CreateInstance<TestRouteNavigatorWindow>();
            try
            {
                var route = new EditorWorkspaceRoute("sample.project", "characters", "profiles");
                IEditorToolAction action = EditorWorkspaceNavigation.CreateRouteAction(route, "角色配置");

                EditorToolActionResult result = action.Execute(new EditorToolActionContext(owner, "atlas", "feature"));

                Assert.That(result.Status, Is.EqualTo(EditorToolActionStatus.Succeeded));
                Assert.That(owner.LastRoute, Is.SameAs(route));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void WorkspaceWindowPanel_CreatesOnlyActiveView_AndRestoresExplicitState()
        {
            const string moduleId = "zeroengine.tests";
            const string panelId = "embedded-window";
            string stateKey = "ZeroEngine.EditorUI.WorkspaceWindow." + moduleId + "." + panelId + "." +
                              typeof(TestEmbeddedWindow).FullName;
            EditorPrefs.DeleteKey(stateKey);
            var owner = ScriptableObject.CreateInstance<EditorWindow>();
            var context = new EditorWorkspacePanelContext(owner, moduleId, panelId, (_, __) => false);
            try
            {
                using (var panel = new EditorWindowWorkspacePanel<TestEmbeddedWindow>())
                {
                    panel.Activate(context);
                    Assert.That(TestEmbeddedWindow.ActiveCount, Is.EqualTo(1));
                    Assert.That(TestEmbeddedWindow.LastInstance.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
                    TestEmbeddedWindow.LastInstance.Value = "remembered";
                    panel.Deactivate();
                    Assert.That(TestEmbeddedWindow.ActiveCount, Is.Zero);
                }

                using (var panel = new EditorWindowWorkspacePanel<TestEmbeddedWindow>())
                {
                    panel.Activate(context);
                    Assert.That(TestEmbeddedWindow.LastInstance.Value, Is.EqualTo("remembered"));
                }
                Assert.That(TestEmbeddedWindow.ActiveCount, Is.Zero);
            }
            finally
            {
                EditorPrefs.DeleteKey(stateKey);
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void WorkspaceWindowPanel_ForwardsOnlyDeclaredSubroutes()
        {
            var owner = ScriptableObject.CreateInstance<EditorWindow>();
            var context = new EditorWorkspacePanelContext(owner, "zeroengine.tests", "embedded-route", (_, __) => false);
            try
            {
                using (var panel = new EditorWindowWorkspacePanel<TestEmbeddedWindow>())
                {
                    panel.Activate(context);

                    Assert.That(panel.TryApplyWorkspaceRoute("known"), Is.True);
                    Assert.That(TestEmbeddedWindow.LastInstance.LastSubroute, Is.EqualTo("known"));
                    Assert.That(panel.TryApplyWorkspaceRoute("unknown"), Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private sealed class TestEmbeddedWindow : EditorWindow,
            IEditorWorkspaceEmbeddedView,
            IEditorWorkspaceStatefulView,
            IEditorWorkspaceRouteReceiver
        {
            [Serializable]
            private sealed class State
            {
                public string value;
            }

            public static int ActiveCount { get; private set; }
            public static TestEmbeddedWindow LastInstance { get; private set; }
            public string Value { get; set; }
            public string LastSubroute { get; private set; }

            private void OnEnable()
            {
                ActiveCount++;
                LastInstance = this;
            }

            private void OnDisable()
            {
                ActiveCount--;
            }

            public void OnWorkspaceGUI(EditorWorkspacePanelContext context)
            {
            }

            public string CaptureWorkspaceState()
            {
                return JsonUtility.ToJson(new State { value = Value });
            }

            public void RestoreWorkspaceState(string state)
            {
                Value = JsonUtility.FromJson<State>(state)?.value;
            }

            public bool TryApplyWorkspaceRoute(string subrouteId)
            {
                if (!string.Equals(subrouteId, "known", StringComparison.Ordinal))
                    return false;
                LastSubroute = subrouteId;
                return true;
            }
        }

        private sealed class TestRouteNavigatorWindow : EditorWindow, IEditorWorkspaceRouteNavigator
        {
            public EditorWorkspaceRoute LastRoute { get; private set; }

            public bool TryShowWorkspace(EditorWorkspaceRoute route)
            {
                LastRoute = route;
                return route != null;
            }
        }
    }
}
