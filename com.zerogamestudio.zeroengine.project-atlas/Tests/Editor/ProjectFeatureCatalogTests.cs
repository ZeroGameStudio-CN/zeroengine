using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.EditorUI;

namespace ZeroEngine.ProjectAtlas.Tests.Editor
{
    public sealed class ProjectFeatureCatalogTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "zeroengine-project-feature-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "docs", "project", "features"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
                Directory.Delete(_projectRoot, true);
        }

        [Test]
        public void LoadProject_ValidHumanCatalog_ResolvesTypedRoute()
        {
            WriteCatalog();

            ProjectFeatureCatalog catalog = ProjectFeatureCatalogLoader.LoadProject(
                _projectRoot,
                new[] { typeof(FixtureRouteProvider) });

            Assert.That(catalog.HasErrors, Is.False, FormatDiagnostics(catalog));
            Assert.That(catalog.ProjectId, Is.EqualTo("fixture"));
            Assert.That(catalog.Domains.Single().DisplayName, Is.EqualTo("角色"));
            Assert.That(catalog.FindFeature("characters").Capabilities, Does.Contain("维护角色资料"));
            Assert.That(catalog.Routes.TryGetRoute("fixture.characters", out ProjectFeatureRouteDescriptor route), Is.True);
            Assert.That(route.Action, Is.Not.Null);
        }

        [Test]
        public void LoadProject_UnknownIntent_IsRejected()
        {
            WriteCatalog(fragmentTransform: value => value.Replace("\"intent\":\"configure\"", "\"intent\":\"execute\""));

            ProjectFeatureCatalog catalog = Load();

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-invalid-intent"), Is.True, FormatDiagnostics(catalog));
        }

        [Test]
        public void LoadProject_DuplicateFeature_IsRejected()
        {
            WriteCatalog(fragmentTransform: value => value.Replace("\"features\":[", "\"features\":[" + FeatureJson() + ","));

            ProjectFeatureCatalog catalog = Load();

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-duplicate-feature"), Is.True, FormatDiagnostics(catalog));
        }

        [Test]
        public void LoadProject_UnlistedFeature_IsRejected()
        {
            WriteCatalog(fragmentTransform: value => value.Replace("\"featureIds\":[\"characters\"]", "\"featureIds\":[\"other\"]"));

            ProjectFeatureCatalog catalog = Load();

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-unlisted-feature"), Is.True, FormatDiagnostics(catalog));
        }

        [Test]
        public void LoadProject_ConfigurableFeatureWithoutRoute_IsRejected()
        {
            WriteCatalog(fragmentTransform: value => value.Replace("fixture.characters", "fixture.missing"));

            ProjectFeatureCatalog catalog = Load();

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-route-missing"), Is.True, FormatDiagnostics(catalog));
            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-configurable-without-route"), Is.True, FormatDiagnostics(catalog));
        }

        [Test]
        public void LoadProject_NoneFeatureWithConfigureAction_IsRejected()
        {
            WriteCatalog(fragmentTransform: value => value
                .Replace("\"configurationMode\":\"configurable\"", "\"configurationMode\":\"none\",\"configurationReason\":\"没有日常配置。\""));

            ProjectFeatureCatalog catalog = Load();

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-none-has-configure"), Is.True, FormatDiagnostics(catalog));
        }

        [TestCase("../outside.json")]
        [TestCase("docs/project/../outside.json")]
        [TestCase("C:/outside.json")]
        public void LoadProject_UnsafeSource_IsRejected(string source)
        {
            WriteCatalog(rootSource: source);

            ProjectFeatureCatalog catalog = Load();

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-invalid-source"), Is.True, FormatDiagnostics(catalog));
        }

        [Test]
        public void LoadProject_UnknownJsonField_IsRejected()
        {
            WriteCatalog(rootTransform: value => value.Replace("\"sources\":", "\"command\":\"do-not-run\",\"sources\":"));

            ProjectFeatureCatalog catalog = Load();

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-invalid-root-json"), Is.True, FormatDiagnostics(catalog));
        }

        [Test]
        public void LoadProject_ThreeDuplicateRoutes_RemainsFailClosed()
        {
            WriteCatalog();

            ProjectFeatureCatalog catalog = ProjectFeatureCatalogLoader.LoadProject(
                _projectRoot,
                new[]
                {
                    typeof(FixtureRouteProvider),
                    typeof(DuplicateRouteProvider),
                    typeof(ThirdDuplicateRouteProvider)
                });

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-duplicate-route"), Is.True, FormatDiagnostics(catalog));
            Assert.That(catalog.Routes.TryGetRoute("fixture.characters", out _), Is.False);
            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-route-missing"), Is.True, FormatDiagnostics(catalog));
        }

        [Test]
        public void LoadProject_InvalidRouteDescriptor_IsRejected()
        {
            WriteCatalog();

            ProjectFeatureCatalog catalog = ProjectFeatureCatalogLoader.LoadProject(
                _projectRoot,
                new[] { typeof(InvalidRouteProvider) });

            Assert.That(catalog.Diagnostics.Any(item => item.Code == "feature-invalid-route"), Is.True, FormatDiagnostics(catalog));
            Assert.That(catalog.Routes.TryGetRoute("fixture.characters", out _), Is.False);
        }

        private ProjectFeatureCatalog Load()
        {
            return ProjectFeatureCatalogLoader.LoadProject(_projectRoot, new[] { typeof(FixtureRouteProvider) });
        }

        private void WriteCatalog(
            string rootSource = "docs/project/features/fixture.json",
            Func<string, string> rootTransform = null,
            Func<string, string> fragmentTransform = null)
        {
            string root = "{\"schemaVersion\":1,\"projectId\":\"fixture\",\"defaultDomainId\":\"characters\",\"sources\":[\"" + rootSource + "\"]}";
            File.WriteAllText(
                Path.Combine(_projectRoot, "docs", "project", "feature-map.json"),
                rootTransform == null ? root : rootTransform(root));
            if (rootSource != "docs/project/features/fixture.json")
                return;
            string fragment = "{\"schemaVersion\":1,\"domains\":[{" +
                              "\"id\":\"characters\",\"displayName\":\"角色\",\"summary\":\"维护角色内容。\",\"order\":10," +
                              "\"audienceTags\":[\"策划\"],\"keywords\":[\"角色\"],\"featureIds\":[\"characters\"]}]," +
                              "\"features\":[" + FeatureJson() + "]}";
            File.WriteAllText(
                Path.Combine(_projectRoot, "docs", "project", "features", "fixture.json"),
                fragmentTransform == null ? fragment : fragmentTransform(fragment));
        }

        private static string FeatureJson()
        {
            return "{\"id\":\"characters\",\"domainId\":\"characters\",\"displayName\":\"角色档案\"," +
                   "\"summary\":\"维护角色资料。\",\"capabilities\":[\"维护角色资料\"],\"audienceTags\":[\"策划\"]," +
                   "\"keywords\":[\"人物\"],\"configurationMode\":\"configurable\"," +
                   "\"actions\":[{\"id\":\"open\",\"label\":\"打开配置\",\"intent\":\"configure\"," +
                   "\"routeId\":\"fixture.characters\",\"primary\":true}]}";
        }

        private static string FormatDiagnostics(ProjectFeatureCatalog catalog)
        {
            return string.Join("\n", catalog.Diagnostics.Select(item => item.Code + " " + item.Message));
        }

        [ProjectFeatureRouteProvider("fixture", "fixture.routes")]
        public sealed class FixtureRouteProvider : IProjectFeatureRouteProvider
        {
            public IEnumerable<ProjectFeatureRouteDescriptor> GetRoutes(ProjectAtlasContext context)
            {
                return new[]
                {
                    new ProjectFeatureRouteDescriptor(
                        "fixture.characters",
                        "角色配置",
                        "workspace",
                        true,
                        string.Empty,
                        new DelegateEditorToolAction(_ => new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开角色配置。")))
                };
            }
        }

        [ProjectFeatureRouteProvider("fixture", "fixture.duplicate-routes")]
        public sealed class DuplicateRouteProvider : IProjectFeatureRouteProvider
        {
            public IEnumerable<ProjectFeatureRouteDescriptor> GetRoutes(ProjectAtlasContext context)
            {
                return new FixtureRouteProvider().GetRoutes(context);
            }
        }

        [ProjectFeatureRouteProvider("fixture", "fixture.third-duplicate-routes")]
        public sealed class ThirdDuplicateRouteProvider : IProjectFeatureRouteProvider
        {
            public IEnumerable<ProjectFeatureRouteDescriptor> GetRoutes(ProjectAtlasContext context)
            {
                return new FixtureRouteProvider().GetRoutes(context);
            }
        }

        [ProjectFeatureRouteProvider("fixture", "fixture.invalid-routes")]
        public sealed class InvalidRouteProvider : IProjectFeatureRouteProvider
        {
            public IEnumerable<ProjectFeatureRouteDescriptor> GetRoutes(ProjectAtlasContext context)
            {
                return new[]
                {
                    new ProjectFeatureRouteDescriptor(
                        "fixture.characters",
                        "角色配置",
                        "workspace",
                        false,
                        string.Empty,
                        new DelegateEditorToolAction(_ => new EditorToolActionResult(EditorToolActionStatus.Succeeded, "不应执行。")))
                };
            }
        }
    }
}
