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
            Assert.That(typeof(IEditorWorkspaceRouteNavigator).IsAssignableFrom(typeof(ZeroEngineDashboard)), Is.True);
        }

        [Test]
        public void Dashboard_NavigationKeepsOnlyWorkspaceAndSystemPages()
        {
            FieldInfo pageNamesField = typeof(ZeroEngineDashboard).GetField(
                "PageNames",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(pageNamesField, Is.Not.Null);

            var pageNames = (GUIContent[])pageNamesField.GetValue(null);

            Assert.That(pageNames, Has.Length.EqualTo(2));
            Assert.That(pageNames[0].text, Is.EqualTo(DashboardText.Home));
            Assert.That(pageNames[1].text, Is.EqualTo(DashboardText.System));
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

        [TestCase(0, "", false)]
        [TestCase(0, "数据", true)]
        [TestCase(1, "数据", false)]
        [TestCase(2, "数据", false)]
        public void Dashboard_WorkspaceSearchIsLimitedToHomePage(int page, string search, bool expected)
        {
            Assert.That(ZeroEngineDashboard.UsesWorkspaceSearch(page, search), Is.EqualTo(expected));
        }

        [Test]
        public void Dashboard_WorkspaceNavigationAlwaysReservesScrollbarGutter()
        {
            Assert.That(ZeroEngineDashboard.ReservesWorkspaceNavigationScrollbar(), Is.True);
        }

        [TestCase(320f, 320f, false)]
        [TestCase(320.5f, 320f, false)]
        [TestCase(321f, 320f, true)]
        public void Dashboard_StableScrollbarChromeOnlyAppearsForOverflow(
            float contentHeight,
            float viewportHeight,
            bool expected)
        {
            Assert.That(
                ZeroEngineDashboard.ShouldShowStableVerticalScrollbar(contentHeight, viewportHeight),
                Is.EqualTo(expected));
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
        public void WorkspaceModuleOrigin_UsesCapabilityDependenciesInsteadOfPackagePrefix()
        {
            var installedPackages = new[]
            {
                new DashboardInstalledPackage(
                    "com.zerogamestudio.pob.formula",
                    "0.2.0",
                    "Packages/com.zerogamestudio.pob.formula",
                    dependencies: new[]
                    {
                        "com.zerogamestudio.zeroengine.formula",
                        "com.zerogamestudio.zeroengine.editor-ui"
                    }),
                new DashboardInstalledPackage(
                    "com.zerogamestudio.pob.quest",
                    "0.1.0",
                    "Packages/com.zerogamestudio.pob.quest",
                    dependencies: new[]
                    {
                        "com.zerogamestudio.zeroengine.core",
                        "com.zerogamestudio.zeroengine.dashboard",
                        "com.zerogamestudio.zeroengine.editor-ui"
                    }),
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.formula",
                    "0.6.0",
                    "Packages/com.zerogamestudio.zeroengine.formula",
                    "ZeroEngine Formula"),
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.editor-ui",
                    "1.5.0",
                    "Packages/com.zerogamestudio.zeroengine.editor-ui",
                    "ZeroEngine Editor UI")
            };
            DashboardModule zeroEngine = Module(
                DashboardSourceKind.Package,
                "com.zerogamestudio.zeroengine.data-toolkit",
                DashboardModuleScope.Universal);
            DashboardModule projectAdapter = Module(
                DashboardSourceKind.Package,
                "com.zerogamestudio.pob.formula",
                DashboardModuleScope.Project,
                "POB");
            DashboardModule legacyProjectAdapter = Module(
                DashboardSourceKind.Package,
                "com.zerogamestudio.pob.formula",
                DashboardModuleScope.Universal);
            DashboardModule projectPackage = Module(
                DashboardSourceKind.Package,
                "com.zerogamestudio.pob.quest",
                DashboardModuleScope.Project,
                "POB");
            DashboardModule projectModule = Module(
                DashboardSourceKind.Project,
                string.Empty,
                DashboardModuleScope.Project,
                "POB");

            DashboardWorkspaceOriginPresentation zeroEngineOrigin =
                ZeroEngineDashboard.ResolveWorkspaceModuleOrigin(zeroEngine, installedPackages);
            DashboardWorkspaceOriginPresentation adapterOrigin =
                ZeroEngineDashboard.ResolveWorkspaceModuleOrigin(projectAdapter, installedPackages);
            DashboardWorkspaceOriginPresentation legacyAdapterOrigin =
                ZeroEngineDashboard.ResolveWorkspaceModuleOrigin(legacyProjectAdapter, installedPackages);
            DashboardWorkspaceOriginPresentation projectPackageOrigin =
                ZeroEngineDashboard.ResolveWorkspaceModuleOrigin(projectPackage, installedPackages);
            DashboardWorkspaceOriginPresentation projectModuleOrigin =
                ZeroEngineDashboard.ResolveWorkspaceModuleOrigin(projectModule, installedPackages);

            Assert.That(zeroEngineOrigin.ShortLabel, Is.EqualTo("ZE"));
            Assert.That(zeroEngineOrigin.LongLabel, Is.EqualTo("ZE 通用"));
            Assert.That(adapterOrigin.ShortLabel, Is.EqualTo("ZE·POB"));
            Assert.That(adapterOrigin.LongLabel, Is.EqualTo("ZE 能力 · POB 适配"));
            Assert.That(legacyAdapterOrigin.ShortLabel, Is.EqualTo("ZE·POB"));
            Assert.That(legacyAdapterOrigin.LongLabel, Is.EqualTo("ZE 能力 · POB 适配"));
            Assert.That(projectPackageOrigin.ShortLabel, Is.EqualTo("POB"));
            Assert.That(projectPackageOrigin.LongLabel, Is.EqualTo("POB 项目"));
            Assert.That(projectModuleOrigin.LongLabel, Is.EqualTo("POB 项目"));
        }

        [Test]
        public void WorkspacePanelTooltip_KeepsDescriptionOwnershipAndTechnicalSource()
        {
            var installedPackages = new[]
            {
                new DashboardInstalledPackage(
                    "com.zerogamestudio.pob.formula",
                    "0.2.0",
                    "Packages/com.zerogamestudio.pob.formula",
                    dependencies: new[]
                    {
                        "com.zerogamestudio.zeroengine.formula",
                        "com.zerogamestudio.zeroengine.editor-ui"
                    }),
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.formula",
                    "0.6.0",
                    "Packages/com.zerogamestudio.zeroengine.formula",
                    "ZeroEngine Formula")
            };
            DashboardModule module = Module(
                DashboardSourceKind.Package,
                "com.zerogamestudio.pob.formula",
                DashboardModuleScope.Project,
                "POB");
            var panel = new DashboardPanel(
                "project.workspace",
                "runtime",
                "运行概览",
                "查看项目运行状态。",
                string.Empty,
                "诊断",
                "workspace.lazy",
                0,
                DashboardEntrySafety.ReadOnly,
                DashboardEntryAvailability.Always,
                "Assets/Editor/ZeroEngineDashboardModule.json");

            DashboardWorkspaceOriginPresentation origin =
                ZeroEngineDashboard.ResolveWorkspaceModuleOrigin(module, installedPackages);
            string tooltip = ZeroEngineDashboard.BuildWorkspacePanelTooltip(origin, panel);

            Assert.That(tooltip, Does.StartWith("查看项目运行状态。"));
            Assert.That(tooltip, Does.Contain("归属：ZE 能力 · POB 适配"));
            Assert.That(
                tooltip,
                Does.Contain(
                    "能力来源：ZeroEngine Formula（com.zerogamestudio.zeroengine.formula）"));
            Assert.That(tooltip, Does.Contain("项目接入：POB 薄适配"));
            Assert.That(tooltip, Does.Contain("来源包：com.zerogamestudio.pob.formula"));
            Assert.That(tooltip, Does.Not.Contain("com.zerogamestudio.zeroengine.editor-ui"));
        }

        [TestCase(120f, 70f, 20f, true)]
        [TestCase(100f, 70f, 20f, false)]
        public void WorkspaceModuleOriginBadge_OnlyAppearsWhenTitleStillFits(
            float buttonWidth,
            float titleWidth,
            float badgeWidth,
            bool expected)
        {
            Assert.That(
                ZeroEngineDashboard.ShouldShowWorkspaceModuleOriginBadge(buttonWidth, titleWidth, badgeWidth),
                Is.EqualTo(expected));
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
            Assert.That(
                DashboardText.InstalledWithoutWorkspaceEntry,
                Is.EqualTo("已安装 · 基础能力（无工作台面板）"));
            Assert.That(
                DashboardText.InstalledWorkspaceContent(2, 1, 3),
                Is.EqualTo("已安装 · 2 个工具 · 1 个面板 · 3 份资料"));
        }

        [Test]
        public void PackageCatalog_InstallPlan_UsesDashboardGitPinAndDependencyClosure()
        {
            const string commit = "0123456789abcdef0123456789abcdef01234567";
            const string repository = "https://github.com/ZeroGameStudio-CN/zeroengine.git";
            string dashboardPackageId = repository +
                                        "?path=com.zerogamestudio.zeroengine.dashboard#" +
                                        commit;
            var installed = new[]
            {
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.dashboard",
                    "4.5.3",
                    "Packages/dashboard",
                    packageId: dashboardPackageId,
                    isDirectDependency: true),
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.core",
                    "2.0.0",
                    "Packages/core",
                    packageId: repository + "?path=com.zerogamestudio.zeroengine.core#" + commit,
                    isDirectDependency: true)
            };

            Assert.That(
                DashboardPackageCatalog.TryCreateInstallPlan(
                    dashboardPackageId,
                    "com.zerogamestudio.zeroengine.audio",
                    installed,
                    out DashboardPackageInstallPlan plan,
                    out string reason),
                Is.True,
                reason);
            Assert.That(plan.PackageUrls, Is.EquivalentTo(new[]
            {
                repository + "?path=com.zerogamestudio.zeroengine.audio#" + commit,
                repository + "?path=com.zerogamestudio.zeroengine.persistence#" + commit
            }));
        }

        [Test]
        public void PackageCatalog_InstallPlan_RejectsMixedZeroEnginePins()
        {
            const string commit = "0123456789abcdef0123456789abcdef01234567";
            const string repository = "https://github.com/ZeroGameStudio-CN/zeroengine.git";
            string dashboardPackageId = repository +
                                        "?path=com.zerogamestudio.zeroengine.dashboard#" +
                                        commit;
            var installed = new[]
            {
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.data",
                    "2.0.0",
                    "Packages/data",
                    packageId: repository + "?path=com.zerogamestudio.zeroengine.data#abcdef0123456789abcdef0123456789abcdef01",
                    isDirectDependency: true)
            };

            Assert.That(
                DashboardPackageCatalog.TryCreateInstallPlan(
                    dashboardPackageId,
                    "com.zerogamestudio.zeroengine.audio",
                    installed,
                    out _,
                    out string reason),
                Is.False);
            StringAssert.Contains("统一", reason);
        }

        [Test]
        public void PackageCatalog_InstallPlan_IgnoresTransitivePackagePins()
        {
            const string commit = "0123456789abcdef0123456789abcdef01234567";
            const string repository = "https://github.com/ZeroGameStudio-CN/zeroengine.git";
            string dashboardPackageId = repository +
                                        "?path=com.zerogamestudio.zeroengine.dashboard#" +
                                        commit;
            var installed = new[]
            {
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.dashboard",
                    "4.5.3",
                    "Packages/dashboard",
                    packageId: dashboardPackageId,
                    isDirectDependency: true),
                new DashboardInstalledPackage(
                    "com.zerogamestudio.zeroengine.data",
                    "2.0.0",
                    "Packages/data",
                    packageId: repository + "?path=com.zerogamestudio.zeroengine.data#abcdef0123456789abcdef0123456789abcdef01",
                    isDirectDependency: false)
            };

            Assert.That(
                DashboardPackageCatalog.TryCreateInstallPlan(
                    dashboardPackageId,
                    "com.zerogamestudio.zeroengine.audio",
                    installed,
                    out DashboardPackageInstallPlan plan,
                    out string reason),
                Is.True,
                reason);
            Assert.That(plan.PackageUrls, Is.Not.Empty);
        }

        [Test]
        public void CatalogDiscovery_UsesRequestedManifestPinForDirectPackages()
        {
            const string packageName = "com.zerogamestudio.zeroengine.dashboard";
            const string packageId = "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=" +
                                     packageName +
                                     "#0123456789abcdef0123456789abcdef01234567";
            IReadOnlyDictionary<string, string> packageIds = DashboardCatalogDiscovery.ParseRequestedPackageIds(
                "{\"dependencies\":{\"" + packageName + "\":\"" + packageId + "\"}," +
                "\"testables\":[\"" + packageName + "\"]}");

            Assert.That(packageIds[packageName], Is.EqualTo(packageId));
        }

        [Test]
        public void PackageCatalog_RemoveEligibility_ProtectsInfrastructureAndReverseDependencies()
        {
            var dashboard = new DashboardInstalledPackage(
                "com.zerogamestudio.zeroengine.dashboard",
                "4.5.3",
                "Packages/dashboard",
                isDirectDependency: true);
            var core = new DashboardInstalledPackage(
                "com.zerogamestudio.zeroengine.core",
                "2.0.0",
                "Packages/core",
                isDirectDependency: true);
            var audio = new DashboardInstalledPackage(
                "com.zerogamestudio.zeroengine.audio",
                "2.0.0",
                "Packages/audio",
                displayName: "音频",
                isDirectDependency: true,
                dependencies: new[] { "com.zerogamestudio.zeroengine.core" });
            var assetCatalog = new DashboardInstalledPackage(
                "com.zerogamestudio.zeroengine.asset-catalog",
                "1.0.0",
                "Packages/catalog",
                isDirectDependency: true);
            DashboardInstalledPackage[] installed = { dashboard, core, audio, assetCatalog };

            Assert.That(DashboardPackageCatalog.CanRemove(dashboard, installed, out string dashboardReason), Is.False);
            StringAssert.Contains("工作台", dashboardReason);
            Assert.That(DashboardPackageCatalog.CanRemove(core, installed, out string coreReason), Is.False);
            StringAssert.Contains("音频", coreReason);
            Assert.That(DashboardPackageCatalog.CanRemove(assetCatalog, installed, out string assetCatalogReason), Is.True, assetCatalogReason);
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
        public void ViewStateStore_RoundTripsWorkspaceNavigationAndScrolls()
        {
            string prefix = "ZGS.Dashboard.Tests." + Guid.NewGuid().ToString("N") + ".";
            try
            {
                DashboardViewStateStore.Save(new DashboardViewState
                {
                    Page = 1,
                    Search = "数据",
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
                    SystemScroll = new Vector2(5f, 6f),
                    WorkspaceNavigationScroll = new Vector2(7f, 8f),
                    WorkspaceContentScroll = new Vector2(9f, 10f),
                    ContextScroll = new Vector2(11f, 12f),
                    RouteSourceModuleId = "com.zerogamestudio.zeroengine.project-atlas",
                    RouteSourcePanelId = "project-atlas",
                    RouteSourceSubrouteId = "characters",
                    RouteSourceDisplayName = "角色 > 角色档案"
                }, prefix);

                DashboardViewState state = DashboardViewStateStore.Load(prefix);

                Assert.That(state.Page, Is.EqualTo(1));
                Assert.That(state.Search, Is.EqualTo("数据"));
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
                Assert.That(state.RouteSourceModuleId, Is.EqualTo("com.zerogamestudio.zeroengine.project-atlas"));
                Assert.That(state.RouteSourcePanelId, Is.EqualTo("project-atlas"));
                Assert.That(state.RouteSourceSubrouteId, Is.EqualTo("characters"));
                Assert.That(state.RouteSourceDisplayName, Is.EqualTo("角色 > 角色档案"));
            }
            finally
            {
                DashboardViewStateStore.Delete(prefix);
            }
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(3, 0)]
        public void ViewStateStore_MigratesLegacyFourPageNavigation(int legacyPage, int expectedPage)
        {
            string prefix = "ZGS.Dashboard.Tests." + Guid.NewGuid().ToString("N") + ".";
            try
            {
                UnityEditor.EditorPrefs.SetInt(prefix + "Page", legacyPage);

                DashboardViewState state = DashboardViewStateStore.Load(prefix);

                Assert.That(state.Page, Is.EqualTo(expectedPage));
            }
            finally
            {
                DashboardViewStateStore.Delete(prefix);
            }
        }

        [Test]
        public void ViewStateStore_MigratesRemovedHelpPageToHome()
        {
            string prefix = "ZGS.Dashboard.Tests." + Guid.NewGuid().ToString("N") + ".";
            try
            {
                UnityEditor.EditorPrefs.SetInt(prefix + "NavigationVersion", 2);
                UnityEditor.EditorPrefs.SetInt(prefix + "Page", 2);

                DashboardViewState state = DashboardViewStateStore.Load(prefix);

                Assert.That(state.Page, Is.EqualTo(0));
            }
            finally
            {
                DashboardViewStateStore.Delete(prefix);
            }
        }

        [Test]
        public void ViewStateStore_MigratesAndDeletesDeprecatedAllToolsState()
        {
            string prefix = "ZGS.Dashboard.Tests." + Guid.NewGuid().ToString("N") + ".";
            try
            {
                UnityEditor.EditorPrefs.SetInt(prefix + "NavigationVersion", 1);
                UnityEditor.EditorPrefs.SetInt(prefix + "Page", 0);
                UnityEditor.EditorPrefs.SetInt(prefix + "HomeView", 1);
                UnityEditor.EditorPrefs.SetString(prefix + "SelectedCategory", "diagnostics");
                UnityEditor.EditorPrefs.SetBool(prefix + "ShowMaintenance", true);

                DashboardViewState state = DashboardViewStateStore.Load(prefix);
                Assert.That(state.Page, Is.EqualTo(0));

                DashboardViewStateStore.Save(state, prefix);

                Assert.That(UnityEditor.EditorPrefs.HasKey(prefix + "HomeView"), Is.False);
                Assert.That(UnityEditor.EditorPrefs.HasKey(prefix + "SelectedCategory"), Is.False);
                Assert.That(UnityEditor.EditorPrefs.HasKey(prefix + "ShowMaintenance"), Is.False);
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

        private static DashboardModule Module(
            DashboardSourceKind sourceKind,
            string packageName,
            DashboardModuleScope scope,
            string projectDisplayName = null)
        {
            var source = new DashboardDescriptorSource(
                sourceKind,
                sourceKind == DashboardSourceKind.Package
                    ? "Packages/" + packageName + "/Editor/ZeroEngineDashboardModule.json"
                    : "Assets/Editor/ZeroEngineDashboardModule.json",
                sourceKind == DashboardSourceKind.Package ? "Packages/" + packageName : "Assets/Editor",
                packageName,
                string.Empty,
                "{}");
            return new DashboardModule(
                packageName.Length == 0 ? "project.workspace" : packageName,
                "测试模块",
                "测试模块说明",
                0,
                string.Empty,
                string.Empty,
                source,
                Array.Empty<DashboardEntry>(),
                panels: new[] { Panel("workspace.lazy") },
                schemaVersion: 2,
                scope: scope,
                projectId: projectDisplayName,
                projectDisplayName: projectDisplayName);
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
