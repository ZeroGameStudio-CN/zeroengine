using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using ZeroEngine.EditorUI;

namespace ZeroEngine.ProjectAtlas
{
    public enum ProjectFeatureConfigurationMode
    {
        Configurable,
        ReadOnly,
        None
    }

    public enum ProjectFeatureActionIntent
    {
        Configure,
        Preview,
        Validate,
        Help
    }

    public sealed class ProjectFeatureDomain
    {
        public ProjectFeatureDomain(
            string id,
            string displayName,
            string summary,
            int order,
            IEnumerable<string> audienceTags,
            IEnumerable<string> keywords,
            IEnumerable<string> featureIds)
        {
            Id = id;
            DisplayName = displayName;
            Summary = summary;
            Order = order;
            AudienceTags = Freeze(audienceTags);
            Keywords = Freeze(keywords);
            FeatureIds = Freeze(featureIds);
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Summary { get; }
        public int Order { get; }
        public IReadOnlyList<string> AudienceTags { get; }
        public IReadOnlyList<string> Keywords { get; }
        public IReadOnlyList<string> FeatureIds { get; }

        private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
        {
            return Array.AsReadOnly((values ?? Array.Empty<string>()).ToArray());
        }
    }

    public sealed class ProjectFeatureAction
    {
        public ProjectFeatureAction(string id, string label, ProjectFeatureActionIntent intent, string routeId, bool primary)
        {
            Id = id;
            Label = label;
            Intent = intent;
            RouteId = routeId;
            Primary = primary;
        }

        public string Id { get; }
        public string Label { get; }
        public ProjectFeatureActionIntent Intent { get; }
        public string RouteId { get; }
        public bool Primary { get; }
    }

    public sealed class ProjectFeature
    {
        public ProjectFeature(
            string id,
            string domainId,
            string displayName,
            string summary,
            IEnumerable<string> capabilities,
            IEnumerable<string> audienceTags,
            IEnumerable<string> keywords,
            ProjectFeatureConfigurationMode configurationMode,
            string configurationReason,
            IEnumerable<ProjectFeatureAction> actions)
        {
            Id = id;
            DomainId = domainId;
            DisplayName = displayName;
            Summary = summary;
            Capabilities = Array.AsReadOnly((capabilities ?? Array.Empty<string>()).ToArray());
            AudienceTags = Array.AsReadOnly((audienceTags ?? Array.Empty<string>()).ToArray());
            Keywords = Array.AsReadOnly((keywords ?? Array.Empty<string>()).ToArray());
            ConfigurationMode = configurationMode;
            ConfigurationReason = configurationReason ?? string.Empty;
            Actions = Array.AsReadOnly((actions ?? Array.Empty<ProjectFeatureAction>()).ToArray());
        }

        public string Id { get; }
        public string DomainId { get; }
        public string DisplayName { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Capabilities { get; }
        public IReadOnlyList<string> AudienceTags { get; }
        public IReadOnlyList<string> Keywords { get; }
        public ProjectFeatureConfigurationMode ConfigurationMode { get; }
        public string ConfigurationReason { get; }
        public IReadOnlyList<ProjectFeatureAction> Actions { get; }

        public string SearchText => string.Join(" ", new[] { DisplayName, Summary }
            .Concat(Capabilities)
            .Concat(AudienceTags)
            .Concat(Keywords));
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ProjectFeatureRouteProviderAttribute : Attribute
    {
        public ProjectFeatureRouteProviderAttribute(string projectId, string providerId)
        {
            ProjectId = projectId ?? string.Empty;
            ProviderId = providerId ?? string.Empty;
        }

        public string ProjectId { get; }
        public string ProviderId { get; }
    }

    public interface IProjectFeatureRouteProvider
    {
        IEnumerable<ProjectFeatureRouteDescriptor> GetRoutes(ProjectAtlasContext context);
    }

    public sealed class ProjectFeatureRouteDescriptor
    {
        public ProjectFeatureRouteDescriptor(
            string routeId,
            string displayName,
            string kind,
            bool available,
            string disabledReason,
            IEditorToolAction action)
        {
            RouteId = routeId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Kind = kind ?? string.Empty;
            Available = available;
            DisabledReason = disabledReason ?? string.Empty;
            Action = action;
        }

        public string RouteId { get; }
        public string DisplayName { get; }
        public string Kind { get; }
        public bool Available { get; }
        public string DisabledReason { get; }
        public IEditorToolAction Action { get; }
    }

    public sealed class ProjectFeatureRouteCatalog
    {
        private readonly IReadOnlyDictionary<string, ProjectFeatureRouteDescriptor> _routes;

        internal ProjectFeatureRouteCatalog(
            IDictionary<string, ProjectFeatureRouteDescriptor> routes,
            IEnumerable<ProjectAtlasDiagnostic> diagnostics)
        {
            _routes = new ReadOnlyDictionary<string, ProjectFeatureRouteDescriptor>(
                new Dictionary<string, ProjectFeatureRouteDescriptor>(routes, StringComparer.Ordinal));
            Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ProjectAtlasDiagnostic>()).ToArray());
        }

