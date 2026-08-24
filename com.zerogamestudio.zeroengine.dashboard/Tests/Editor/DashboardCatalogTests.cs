using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine.TestTools;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;
using ZeroEngine.Editor;
using ZeroEngine.Editor.Dashboard;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Dashboard.Tests.Editor
{
    [TestFixture]
    public sealed class DashboardCatalogTests
    {
        private const string RemovalFixturePackageName = "com.zerogamestudio.dashboard-removal-fixture";

        [Test]
        public void DashboardFixedTooltips_AreNonEmptyChineseText()
        {
            Type textType = typeof(ZeroEngineDashboard).Assembly.GetType("ZeroEngine.Editor.DashboardText");
            Assert.That(textType, Is.Not.Null);
            FieldInfo[] tooltipFields = textType.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(string) && field.Name.EndsWith("Tooltip", StringComparison.Ordinal))
                .ToArray();

            Assert.That(tooltipFields, Has.Length.GreaterThanOrEqualTo(12));
            foreach (FieldInfo field in tooltipFields)
            {
                string value = (string)field.GetRawConstantValue();
                Assert.That(value, Is.Not.Empty, field.Name);
                Assert.That(value.Any(character => character >= '\u3400' && character <= '\u9fff'), Is.True, field.Name);
            }
        }

        [Test]
        public void Build_ValidUtf8Descriptor_UsesDeterministicDefaults()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.tools",
                "工具",
                Entry("open", "窗口", "ZGS/工具/窗口"))));

            Assert.AreEqual(1, catalog.Modules.Count);
            Assert.AreEqual("工具", catalog.Modules[0].DisplayName);
            Assert.AreEqual(0, catalog.Modules[0].Order);
            Assert.AreEqual(0, catalog.Modules[0].Entries[0].Order);
            Assert.AreEqual("常规", catalog.Modules[0].VisibleSurfaces[0].Section);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
            Assert.That(catalog.Diagnostics.Single().Code, Is.EqualTo("legacy-schema-v1"));
        }

        [Test]
        public void Build_EmptyEntries_KeepsModuleInstalledButNotVisible()
        {
            DashboardCatalog catalog = Build(Source(Descriptor("project.empty", "Empty", string.Empty)));

            Assert.AreEqual(1, catalog.Modules.Count);
            Assert.AreEqual(0, catalog.VisibleModules.Count);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
        }

        [Test]
        public void Build_PanelOnlyModule_IsVisibleOnlyInWorkspace()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.panels",
                "Panels",
                string.Empty,
                Panel("runtime", "运行概览", "project.panels"))));

            Assert.AreEqual(1, catalog.Modules.Count);
            Assert.AreEqual(0, catalog.VisibleModules.Count);
            Assert.AreEqual(1, catalog.VisibleWorkspaceModules.Count);
            Assert.AreEqual("project.panels/runtime", catalog.Modules[0].Panels[0].FullId);
            Assert.AreEqual("使用运行概览。", catalog.Modules[0].Panels[0].Usage);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
        }

        [Test]
        public void Build_InvalidPanelProviderId_RejectsDescriptor()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.panels",
                "Panels",
                string.Empty,
                Panel("runtime", "运行概览", "Invalid Provider"))));

            Assert.AreEqual(0, catalog.Modules.Count);
            Assert.That(catalog.Diagnostics.Single().Message, Does.Contain("providerId"));
        }

        [Test]
        public void Build_UnknownSchema_IsolatesOnlyBadDescriptor()
        {
            string bad = Descriptor("project.bad", "Bad", Entry("open", "Open", "ZGS/Bad/Open"))
                .Replace("\"schemaVersion\":1", "\"schemaVersion\":99");
            DashboardCatalog catalog = Build(
                Source(bad, "bad.json"),
                Source(Descriptor("project.good", "Good", Entry("open", "Open", "ZGS/Good/Open")), "good.json"));

            Assert.AreEqual(1, catalog.Modules.Count);
            Assert.AreEqual("project.good", catalog.Modules[0].ModuleId);
            Assert.That(catalog.Diagnostics.Any(item => item.Code == "descriptor-invalid"));
        }

        [Test]
        public void Build_MalformedJson_IsolatesOnlyBadDescriptor()
        {
            DashboardCatalog catalog = Build(
                Source("{not-json", "bad.json"),
                Source(Descriptor("project.good", "Good", Entry("open", "Open", "ZGS/Good/Open")), "good.json"));

            Assert.AreEqual(1, catalog.Modules.Count);
            Assert.AreEqual("project.good", catalog.Modules[0].ModuleId);
            Assert.That(catalog.Diagnostics.Any(item => item.Code == "descriptor-json-invalid"));
        }

        [Test]
        public void Build_DuplicateEntryId_RejectsDescriptor()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.duplicate",
                "Duplicate",
                Entry("open", "One", "ZGS/One") + "," + Entry("open", "Two", "ZGS/Two"))));

            Assert.AreEqual(0, catalog.Modules.Count);
            StringAssert.Contains("duplicate id", catalog.Diagnostics[0].Message);
        }

        [Test]
        public void Build_ValidV2ProviderEntry_UsesExplicitProjectScope()
        {
            DashboardCatalog catalog = Build(Source(DescriptorV2(
                "project.tools",
                "POB 工具",
                EntryV2("open", "打开", "project.tools", "open"),
                scope: "project",
                projectId: "pob",
                projectDisplayName: "POB")));

            DashboardModule module = catalog.Modules.Single();
            DashboardEntry entry = module.Entries.Single();
            Assert.AreEqual(2, module.SchemaVersion);
            Assert.AreEqual(DashboardModuleScope.Project, module.Scope);
            Assert.AreEqual("pob", module.ProjectId);
            Assert.AreEqual(DashboardEntryExecutionKind.Provider, entry.ExecutionKind);
            Assert.AreEqual("project.tools", entry.ProviderId);
            Assert.AreEqual("open", entry.ActionId);
            Assert.IsEmpty(entry.MenuPath);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
        }

        [Test]
        public void Build_V2MenuPath_IsRejected()
        {
            string entry = EntryV2("open", "打开", "project.tools", "open")
                .Replace("\"executionKind\":", "\"menuPath\":\"ZGS/旧入口\",\"executionKind\":");

            DashboardCatalog catalog = Build(Source(DescriptorV2("project.tools", "工具", entry)));

            Assert.AreEqual(0, catalog.Modules.Count);
            StringAssert.Contains("menuPath is not allowed", catalog.Diagnostics.Single().Message);
        }

        [Test]
        public void Build_V2DuplicateProviderActionBinding_IsolatesBothEntries()
        {
            DashboardCatalog catalog = Build(Source(DescriptorV2(
                "project.tools",
                "工具",
                EntryV2("one", "一", "project.tools", "run") + "," +
                EntryV2("two", "二", "project.tools", "run"))));

            Assert.AreEqual(0, catalog.VisibleModules.Count);
            Assert.AreEqual(2, catalog.Diagnostics.Count(item => item.Code == "duplicate-action-binding"));
        }

        [Test]
        public void Build_V2ProjectDocumentation_UsesProjectRoot()
        {
            string projectRoot = Path.Combine(Path.GetTempPath(), "zeroengine-dashboard-project");
            string descriptorRoot = Path.Combine(projectRoot, "Assets", "Editor");
            string descriptor = DescriptorV2("project.docs", "文档", EntryV2("open", "打开", "project.docs", "open"))
                .Replace("\"entries\":", "\"documentationPath\":\"docs/guide.md\",\"entries\":");
            var source = new DashboardDescriptorSource(
                DashboardSourceKind.Project,
                Path.Combine(descriptorRoot, "ZeroEngineDashboardModule.json"),
                descriptorRoot,
                string.Empty,
                string.Empty,
                descriptor,
                projectRootPath: projectRoot);

            DashboardCatalog catalog = Build(new[] { source }, Array.Empty<DashboardInstalledPackage>());

            Assert.AreEqual(Path.GetFullPath(Path.Combine(projectRoot, "docs", "guide.md")), catalog.Modules.Single().DocumentationPath);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
        }

        [Test]
        public void Build_V2RejectsUnknownCategory()
        {
            string entry = EntryV2("open", "打开", "project.tools", "open", category: "setup");

            DashboardCatalog catalog = Build(Source(DescriptorV2("project.tools", "工具", entry)));

            Assert.That(catalog.Modules, Is.Empty);
            StringAssert.Contains("category is invalid", catalog.Diagnostics.Single().Message);
        }

        [Test]
        public void Build_V2ProjectWriteCannotBePrimary()
        {
            string entry = EntryV2(
                "generate",
                "生成",
                "project.tools",
                "generate",
                safety: "project-write",
                confirmation: "确认写入项目？",
                kind: "command");

            DashboardCatalog catalog = Build(Source(DescriptorV2("project.tools", "工具", entry)));

            Assert.That(catalog.Modules, Is.Empty);
            StringAssert.Contains("project-write entries must use advanced or maintenance", catalog.Diagnostics.Single().Message);
        }

        [Test]
        public void Build_V2ContentTypeDefaultsToAction()
        {
            DashboardCatalog catalog = Build(Source(DescriptorV2(
                "project.tools",
                "工具",
                EntryV2("open", "打开", "project.tools", "open"))));

            DashboardModule module = catalog.Modules.Single();
            Assert.That(module.VisibleActions.Select(entry => entry.Id), Is.EqualTo(new[] { "open" }));
            Assert.That(module.VisibleReferences, Is.Empty);
            Assert.That(module.VisibleSurfaces.Count, Is.EqualTo(1));
        }

        [Test]
        public void Build_V2ReferenceIsSeparatedFromActionSurfaces()
        {
            DashboardCatalog catalog = Build(Source(DescriptorV2(
                "project.tools",
                "工具",
                EntryV2("open", "打开", "project.tools", "open") + "," +
                EntryV2("guide", "使用指南", "project.tools", "guide", contentType: "reference"))));

            DashboardModule module = catalog.Modules.Single();
            Assert.That(module.VisibleActions.Select(entry => entry.Id), Is.EqualTo(new[] { "open" }));
            Assert.That(module.VisibleReferences.Select(entry => entry.Id), Is.EqualTo(new[] { "guide" }));
            Assert.That(module.VisibleSurfaces.Select(surface => surface.DefaultEntry.Id), Is.EqualTo(new[] { "open" }));
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
        }

        [Test]
        public void Build_V2ReferenceRejectsWriteSafety()
        {
            string entry = EntryV2(
                "guide",
                "生成指南",
                "project.tools",
                "guide",
                safety: "project-write",
                visibility: "advanced",
                confirmation: "确认生成？",
                kind: "command",
                contentType: "reference");

            DashboardCatalog catalog = Build(Source(DescriptorV2("project.tools", "工具", entry)));

            Assert.That(catalog.Modules, Is.Empty);
            StringAssert.Contains("reference entries must use navigation or read-only", catalog.Diagnostics.Single().Message);
        }

        [Test]
        public void Build_V2RejectsUnknownContentType()
        {
            string entry = EntryV2("guide", "指南", "project.tools", "guide", contentType: "article");

            DashboardCatalog catalog = Build(Source(DescriptorV2("project.tools", "工具", entry)));

            Assert.That(catalog.Modules, Is.Empty);
            StringAssert.Contains("contentType must be action or reference", catalog.Diagnostics.Single().Message);
        }

        [Test]
        public void Build_V2DestructiveActionRequiresMaintenanceVisibility()
        {
            string entry = EntryV2(
                "cleanup",
                "清理",
                "project.tools",
                "cleanup",
                safety: "destructive",
                visibility: "advanced",
                confirmation: "确认清理项目？",
                kind: "command");

            DashboardCatalog catalog = Build(Source(DescriptorV2("project.tools", "工具", entry)));

            Assert.That(catalog.Modules, Is.Empty);
            StringAssert.Contains("destructive entries must use maintenance", catalog.Diagnostics.Single().Message);
        }

        [Test]
        public void Build_V2ProjectScopeRequiresProjectIdentity()
        {
            DashboardCatalog catalog = Build(Source(DescriptorV2(
                "project.tools",
                "工具",
                EntryV2("open", "打开", "project.tools", "open"),
                scope: "project")));

            Assert.That(catalog.Modules, Is.Empty);
            StringAssert.Contains("projectId", catalog.Diagnostics.Single().Message);
            StringAssert.Contains("projectDisplayName", catalog.Diagnostics.Single().Message);
        }

        [Test]
        public void Build_PackageModuleIdMismatch_IsolatesDescriptor()
        {
            DashboardDescriptorSource source = Source(
                Descriptor("com.example.wrong", "Wrong", Entry("open", "Open", "ZGS/Wrong/Open")),
                "package.json",
                DashboardSourceKind.Package,
                "com.example.actual");

            DashboardCatalog catalog = Build(new[] { source }, Installed("com.example.actual"));

            Assert.AreEqual(0, catalog.Modules.Count);
            StringAssert.Contains("Package descriptor moduleId", catalog.Diagnostics[0].Message);
        }

        [Test]
        public void Build_ProjectDescriptorCannotImpersonateInstalledPackage()
        {
            DashboardCatalog catalog = Build(
                new[] { Source(Descriptor("com.example.actual", "Wrong", Entry("open", "Open", "ZGS/Wrong/Open"))) },
                Installed("com.example.actual"));

            Assert.AreEqual(0, catalog.Modules.Count);
            StringAssert.Contains("must not impersonate", catalog.Diagnostics[0].Message);
        }

        [Test]
        public void Build_DuplicateModuleId_IsolatesEveryDescriptor()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.same", "One", Entry("one", "One", "ZGS/One")), "one.json"),
                Source(Descriptor("project.same", "Two", Entry("two", "Two", "ZGS/Two")), "two.json"));

            Assert.AreEqual(0, catalog.Modules.Count);
            Assert.AreEqual(2, catalog.Diagnostics.Count(item => item.Code == "duplicate-module-id"));
        }

        [Test]
        public void Build_DuplicateMenuPath_IsolatesEntriesNotModules()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.one", "One", Entry("open", "One", "ZGS/Shared/Open"))),
                Source(Descriptor("project.two", "Two", Entry("open", "Two", "ZGS/Shared/Open"))));

            Assert.AreEqual(2, catalog.Modules.Count);
            Assert.AreEqual(0, catalog.VisibleModules.Count);
            Assert.AreEqual(2, catalog.Diagnostics.Count(item => item.Code == "duplicate-menu-path"));
        }

        [Test]
        public void Build_CompatibleSurfaceEntries_AppearAsOneSurface()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.formula",
                "Formula",
                Entry(
                    "catalog",
                    "Catalog",
                    "ZGS/Formula/Catalog",
                    section: "Authoring",
                    surfaceId: "formula-studio",
                    surfaceDisplayName: "Formula Studio",
                    surfaceActionLabel: "Catalog",
                    surfaceDefault: true) + "," +
                Entry(
                    "workbench",
                    "Workbench",
                    "ZGS/Formula/Workbench",
                    section: "Authoring",
                    surfaceId: "formula-studio",
                    surfaceDisplayName: "Formula Studio",
                    surfaceActionLabel: "Workbench"))));

            DashboardSurface surface = catalog.VisibleModules.Single().VisibleSurfaces.Single();
            Assert.AreEqual("formula-studio", surface.SurfaceId);
            Assert.AreEqual("Formula Studio", surface.DisplayName);
            Assert.AreEqual("catalog", surface.DefaultEntry.Id);
            CollectionAssert.AreEqual(
                new[] { "Catalog", "Workbench" },
                surface.Entries.Select(entry => entry.SurfaceActionLabel).ToArray());
        }

        [Test]
        public void Build_IncompatibleSurfaceEntries_FallBackToSeparateSurfaces()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.formula",
                "Formula",
                Entry(
                    "catalog",
                    "Catalog",
                    "ZGS/Formula/Catalog",
                    section: "Authoring",
                    surfaceId: "formula-studio",
                    surfaceDisplayName: "Formula Studio") + "," +
                Entry(
                    "workbench",
                    "Workbench",
                    "ZGS/Formula/Workbench",
                    section: "Diagnostics",
                    surfaceId: "formula-studio",
                    surfaceDisplayName: "Formula Studio"))));

            Assert.AreEqual(2, catalog.VisibleModules.Single().VisibleSurfaces.Count);
            Assert.AreEqual(2, catalog.Diagnostics.Count(item => item.Code == "surface-contract-conflict"));
        }

        [Test]
        public void Build_ReplacementChain_HidesTransitiveTargets()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.a", "A", Entry("open", "A", "ZGS/A", "project.b/open"))),
                Source(Descriptor("project.b", "B", Entry("open", "B", "ZGS/B", "project.c/open"))),
                Source(Descriptor("project.c", "C", Entry("open", "C", "ZGS/C"))));

            CollectionAssert.AreEqual(
                new[] { "project.a" },
                catalog.VisibleModules.Select(module => module.ModuleId).ToArray());
            Assert.IsTrue(catalog.Modules.Single(module => module.ModuleId == "project.b").Entries[0].HiddenByReplacement);
            Assert.IsTrue(catalog.Modules.Single(module => module.ModuleId == "project.c").Entries[0].HiddenByReplacement);
        }

        [Test]
        public void Build_MultipleReplacers_IsolatesReplacersAndKeepsTarget()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.a", "A", Entry("open", "A", "ZGS/A", "project.b/open"))),
                Source(Descriptor("project.b", "B", Entry("open", "B", "ZGS/B"))),
                Source(Descriptor("project.c", "C", Entry("open", "C", "ZGS/C", "project.b/open"))));

            CollectionAssert.AreEqual(
                new[] { "project.b" },
                catalog.VisibleModules.Select(module => module.ModuleId).ToArray());
            Assert.AreEqual(2, catalog.Diagnostics.Count(item => item.Code == "multiple-replacers"));
        }

        [Test]
        public void Build_ReplacementCycle_IsolatesCycle()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.a", "A", Entry("open", "A", "ZGS/A", "project.b/open"))),
                Source(Descriptor("project.b", "B", Entry("open", "B", "ZGS/B", "project.a/open"))));

            Assert.AreEqual(0, catalog.VisibleModules.Count);
            Assert.AreEqual(2, catalog.Diagnostics.Count(item => item.Code == "replacement-cycle"));
        }

        [Test]
        public void Build_MountedEntryAppearsOnlyUnderTargetModule()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.target", "Target", string.Empty)),
                Source(Descriptor(
                    "project.adapter",
                    "Adapter",
                    Entry("open", "Profile Tool", "ZGS/Profile/Open", mountModuleId: "project.target"))));

            CollectionAssert.AreEqual(
                new[] { "project.target" },
                catalog.VisibleModules.Select(module => module.ModuleId).ToArray());
            DashboardModule target = catalog.Modules.Single(module => module.ModuleId == "project.target");
            DashboardModule adapter = catalog.Modules.Single(module => module.ModuleId == "project.adapter");
            Assert.AreEqual("project.adapter/open", target.VisibleEntries.Single().FullId);
            Assert.AreEqual(0, adapter.VisibleEntries.Count);
            Assert.AreEqual(1, adapter.OwnedVisibleEntries.Count);
        }

        [Test]
        public void Build_MountPreservesOwnerIdentitySafetyAndSource()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.target", "Target", string.Empty)),
                Source(
                    Descriptor(
                        "project.adapter",
                        "Adapter",
                        Entry(
                            "apply",
                            "Apply Profile",
                            "ZGS/Profile/Apply",
                            replaces: "project.target/legacy",
                            kind: "command",
                            safety: "project-write",
                            confirmation: "Apply the profile?",
                            mountModuleId: "project.target")),
                    "adapter-profile.json"));

            DashboardEntry entry = catalog.Modules
                .Single(module => module.ModuleId == "project.target")
                .VisibleEntries.Single();

            Assert.AreEqual("project.adapter", entry.ModuleId);
            Assert.AreEqual("project.adapter/apply", entry.FullId);
            Assert.AreEqual("ZGS/Profile/Apply", entry.MenuPath);
            Assert.AreEqual(DashboardEntrySafety.ProjectWrite, entry.Safety);
            Assert.AreEqual("Apply the profile?", entry.Confirmation);
            Assert.AreEqual("adapter-profile.json", entry.SourcePath);
            CollectionAssert.AreEqual(new[] { "project.target/legacy" }, entry.Replaces);
        }

        [Test]
        public void Build_MountTargetMissing_HidesEntryAndWarns()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.adapter",
                "Adapter",
                Entry("open", "Profile Tool", "ZGS/Profile/Open", mountModuleId: "project.missing"))));

            Assert.AreEqual(0, catalog.VisibleModules.Count);
            Assert.That(catalog.Diagnostics.Any(item => item.Code == "mount-target-missing"));
        }

        [Test]
        public void Build_MountTargetIsolated_HidesEntryAndWarns()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.target", "Target One", string.Empty), "one.json"),
                Source(Descriptor("project.target", "Target Two", string.Empty), "two.json"),
                Source(Descriptor(
                    "project.adapter",
                    "Adapter",
                    Entry("open", "Profile Tool", "ZGS/Profile/Open", mountModuleId: "project.target"))));

            Assert.AreEqual(0, catalog.VisibleModules.Count);
            Assert.That(catalog.Diagnostics.Any(item => item.Code == "mount-target-missing"));
        }

        [Test]
        public void Build_MountedReplacementUsesTargetModuleAndHidesGenericEntry()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor(
                    "project.target",
                    "Target",
                    Entry("open", "Generic", "ZGS/Generic/Open"))),
                Source(Descriptor(
                    "project.adapter",
                    "Adapter",
                    Entry(
                        "open",
                        "Profile Tool",
                        "ZGS/Profile/Open",
                        "project.target/open",
                        mountModuleId: "project.target"))));

            CollectionAssert.AreEqual(
                new[] { "project.target" },
                catalog.VisibleModules.Select(module => module.ModuleId).ToArray());
            DashboardModule target = catalog.VisibleModules.Single();
            Assert.AreEqual("project.adapter/open", target.VisibleEntries.Single().FullId);
            Assert.IsTrue(target.Entries.Single().HiddenByReplacement);
        }

        [Test]
        public void Build_MountedEntriesUseStableDisplayOrder()
        {
            DashboardCatalog catalog = Build(
                Source(Descriptor("project.target", "Target", string.Empty)),
                Source(Descriptor(
                    "project.adapter-a",
                    "Adapter A",
                    Entry("later", "Later", "ZGS/Profile/Later", order: 200, mountModuleId: "project.target"))),
                Source(Descriptor(
                    "project.adapter-b",
                    "Adapter B",
                    Entry("earlier", "Earlier", "ZGS/Profile/Earlier", order: 100, mountModuleId: "project.target"))));

            CollectionAssert.AreEqual(
                new[] { "project.adapter-b/earlier", "project.adapter-a/later" },
                catalog.VisibleModules.Single().VisibleEntries.Select(entry => entry.FullId).ToArray());
        }

        [Test]
        public void Build_InvalidMountModuleId_RejectsDescriptor()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.adapter",
                "Adapter",
                Entry("open", "Profile Tool", "ZGS/Profile/Open", mountModuleId: "Project Target"))));

            Assert.AreEqual(0, catalog.Modules.Count);
            StringAssert.Contains("mountModuleId", catalog.Diagnostics[0].Message);
        }

        [Test]
        public void Build_MissingTargetModule_IsSilentAndReplacerRemainsVisible()
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.a",
                "A",
                Entry("open", "A", "ZGS/A", "missing.module/open"))));

            Assert.AreEqual(1, catalog.VisibleModules.Count);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Code == "replacement-target-missing"));
        }

        [Test]
        public void Build_InstalledTargetWithoutEntry_WarnsAndReplacerRemainsVisible()
        {
            DashboardCatalog catalog = Build(
                new[]
                {
                    Source(Descriptor(
                        "project.a",
                        "A",
                        Entry("open", "A", "ZGS/A", "com.example.target/open")))
                },
                Installed("com.example.target"));

            Assert.AreEqual(1, catalog.VisibleModules.Count);
            Assert.That(catalog.Diagnostics.Any(item => item.Code == "replacement-target-missing"));
        }

        [TestCase("ZGS/Tools/Open %g")]
        [TestCase("ZGS/Tools/Open _o")]
        public void Build_MenuShortcutSuffix_IsRejected(string menuPath)
        {
            DashboardCatalog catalog = Build(Source(Descriptor(
                "project.bad",
                "Bad",
                Entry("open", "Open", menuPath))));

            Assert.AreEqual(0, catalog.Modules.Count);
            StringAssert.Contains("shortcut suffix", catalog.Diagnostics[0].Message);
        }

        [Test]
        public void Build_WriteCommandWithoutConfirmation_IsRejected()
        {
            string entry = Entry(
                "install",
                "Install",
                "ZGS/Install",
                null,
                "command",
                "project-write",
                confirmation: string.Empty);

            DashboardCatalog catalog = Build(Source(Descriptor("project.bad", "Bad", entry)));

            Assert.AreEqual(0, catalog.Modules.Count);
            StringAssert.Contains("confirmation is required", catalog.Diagnostics[0].Message);
        }

        [Test]
        public void Build_UnknownField_IsIgnoredWithinSchemaVersion()
        {
            string descriptor = Descriptor("project.future", "Future", Entry("open", "Open", "ZGS/Future/Open"))
                .Replace("\"entries\":", "\"futureField\":\"ignored\",\"entries\":");

            DashboardCatalog catalog = Build(Source(descriptor));

            Assert.AreEqual(1, catalog.VisibleModules.Count);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
        }

        [TestCase("../README.md", null)]
        [TestCase(null, "http://example.com/docs")]
        public void Build_UnsafeDocumentationLocation_IsRejected(string documentationPath, string documentationUrl)
        {
            string descriptor = Descriptor("project.docs", "Docs", Entry("open", "Open", "ZGS/Docs/Open"));
            string fields = string.Empty;
            if (documentationPath != null)
                fields += "\"documentationPath\":\"" + documentationPath + "\",";
            if (documentationUrl != null)
                fields += "\"documentationUrl\":\"" + documentationUrl + "\",";
            descriptor = descriptor.Replace("\"entries\":", fields + "\"entries\":");

            DashboardCatalog catalog = Build(Source(descriptor));

            Assert.AreEqual(0, catalog.Modules.Count);
            Assert.AreEqual("descriptor-invalid", catalog.Diagnostics[0].Code);
        }

        [Test]
        public void Discover_LoadsEveryRegisteredPackageDescriptor()
        {
            string[] expectedModuleIds = PackageManagerPackageInfo.GetAllRegisteredPackages()
                .Where(package => File.Exists(Path.Combine(
                    package.resolvedPath,
                    "Editor",
                    DashboardCatalogDiscovery.DescriptorFileName)))
                .Select(package => package.name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            DashboardCatalog catalog = DashboardCatalogDiscovery.Discover();
            string[] actualModuleIds = catalog.Modules
                .Where(module => module.Source.Kind == DashboardSourceKind.Package)
                .Select(module => module.ModuleId)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(expectedModuleIds, actualModuleIds);
            Assert.IsFalse(catalog.Diagnostics.Any(item => item.Severity == DashboardDiagnosticSeverity.Error));
        }

        [Test]
        public void Discover_DescriptorBindingsMatchLoadedOwners()
        {
            var loadedMenuPaths = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(GetLoadableTypes)
                    .SelectMany(type => type.GetMethods(
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly))
                    .SelectMany(method => method.CustomAttributes)
                    .Where(attribute => attribute.AttributeType == typeof(MenuItem) &&
                                        attribute.ConstructorArguments.Count > 0)
                    .Select(attribute => attribute.ConstructorArguments[0].Value as string)
                    .Where(path => !string.IsNullOrEmpty(path)),
                StringComparer.Ordinal);

            DashboardCatalog catalog = DashboardCatalogDiscovery.Discover();
            DashboardEntry[] entries = catalog.Modules
                         .Where(module => module.Source.Kind == DashboardSourceKind.Package &&
                                          module.ModuleId != RemovalFixturePackageName)
                         .SelectMany(module => module.Entries)
                         .ToArray();
            foreach (DashboardEntry entry in entries.Where(entry => entry.IsLegacy))
            {
                Assert.That(
                    loadedMenuPaths.Contains(entry.MenuPath),
                    "No loaded MenuItem attribute matches " + entry.FullId + " -> " + entry.MenuPath);
            }

            DashboardActionRegistry registry = DashboardActionRegistry.Build(catalog);
            Assert.That(registry.Diagnostics, Is.Empty);
            foreach (DashboardEntry entry in entries.Where(entry => !entry.IsLegacy))
            {
                Assert.That(
                    registry.TryGetState(entry, out EditorToolActionState state, out DashboardDiagnostic diagnostic),
                    Is.True,
                    diagnostic?.Message ?? entry.FullId);
                Assert.That(state, Is.Not.Null, entry.FullId);
            }
        }

        [TestCase("Assets/Editor/ZeroEngineDashboardModule.json", true)]
        [TestCase("Assets/Editor/Sub/ZeroEngineDashboardModule.json", true)]
        [TestCase("Assets/Editor/ZeroEngineDashboardModule.json.meta", false)]
        [TestCase("Assets/Editor/Other.json", false)]
        public void AssetPostprocessor_RecognizesOnlyExactDescriptorFilename(string path, bool expected)
        {
            Assert.AreEqual(expected, DashboardDescriptorAssetPostprocessor.ContainsDescriptor(new[] { path }));
        }

        [UnityTest]
        public IEnumerator PackageRemovalEvent_RemovesFixtureEntryFromCatalog()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("ZEROENGINE_DASHBOARD_PACKAGE_REMOVAL_TEST"),
                    "1",
                    StringComparison.Ordinal))
            {
                Assert.Ignore("Runs only in the synthetic package-removal lane.");
            }

            DashboardCatalogSession.Invalidate();
            DashboardCatalog initialCatalog = DashboardCatalogDiscovery.Discover();
            Assert.IsTrue(
                initialCatalog.Modules.Any(module => module.ModuleId == RemovalFixturePackageName),
                "Initial Dashboard discovery did not include the removal fixture. Diagnostics: " +
                string.Join(" | ", initialCatalog.Diagnostics.Select(item => item.Code + ": " + item.Message)));
            bool observedRemovalEvent = false;
            void OnRegisteredPackages(PackageRegistrationEventArgs args)
            {
                observedRemovalEvent |= args.removed.Any(package => package.name == RemovalFixturePackageName);
            }

            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            try
            {
                RemoveRequest request = Client.Remove(RemovalFixturePackageName);
                double deadline = EditorApplication.timeSinceStartup + 60.0;
                while (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline)
                    yield return null;

                Assert.IsTrue(request.IsCompleted, "Package removal request timed out.");
                Assert.AreEqual(StatusCode.Success, request.Status, request.Error?.message);

                while (!observedRemovalEvent && EditorApplication.timeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue(observedRemovalEvent, "registeredPackages did not report the removal.");

                DashboardCatalog refreshedCatalog = DashboardCatalogDiscovery.Discover();
                Assert.IsFalse(refreshedCatalog.Modules.Any(module => module.ModuleId == RemovalFixturePackageName));
            }
            finally
            {
                UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            }
        }

        [Test]
        public void Execute_MenuReturnsFalse_ReportsMenuMissing()
        {
            DashboardEntry entry = BuildEntry();
            var host = new FakeExecutionHost { ExecuteResult = false };

            DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry, host);

            Assert.AreEqual(DashboardExecutionStatus.MenuMissing, result.Status);
            Assert.AreEqual(1, host.ExecuteCount);
        }

        [Test]
        public void Execute_ProjectWrite_DefaultCancel_DoesNotRunMenu()
        {
            DashboardEntry entry = BuildEntry(DashboardEntrySafety.ProjectWrite, "This writes project assets.");
            var host = new FakeExecutionHost { ConfirmResult = false, ExecuteResult = true };

            DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry, host);

            Assert.AreEqual(DashboardExecutionStatus.Cancelled, result.Status);
            Assert.AreEqual(1, host.ConfirmCount);
            Assert.AreEqual(0, host.ExecuteCount);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void Confirm_OnlyAffirmativeDialogResultContinues(bool dialogResult, bool expected)
        {
            Assert.AreEqual(expected, DashboardEntryExecutor.InterpretConfirmationDialogResult(dialogResult));
        }

        [Test]
        public void Execute_Exception_IsLoggedAndReported()
        {
            DashboardEntry entry = BuildEntry();
            var exception = new InvalidOperationException("boom");
            var host = new FakeExecutionHost { ExecuteException = exception };

            DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry, host);

            Assert.AreEqual(DashboardExecutionStatus.Failed, result.Status);
            Assert.AreSame(exception, host.LoggedException);
        }

        [Test]
        public void Execute_EditModeEntryInPlayMode_DoesNotRunMenu()
        {
            DashboardEntry entry = BuildEntry(availability: DashboardEntryAvailability.EditMode);
            var host = new FakeExecutionHost { IsPlaying = true, ExecuteResult = true };

            DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry, host);

            Assert.AreEqual(DashboardExecutionStatus.Unavailable, result.Status);
            Assert.AreEqual(0, host.ExecuteCount);
        }

        [Test]
        public void Execute_PlayModeEntryInEditMode_DoesNotRunMenu()
        {
            DashboardEntry entry = BuildEntry(availability: DashboardEntryAvailability.PlayMode);
            var host = new FakeExecutionHost { IsPlaying = false, ExecuteResult = true };

            DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry, host);

            Assert.AreEqual(DashboardExecutionStatus.Unavailable, result.Status);
            Assert.AreEqual(0, host.ExecuteCount);
        }

        private static DashboardCatalog Build(params DashboardDescriptorSource[] sources)
        {
            return Build(sources, Array.Empty<DashboardInstalledPackage>());
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static DashboardCatalog GetWindowCatalog(ZeroEngineDashboard window)
        {
            FieldInfo field = typeof(ZeroEngineDashboard).GetField(
                "_catalog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Dashboard catalog field was not found.");
            return (DashboardCatalog)field.GetValue(window);
        }

        private static DashboardCatalog Build(
            IReadOnlyList<DashboardDescriptorSource> sources,
            IReadOnlyList<DashboardInstalledPackage> installedPackages)
        {
            return DashboardCatalogBuilder.Build(sources, installedPackages);
        }

        private static DashboardDescriptorSource Source(
            string json,
            string path = "descriptor.json",
            DashboardSourceKind kind = DashboardSourceKind.Project,
            string packageName = "")
        {
            return new DashboardDescriptorSource(
                kind,
                path,
                Path.GetTempPath(),
                packageName,
                "1.0.0",
                json);
        }

        private static DashboardInstalledPackage[] Installed(params string[] names)
        {
            return names.Select(name => new DashboardInstalledPackage(name, "1.0.0", Path.Combine(Path.GetTempPath(), name))).ToArray();
        }

        private static string Descriptor(string moduleId, string displayName, string entries, string panels = null)
        {
            string panelsField = panels == null ? string.Empty : ",\"panels\":[" + panels + "]";
            return "{" +
                   "\"schemaVersion\":1," +
                   "\"moduleId\":\"" + moduleId + "\"," +
                   "\"displayName\":\"" + displayName + "\"," +
                   "\"description\":\"Description\"," +
                   "\"entries\":[" + entries + "]" + panelsField + "}";
        }

        private static string DescriptorV2(
            string moduleId,
            string displayName,
            string entries,
            string scope = "universal",
            string projectId = null,
            string projectDisplayName = null)
        {
            string projectFields = string.IsNullOrEmpty(projectId)
                ? string.Empty
                : "\"projectId\":\"" + projectId + "\"," +
                  "\"projectDisplayName\":\"" + projectDisplayName + "\",";
            return "{" +
                   "\"schemaVersion\":2," +
                   "\"moduleId\":\"" + moduleId + "\"," +
                   "\"displayName\":\"" + displayName + "\"," +
                   "\"description\":\"说明。\"," +
                   "\"scope\":\"" + scope + "\"," +
                   projectFields +
                   "\"entries\":[" + entries + "]}";
        }

        private static string EntryV2(
            string id,
            string displayName,
            string providerId,
            string actionId,
            string safety = "navigation",
            string visibility = "primary",
            string confirmation = null,
            string kind = "window",
            string category = "authoring",
            string contentType = null)
        {
            string confirmationField = confirmation == null
                ? string.Empty
                : ",\"confirmation\":\"" + confirmation + "\"";
            string contentTypeField = contentType == null
                ? string.Empty
                : ",\"contentType\":\"" + contentType + "\"";
            return "{" +
                   "\"id\":\"" + id + "\"," +
                   "\"displayName\":\"" + displayName + "\"," +
                   "\"description\":\"说明。\"," +
                   "\"category\":\"" + category + "\"," +
                   "\"kind\":\"" + kind + "\"," +
                   "\"order\":0," +
                   "\"safety\":\"" + safety + "\"," +
                   "\"availability\":\"always\"," +
                   "\"visibility\":\"" + visibility + "\"," +
                   "\"executionKind\":\"provider\"," +
                   "\"providerId\":\"" + providerId + "\"," +
                   "\"actionId\":\"" + actionId + "\"," +
                   "\"replaces\":[]" + confirmationField + contentTypeField + "}";
        }

        private static string Panel(string id, string displayName, string providerId)
        {
            return "{" +
                   "\"id\":\"" + id + "\"," +
                   "\"displayName\":\"" + displayName + "\"," +
                   "\"description\":\"运行时状态。\"," +
                   "\"usage\":\"使用运行概览。\"," +
                   "\"section\":\"诊断\"," +
                   "\"providerId\":\"" + providerId + "\"," +
                   "\"order\":100," +
                   "\"safety\":\"read-only\"," +
                   "\"availability\":\"always\"}";
        }

        private static string Entry(
            string id,
            string displayName,
            string menuPath,
            string replaces = null,
            string kind = "window",
            string safety = "navigation",
            string availability = "always",
            string confirmation = null,
            int order = 0,
            string mountModuleId = null,
            string section = null,
            string surfaceId = null,
            string surfaceDisplayName = null,
            string surfaceActionLabel = null,
            bool surfaceDefault = false)
        {
            string replaceArray = string.IsNullOrEmpty(replaces) ? string.Empty : "\"" + replaces + "\"";
            string confirmationField = confirmation == null ? string.Empty : ",\"confirmation\":\"" + confirmation + "\"";
            string mountField = string.IsNullOrEmpty(mountModuleId) ? string.Empty :
                ",\"mountModuleId\":\"" + mountModuleId + "\"";
            string sectionField = string.IsNullOrEmpty(section) ? string.Empty : ",\"section\":\"" + section + "\"";
            string surfaceIdField = string.IsNullOrEmpty(surfaceId) ? string.Empty : ",\"surfaceId\":\"" + surfaceId + "\"";
            string surfaceDisplayNameField = string.IsNullOrEmpty(surfaceDisplayName) ? string.Empty :
                ",\"surfaceDisplayName\":\"" + surfaceDisplayName + "\"";
            string surfaceActionLabelField = string.IsNullOrEmpty(surfaceActionLabel) ? string.Empty :
                ",\"surfaceActionLabel\":\"" + surfaceActionLabel + "\"";
            string surfaceDefaultField = surfaceDefault ? ",\"surfaceDefault\":true" : string.Empty;
            return "{" +
                   "\"id\":\"" + id + "\"," +
                   "\"displayName\":\"" + displayName + "\"," +
                   "\"description\":\"Description\"," +
                   "\"category\":\"authoring\"," +
                   "\"kind\":\"" + kind + "\"," +
                   "\"menuPath\":\"" + menuPath + "\"," +
                   "\"order\":" + order + "," +
                   "\"safety\":\"" + safety + "\"," +
                   "\"availability\":\"" + availability + "\"," +
                   "\"replaces\":[" + replaceArray + "]" +
                   confirmationField + mountField + sectionField + surfaceIdField +
                   surfaceDisplayNameField + surfaceActionLabelField + surfaceDefaultField + "}";
        }

        private static DashboardEntry BuildEntry(
            DashboardEntrySafety safety = DashboardEntrySafety.Navigation,
            string confirmation = "",
            DashboardEntryAvailability availability = DashboardEntryAvailability.Always)
        {
            return new DashboardEntry(
                "project.test",
                "open",
                "Open",
                string.Empty,
                "authoring",
                DashboardEntryKind.Command,
                "ZGS/Test/Open",
                0,
                safety,
                confirmation,
                availability,
                Array.Empty<string>(),
                string.Empty,
                "test.json",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                string.Empty);
        }

        private sealed class FakeExecutionHost : IDashboardExecutionHost
        {
            internal bool IsPlaying { get; set; }
            internal bool ConfirmResult { get; set; }
            internal bool ExecuteResult { get; set; }
            internal Exception ExecuteException { get; set; }
            internal int ConfirmCount { get; private set; }
            internal int ExecuteCount { get; private set; }
            internal Exception LoggedException { get; private set; }

            bool IDashboardExecutionHost.IsPlaying => IsPlaying;

            public bool Confirm(DashboardEntry entry)
            {
                ConfirmCount++;
                return ConfirmResult;
            }

            public bool ExecuteMenuItem(string menuPath)
            {
                ExecuteCount++;
                if (ExecuteException != null)
                    throw ExecuteException;
                return ExecuteResult;
            }

            public void LogException(Exception exception)
            {
                LoggedException = exception;
            }
        }
    }
}
