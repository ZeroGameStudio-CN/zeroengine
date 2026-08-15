using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Editor;
using ZeroEngine.Editor.Dashboard;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Dashboard.Tests.Editor
{
    public sealed class DashboardWorkspaceRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            LazyProvider.Created = 0;
        }

        [Test]
        public void Build_DoesNotInstantiateProviderUntilSelectedPanelIsCreated()
        {
            DashboardPanel descriptor = Panel("workspace.lazy");
            DashboardWorkspaceRegistry registry = DashboardWorkspaceRegistry.Build(
                Catalog(descriptor),
                new[] { typeof(LazyProvider) });

            Assert.AreEqual(0, LazyProvider.Created);
            Assert.IsTrue(registry.IsAvailable(descriptor));
            Assert.IsTrue(registry.TryCreate(descriptor, out IEditorWorkspacePanel first, out DashboardDiagnostic diagnostic));
            Assert.IsNull(diagnostic);
            Assert.AreEqual(1, LazyProvider.Created);
            Assert.IsTrue(registry.TryCreate(descriptor, out IEditorWorkspacePanel second, out diagnostic));
            Assert.AreEqual(2, LazyProvider.Created);
            Assert.AreNotSame(first, second);
            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void Build_MissingProvider_IsolatesPanelWithDiagnostic()
        {
            DashboardPanel descriptor = Panel("workspace.missing");
            DashboardWorkspaceRegistry registry = DashboardWorkspaceRegistry.Build(Catalog(descriptor), Array.Empty<Type>());

            Assert.IsFalse(registry.IsAvailable(descriptor));
            Assert.AreEqual("workspace-provider-missing", registry.Diagnostics[0].Code);
        }

        [Test]
        public void Build_DuplicateProviderId_IsolatesPanelWithDiagnostic()
        {
            DashboardPanel descriptor = Panel("workspace.duplicate");
            DashboardWorkspaceRegistry registry = DashboardWorkspaceRegistry.Build(
                Catalog(descriptor),
                new[] { typeof(DuplicateProviderA), typeof(DuplicateProviderB) });

            Assert.IsFalse(registry.IsAvailable(descriptor));
            Assert.AreEqual("workspace-provider-duplicate", registry.Diagnostics[0].Code);
        }

        [Test]
        public void TryCreate_ProviderFailure_IsReturnedAsPanelDiagnostic()
        {
            DashboardPanel descriptor = Panel("workspace.throwing");
            DashboardWorkspaceRegistry registry = DashboardWorkspaceRegistry.Build(
                Catalog(descriptor),
                new[] { typeof(ThrowingProvider) });

            Assert.IsFalse(registry.TryCreate(descriptor, out IEditorWorkspacePanel panel, out DashboardDiagnostic diagnostic));
            Assert.IsNull(panel);
            Assert.AreEqual("workspace-provider-failed", diagnostic.Code);
            StringAssert.Contains("provider failure", diagnostic.Message);
        }

        [Test]
        public void TryCreate_NullPanel_IsReturnedAsPanelDiagnostic()
        {
            DashboardPanel descriptor = Panel("workspace.null");
            DashboardWorkspaceRegistry registry = DashboardWorkspaceRegistry.Build(
                Catalog(descriptor),
                new[] { typeof(NullProvider) });

            Assert.IsFalse(registry.TryCreate(descriptor, out IEditorWorkspacePanel panel, out DashboardDiagnostic diagnostic));
            Assert.IsNull(panel);
            Assert.AreEqual("workspace-panel-not-created", diagnostic.Code);
        }

        [Test]
        public void Dashboard_ImplementsTypedWorkspaceNavigation()
        {
            Assert.That(typeof(IEditorWorkspaceNavigator).IsAssignableFrom(typeof(ZeroEngineDashboard)), Is.True);
        }

        [TestCase(240f)]
        [TestCase(320f)]
        [TestCase(500f)]
        [TestCase(980f)]
        public void Dashboard_WorkspaceSplitKeepsSidebarAndContentVisible(float width)
        {
            DashboardWorkspaceSplitLayout layout = ZeroEngineDashboard.CalculateWorkspaceSplitLayout(width, 244f);

            Assert.That(layout.SidebarWidth, Is.GreaterThan(0f));
            Assert.That(layout.ContentWidth, Is.GreaterThan(0f));
            Assert.That(layout.SidebarWidth, Is.LessThan(layout.ContentWidth));
        }

        [Test]
        public void Dashboard_WorkspaceSplitRestoresPreferredWidthAfterNarrowResize()
        {
            DashboardWorkspaceSplitLayout narrow = ZeroEngineDashboard.CalculateWorkspaceSplitLayout(400f, 244f);
            DashboardWorkspaceSplitLayout wide = ZeroEngineDashboard.CalculateWorkspaceSplitLayout(980f, 244f);

            Assert.That(narrow.SidebarWidth, Is.LessThan(244f));
            Assert.That(wide.SidebarWidth, Is.EqualTo(244f).Within(0.01f));
        }

        [TestCase(559f, true)]
        [TestCase(560f, false)]
        [TestCase(980f, false)]
        public void Dashboard_TopNavigationStacksOnlyAtNarrowWidths(float width, bool expected)
        {
            Assert.That(ZeroEngineDashboard.UsesStackedTopNavigation(width), Is.EqualTo(expected));
        }

        [Test]
        public void WorkspacePanelLayout_ReservesRightInsetAndAlignsSelectionBar()
        {
            Rect row = new Rect(10f, 20f, 300f, 28f);

            DashboardWorkspacePanelLayout layout = DashboardWorkspaceLayout.CalculatePanelLayout(
                row,
                handleInset: 12f,
                handleWidth: 14f,
                gap: 2f,
                rightInset: 8f,
                verticalInset: 4f,
                selectionWidth: 3f);

            Assert.That(layout.HandleRect, Is.EqualTo(new Rect(22f, 20f, 14f, 28f)));
            Assert.That(layout.ButtonRect, Is.EqualTo(new Rect(38f, 24f, 264f, 20f)));
            Assert.That(layout.ButtonRect.xMax, Is.EqualTo(row.xMax - 8f));
            Assert.That(layout.SelectionRect, Is.EqualTo(new Rect(38f, 25f, 3f, 18f)));
        }

        [Test]
        public void WorkspacePanelLayout_NarrowRowNeverOverflowsRightEdge()
        {
            Rect row = new Rect(10f, 20f, 30f, 28f);

            DashboardWorkspacePanelLayout layout = DashboardWorkspaceLayout.CalculatePanelLayout(
                row,
                handleInset: 12f,
                handleWidth: 14f,
                gap: 2f,
                rightInset: 8f,
                verticalInset: 4f,
                selectionWidth: 3f);

            Assert.That(layout.ButtonRect.width, Is.GreaterThanOrEqualTo(1f));
            Assert.That(layout.ButtonRect.xMax, Is.LessThanOrEqualTo(row.xMax));
            Assert.That(layout.SelectionRect.x, Is.EqualTo(layout.ButtonRect.x));
            Assert.That(layout.SelectionRect.yMin, Is.GreaterThan(layout.ButtonRect.yMin));
            Assert.That(layout.SelectionRect.yMax, Is.LessThan(layout.ButtonRect.yMax));
        }

        [Test]
        public void InstalledPackagePresentation_SeparatesInstallAndWorkspaceState()
        {
            var namedPackage = new DashboardInstalledPackage(
                "com.zerogamestudio.zeroengine.dashboard",
                "4.5.0",
                "Packages/dashboard",
                "ZeroEngine Dashboard");
            var unnamedPackage = new DashboardInstalledPackage(
                "com.zerogamestudio.zeroengine.core",
                "2.0.0",
                "Packages/core");

            Assert.That(namedPackage.DisplayName, Is.EqualTo("ZeroEngine Dashboard"));
            Assert.That(unnamedPackage.DisplayName, Is.EqualTo(unnamedPackage.Name));
            Assert.That(DashboardText.InstalledWithoutWorkspaceEntry, Is.EqualTo("已安装 · 无工作台入口"));
            Assert.That(
                DashboardText.InstalledWorkspaceContent(2, 1, 3),
                Is.EqualTo("已安装 · 2 个工具 · 1 个面板 · 3 份资料"));
        }

        [Test]
        public void Dashboard_OnEnable_QueuesColdCatalogDiscoveryInsteadOfRunningIt()
        {
            DashboardViewState originalState = DashboardViewStateStore.Load();
            bool hadCachedCatalog = DashboardCatalogSession.TryGet(out DashboardCatalog originalCatalog);
            ZeroEngineDashboard window = null;
            try
            {
                DashboardCatalogSession.Invalidate();
                window = ScriptableObject.CreateInstance<ZeroEngineDashboard>();

                Assert.IsFalse(DashboardCatalogSession.TryGet(out _));
                Assert.IsTrue(GetPrivateField<bool>(window, "_catalogLoading"));
                Assert.IsTrue(GetPrivateField<bool>(window, "_catalogRefreshQueued"));
                Assert.IsFalse(GetPrivateField<bool>(window, "_hasDrawnShell"));
                Assert.AreSame(DashboardCatalog.Empty, GetPrivateField<DashboardCatalog>(window, "_catalog"));

                typeof(ZeroEngineDashboard)
                    .GetMethod("OnEditorUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, null);

                Assert.IsFalse(DashboardCatalogSession.TryGet(out _), "Catalog discovery must wait until the shell has drawn.");
            }
            finally
            {
                if (window != null)
                    UnityEngine.Object.DestroyImmediate(window);
                DashboardViewStateStore.Save(originalState);
                if (hadCachedCatalog)
                    DashboardCatalogSession.Store(originalCatalog);
                else
                    DashboardCatalogSession.Invalidate();
            }
        }

        [Test]
        public void FullWidthPanelMarker_SelectsUnconstrainedWorkspaceLayout()
        {
            MethodInfo method = typeof(ZeroEngineDashboard).GetMethod(
                "UsesFullWidthWorkspaceLayout",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            Assert.IsFalse((bool)method.Invoke(null, new object[] { new TestPanel() }));
            Assert.IsTrue((bool)method.Invoke(null, new object[] { new FullWidthTestPanel() }));
        }

        [Test]
        public void ViewStateStore_RoundTripsNavigationFiltersAndScrolls()
        {
            string prefix = "ZGS.Dashboard.Tests." + Guid.NewGuid().ToString("N") + ".";
            try
            {
                DashboardViewStateStore.Save(new DashboardViewState
                {
                    Page = 2,
                    HomeView = 1,
                    Search = "数据",
                    SelectedCategoryId = "data-localization",
                    SelectedScopeId = "pob",
                    SelectedSafetyId = "read-only",
                    SelectedAvailabilityId = "available",
                    ShowAdvanced = false,
                    ShowMaintenance = true,
                    SelectedPanelFullId = "pob.tools.data-manager/data-manager",
                    WorkspaceModuleOrder = new[]
                    {
                        "pob.tools.data-manager",
                        "pob.dashboard"
                    },
                    WorkspacePanelOrder = new[]
                    {
                        "pob.tools.data-manager/data-manager",
                        "pob.dashboard/runtime-overview"
                    },
                    CollapsedWorkspaceModuleIds = new[]
                    {
                        "pob.dashboard"
                    },
                    WorkspaceSidebarWidth = 286f,
                    ModuleScroll = new Vector2(1f, 2f),
                    ContentScroll = new Vector2(3f, 4f),
                    SystemScroll = new Vector2(5f, 6f),
                    WorkspaceNavigationScroll = new Vector2(7f, 8f),
                    WorkspaceContentScroll = new Vector2(9f, 10f),
                    ContextScroll = new Vector2(11f, 12f)
                }, prefix);

                DashboardViewState state = DashboardViewStateStore.Load(prefix);

                Assert.That(state.Page, Is.EqualTo(2));
                Assert.That(state.HomeView, Is.EqualTo(1));
                Assert.That(state.Search, Is.EqualTo("数据"));
                Assert.That(state.SelectedCategoryId, Is.EqualTo("data-localization"));
                Assert.That(state.SelectedScopeId, Is.EqualTo("pob"));
                Assert.That(state.SelectedSafetyId, Is.EqualTo("read-only"));
                Assert.That(state.SelectedAvailabilityId, Is.EqualTo("available"));
                Assert.That(state.ShowAdvanced, Is.False);
                Assert.That(state.ShowMaintenance, Is.True);
                Assert.That(state.SelectedPanelFullId, Is.EqualTo("pob.tools.data-manager/data-manager"));
                Assert.That(state.WorkspaceModuleOrder, Is.EqualTo(new[]
                {
                    "pob.tools.data-manager",
                    "pob.dashboard"
                }));
                Assert.That(state.WorkspacePanelOrder, Is.EqualTo(new[]
                {
                    "pob.tools.data-manager/data-manager",
                    "pob.dashboard/runtime-overview"
                }));
                Assert.That(state.CollapsedWorkspaceModuleIds, Is.EqualTo(new[] { "pob.dashboard" }));
                Assert.That(state.WorkspaceSidebarWidth, Is.EqualTo(286f));
                Assert.That(state.WorkspaceContentScroll, Is.EqualTo(new Vector2(9f, 10f)));
                Assert.That(state.ContextScroll, Is.EqualTo(new Vector2(11f, 12f)));
            }
            finally
            {
                DashboardViewStateStore.Delete(prefix);
            }
        }

        [TestCase(0, 0, 0)]
        [TestCase(1, 0, 1)]
        [TestCase(2, 1, 0)]
        [TestCase(3, 2, 0)]
        public void ViewStateStore_MigratesLegacyFourPageNavigation(int legacyPage, int expectedPage, int expectedHomeView)
        {
            string prefix = "ZGS.Dashboard.Tests." + Guid.NewGuid().ToString("N") + ".";
            try
            {
                UnityEditor.EditorPrefs.SetInt(prefix + "Page", legacyPage);

                DashboardViewState state = DashboardViewStateStore.Load(prefix);

                Assert.That(state.Page, Is.EqualTo(expectedPage));
                Assert.That(state.HomeView, Is.EqualTo(expectedHomeView));
            }
            finally
            {
                DashboardViewStateStore.Delete(prefix);
            }
        }

        [Test]
        public void WorkspaceOrder_Move_PreservesMissingIdsAndAppendsNewItems()
        {
            string[] preferred =
            {
                "module.a/first",
                "removed.module/old",
                "module.b/second"
            };
            string[] available =
            {
                "module.a/first",
                "module.b/second",
                "module.c/new"
            };

            string[] reordered = DashboardWorkspaceOrder.Move(
                preferred,
                available,
                "module.a/first",
                "module.b/second",
                before: false);

            Assert.That(reordered, Is.EqualTo(new[]
            {
                "removed.module/old",
                "module.b/second",
                "module.a/first",
                "module.c/new"
            }));
            Assert.That(
                DashboardWorkspaceOrder.Visible(reordered, available),
                Is.EqualTo(new[] { "module.b/second", "module.a/first", "module.c/new" }));
        }

        [Test]
        public void WorkspaceOrder_MoveModules_PreservesMissingIdsAndAppendsNewModules()
        {
            string[] reordered = DashboardWorkspaceOrder.Move(
                new[] { "module.a", "removed.module", "module.b" },
                new[] { "module.a", "module.b", "module.c" },
                "module.a",
                "module.b",
                before: false);

            Assert.That(reordered, Is.EqualTo(new[]
            {
                "removed.module",
                "module.b",
                "module.a",
                "module.c"
            }));
            Assert.That(
                DashboardWorkspaceOrder.Visible(reordered, new[] { "module.a", "module.b", "module.c" }),
                Is.EqualTo(new[] { "module.b", "module.a", "module.c" }));
        }

        [Test]
        public void WorkspaceFoldout_SearchTemporarilyExpandsCollapsedGroup()
        {
            string[] collapsed = { "module.a" };

            Assert.That(DashboardWorkspaceFoldout.IsExpanded(collapsed, "module.a", searchActive: false), Is.False);
            Assert.That(DashboardWorkspaceFoldout.IsExpanded(collapsed, "module.a", searchActive: true), Is.True);
            Assert.That(collapsed, Is.EqualTo(new[] { "module.a" }));
        }

        [Test]
        public void WorkspaceFoldout_SetAll_ChangesAvailableGroupsAndPreservesMissingGroups()
        {
            var collapsed = new HashSet<string>(StringComparer.Ordinal)
            {
                "module.a",
                "removed.module"
            };

            DashboardWorkspaceFoldout.SetAll(
                collapsed,
                new[] { "module.a", "module.b" },
                expanded: true);
            Assert.That(collapsed, Is.EquivalentTo(new[] { "removed.module" }));

            DashboardWorkspaceFoldout.SetAll(
                collapsed,
                new[] { "module.a", "module.b" },
                expanded: false);
            Assert.That(collapsed, Is.EquivalentTo(new[]
            {
                "module.a",
                "module.b",
                "removed.module"
            }));
        }

        private static T GetPrivateField<T>(ZeroEngineDashboard window, string name)
        {
            FieldInfo field = typeof(ZeroEngineDashboard).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Dashboard field was not found: " + name);
            return (T)field.GetValue(window);
        }

        private static DashboardCatalog Catalog(DashboardPanel panel)
        {
            var source = new DashboardDescriptorSource(
                DashboardSourceKind.Project,
                "Assets/Editor/ZeroEngineDashboardModule.json",
                "Assets/Editor",
                string.Empty,
                string.Empty,
                "{}");
            var module = new DashboardModule(
                "project.workspace",
                "工作区",
                string.Empty,
                0,
                string.Empty,
                string.Empty,
                source,
                Array.Empty<DashboardEntry>(),
                panels: new[] { panel });
            return new DashboardCatalog(
                new[] { module },
                Array.Empty<DashboardInstalledPackage>(),
                Array.Empty<DashboardDiagnostic>());
        }

        private static DashboardPanel Panel(string providerId) => new DashboardPanel(
            "project.workspace",
            "runtime",
            "运行概览",
            string.Empty,
            string.Empty,
            "诊断",
            providerId,
            0,
            DashboardEntrySafety.ReadOnly,
            DashboardEntryAvailability.Always,
            "Assets/Editor/ZeroEngineDashboardModule.json");

        [EditorWorkspacePanelProvider("workspace.lazy")]
        public sealed class LazyProvider : IEditorWorkspacePanelProvider
        {
            internal static int Created;

            public LazyProvider()
            {
                Created++;
            }

            public IEditorWorkspacePanel CreatePanel(string panelId) => new TestPanel();
        }

        [EditorWorkspacePanelProvider("workspace.duplicate")]
        public sealed class DuplicateProviderA : IEditorWorkspacePanelProvider
        {
            public IEditorWorkspacePanel CreatePanel(string panelId) => new TestPanel();
        }

        [EditorWorkspacePanelProvider("workspace.duplicate")]
        public sealed class DuplicateProviderB : IEditorWorkspacePanelProvider
        {
            public IEditorWorkspacePanel CreatePanel(string panelId) => new TestPanel();
        }

        [EditorWorkspacePanelProvider("workspace.throwing")]
        public sealed class ThrowingProvider : IEditorWorkspacePanelProvider
        {
            public IEditorWorkspacePanel CreatePanel(string panelId) => throw new InvalidOperationException("provider failure");
        }

        [EditorWorkspacePanelProvider("workspace.null")]
        public sealed class NullProvider : IEditorWorkspacePanelProvider
        {
            public IEditorWorkspacePanel CreatePanel(string panelId) => null;
        }

        private class TestPanel : IEditorWorkspacePanel
        {
            public float RefreshInterval => 0f;
            public void Activate(EditorWorkspacePanelContext context) { }
            public void Deactivate() { }
            public void Tick(EditorWorkspacePanelContext context, double timeSinceStartup) { }
            public void OnGUI(EditorWorkspacePanelContext context) { }
            public void Dispose() { }
        }

        private sealed class FullWidthTestPanel : TestPanel, IEditorWorkspaceFullWidthPanel
        {
        }
    }
}