        public IReadOnlyDictionary<string, ProjectFeatureRouteDescriptor> Routes => _routes;
        public IReadOnlyList<ProjectAtlasDiagnostic> Diagnostics { get; }

        public bool TryGetRoute(string routeId, out ProjectFeatureRouteDescriptor descriptor)
        {
            return _routes.TryGetValue(routeId ?? string.Empty, out descriptor);
        }
    }

    public sealed class ProjectFeatureCatalog
    {
        internal ProjectFeatureCatalog(
            string projectRoot,
            string projectId,
            string defaultDomainId,
            IEnumerable<ProjectFeatureDomain> domains,
            IEnumerable<ProjectFeature> features,
            ProjectFeatureRouteCatalog routes,
            IEnumerable<ProjectAtlasDiagnostic> diagnostics)
        {
            ProjectRoot = projectRoot ?? string.Empty;
            ProjectId = projectId ?? string.Empty;
            DefaultDomainId = defaultDomainId ?? string.Empty;
            Domains = Array.AsReadOnly((domains ?? Array.Empty<ProjectFeatureDomain>())
                .OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray());
            Features = Array.AsReadOnly((features ?? Array.Empty<ProjectFeature>()).ToArray());
            Routes = routes ?? new ProjectFeatureRouteCatalog(
                new Dictionary<string, ProjectFeatureRouteDescriptor>(),
                Array.Empty<ProjectAtlasDiagnostic>());
            Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ProjectAtlasDiagnostic>()).ToArray());
        }

        public string ProjectRoot { get; }
        public string ProjectId { get; }
        public string DefaultDomainId { get; }
        public IReadOnlyList<ProjectFeatureDomain> Domains { get; }
        public IReadOnlyList<ProjectFeature> Features { get; }
        public ProjectFeatureRouteCatalog Routes { get; }
        public IReadOnlyList<ProjectAtlasDiagnostic> Diagnostics { get; }
        public bool HasErrors => Diagnostics.Any(item => item.Severity == ProjectAtlasDiagnosticSeverity.Error);

