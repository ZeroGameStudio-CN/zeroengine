using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroEngine.ProjectAtlas.Tests
{
    public sealed class ProjectAtlasCatalogTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "project-atlas-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
                Directory.Delete(_projectRoot, true);
        }

        [Test]
        public void LoadProject_MinimalValidProject_BuildsOneGraphForThreeViews()
        {
            WriteValidProject();

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.HasErrors, Is.False, FormatDiagnostics(graph));
            Assert.That(graph.Systems.Select(system => system.Id), Is.EqualTo(new[] { "project-foundation" }));
            Assert.That(graph.Systems[0].Team.Purpose, Is.Not.Empty);
            Assert.That(graph.Systems[0].Program.StructureRefs, Contains.Item("package.atlas"));
            Assert.That(graph.Systems[0].Agent.ReadFirstRefs, Contains.Item("rules.agents"));
            Assert.That(graph.Systems, Is.Not.InstanceOf<ProjectAtlasSystem[]>());
            Assert.That(graph.Resolutions, Is.Not.InstanceOf<Dictionary<string, ProjectAtlasReferenceResolution>>());
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ProjectAtlasSystem>)graph.Systems).Add(graph.Systems[0]));
        }

        [Test]
        public void LoadProject_MultipleFragments_ResolvesCrossFragmentRelation()
        {
            WriteValidProject(
                sources: new[]
                {
                    "docs/architecture/project-atlas/foundation.json",
                    "docs/architecture/project-atlas/feature.json"
                },
                extraFragment: "{\"schemaVersion\":1,\"references\":[],\"systems\":[" +
                               SystemJson("feature", "功能", "project-foundation") + "]}");

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.HasErrors, Is.False, FormatDiagnostics(graph));
            Assert.That(graph.Systems.Count, Is.EqualTo(2));
            Assert.That(graph.FindSystem("feature").Relations.Single().TargetSystemId, Is.EqualTo("project-foundation"));
        }

        [Test]
        public void LoadProject_DuplicateCaseFoldedSource_FailsClosed()
        {
            WriteValidProject(sources: new[]
            {
                "docs/architecture/project-atlas/foundation.json",
                "docs/architecture/project-atlas/Foundation.json"
            });

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.Diagnostics.Any(item => item.Code == "duplicate-source"), Is.True, FormatDiagnostics(graph));
        }

        [Test]
        public void LoadProject_FragmentOutsideContractDirectory_FailsClosed()
        {
            WriteValidProject(sources: new[] { "docs/architecture/other.json" });

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.Diagnostics.Any(item => item.Code == "invalid-source-location"), Is.True, FormatDiagnostics(graph));
        }

        [Test]
        public void LoadProject_DuplicateReferenceAndSystemIds_FailClosed()
        {
            WriteValidProject(
                sources: new[]
                {
                    "docs/architecture/project-atlas/foundation.json",
                    "docs/architecture/project-atlas/feature.json"
                },
                extraFragment: "{\"schemaVersion\":1,\"references\":[],\"systems\":[" +
                               SystemJson("project-foundation", "重复项目基础", null) + "]}",
                extraReference: "," + ReferenceJson("rules.agents", "doc", "AGENTS.md", true, string.Empty));

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.Diagnostics.Any(item => item.Code == "duplicate-reference-id"), Is.True, FormatDiagnostics(graph));
            Assert.That(graph.Diagnostics.Any(item => item.Code == "duplicate-system-id"), Is.True, FormatDiagnostics(graph));
        }

        [Test]
        public void LoadProject_UnknownSchema_FailsClosed()
        {
            WriteValidProject(rootSchemaVersion: 99);

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.Project, Is.Null);
            Assert.That(graph.Diagnostics.Any(item => item.Code == "unsupported-schema-version"), Is.True);
        }

        [Test]
        public void LoadProject_UnknownJsonField_IsRejected()
        {
            WriteValidProject(rootExtraField: ",\"command\":\"do-not-run\"");

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.Diagnostics.Any(item => item.Code == "invalid-root-json"), Is.True, FormatDiagnostics(graph));
        }

        [TestCase("../outside.json")]
        [TestCase("docs/architecture/../../outside.json")]
        [TestCase("C:/outside.json")]
        [TestCase("docs\\architecture\\project-atlas.json")]
        public void ResolveSafeProjectPath_UnsafePath_Throws(string path)
        {
            Assert.Throws<InvalidOperationException>(() =>
                ProjectAtlasCatalogLoader.ResolveSafeProjectPath(_projectRoot, path, false));
        }

        [Test]
        public void LoadProject_OptionalMissingReference_IsWarningOnly()
        {
            WriteValidProject(extraReference: "," + ReferenceJson("optional.missing", "doc", "docs/missing.md", false, string.Empty));

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.HasErrors, Is.False, FormatDiagnostics(graph));
            Assert.That(graph.Diagnostics.Any(item => item.Code == "unresolved-reference" && item.Severity == ProjectAtlasDiagnosticSeverity.Warning), Is.True);
        }

        [Test]
        public void LoadProject_RequiredMissingReference_IsError()
        {
            WriteValidProject(
                extraReference: "," + ReferenceJson("required.missing", "doc", "docs/missing.md", true, string.Empty),
                extraProgramReference: ",\"required.missing\"");

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.Diagnostics.Any(item => item.Code == "unresolved-reference" && item.Severity == ProjectAtlasDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void LoadProject_WhitespaceLifecycle_CannotBypassActiveProjectionRequirements()
        {
            WriteValidProject();
            string fragmentPath = Path.Combine(
                _projectRoot,
                "docs",
                "architecture",
                "project-atlas",
                "foundation.json");
            string fragment = File.ReadAllText(fragmentPath)
                .Replace("\"lifecycle\":\"active\"", "\"lifecycle\":\" active \"")
                .Replace("\"entryRefs\":[\"rules.agents\"]", "\"entryRefs\":[]")
                .Replace("\"structureRefs\":[\"package.atlas\"]", "\"structureRefs\":[]")
                .Replace("\"readFirstRefs\":[\"rules.agents\"]", "\"readFirstRefs\":[]")
                .Replace("\"verificationRefs\":[\"validation.atlas\"]", "\"verificationRefs\":[]");
            File.WriteAllText(fragmentPath, fragment);

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(graph.Diagnostics.Any(item => item.Code == "missing-program-route"), Is.True, FormatDiagnostics(graph));
            Assert.That(graph.Diagnostics.Any(item => item.Code == "missing-agent-rule"), Is.True, FormatDiagnostics(graph));
            Assert.That(graph.Diagnostics.Any(item => item.Code == "missing-agent-verification"), Is.True, FormatDiagnostics(graph));
        }

        [Test]
        public void Coverage_ValidationReferenceMustBeBackedByProjectProvider()
        {
            WriteValidProject(
                extraReference: "," + ReferenceJson("validation.phantom", "validation-lane", "phantom-lane", true, string.Empty),
                extraProgramReference: ",\"validation.phantom\"");

            ProjectAtlasGraph graph = LoadFixture();

            Assert.That(
                graph.Diagnostics.Any(item => item.Code == "reference-not-backed-by-coverage"),
                Is.True,
                FormatDiagnostics(graph));
        }

        [Test]
        public void LoadProject_DuplicateResolverKind_IsIsolatedAsDiagnostic()
        {
            WriteValidProject(extraReference: "," + ReferenceJson("fixture.custom", "fixture.ref", "one", false, string.Empty));

            ProjectAtlasGraph graph = ProjectAtlasCatalogLoader.LoadProject(
                _projectRoot,
                new[] { typeof(FixtureResolverA), typeof(FixtureResolverB) },
                FixtureCoverageTypes,
                true);

            Assert.That(graph.Diagnostics.Any(item => item.Code == "duplicate-resolver-kind"), Is.True, FormatDiagnostics(graph));
        }

        [Test]
        public void LoadProject_ThrowingResolver_DoesNotStopBuiltInReferences()
        {
            WriteValidProject(extraReference: "," + ReferenceJson("fixture.throwing", "fixture.throwing", "one", false, string.Empty));

            ProjectAtlasGraph graph = ProjectAtlasCatalogLoader.LoadProject(
                _projectRoot,
                new[] { typeof(ThrowingResolver) },
                FixtureCoverageTypes,
                true);

            Assert.That(graph.Diagnostics.Any(item => item.Code == "resolver-exception"), Is.True, FormatDiagnostics(graph));
            Assert.That(graph.Resolutions["rules.agents"].Status, Is.EqualTo(ProjectAtlasResolutionStatus.Resolved));
        }

        [Test]
        public void Coverage_ExactExclusion_IsAcceptedAndStaleExclusionFails()
        {
            WriteValidProject(
                projectId: "fixture-exclusion",
                exclusions: "{\"kind\":\"assembly\",\"target\":\"Assets/Runtime.asmdef\",\"reason\":\"测试 fixture 不创建运行时程序集。\"}",
                packageDependencies: string.Empty);

            ProjectAtlasGraph accepted = ProjectAtlasCatalogLoader.LoadProject(
                _projectRoot,
                Array.Empty<Type>(),
                ExclusionCoverageTypes,
                true);
            Assert.That(accepted.Diagnostics.Any(item => item.Code == "unowned-coverage-item"), Is.False, FormatDiagnostics(accepted));
            Assert.That(accepted.Diagnostics.Any(item => item.Code == "stale-coverage-exclusion"), Is.False, FormatDiagnostics(accepted));

            string rootPath = Path.Combine(_projectRoot, ProjectAtlasCatalogLoader.RootCatalogPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(rootPath, File.ReadAllText(rootPath).Replace("Assets/Runtime.asmdef", "Assets/Stale.asmdef"));
            ProjectAtlasGraph stale = ProjectAtlasCatalogLoader.LoadProject(
                _projectRoot,
                Array.Empty<Type>(),
                ExclusionCoverageTypes,
                true);
            Assert.That(stale.Diagnostics.Any(item => item.Code == "stale-coverage-exclusion"), Is.True, FormatDiagnostics(stale));
        }

        [Test]
        public void Markdown_SameGraph_IsByteDeterministicAndFreshnessDetectsDrift()
        {
            WriteValidProject();
            ProjectAtlasGraph graph = LoadFixture();
            Assert.That(graph.HasErrors, Is.False, FormatDiagnostics(graph));

            string first = ProjectAtlasMarkdownProjector.Render(graph);
            string second = ProjectAtlasMarkdownProjector.Render(graph);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Contain("## 项目与功能"));
            Assert.That(first, Does.Contain("## 架构与路由"));
            Assert.That(first, Does.Contain("## Agent 改动合同"));
            Assert.That(first, Does.Contain("[项目基础](#team-project-foundation)"));
            Assert.That(first, Does.Contain("<a id=\"team-project-foundation\"></a>"));

            ProjectAtlasProjectWriter.WriteGeneratedIndex(graph);
            Assert.That(ProjectAtlasValidator.IsProjectionCurrent(graph), Is.True);
            string indexPath = Path.Combine(_projectRoot, ProjectAtlasCatalogLoader.GeneratedIndexPath.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(indexPath, "manual drift\n");
            Assert.That(ProjectAtlasValidator.IsProjectionCurrent(graph), Is.False);
        }

        [Test]
        public void Markdown_AbsoluteLocalPackageReference_IsRedacted()
        {
            WriteValidProject(packageDependencies:
                "\"com.zerogamestudio.zeroengine.project-atlas\":\"file:D:/private/worktrees/project-atlas\"");
            ProjectAtlasGraph graph = LoadFixture();
            Assert.That(graph.HasErrors, Is.False, FormatDiagnostics(graph));

            string projection = ProjectAtlasMarkdownProjector.Render(graph);

            Assert.That(projection, Does.Contain("file:<local>"));
            Assert.That(projection, Does.Not.Contain("D:/private/worktrees"));
        }

        [Test]
        public void LoadProject_NoRootCatalog_ReturnsReadOnlyOnboardingDiagnostic()
        {
            ProjectAtlasGraph graph = ProjectAtlasCatalogLoader.LoadProject(_projectRoot);

            Assert.That(graph.Project, Is.Null);
            Assert.That(graph.HasErrors, Is.False);
            Assert.That(graph.Diagnostics.Single().Code, Is.EqualTo("catalog-not-configured"));
        }

        [Test]
        public void WorkspaceProvider_KnownPanel_IsFullWidthAndUnknownPanelIsRejected()
        {
            var provider = new ProjectAtlasWorkspacePanelProvider();

            IEditorWorkspacePanel panel = provider.CreatePanel("project-atlas");

            Assert.That(panel, Is.InstanceOf<IEditorWorkspaceFullWidthPanel>());
            Assert.That(provider.CreatePanel("unknown"), Is.Null);
            panel.Dispose();
        }

        [Test]
        public void WorkspacePanel_NarrowWidth_ShrinksThreeColumnsAndElidesLongLabels()
        {
            var provider = new ProjectAtlasWorkspacePanelProvider();
            IEditorWorkspacePanel panel = provider.CreatePanel("project-atlas");
            Type panelType = panel.GetType();
            MethodInfo resolveBodyContentWidth = panelType.GetMethod(
                "ResolveBodyContentWidth",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo calculateBodyLayoutRects = panelType.GetMethod(
                "CalculateBodyLayoutRects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo elideLabel = panelType.GetMethod(
                "ElideLabel",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resolveBodyContentWidth, Is.Not.Null);
            Assert.That(
                (float)resolveBodyContentWidth.Invoke(null, new object[] { 420f }),
                Is.EqualTo(420f),
                "420 point 窗口应直接收缩三栏，不应为了完整标题强制横向滚动。");
            Assert.That(
                (float)resolveBodyContentWidth.Invoke(null, new object[] { 1440f }),
                Is.EqualTo(1440f));

            Assert.That(calculateBodyLayoutRects, Is.Not.Null);
            panelType.GetField("_domainColumnWidth", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(panel, 300f);
            panelType.GetField("_featureColumnWidth", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(panel, 420f);
            object layoutRects = calculateBodyLayoutRects.Invoke(
                panel,
                new object[] { new Rect(0f, 0f, 420f, 600f) });
            Type layoutType = layoutRects.GetType();
            Rect domainRect = (Rect)layoutType.GetProperty("DomainColumn")?.GetValue(layoutRects);
            Rect featureRect = (Rect)layoutType.GetProperty("FeatureColumn")?.GetValue(layoutRects);
            Rect detailRect = (Rect)layoutType.GetProperty("DetailColumn")?.GetValue(layoutRects);
            Assert.That(domainRect.width, Is.LessThan(300f));
            Assert.That(featureRect.width, Is.LessThan(420f));
            Assert.That(detailRect.width, Is.GreaterThanOrEqualTo(180f));
            Assert.That(detailRect.xMax, Is.EqualTo(420f).Within(0.01f));

            Assert.That(elideLabel, Is.Not.Null);
            const string longLabel = "地图导航与小地图配置入口";
            string elided = (string)elideLabel.Invoke(
                null,
                new object[] { longLabel, EditorStyles.miniButton, 72f });
            Assert.That(elided, Is.Not.EqualTo(longLabel));
            Assert.That(elided, Does.EndWith("…"));
            Assert.That(
                panelType.GetMethod("DrawCompact", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null,
                "项目领域与功能导航不得恢复为紧凑下拉模式。");
            Assert.That(
                panelType.GetMethod("DrawFeaturePicker", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null,
                "功能导航不得恢复为 Popup picker。");
            Assert.That(
                panelType.GetMethod("DrawHeader", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null,
                "工作台宿主已经显示面板名称，内容区不得再重复绘制“项目功能”标题。");

            Assert.That(
                panelType.GetMethod("DrawFeatureRow", BindingFlags.Static | BindingFlags.NonPublic),
                Is.Null,
                "功能按钮不得恢复为名称与状态左右叠绘的自定义行。");

            panel.Dispose();
        }

        private static Type[] FixtureCoverageTypes => new[]
        {
            typeof(FixtureRuntimeCoverage),
            typeof(FixtureConfigCoverage),
            typeof(FixtureValidationCoverage)
        };

        private static Type[] ExclusionCoverageTypes => new[]
        {
            typeof(ExclusionRuntimeCoverage),
            typeof(ExclusionConfigCoverage),
            typeof(ExclusionValidationCoverage)
        };

        private ProjectAtlasGraph LoadFixture()
        {
            return ProjectAtlasCatalogLoader.LoadProject(
                _projectRoot,
                Array.Empty<Type>(),
                FixtureCoverageTypes,
                true);
        }

        private void WriteValidProject(
            string projectId = "fixture",
            int rootSchemaVersion = 1,
            string rootExtraField = "",
            string[] sources = null,
            string extraFragment = null,
            string extraReference = "",
            string extraProgramReference = "",
            string exclusions = "",
            string packageDependencies = "\"com.zerogamestudio.zeroengine.project-atlas\":\"file:../atlas\"")
        {
            string docsPath = Path.Combine(_projectRoot, "docs", "architecture", "project-atlas");
            string packagesPath = Path.Combine(_projectRoot, "Packages");
            Directory.CreateDirectory(docsPath);
            Directory.CreateDirectory(packagesPath);
            File.WriteAllText(Path.Combine(_projectRoot, "AGENTS.md"), "# Agent rules\n");

            string[] actualSources = sources ?? new[] { "docs/architecture/project-atlas/foundation.json" };
            string sourceJson = string.Join(",", actualSources.Select(value => "\"" + value + "\""));
            string exclusionJson = string.IsNullOrEmpty(exclusions) ? string.Empty : exclusions;
            string rootJson = "{\"schemaVersion\":" + rootSchemaVersion +
                              ",\"project\":{\"id\":\"" + projectId +
                              "\",\"displayName\":\"Fixture\",\"summary\":\"用于验证跨项目合同的合成项目。\",\"rootAgentRule\":\"rules.agents\"}" +
                              ",\"sources\":[" + sourceJson + "]" +
                              ",\"coverageExclusions\":[" + exclusionJson + "]" + rootExtraField + "}";
            File.WriteAllText(Path.Combine(_projectRoot, "docs", "architecture", "project-atlas.json"), rootJson);
            File.WriteAllText(Path.Combine(docsPath, "foundation.json"), ValidFragment(extraReference, extraProgramReference));
            if (actualSources.Length > 1 && extraFragment != null)
                File.WriteAllText(Path.Combine(docsPath, "feature.json"), extraFragment);

            File.WriteAllText(Path.Combine(packagesPath, "manifest.json"),
                "{\"dependencies\":{" + packageDependencies + "}}");
            File.WriteAllText(Path.Combine(packagesPath, "packages-lock.json"),
                "{\"dependencies\":{\"com.zerogamestudio.zeroengine.project-atlas\":{\"version\":\"file:../atlas\",\"depth\":0}}}");
        }

        private static string ValidFragment(string extraReference, string extraProgramReference)
        {
            return "{\"schemaVersion\":1,\"references\":[" +
                   ReferenceJson("rules.agents", "doc", "AGENTS.md", true, string.Empty) + "," +
                   ReferenceJson("package.atlas", "package", "com.zerogamestudio.zeroengine.project-atlas", true, "project-foundation") + "," +
                   ReferenceJson("validation.atlas", "validation-lane", "atlas-boundary", true, "project-foundation") +
                   extraReference + "],\"systems\":[" + SystemJson("project-foundation", "项目基础", null, extraProgramReference) + "]}";
        }

        private static string ReferenceJson(string id, string kind, string target, bool required, string owner)
        {
            return "{\"id\":\"" + id + "\",\"kind\":\"" + kind + "\",\"target\":\"" + target +
                   "\",\"displayName\":\"" + id + "\",\"required\":" + required.ToString().ToLowerInvariant() +
                   (string.IsNullOrEmpty(owner) ? string.Empty : ",\"coverageOwnerSystemId\":\"" + owner + "\"") + "}";
        }

        private static string SystemJson(string id, string displayName, string relationTarget, string extraProgramReference = "")
        {
            string relations = string.IsNullOrEmpty(relationTarget)
                ? string.Empty
                : "{\"kind\":\"depends-on\",\"targetSystemId\":\"" + relationTarget + "\"}";
            return "{\"id\":\"" + id + "\",\"displayName\":\"" + displayName +
                   "\",\"summary\":\"说明系统为项目提供的能力。\",\"category\":\"foundation\",\"order\":10," +
                   "\"keywords\":[\"fixture\"],\"ownerRoles\":[\"程序\"],\"lifecycle\":\"active\",\"ownership\":\"mixed\"," +
                   "\"team\":{\"purpose\":\"让团队理解系统用途。\",\"audiences\":[\"测试\"],\"workflows\":[\"查看系统\"]," +
                   "\"configurationMode\":\"none\",\"configurationReason\":\"合成项目没有业务配置。\",\"configurationRefs\":[],\"diagnosticRefs\":[\"rules.agents\"]}," +
                   "\"program\":{\"entryRefs\":[\"rules.agents\"],\"structureRefs\":[\"package.atlas\"" + extraProgramReference +
                   "],\"dataFlow\":[\"catalog → graph\"],\"verificationRefs\":[\"validation.atlas\"]}," +
                   "\"agent\":{\"readFirstRefs\":[\"rules.agents\"],\"changeBoundary\":\"只改 fixture。\",\"verificationRefs\":[\"validation.atlas\"]," +
                   "\"updateTriggers\":[\"系统入口变化\"]},\"relations\":[" + relations + "]}";
        }

        private static string FormatDiagnostics(ProjectAtlasGraph graph)
        {
            return string.Join("\n", graph.Diagnostics.Select(item => item.Severity + " " + item.Code + " " + item.Message));
        }

        [ProjectAtlasReferenceResolver("fixture", "fixture.resolver-a", "fixture.ref")]
        private sealed class FixtureResolverA : IProjectAtlasReferenceResolver
        {
            public ProjectAtlasReferenceResolution Resolve(ProjectAtlasContext context, ProjectAtlasReference reference)
            {
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Resolved, reference.Target, "fixture");
            }
        }

        [ProjectAtlasReferenceResolver("fixture", "fixture.resolver-b", "fixture.ref")]
        private sealed class FixtureResolverB : IProjectAtlasReferenceResolver
        {
            public ProjectAtlasReferenceResolution Resolve(ProjectAtlasContext context, ProjectAtlasReference reference)
            {
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Resolved, reference.Target, "fixture");
            }
        }

        [ProjectAtlasReferenceResolver("fixture", "fixture.throwing", "fixture.throwing")]
        private sealed class ThrowingResolver : IProjectAtlasReferenceResolver
        {
            public ProjectAtlasReferenceResolution Resolve(ProjectAtlasContext context, ProjectAtlasReference reference)
            {
                throw new InvalidOperationException("synthetic resolver failure");
            }
        }

        [ProjectAtlasCoverageProvider("fixture", "fixture.runtime", "runtime-assemblies")]
        private sealed class FixtureRuntimeCoverage : IProjectAtlasCoverageProvider
        {
            public ProjectAtlasCoverageContribution GetCoverage(ProjectAtlasContext context)
            {
                return ProjectAtlasCoverageContribution.NotRequired("合成项目没有运行时程序集。");
            }
        }

        [ProjectAtlasCoverageProvider("fixture", "fixture.config", "config-sets")]
        private sealed class FixtureConfigCoverage : IProjectAtlasCoverageProvider
        {
            public ProjectAtlasCoverageContribution GetCoverage(ProjectAtlasContext context)
            {
                return ProjectAtlasCoverageContribution.NotRequired("合成项目没有配置集。");
            }
        }

        [ProjectAtlasCoverageProvider("fixture", "fixture.validation", "validation-lanes")]
        private sealed class FixtureValidationCoverage : IProjectAtlasCoverageProvider
        {
            public ProjectAtlasCoverageContribution GetCoverage(ProjectAtlasContext context)
            {
                return ProjectAtlasCoverageContribution.Required(new[]
                {
                    new ProjectAtlasCoverageItem("validation-lanes", "validation-lane", "atlas-boundary", "Atlas boundary")
                });
            }
        }

        [ProjectAtlasCoverageProvider("fixture-exclusion", "fixture-exclusion.runtime", "runtime-assemblies")]
        private sealed class ExclusionRuntimeCoverage : IProjectAtlasCoverageProvider
        {
            public ProjectAtlasCoverageContribution GetCoverage(ProjectAtlasContext context)
            {
                return ProjectAtlasCoverageContribution.Required(new[]
                {
                    new ProjectAtlasCoverageItem("runtime-assemblies", "assembly", "Assets/Runtime.asmdef", "Runtime")
                });
            }
        }

        [ProjectAtlasCoverageProvider("fixture-exclusion", "fixture-exclusion.config", "config-sets")]
        private sealed class ExclusionConfigCoverage : IProjectAtlasCoverageProvider
        {
            public ProjectAtlasCoverageContribution GetCoverage(ProjectAtlasContext context)
            {
                return ProjectAtlasCoverageContribution.NotRequired("合成项目没有配置集。");
            }
        }

        [ProjectAtlasCoverageProvider("fixture-exclusion", "fixture-exclusion.validation", "validation-lanes")]
        private sealed class ExclusionValidationCoverage : IProjectAtlasCoverageProvider
        {
            public ProjectAtlasCoverageContribution GetCoverage(ProjectAtlasContext context)
            {
                return ProjectAtlasCoverageContribution.Required(new[]
                {
                    new ProjectAtlasCoverageItem("validation-lanes", "validation-lane", "atlas-boundary", "Atlas boundary")
                });
            }
        }
    }
}
