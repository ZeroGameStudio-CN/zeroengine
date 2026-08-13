using System;
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

        [Test]
        public void ViewStateStore_RoundTripsNavigationFiltersAndScrolls()
        {
            string prefix = "ZGS.Dashboard.Tests." + Guid.NewGuid().ToString("N") + ".";
            try
            {
                DashboardViewStateStore.Save(new DashboardViewState
                {
                    Page = 3,
                    Search = "数据",
                    SelectedCategoryId = "data-localization",
                    SelectedScopeId = "pob",
                    SelectedSafetyId = "read-only",
                    SelectedAvailabilityId = "available",
                    ShowAdvanced = false,
                    ShowMaintenance = true,
                    SelectedPanelFullId = "pob.tools.data-manager/data-manager",
                    ModuleScroll = new Vector2(1f, 2f),
                    ContentScroll = new Vector2(3f, 4f),
                    SystemScroll = new Vector2(5f, 6f),
                    WorkspaceNavigationScroll = new Vector2(7f, 8f),
                    WorkspaceContentScroll = new Vector2(9f, 10f),
                    ContextScroll = new Vector2(11f, 12f)
                }, prefix);

                DashboardViewState state = DashboardViewStateStore.Load(prefix);

                Assert.That(state.Page, Is.EqualTo(3));
                Assert.That(state.Search, Is.EqualTo("数据"));
                Assert.That(state.SelectedCategoryId, Is.EqualTo("data-localization"));
                Assert.That(state.SelectedScopeId, Is.EqualTo("pob"));
                Assert.That(state.SelectedSafetyId, Is.EqualTo("read-only"));
                Assert.That(state.SelectedAvailabilityId, Is.EqualTo("available"));
                Assert.That(state.ShowAdvanced, Is.False);
                Assert.That(state.ShowMaintenance, Is.True);
                Assert.That(state.SelectedPanelFullId, Is.EqualTo("pob.tools.data-manager/data-manager"));
                Assert.That(state.WorkspaceContentScroll, Is.EqualTo(new Vector2(9f, 10f)));
                Assert.That(state.ContextScroll, Is.EqualTo(new Vector2(11f, 12f)));
            }
            finally
            {
                DashboardViewStateStore.Delete(prefix);
            }
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

        private sealed class TestPanel : IEditorWorkspacePanel
        {
            public float RefreshInterval => 0f;
            public void Activate(EditorWorkspacePanelContext context) { }
            public void Deactivate() { }
            public void Tick(EditorWorkspacePanelContext context, double timeSinceStartup) { }
            public void OnGUI(EditorWorkspacePanelContext context) { }
            public void Dispose() { }
        }
    }
}