        public ProjectFeature FindFeature(string id)
        {
            return Features.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        }
    }

    public static class ProjectFeatureCatalogLoader
    {
        public const int SchemaVersion = 1;
        public const string RootCatalogPath = "docs/project/feature-map.json";
        public const string FragmentDirectory = "docs/project/features/";

        private static readonly Regex StableIdPattern =
            new Regex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static Type[] _cachedRouteProviderTypes;

        public static ProjectFeatureCatalog LoadProject(string projectRoot)
        {
            return LoadProject(projectRoot, null);
        }

        public static ProjectFeatureCatalog LoadProject(string projectRoot, IEnumerable<Type> routeProviderTypes)
        {
            string normalizedRoot = NormalizeRoot(projectRoot);
            var diagnostics = new List<ProjectAtlasDiagnostic>();
            var domains = new List<ProjectFeatureDomain>();
            var features = new List<ProjectFeature>();
            if (string.IsNullOrEmpty(normalizedRoot))
            {
                diagnostics.Add(Error("feature-invalid-project-root", "项目根路径无效。"));
                return Empty(projectRoot, diagnostics);
            }

            string rootPath;
            try
            {
                rootPath = ProjectAtlasCatalogLoader.ResolveSafeProjectPath(normalizedRoot, RootCatalogPath, false);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("feature-invalid-root-path", exception.Message, RootCatalogPath));
                return Empty(normalizedRoot, diagnostics);
            }

            if (!File.Exists(rootPath))
            {
                diagnostics.Add(new ProjectAtlasDiagnostic(
                    ProjectAtlasDiagnosticSeverity.Warning,
                    "feature-map-not-configured",
                    "项目尚未建立功能导航。",
                    RootCatalogPath));
                return Empty(normalizedRoot, diagnostics);
            }

            RootData root;
            try
            {
                root = ReadStrict<RootData>(rootPath);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("feature-invalid-root-json", exception.Message, RootCatalogPath));
                return Empty(normalizedRoot, diagnostics);
            }

            if (root.schemaVersion != SchemaVersion)
                diagnostics.Add(Error("feature-unsupported-schema-version", "不支持的功能目录 schemaVersion：" + root.schemaVersion + "。", RootCatalogPath, "schemaVersion"));
            RequireStableId(root.projectId, "projectId", RootCatalogPath, diagnostics);
            RequireStableId(root.defaultDomainId, "defaultDomainId", RootCatalogPath, diagnostics);
            ReadFragments(normalizedRoot, root.sources, domains, features, diagnostics);
            ValidateCombined(root, domains, features, diagnostics);

            ProjectFeatureRouteCatalog routes = BuildRoutes(
                normalizedRoot,
                root.projectId,
                routeProviderTypes ?? GetCachedRouteProviderTypes(),
                diagnostics);
            ValidateActions(features, routes, diagnostics);
            return new ProjectFeatureCatalog(
                normalizedRoot,
                root.projectId,
                root.defaultDomainId,
                domains,
                features,
                routes,
                diagnostics);
        }

        private static void ReadFragments(
            string projectRoot,
            IEnumerable<string> sources,
            ICollection<ProjectFeatureDomain> domains,
            ICollection<ProjectFeature> features,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string source in sources ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(source) ||
                    !source.StartsWith(FragmentDirectory, StringComparison.Ordinal) ||
                    !source.EndsWith(".json", StringComparison.Ordinal) ||
                    !seenSources.Add(source))
                {
                    diagnostics.Add(Error("feature-invalid-source", "功能目录 source 必须是唯一的 " + FragmentDirectory + "*.json：" + source, RootCatalogPath, "sources"));
                    continue;
                }

                string path;
                try
                {
                    path = ProjectAtlasCatalogLoader.ResolveSafeProjectPath(projectRoot, source, true);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("feature-source-unavailable", exception.Message, source));
                    continue;
                }

                FragmentData fragment;
                try
                {
                    fragment = ReadStrict<FragmentData>(path);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("feature-invalid-fragment-json", exception.Message, source));
                    continue;
                }
                if (fragment.schemaVersion != SchemaVersion)
                {
                    diagnostics.Add(Error("feature-unsupported-fragment-version", "不支持的功能碎片 schemaVersion：" + fragment.schemaVersion + "。", source, "schemaVersion"));
                    continue;
                }

                foreach (DomainData data in fragment.domains ?? Array.Empty<DomainData>())
                    domains.Add(ReadDomain(data, source, diagnostics));
                foreach (FeatureData data in fragment.features ?? Array.Empty<FeatureData>())
                    features.Add(ReadFeature(data, source, diagnostics));
            }
        }

        private static ProjectFeatureDomain ReadDomain(DomainData data, string source, ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (data == null)
            {
                diagnostics.Add(Error("feature-null-domain", "domains 不能包含 null。", source));
                return new ProjectFeatureDomain(string.Empty, string.Empty, string.Empty, 0, null, null, null);
            }
            RequireStableId(data.id, "domains.id", source, diagnostics);
            RequireText(data.displayName, "domains.displayName", source, diagnostics);
            RequireText(data.summary, "domains.summary", source, diagnostics);
            RequireTexts(data.audienceTags, "domains.audienceTags", source, diagnostics);
            RequireTexts(data.keywords, "domains.keywords", source, diagnostics);
            RequireIds(data.featureIds, "domains.featureIds", source, diagnostics);
            return new ProjectFeatureDomain(
                Trim(data.id), Trim(data.displayName), Trim(data.summary), data.order,
                TrimAll(data.audienceTags), TrimAll(data.keywords), TrimAll(data.featureIds));
        }

        private static ProjectFeature ReadFeature(FeatureData data, string source, ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (data == null)
            {
                diagnostics.Add(Error("feature-null-feature", "features 不能包含 null。", source));
                return new ProjectFeature(string.Empty, string.Empty, string.Empty, string.Empty, null, null, null, ProjectFeatureConfigurationMode.None, string.Empty, null);
            }
            RequireStableId(data.id, "features.id", source, diagnostics);
            RequireStableId(data.domainId, "features.domainId", source, diagnostics);
            RequireText(data.displayName, "features.displayName", source, diagnostics);
            RequireText(data.summary, "features.summary", source, diagnostics);
            RequireTexts(data.capabilities, "features.capabilities", source, diagnostics);
            RequireTexts(data.audienceTags, "features.audienceTags", source, diagnostics);
            RequireTexts(data.keywords, "features.keywords", source, diagnostics);

            bool modeValid = TryParseMode(data.configurationMode, out ProjectFeatureConfigurationMode mode);
            if (!modeValid)
                diagnostics.Add(Error("feature-invalid-configuration-mode", "configurationMode 只允许 configurable、read-only、none。", source, data.id));
            var actions = new List<ProjectFeatureAction>();
            foreach (ActionData action in data.actions ?? Array.Empty<ActionData>())
            {
                if (action == null)
                {
                    diagnostics.Add(Error("feature-null-action", "actions 不能包含 null。", source, data.id));
                    continue;
                }
                RequireStableId(action.id, "actions.id", source, diagnostics);
                RequireText(action.label, "actions.label", source, diagnostics);
                RequireStableId(action.routeId, "actions.routeId", source, diagnostics);
                if (!TryParseIntent(action.intent, out ProjectFeatureActionIntent intent))
                    diagnostics.Add(Error("feature-invalid-intent", "intent 只允许 configure、preview、validate、help。", source, data.id + "." + action.id));
                actions.Add(new ProjectFeatureAction(Trim(action.id), Trim(action.label), intent, Trim(action.routeId), action.primary));
            }

            if (mode == ProjectFeatureConfigurationMode.None && string.IsNullOrWhiteSpace(data.configurationReason))
                diagnostics.Add(Error("feature-missing-configuration-reason", "暂无日常配置的功能必须说明原因。", source, data.id));
            if (mode != ProjectFeatureConfigurationMode.None && !string.IsNullOrWhiteSpace(data.configurationReason))
                diagnostics.Add(Error("feature-unexpected-configuration-reason", "仅 configurationMode=none 可以填写 configurationReason。", source, data.id));
            return new ProjectFeature(
                Trim(data.id), Trim(data.domainId), Trim(data.displayName), Trim(data.summary),
                TrimAll(data.capabilities), TrimAll(data.audienceTags), TrimAll(data.keywords),
                mode, Trim(data.configurationReason), actions);
        }

        private static void ValidateCombined(
            RootData root,
            IReadOnlyCollection<ProjectFeatureDomain> domains,
            IReadOnlyCollection<ProjectFeature> features,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, ProjectFeatureDomain> duplicate in domains.GroupBy(item => item.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
                diagnostics.Add(Error("feature-duplicate-domain", "重复的领域 ID：" + duplicate.Key, RootCatalogPath));
            foreach (IGrouping<string, ProjectFeature> duplicate in features.GroupBy(item => item.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
                diagnostics.Add(Error("feature-duplicate-feature", "重复的功能 ID：" + duplicate.Key, RootCatalogPath));

            var domainById = domains.Where(item => !string.IsNullOrEmpty(item.Id)).GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var featureById = features.Where(item => !string.IsNullOrEmpty(item.Id)).GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            if (!domainById.ContainsKey(Trim(root.defaultDomainId)))
                diagnostics.Add(Error("feature-invalid-default-domain", "defaultDomainId 未指向已声明领域。", RootCatalogPath, "defaultDomainId"));

            var listedFeatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectFeatureDomain domain in domains)
            {
                foreach (string featureId in domain.FeatureIds)
                {
                    if (!listedFeatures.Add(featureId))
                        diagnostics.Add(Error("feature-duplicate-domain-entry", "功能在领域顺序中出现多次：" + featureId, RootCatalogPath, domain.Id));
                    if (!featureById.TryGetValue(featureId, out ProjectFeature feature))
                        diagnostics.Add(Error("feature-unknown-domain-entry", "领域引用了不存在的功能：" + featureId, RootCatalogPath, domain.Id));
                    else if (!string.Equals(feature.DomainId, domain.Id, StringComparison.Ordinal))
                        diagnostics.Add(Error("feature-domain-mismatch", "功能与领域归属不一致：" + featureId, RootCatalogPath, domain.Id));
                }
            }
            foreach (ProjectFeature feature in features)
            {
                if (!domainById.ContainsKey(feature.DomainId))
                    diagnostics.Add(Error("feature-unknown-domain", "功能引用了不存在的领域：" + feature.DomainId, RootCatalogPath, feature.Id));
                if (!listedFeatures.Contains(feature.Id))
                    diagnostics.Add(Error("feature-unlisted-feature", "功能未进入任何领域的显示顺序：" + feature.Id, RootCatalogPath, feature.Id));
                if (feature.Actions.GroupBy(item => item.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
                    diagnostics.Add(Error("feature-duplicate-action", "功能内存在重复 action ID：" + feature.Id, RootCatalogPath, feature.Id));
                if (feature.Actions.Count(item => item.Primary) > 1)
                    diagnostics.Add(Error("feature-multiple-primary-actions", "每个功能最多有一个主要动作：" + feature.Id, RootCatalogPath, feature.Id));
                if (feature.ConfigurationMode == ProjectFeatureConfigurationMode.None &&
                    feature.Actions.Any(item => item.Intent == ProjectFeatureActionIntent.Configure))
                    diagnostics.Add(Error("feature-none-has-configure", "暂无日常配置的功能不能声明配置动作：" + feature.Id, RootCatalogPath, feature.Id));
                if (feature.ConfigurationMode == ProjectFeatureConfigurationMode.ReadOnly &&
                    feature.Actions.All(item => item.Intent != ProjectFeatureActionIntent.Preview && item.Intent != ProjectFeatureActionIntent.Validate && item.Intent != ProjectFeatureActionIntent.Help))
                    diagnostics.Add(Error("feature-readonly-without-action", "仅查看功能必须提供预览、检查或说明动作：" + feature.Id, RootCatalogPath, feature.Id));
            }
        }

        private static ProjectFeatureRouteCatalog BuildRoutes(
            string projectRoot,
            string projectId,
            IEnumerable<Type> providerTypes,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            var routeDiagnostics = new List<ProjectAtlasDiagnostic>();
            var routes = new Dictionary<string, ProjectFeatureRouteDescriptor>(StringComparer.Ordinal);
            var duplicateRouteIds = new HashSet<string>(StringComparer.Ordinal);
            var context = new ProjectAtlasContext(projectRoot, projectId);
            foreach (Type type in (providerTypes ?? Array.Empty<Type>()).OrderBy(item => item.FullName, StringComparer.Ordinal))
            {
                if (type == null || type.IsAbstract || type.IsInterface || !typeof(IProjectFeatureRouteProvider).IsAssignableFrom(type))
                    continue;
                var attribute = (ProjectFeatureRouteProviderAttribute)Attribute.GetCustomAttribute(type, typeof(ProjectFeatureRouteProviderAttribute), false);
                if (attribute == null)
                {
                    routeDiagnostics.Add(Error("feature-route-provider-missing-attribute", type.FullName + " 缺少 ProjectFeatureRouteProviderAttribute。"));
                    continue;
                }
                if (!string.Equals(attribute.ProjectId, projectId, StringComparison.Ordinal))
                    continue;
                if (!StableIdPattern.IsMatch(attribute.ProviderId ?? string.Empty))
                {
                    routeDiagnostics.Add(Error("feature-route-provider-invalid-id", type.FullName + " 的 providerId 无效。"));
                    continue;
                }
                try
                {
                    var provider = (IProjectFeatureRouteProvider)Activator.CreateInstance(type);
                    foreach (ProjectFeatureRouteDescriptor route in provider.GetRoutes(context) ?? Array.Empty<ProjectFeatureRouteDescriptor>())
                    {
                        if (route == null || !StableIdPattern.IsMatch(route.RouteId) || string.IsNullOrWhiteSpace(route.DisplayName) ||
                            string.IsNullOrWhiteSpace(route.Kind) || route.Action == null || (!route.Available && string.IsNullOrWhiteSpace(route.DisabledReason)))
                        {
                            routeDiagnostics.Add(Error("feature-invalid-route", attribute.ProviderId + " 返回了无效 route descriptor。"));
                            continue;
                        }
                        if (duplicateRouteIds.Contains(route.RouteId))
                            continue;
                        if (!routes.TryAdd(route.RouteId, route))
                        {
                            routes.Remove(route.RouteId);
                            duplicateRouteIds.Add(route.RouteId);
                            routeDiagnostics.Add(Error("feature-duplicate-route", "重复的功能 routeId：" + route.RouteId));
                        }
                    }
                }
                catch (Exception exception)
                {
                    routeDiagnostics.Add(Error("feature-route-provider-exception", attribute.ProviderId + "：" + exception.Message));
                }
            }
            foreach (ProjectAtlasDiagnostic diagnostic in routeDiagnostics)
                diagnostics.Add(diagnostic);
            return new ProjectFeatureRouteCatalog(routes, routeDiagnostics);
        }

        private static void ValidateActions(
            IEnumerable<ProjectFeature> features,
            ProjectFeatureRouteCatalog routes,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            foreach (ProjectFeature feature in features)
            {
                foreach (ProjectFeatureAction action in feature.Actions)
                {
                    if (!routes.TryGetRoute(action.RouteId, out _))
                        diagnostics.Add(Error("feature-route-missing", "功能动作没有唯一可用路由：" + feature.Id + "/" + action.Id, RootCatalogPath, action.RouteId));
                }
                if (feature.ConfigurationMode == ProjectFeatureConfigurationMode.Configurable)
                {
                    bool hasConfigureRoute = feature.Actions.Any(action =>
                        action.Intent == ProjectFeatureActionIntent.Configure &&
                        routes.TryGetRoute(action.RouteId, out ProjectFeatureRouteDescriptor route) &&
                        route.Available);
                    if (!hasConfigureRoute)
                        diagnostics.Add(Error("feature-configurable-without-route", "可配置功能缺少可用配置入口：" + feature.Id, RootCatalogPath, feature.Id));
                }
            }
        }

        private static Type[] GetCachedRouteProviderTypes()
        {
            return _cachedRouteProviderTypes ??= TypeCache.GetTypesDerivedFrom<IProjectFeatureRouteProvider>()
                .Where(type => type != null).ToArray();
        }

        private static ProjectFeatureCatalog Empty(string projectRoot, IEnumerable<ProjectAtlasDiagnostic> diagnostics)
        {
            return new ProjectFeatureCatalog(
                projectRoot,
                string.Empty,
                string.Empty,
                Array.Empty<ProjectFeatureDomain>(),
                Array.Empty<ProjectFeature>(),
                null,
                diagnostics);
        }

        private static string NormalizeRoot(string projectRoot)
        {
            try
            {
                return string.IsNullOrWhiteSpace(projectRoot) ? string.Empty : Path.GetFullPath(projectRoot);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static T ReadStrict<T>(string path)
        {
            return JsonConvert.DeserializeObject<T>(
                File.ReadAllText(path),
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
        }

        private static void RequireStableId(string value, string field, string source, ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(value) || !StableIdPattern.IsMatch(value.Trim()))
                diagnostics.Add(Error("feature-invalid-id", field + " 必须是稳定小写 ID。", source, field));
        }

        private static void RequireIds(IEnumerable<string> values, string field, string source, ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            string[] array = (values ?? Array.Empty<string>()).ToArray();
            if (array.Length == 0)
                diagnostics.Add(Error("feature-empty-list", field + " 不能为空。", source, field));
            foreach (string value in array)
                RequireStableId(value, field, source, diagnostics);
            if (array.Where(value => value != null).GroupBy(value => value.Trim(), StringComparer.Ordinal).Any(group => group.Count() > 1))
                diagnostics.Add(Error("feature-duplicate-list-item", field + " 不能包含重复项。", source, field));
        }

        private static void RequireText(string value, string field, string source, ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(value))
                diagnostics.Add(Error("feature-missing-text", field + " 必填。", source, field));
        }

        private static void RequireTexts(IEnumerable<string> values, string field, string source, ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            string[] array = (values ?? Array.Empty<string>()).ToArray();
            if (array.Length == 0 || array.Any(string.IsNullOrWhiteSpace))
                diagnostics.Add(Error("feature-invalid-text-list", field + " 必须包含非空中文工作语言。", source, field));
        }

        private static bool TryParseMode(string value, out ProjectFeatureConfigurationMode mode)
        {
            switch (Trim(value))
            {
                case "configurable": mode = ProjectFeatureConfigurationMode.Configurable; return true;
                case "read-only": mode = ProjectFeatureConfigurationMode.ReadOnly; return true;
                case "none": mode = ProjectFeatureConfigurationMode.None; return true;
                default: mode = ProjectFeatureConfigurationMode.None; return false;
            }
        }

        private static bool TryParseIntent(string value, out ProjectFeatureActionIntent intent)
        {
            switch (Trim(value))
            {
                case "configure": intent = ProjectFeatureActionIntent.Configure; return true;
                case "preview": intent = ProjectFeatureActionIntent.Preview; return true;
                case "validate": intent = ProjectFeatureActionIntent.Validate; return true;
                case "help": intent = ProjectFeatureActionIntent.Help; return true;
                default: intent = ProjectFeatureActionIntent.Help; return false;
            }
        }

        private static string Trim(string value) => (value ?? string.Empty).Trim();
        private static IEnumerable<string> TrimAll(IEnumerable<string> values) => (values ?? Array.Empty<string>()).Select(Trim);

        private static ProjectAtlasDiagnostic Error(string code, string message, string source = null, string field = null)
        {
            return new ProjectAtlasDiagnostic(ProjectAtlasDiagnosticSeverity.Error, code, message, source, field);
        }

        [Serializable]
        private sealed class RootData
        {
            public int schemaVersion;
            public string projectId;
            public string defaultDomainId;
            public string[] sources;
        }

        [Serializable]
        private sealed class FragmentData
        {
            public int schemaVersion;
            public DomainData[] domains;
            public FeatureData[] features;
        }

        [Serializable]
        private sealed class DomainData
        {
            public string id;
            public string displayName;
            public string summary;
            public int order;
            public string[] audienceTags;
            public string[] keywords;
            public string[] featureIds;
        }

        [Serializable]
        private sealed class FeatureData
        {
            public string id;
            public string domainId;
            public string displayName;
            public string summary;
            public string[] capabilities;
            public string[] audienceTags;
            public string[] keywords;
            public string configurationMode;
            public string configurationReason;
            public ActionData[] actions;
        }

        [Serializable]
        private sealed class ActionData
        {
            public string id;
            public string label;
            public string intent;
            public string routeId;
            public bool primary;
        }
    }
}
