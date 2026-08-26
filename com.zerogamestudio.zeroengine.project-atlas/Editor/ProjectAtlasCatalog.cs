using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace ZeroEngine.ProjectAtlas
{
    public static class ProjectAtlasCatalogLoader
    {
        public const int SchemaVersion = 1;
        public const string RootCatalogPath = "docs/architecture/project-atlas.json";
        public const string GeneratedIndexPath = "docs/architecture/system-routing-index.md";

        private const string FragmentDirectory = "docs/architecture/project-atlas/";

        private static Type[] _cachedResolverTypes;
        private static Type[] _cachedCoverageProviderTypes;

        private static readonly Regex StableIdPattern =
            new Regex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Lifecycles =
            new HashSet<string>(new[] { "active", "experimental", "retiring", "retired" }, StringComparer.Ordinal);

        private static readonly HashSet<string> Ownerships =
            new HashSet<string>(new[] { "framework", "project", "mixed", "vendor" }, StringComparer.Ordinal);

        private static readonly HashSet<string> RelationKinds =
            new HashSet<string>(new[] { "depends-on", "feeds", "adapts", "extends", "validates" }, StringComparer.Ordinal);

        private static readonly string[] RequiredProjectCoverageDimensions =
        {
            "runtime-assemblies",
            "config-sets",
            "validation-lanes"
        };

        public static ProjectAtlasGraph LoadProject(string projectRoot)
        {
            return LoadProject(projectRoot, null, null, true);
        }

        public static ProjectAtlasGraph LoadProject(
            string projectRoot,
            IEnumerable<Type> resolverTypes,
            IEnumerable<Type> coverageProviderTypes,
            bool validateCoverage)
        {
            string normalizedRoot = NormalizeRoot(projectRoot);
            var diagnostics = new List<ProjectAtlasDiagnostic>();
            var references = new List<ProjectAtlasReference>();
            var systems = new List<ProjectAtlasSystem>();
            var exclusions = new List<ProjectAtlasCoverageExclusion>();
            var resolutions = new Dictionary<string, ProjectAtlasReferenceResolution>(StringComparer.Ordinal);
            var coverage = new List<ProjectAtlasCoverageItem>();

            if (string.IsNullOrEmpty(normalizedRoot))
            {
                diagnostics.Add(Error("invalid-project-root", "项目根路径无效。"));
                return EmptyGraph(projectRoot, diagnostics);
            }

            string rootPath;
            try
            {
                rootPath = ResolveSafeProjectPath(normalizedRoot, RootCatalogPath, false);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("invalid-root-path", exception.Message, RootCatalogPath));
                return EmptyGraph(normalizedRoot, diagnostics);
            }

            if (!File.Exists(rootPath))
            {
                diagnostics.Add(Warning(
                    "catalog-not-configured",
                    "项目尚未接入 Project Atlas；未找到 " + RootCatalogPath + "。",
                    RootCatalogPath));
                return EmptyGraph(normalizedRoot, diagnostics);
            }

            RootData root;
            try
            {
                root = ReadStrict<RootData>(rootPath);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("invalid-root-json", exception.Message, RootCatalogPath));
                return EmptyGraph(normalizedRoot, diagnostics);
            }

            if (root.schemaVersion != SchemaVersion)
            {
                diagnostics.Add(Error(
                    "unsupported-schema-version",
                    "不支持的 Project Atlas schemaVersion：" + root.schemaVersion + "。",
                    RootCatalogPath,
                    "schemaVersion"));
                return EmptyGraph(normalizedRoot, diagnostics);
            }

            ProjectAtlasProject project = ValidateProject(root.project, diagnostics);
            if (project == null)
                return EmptyGraph(normalizedRoot, diagnostics);

            ReadExclusions(root.coverageExclusions, exclusions, diagnostics);
            ReadFragments(normalizedRoot, root.sources, references, systems, diagnostics);
            ValidateCombinedGraph(project, references, systems, diagnostics);

            if (!diagnostics.Any(item => item.Severity == ProjectAtlasDiagnosticSeverity.Error))
            {
                ProjectAtlasContext context = new ProjectAtlasContext(normalizedRoot, project.Id);
                ResolveReferences(
                    context,
                    references,
                    resolverTypes ?? GetCachedResolverTypes(),
                    resolutions,
                    diagnostics);
                if (validateCoverage)
                {
                    EvaluateCoverage(
                        context,
                        references,
                        systems,
                        exclusions,
                        coverageProviderTypes ?? GetCachedCoverageProviderTypes(),
                        coverage,
                        diagnostics);
                }
            }

            return new ProjectAtlasGraph(
                normalizedRoot,
                project,
                systems,
                references,
                resolutions,
                coverage,
                diagnostics,
                exclusions);
        }

        public static string ResolveSafeProjectPath(string projectRoot, string relativePath, bool mustExist)
        {
            string normalizedRoot = NormalizeRoot(projectRoot);
            if (string.IsNullOrEmpty(normalizedRoot))
                throw new ArgumentException("项目根路径无效。", nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("项目相对路径不能为空。", nameof(relativePath));
            if (Path.IsPathRooted(relativePath))
                throw new InvalidOperationException("Project Atlas 路径不得是绝对路径：" + relativePath);
            if (relativePath.IndexOf('\\') >= 0)
                throw new InvalidOperationException("Project Atlas 路径必须使用 /：" + relativePath);

            string[] segments = relativePath.Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".."))
                throw new InvalidOperationException("Project Atlas 路径包含空段、. 或 ..：" + relativePath);

            string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, Path.Combine(segments)));
            string rootPrefix = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Project Atlas 路径越过项目根：" + relativePath);

            string current = normalizedRoot;
            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current))
                    break;
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Project Atlas 路径不得经过符号链接或重解析点：" + relativePath);
            }

            if (mustExist && !File.Exists(candidate) && !Directory.Exists(candidate))
                throw new FileNotFoundException("Project Atlas 路径不存在：" + relativePath, candidate);
            return candidate;
        }

        private static ProjectAtlasGraph EmptyGraph(string projectRoot, IEnumerable<ProjectAtlasDiagnostic> diagnostics)
        {
            return new ProjectAtlasGraph(
                projectRoot,
                null,
                Array.Empty<ProjectAtlasSystem>(),
                Array.Empty<ProjectAtlasReference>(),
                new Dictionary<string, ProjectAtlasReferenceResolution>(),
                Array.Empty<ProjectAtlasCoverageItem>(),
                diagnostics,
                Array.Empty<ProjectAtlasCoverageExclusion>());
        }

        private static ProjectAtlasProject ValidateProject(ProjectData data, ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (data == null)
            {
                diagnostics.Add(Error("missing-project", "project 对象必填。", RootCatalogPath, "project"));
                return null;
            }

            ValidateStableId(data.id, "project.id", RootCatalogPath, diagnostics);
            RequireText(data.displayName, "project.displayName", RootCatalogPath, diagnostics);
            RequireText(data.summary, "project.summary", RootCatalogPath, diagnostics);
            ValidateStableId(data.rootAgentRule, "project.rootAgentRule", RootCatalogPath, diagnostics);
            if (diagnostics.Any(item => item.Severity == ProjectAtlasDiagnosticSeverity.Error))
                return null;
            return new ProjectAtlasProject(data.id.Trim(), data.displayName.Trim(), data.summary.Trim(), data.rootAgentRule.Trim());
        }

        private static void ReadExclusions(
            CoverageExclusionData[] data,
            ICollection<ProjectAtlasCoverageExclusion> exclusions,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (CoverageExclusionData item in data ?? Array.Empty<CoverageExclusionData>())
            {
                if (item == null)
                {
                    diagnostics.Add(Error("invalid-coverage-exclusion", "coverageExclusions 不能包含 null。", RootCatalogPath));
                    continue;
                }
                RequireText(item.kind, "coverageExclusions.kind", RootCatalogPath, diagnostics);
                RequireText(item.target, "coverageExclusions.target", RootCatalogPath, diagnostics);
                RequireText(item.reason, "coverageExclusions.reason", RootCatalogPath, diagnostics);
                string key = (item.kind ?? string.Empty).Trim() + "\n" + (item.target ?? string.Empty).Trim();
                if (!keys.Add(key))
                    diagnostics.Add(Error("duplicate-coverage-exclusion", "重复的 coverage exclusion：" + key.Replace('\n', '/'), RootCatalogPath));
                exclusions.Add(new ProjectAtlasCoverageExclusion(
                    (item.kind ?? string.Empty).Trim(),
                    (item.target ?? string.Empty).Trim(),
                    (item.reason ?? string.Empty).Trim()));
            }
        }

        private static void ReadFragments(
            string projectRoot,
            string[] sourcePaths,
            ICollection<ProjectAtlasReference> references,
            ICollection<ProjectAtlasSystem> systems,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (sourcePaths == null || sourcePaths.Length == 0)
            {
                diagnostics.Add(Error("missing-sources", "sources 至少需要一个显式领域碎片。", RootCatalogPath, "sources"));
                return;
            }

            var ordinalPaths = new HashSet<string>(StringComparer.Ordinal);
            var caseFoldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int sourceIndex = 0; sourceIndex < sourcePaths.Length; sourceIndex++)
            {
                string sourcePath = (sourcePaths[sourceIndex] ?? string.Empty).Trim();
                if (!ordinalPaths.Add(sourcePath) || !caseFoldedPaths.Add(sourcePath))
                {
                    diagnostics.Add(Error(
                        "duplicate-source",
                        "sources 包含重复或仅大小写不同的路径：" + sourcePath,
                        RootCatalogPath,
                        "sources[" + sourceIndex + "]"));
                    continue;
                }

                string fragmentName = sourcePath.StartsWith(FragmentDirectory, StringComparison.Ordinal)
                    ? sourcePath.Substring(FragmentDirectory.Length)
                    : string.Empty;
                if (string.IsNullOrEmpty(fragmentName) ||
                    fragmentName.IndexOf('/') >= 0 ||
                    !fragmentName.EndsWith(".json", StringComparison.Ordinal))
                {
                    diagnostics.Add(Error(
                        "invalid-source-location",
                        "sources 只能显式引用 docs/architecture/project-atlas/*.json：" + sourcePath,
                        RootCatalogPath,
                        "sources[" + sourceIndex + "]"));
                    continue;
                }

                string absolutePath;
                try
                {
                    absolutePath = ResolveSafeProjectPath(projectRoot, sourcePath, true);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("invalid-source-path", exception.Message, sourcePath));
                    continue;
                }

                FragmentData fragment;
                try
                {
                    fragment = ReadStrict<FragmentData>(absolutePath);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("invalid-fragment-json", exception.Message, sourcePath));
                    continue;
                }

                if (fragment.schemaVersion != SchemaVersion)
                {
                    diagnostics.Add(Error(
                        "unsupported-fragment-schema-version",
                        "领域碎片 schemaVersion 必须是 1。",
                        sourcePath,
                        "schemaVersion"));
                    continue;
                }

                ReadReferences(fragment.references, sourcePath, references, diagnostics);
                ReadSystems(fragment.systems, sourcePath, systems, diagnostics);
            }
        }

        private static void ReadReferences(
            ReferenceData[] data,
            string sourcePath,
            ICollection<ProjectAtlasReference> references,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            foreach (ReferenceData item in data ?? Array.Empty<ReferenceData>())
            {
                if (item == null)
                {
                    diagnostics.Add(Error("invalid-reference", "references 不能包含 null。", sourcePath));
                    continue;
                }
                ValidateStableId(item.id, "references.id", sourcePath, diagnostics);
                ValidateStableId(item.kind, "references.kind", sourcePath, diagnostics);
                RequireText(item.target, "references.target", sourcePath, diagnostics);
                RequireText(item.displayName, "references.displayName", sourcePath, diagnostics);
                if (!string.IsNullOrWhiteSpace(item.coverageOwnerSystemId))
                    ValidateStableId(item.coverageOwnerSystemId, "references.coverageOwnerSystemId", sourcePath, diagnostics);
                references.Add(new ProjectAtlasReference(
                    Trim(item.id),
                    Trim(item.kind),
                    Trim(item.target),
                    Trim(item.displayName),
                    item.required,
                    Trim(item.coverageOwnerSystemId),
                    sourcePath));
            }
        }

        private static void ReadSystems(
            SystemData[] data,
            string sourcePath,
            ICollection<ProjectAtlasSystem> systems,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            foreach (SystemData item in data ?? Array.Empty<SystemData>())
            {
                if (item == null)
                {
                    diagnostics.Add(Error("invalid-system", "systems 不能包含 null。", sourcePath));
                    continue;
                }

                ValidateStableId(item.id, "systems.id", sourcePath, diagnostics);
                RequireText(item.displayName, "systems.displayName", sourcePath, diagnostics);
                RequireText(item.summary, "systems.summary", sourcePath, diagnostics);
                ValidateStableId(item.category, "systems.category", sourcePath, diagnostics);
                if (!Lifecycles.Contains(Trim(item.lifecycle)))
                    diagnostics.Add(Error("invalid-lifecycle", "未知 lifecycle：" + item.lifecycle, sourcePath, "systems.lifecycle"));
                if (!Ownerships.Contains(Trim(item.ownership)))
                    diagnostics.Add(Error("invalid-ownership", "未知 ownership：" + item.ownership, sourcePath, "systems.ownership"));
                if (item.ownerRoles == null || item.ownerRoles.All(string.IsNullOrWhiteSpace))
                    diagnostics.Add(Error("missing-owner-role", "系统至少需要一个 ownerRoles。", sourcePath, "systems.ownerRoles"));
                if (item.team == null || item.program == null || item.agent == null)
                    diagnostics.Add(Error("missing-projection", "系统必须同时声明 team、program 和 agent。", sourcePath, "systems"));

                ProjectAtlasTeamProjection team = ReadTeam(item, sourcePath, diagnostics);
                ProjectAtlasProgramProjection program = ReadProgram(item, sourcePath, diagnostics);
                ProjectAtlasAgentProjection agent = ReadAgent(item, sourcePath, diagnostics);
                ProjectAtlasRelation[] relations = ReadRelations(item.relations, sourcePath, diagnostics);

                systems.Add(new ProjectAtlasSystem(
                    Trim(item.id),
                    Trim(item.displayName),
                    Trim(item.summary),
                    Trim(item.category),
                    item.order,
                    Clean(item.keywords),
                    Clean(item.ownerRoles),
                    Trim(item.lifecycle),
                    Trim(item.ownership),
                    team,
                    program,
                    agent,
                    relations,
                    sourcePath));
            }
        }

        private static ProjectAtlasTeamProjection ReadTeam(
            SystemData system,
            string sourcePath,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            TeamData data = system.team;
            if (data == null)
                return new ProjectAtlasTeamProjection(string.Empty, null, null, string.Empty, string.Empty, null, null);
            string purpose = string.IsNullOrWhiteSpace(data.purpose) ? system.summary : data.purpose;
            if (data.audiences == null || data.audiences.All(string.IsNullOrWhiteSpace))
                diagnostics.Add(Error("missing-team-audience", "team.audiences 至少需要一项。", sourcePath));
            if (data.workflows == null || data.workflows.All(string.IsNullOrWhiteSpace))
                diagnostics.Add(Error("missing-team-workflow", "team.workflows 至少需要一项。", sourcePath));
            if (Trim(data.configurationMode) == "none" && string.IsNullOrWhiteSpace(data.configurationReason))
                diagnostics.Add(Error("missing-configuration-reason", "configurationMode=none 时必须说明原因。", sourcePath));
            if (Trim(data.configurationMode) != "none" && Trim(data.configurationMode) != "owned")
                diagnostics.Add(Error("invalid-configuration-mode", "configurationMode 只允许 owned 或 none。", sourcePath));
            return new ProjectAtlasTeamProjection(
                Trim(purpose),
                Clean(data.audiences),
                Clean(data.workflows),
                Trim(data.configurationMode),
                Trim(data.configurationReason),
                Clean(data.configurationRefs),
                Clean(data.diagnosticRefs));
        }

        private static ProjectAtlasProgramProjection ReadProgram(
            SystemData system,
            string sourcePath,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            ProgramData data = system.program;
            if (data == null)
                return new ProjectAtlasProgramProjection(null, null, null, null);
            string[] entryRefs = Clean(data.entryRefs);
            string[] structureRefs = Clean(data.structureRefs);
            string[] verificationRefs = Clean(data.verificationRefs);
            string lifecycle = Trim(system.lifecycle);
            if ((lifecycle == "active" || lifecycle == "experimental") &&
                entryRefs.Length == 0 && structureRefs.Length == 0)
            {
                diagnostics.Add(Error("missing-program-route", "active/experimental 系统至少需要一个程序入口或结构引用。", sourcePath));
            }
            return new ProjectAtlasProgramProjection(entryRefs, structureRefs, Clean(data.dataFlow), verificationRefs);
        }

        private static ProjectAtlasAgentProjection ReadAgent(
            SystemData system,
            string sourcePath,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            AgentData data = system.agent;
            if (data == null)
                return new ProjectAtlasAgentProjection(null, string.Empty, null, null);
            string[] readFirst = Clean(data.readFirstRefs);
            string[] verification = Clean(data.verificationRefs);
            string lifecycle = Trim(system.lifecycle);
            if ((lifecycle == "active" || lifecycle == "experimental") && readFirst.Length == 0)
                diagnostics.Add(Error("missing-agent-rule", "active/experimental 系统必须声明 agent.readFirstRefs。", sourcePath));
            if ((lifecycle == "active" || lifecycle == "experimental") && verification.Length == 0)
                diagnostics.Add(Error("missing-agent-verification", "active/experimental 系统必须声明 agent.verificationRefs。", sourcePath));
            RequireText(data.changeBoundary, "agent.changeBoundary", sourcePath, diagnostics);
            if (data.updateTriggers == null || data.updateTriggers.All(string.IsNullOrWhiteSpace))
                diagnostics.Add(Error("missing-update-trigger", "agent.updateTriggers 至少需要一项。", sourcePath));
            return new ProjectAtlasAgentProjection(readFirst, Trim(data.changeBoundary), verification, Clean(data.updateTriggers));
        }

        private static ProjectAtlasRelation[] ReadRelations(
            RelationData[] data,
            string sourcePath,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            var result = new List<ProjectAtlasRelation>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RelationData item in data ?? Array.Empty<RelationData>())
            {
                if (item == null)
                {
                    diagnostics.Add(Error("invalid-relation", "relations 不能包含 null。", sourcePath));
                    continue;
                }
                if (!RelationKinds.Contains(Trim(item.kind)))
                    diagnostics.Add(Error("invalid-relation-kind", "未知 relation kind：" + item.kind, sourcePath));
                ValidateStableId(item.targetSystemId, "relations.targetSystemId", sourcePath, diagnostics);
                string key = Trim(item.kind) + "\n" + Trim(item.targetSystemId);
                if (!keys.Add(key))
                    diagnostics.Add(Error("duplicate-relation", "重复关系：" + key.Replace('\n', '/'), sourcePath));
                result.Add(new ProjectAtlasRelation(Trim(item.kind), Trim(item.targetSystemId)));
            }
            return result.ToArray();
        }

        private static void ValidateCombinedGraph(
            ProjectAtlasProject project,
            IReadOnlyCollection<ProjectAtlasReference> references,
            IReadOnlyCollection<ProjectAtlasSystem> systems,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, ProjectAtlasReference> group in references.GroupBy(item => item.Id, StringComparer.Ordinal))
            {
                if (group.Count() > 1)
                    diagnostics.Add(Error("duplicate-reference-id", "重复 reference id：" + group.Key, group.First().SourcePath));
            }
            foreach (IGrouping<string, ProjectAtlasSystem> group in systems.GroupBy(item => item.Id, StringComparer.Ordinal))
            {
                if (group.Count() > 1)
                    diagnostics.Add(Error("duplicate-system-id", "重复 system id：" + group.Key, group.First().SourcePath));
            }

            var referenceIds = new HashSet<string>(references.Select(item => item.Id), StringComparer.Ordinal);
            var systemIds = new HashSet<string>(systems.Select(item => item.Id), StringComparer.Ordinal);
            if (!referenceIds.Contains(project.RootAgentRule))
                diagnostics.Add(Error("missing-root-agent-rule", "project.rootAgentRule 未指向现有 reference：" + project.RootAgentRule, RootCatalogPath));

            foreach (ProjectAtlasSystem system in systems)
            {
                IEnumerable<string> allRefs = system.Team.ConfigurationRefs
                    .Concat(system.Team.DiagnosticRefs)
                    .Concat(system.Program.AllReferenceIds)
                    .Concat(system.Agent.ReadFirstRefs)
                    .Concat(system.Agent.VerificationRefs);
                foreach (string referenceId in allRefs.Distinct(StringComparer.Ordinal))
                {
                    if (!referenceIds.Contains(referenceId))
                        diagnostics.Add(Error("missing-reference-id", system.Id + " 引用了不存在的 reference：" + referenceId, system.SourcePath));
                }
                foreach (ProjectAtlasRelation relation in system.Relations)
                {
                    if (!systemIds.Contains(relation.TargetSystemId))
                        diagnostics.Add(Error("missing-relation-target", system.Id + " 的关系目标不存在：" + relation.TargetSystemId, system.SourcePath));
                }
            }

            foreach (ProjectAtlasReference reference in references.Where(item => !string.IsNullOrEmpty(item.CoverageOwnerSystemId)))
            {
                ProjectAtlasSystem owner = systems.FirstOrDefault(item => item.Id == reference.CoverageOwnerSystemId);
                if (owner == null)
                {
                    diagnostics.Add(Error("missing-coverage-owner", reference.Id + " 的 coverage owner 不存在：" + reference.CoverageOwnerSystemId, reference.SourcePath));
                    continue;
                }
                if (!owner.Program.AllReferenceIds.Contains(reference.Id, StringComparer.Ordinal))
                    diagnostics.Add(Error("coverage-owner-does-not-reference", owner.Id + " 未在 program 投影引用其负责的 " + reference.Id, reference.SourcePath));
            }
        }

        private static void ResolveReferences(
            ProjectAtlasContext context,
            IEnumerable<ProjectAtlasReference> references,
            IEnumerable<Type> resolverTypes,
            IDictionary<string, ProjectAtlasReferenceResolution> resolutions,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            Dictionary<string, IProjectAtlasReferenceResolver> customResolvers = DiscoverResolvers(
                context.ProjectId,
                resolverTypes,
                diagnostics);
            foreach (ProjectAtlasReference reference in references)
            {
                ProjectAtlasReferenceResolution resolution;
                try
                {
                    resolution = ResolveBuiltIn(context, reference);
                    if (resolution == null && customResolvers.TryGetValue(reference.Kind, out IProjectAtlasReferenceResolver resolver))
                        resolution = resolver.Resolve(context, reference);
                    if (resolution == null)
                        resolution = new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, reference.Kind, "未注册对应 resolver。");
                }
                catch (Exception exception)
                {
                    resolution = new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, reference.Kind, exception.Message);
                    diagnostics.Add(Error("resolver-exception", reference.Id + " 解析失败：" + exception.Message, reference.SourcePath));
                }
                resolutions[reference.Id] = resolution;
                if (resolution.Status == ProjectAtlasResolutionStatus.Missing)
                {
                    diagnostics.Add(new ProjectAtlasDiagnostic(
                        reference.Required ? ProjectAtlasDiagnosticSeverity.Error : ProjectAtlasDiagnosticSeverity.Warning,
                        "unresolved-reference",
                        reference.DisplayName + " 未解析：" + (string.IsNullOrEmpty(resolution.Detail) ? reference.Target : resolution.Detail),
                        reference.SourcePath,
                        reference.Id));
                }
            }
        }

        private static Dictionary<string, IProjectAtlasReferenceResolver> DiscoverResolvers(
            string projectId,
            IEnumerable<Type> types,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            var result = new Dictionary<string, IProjectAtlasReferenceResolver>(StringComparer.Ordinal);
            foreach (Type type in (types ?? Array.Empty<Type>()).Where(type => type != null).OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IProjectAtlasReferenceResolver).IsAssignableFrom(type))
                    continue;
                var attribute = (ProjectAtlasReferenceResolverAttribute)Attribute.GetCustomAttribute(
                    type,
                    typeof(ProjectAtlasReferenceResolverAttribute),
                    false);
                if (attribute == null)
                {
                    diagnostics.Add(Error("resolver-missing-attribute", type.FullName + " 缺少 ProjectAtlasReferenceResolverAttribute。"));
                    continue;
                }
                if (!string.Equals(attribute.ProjectId, projectId, StringComparison.Ordinal))
                    continue;
                ValidateStableExtensionId(attribute.ResolverId, "resolverId", type.FullName, diagnostics);
                ValidateStableExtensionId(attribute.Kind, "resolver kind", type.FullName, diagnostics);
                if (IsBuiltInKind(attribute.Kind) || result.ContainsKey(attribute.Kind))
                {
                    diagnostics.Add(Error("duplicate-resolver-kind", "重复 resolver kind：" + attribute.Kind + "（" + type.FullName + "）"));
                    continue;
                }
                try
                {
                    result.Add(attribute.Kind, (IProjectAtlasReferenceResolver)Activator.CreateInstance(type));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("resolver-construction-failed", type.FullName + " 构造失败：" + exception.GetBaseException().Message));
                }
            }
            return result;
        }

        private static ProjectAtlasReferenceResolution ResolveBuiltIn(ProjectAtlasContext context, ProjectAtlasReference reference)
        {
            switch (reference.Kind)
            {
                case "path":
                case "doc":
                    return ResolvePath(context, reference);
                case "assembly":
                    return ResolveAssembly(context, reference);
                case "package":
                    return ResolvePackage(context, reference);
                case "dashboard-panel":
                    return ResolveDashboardPanel(context, reference);
                case "validation-lane":
                    return new ProjectAtlasReferenceResolution(
                        string.IsNullOrWhiteSpace(reference.Target) ? ProjectAtlasResolutionStatus.Missing : ProjectAtlasResolutionStatus.Resolved,
                        reference.Target,
                        "Project Atlas catalog",
                        string.IsNullOrWhiteSpace(reference.Target) ? "validation lane ID 为空。" : string.Empty);
                default:
                    return null;
            }
        }

        private static ProjectAtlasReferenceResolution ResolvePath(ProjectAtlasContext context, ProjectAtlasReference reference)
        {
            string absolutePath = ResolveSafeProjectPath(context.ProjectRoot, reference.Target, false);
            bool exists = File.Exists(absolutePath) || Directory.Exists(absolutePath);
            return new ProjectAtlasReferenceResolution(
                exists ? ProjectAtlasResolutionStatus.Resolved : ProjectAtlasResolutionStatus.Missing,
                reference.Target,
                reference.Target,
                exists ? string.Empty : "项目路径不存在。");
        }

        private static ProjectAtlasReferenceResolution ResolveAssembly(ProjectAtlasContext context, ProjectAtlasReference reference)
        {
            string absolutePath = ResolveSafeProjectPath(context.ProjectRoot, reference.Target, false);
            if (!File.Exists(absolutePath))
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, reference.Target, "asmdef 不存在。");
            JObject data = ReadJObjectStrict(absolutePath);
            string name = (string)data["name"] ?? Path.GetFileNameWithoutExtension(reference.Target);
            string[] directReferences = data["references"] is JArray array
                ? array.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
                : Array.Empty<string>();
            string detail = directReferences.Length == 0 ? "无直接程序集引用" : "直接引用：" + string.Join("、", directReferences);
            return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Resolved, name, reference.Target, detail);
        }

        private static ProjectAtlasReferenceResolution ResolvePackage(ProjectAtlasContext context, ProjectAtlasReference reference)
        {
            string manifestPath = ResolveSafeProjectPath(context.ProjectRoot, "Packages/manifest.json", false);
            if (!File.Exists(manifestPath))
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, "Packages/manifest.json", "manifest.json 不存在。");
            JObject manifest = ReadJObjectStrict(manifestPath);
            JObject dependencies = manifest["dependencies"] as JObject;
            string packageId = (string)dependencies?[reference.Target];
            if (string.IsNullOrWhiteSpace(packageId))
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, "Packages/manifest.json", "不是直接依赖。");

            string detail = "直接依赖";
            string lockPath = ResolveSafeProjectPath(context.ProjectRoot, "Packages/packages-lock.json", false);
            if (File.Exists(lockPath))
            {
                JObject lockData = ReadJObjectStrict(lockPath);
                JToken locked = lockData["dependencies"]?[reference.Target];
                string version = (string)locked?["version"];
                if (!string.IsNullOrWhiteSpace(version))
                    detail += "；lock=" + version;
            }
            return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Resolved, packageId, "Packages/manifest.json", detail);
        }

        private static ProjectAtlasReferenceResolution ResolveDashboardPanel(ProjectAtlasContext context, ProjectAtlasReference reference)
        {
            string[] parts = reference.Target.Split('/');
            if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, "Dashboard descriptors", "目标必须是 moduleId/panelId。");
            string assetsPath = Path.Combine(context.ProjectRoot, "Assets");
            if (!Directory.Exists(assetsPath))
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, "Assets/**/Editor/ZeroEngineDashboardModule.json", "Assets 目录不存在。");

            foreach (string descriptorPath in EnumerateProjectFiles(
                         context.ProjectRoot,
                         "Assets",
                         "ZeroEngineDashboardModule.json",
                         null))
            {
                JObject descriptor;
                try
                {
                    descriptor = ReadJObjectStrict(descriptorPath);
                }
                catch
                {
                    continue;
                }
                if (!string.Equals((string)descriptor["moduleId"], parts[0], StringComparison.Ordinal))
                    continue;
                JToken panel = (descriptor["panels"] as JArray)?.FirstOrDefault(item => string.Equals((string)item?["id"], parts[1], StringComparison.Ordinal));
                if (panel == null)
                    continue;
                string providerId = (string)panel["providerId"] ?? string.Empty;
                string relative = MakeRelativePath(context.ProjectRoot, descriptorPath);
                return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Resolved, reference.Target, relative, "provider=" + providerId);
            }
            return new ProjectAtlasReferenceResolution(ProjectAtlasResolutionStatus.Missing, reference.Target, "Assets/**/Editor/ZeroEngineDashboardModule.json", "未找到面板描述符。");
        }

        private static void EvaluateCoverage(
            ProjectAtlasContext context,
            IReadOnlyCollection<ProjectAtlasReference> references,
            IReadOnlyCollection<ProjectAtlasSystem> systems,
            IReadOnlyCollection<ProjectAtlasCoverageExclusion> exclusions,
            IEnumerable<Type> providerTypes,
            List<ProjectAtlasCoverageItem> coverage,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            coverage.AddRange(ReadDirectZeroEnginePackages(context, diagnostics));
            coverage.AddRange(ReadProjectDashboardPanels(context, diagnostics));

            Dictionary<string, ProjectAtlasCoverageContribution> contributions = DiscoverCoverage(
                context,
                providerTypes,
                diagnostics);
            foreach (string dimension in RequiredProjectCoverageDimensions)
            {
                if (!contributions.TryGetValue(dimension, out ProjectAtlasCoverageContribution contribution))
                {
                    diagnostics.Add(Error("missing-coverage-provider", "项目缺少 coverage dimension：" + dimension));
                    continue;
                }
                if (contribution.NotApplicable)
                {
                    if (string.IsNullOrWhiteSpace(contribution.Reason))
                        diagnostics.Add(Error("invalid-not-applicable", dimension + " 声明 not-applicable 时必须说明原因。"));
                    continue;
                }
                if (contribution.Items.Count == 0)
                    diagnostics.Add(Error("empty-coverage-dimension", dimension + " 没有枚举项，也未声明 not-applicable。"));
            }
            foreach (ProjectAtlasCoverageContribution contribution in contributions.Values.Where(item => !item.NotApplicable))
                coverage.AddRange(contribution.Items);

            var coverageKeys = new HashSet<string>(coverage.Select(CoverageKey), StringComparer.Ordinal);
            foreach (ProjectAtlasReference reference in references.Where(item =>
                         item.Kind == "validation-lane" || !string.IsNullOrEmpty(item.CoverageOwnerSystemId)))
            {
                string key = reference.Kind + "\n" + reference.Target;
                if (!coverageKeys.Contains(key))
                {
                    diagnostics.Add(Error(
                        "reference-not-backed-by-coverage",
                        reference.Id + " 声明了 coverage 引用，但项目 provider 未枚举 " +
                        reference.Kind + "/" + reference.Target + "。",
                        reference.SourcePath,
                        reference.Id));
                }
            }

            foreach (IGrouping<string, ProjectAtlasCoverageItem> group in coverage.GroupBy(CoverageKey, StringComparer.Ordinal))
            {
                if (group.Count() > 1)
                    diagnostics.Add(Error("duplicate-coverage-item", "coverage 重复枚举：" + group.Key.Replace('\n', '/')));
            }

            var usedExclusions = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectAtlasCoverageItem item in coverage.GroupBy(CoverageKey, StringComparer.Ordinal).Select(group => group.First()))
            {
                string key = CoverageKey(item);
                ProjectAtlasCoverageExclusion exclusion = exclusions.FirstOrDefault(candidate => CoverageKey(candidate) == key);
                if (exclusion != null)
                {
                    usedExclusions.Add(key);
                    continue;
                }

                ProjectAtlasReference[] owners = references
                    .Where(reference => reference.Kind == item.Kind &&
                                        reference.Target == item.Target &&
                                        !string.IsNullOrEmpty(reference.CoverageOwnerSystemId))
                    .ToArray();
                if (owners.Length == 0)
                {
                    diagnostics.Add(Error("unowned-coverage-item", item.DimensionId + " 未归属：" + item.Kind + "/" + item.Target));
                    continue;
                }
                if (owners.Length > 1)
                    diagnostics.Add(Error("multiple-coverage-owners", item.Kind + "/" + item.Target + " 有多个 primary owner。"));
                foreach (ProjectAtlasReference owner in owners)
                {
                    if (systems.All(system => system.Id != owner.CoverageOwnerSystemId))
                        diagnostics.Add(Error("invalid-coverage-owner", item.Kind + "/" + item.Target + " 的 owner 不存在。", owner.SourcePath));
                }
            }

            foreach (ProjectAtlasCoverageExclusion exclusion in exclusions)
            {
                string key = CoverageKey(exclusion);
                if (!usedExclusions.Contains(key))
                    diagnostics.Add(Error("stale-coverage-exclusion", "coverage exclusion 未匹配权威源实际枚举项：" + exclusion.Kind + "/" + exclusion.Target, RootCatalogPath));
            }
        }

        private static Dictionary<string, ProjectAtlasCoverageContribution> DiscoverCoverage(
            ProjectAtlasContext context,
            IEnumerable<Type> providerTypes,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            var result = new Dictionary<string, ProjectAtlasCoverageContribution>(StringComparer.Ordinal);
            var providerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type type in (providerTypes ?? Array.Empty<Type>()).Where(type => type != null).OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IProjectAtlasCoverageProvider).IsAssignableFrom(type))
                    continue;
                var attribute = (ProjectAtlasCoverageProviderAttribute)Attribute.GetCustomAttribute(
                    type,
                    typeof(ProjectAtlasCoverageProviderAttribute),
                    false);
                if (attribute == null)
                {
                    diagnostics.Add(Error("coverage-provider-missing-attribute", type.FullName + " 缺少 ProjectAtlasCoverageProviderAttribute。"));
                    continue;
                }
                if (!string.Equals(attribute.ProjectId, context.ProjectId, StringComparison.Ordinal))
                    continue;
                ValidateStableExtensionId(attribute.ProviderId, "coverage providerId", type.FullName, diagnostics);
                ValidateStableExtensionId(attribute.DimensionId, "coverage dimensionId", type.FullName, diagnostics);
                if (!providerIds.Add(attribute.ProviderId))
                {
                    diagnostics.Add(Error("duplicate-coverage-provider-id", "重复 coverage providerId：" + attribute.ProviderId));
                    continue;
                }
                if (result.ContainsKey(attribute.DimensionId))
                {
                    diagnostics.Add(Error("duplicate-coverage-dimension", "重复 coverage dimensionId：" + attribute.DimensionId));
                    continue;
                }
                try
                {
                    var provider = (IProjectAtlasCoverageProvider)Activator.CreateInstance(type);
                    ProjectAtlasCoverageContribution contribution = provider.GetCoverage(context);
                    if (contribution == null)
                        throw new InvalidOperationException("provider 返回 null。");
                    foreach (ProjectAtlasCoverageItem item in contribution.Items)
                    {
                        if (item == null || item.DimensionId != attribute.DimensionId ||
                            string.IsNullOrWhiteSpace(item.Kind) || string.IsNullOrWhiteSpace(item.Target))
                        {
                            throw new InvalidOperationException("provider 返回了无效 coverage item。");
                        }
                    }
                    result.Add(attribute.DimensionId, contribution);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("coverage-provider-failed", type.FullName + " 执行失败：" + exception.GetBaseException().Message));
                }
            }
            return result;
        }

        private static IEnumerable<ProjectAtlasCoverageItem> ReadDirectZeroEnginePackages(
            ProjectAtlasContext context,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            string path = ResolveSafeProjectPath(context.ProjectRoot, "Packages/manifest.json", false);
            if (!File.Exists(path))
            {
                diagnostics.Add(Error("missing-package-manifest", "无法枚举直接 ZE 包：Packages/manifest.json 不存在。"));
                return Array.Empty<ProjectAtlasCoverageItem>();
            }
            try
            {
                JObject dependencies = ReadJObjectStrict(path)["dependencies"] as JObject;
                return (dependencies?.Properties() ?? Enumerable.Empty<JProperty>())
                    .Where(property => property.Name.StartsWith("com.zerogamestudio.zeroengine", StringComparison.Ordinal))
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new ProjectAtlasCoverageItem("ze-packages", "package", property.Name, property.Name))
                    .ToArray();
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("invalid-package-manifest", exception.Message, "Packages/manifest.json"));
                return Array.Empty<ProjectAtlasCoverageItem>();
            }
        }

        private static IEnumerable<ProjectAtlasCoverageItem> ReadProjectDashboardPanels(
            ProjectAtlasContext context,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            var result = new List<ProjectAtlasCoverageItem>();
            string assetsPath = Path.Combine(context.ProjectRoot, "Assets");
            if (!Directory.Exists(assetsPath))
                return result;
            foreach (string path in EnumerateProjectFiles(
                         context.ProjectRoot,
                         "Assets",
                         "ZeroEngineDashboardModule.json",
                         diagnostics))
            {
                try
                {
                    JObject descriptor = ReadJObjectStrict(path);
                    if (!string.Equals((string)descriptor["scope"], "project", StringComparison.Ordinal))
                        continue;
                    string moduleId = (string)descriptor["moduleId"] ?? string.Empty;
                    foreach (JToken panel in descriptor["panels"] as JArray ?? new JArray())
                    {
                        string panelId = (string)panel?["id"] ?? string.Empty;
                        if (!string.IsNullOrEmpty(moduleId) && !string.IsNullOrEmpty(panelId))
                            result.Add(new ProjectAtlasCoverageItem("dashboard-panels", "dashboard-panel", moduleId + "/" + panelId, (string)panel?["displayName"]));
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("invalid-dashboard-descriptor", MakeRelativePath(context.ProjectRoot, path) + " 无法用于 Atlas coverage：" + exception.Message));
                }
            }
            return result;
        }

        private static Type[] GetCachedResolverTypes()
        {
            if (_cachedResolverTypes == null)
            {
                _cachedResolverTypes = TypeCache.GetTypesDerivedFrom<IProjectAtlasReferenceResolver>()
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToArray();
            }
            return _cachedResolverTypes;
        }

        private static Type[] GetCachedCoverageProviderTypes()
        {
            if (_cachedCoverageProviderTypes == null)
            {
                _cachedCoverageProviderTypes = TypeCache.GetTypesDerivedFrom<IProjectAtlasCoverageProvider>()
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToArray();
            }
            return _cachedCoverageProviderTypes;
        }

        private static IEnumerable<string> EnumerateProjectFiles(
            string projectRoot,
            string relativeRoot,
            string searchPattern,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            string absoluteRoot = ResolveSafeProjectPath(projectRoot, relativeRoot, false);
            if (!Directory.Exists(absoluteRoot))
                return Array.Empty<string>();

            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(absoluteRoot);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                                 .OrderBy(path => path, StringComparer.Ordinal))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        {
                            diagnostics?.Add(Warning(
                                "reparse-path-skipped",
                                "为避免越过项目根，已跳过重解析文件：" + MakeRelativePath(projectRoot, file)));
                            continue;
                        }
                        files.Add(file);
                    }

                    foreach (string child in Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                                 .OrderByDescending(path => path, StringComparer.Ordinal))
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        {
                            diagnostics?.Add(Warning(
                                "reparse-path-skipped",
                                "为避免越过项目根，已跳过重解析目录：" + MakeRelativePath(projectRoot, child)));
                            continue;
                        }
                        pending.Push(child);
                    }
                }
                catch (Exception exception)
                {
                    diagnostics?.Add(Error(
                        "project-enumeration-failed",
                        MakeRelativePath(projectRoot, directory) + " 枚举失败：" + exception.Message));
                }
            }
            return files.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static bool IsBuiltInKind(string kind)
        {
            return kind == "path" || kind == "doc" || kind == "assembly" || kind == "package" ||
                   kind == "dashboard-panel" || kind == "validation-lane";
        }

        private static string CoverageKey(ProjectAtlasCoverageItem item)
        {
            return item.Kind + "\n" + item.Target;
        }

        private static string CoverageKey(ProjectAtlasCoverageExclusion item)
        {
            return item.Kind + "\n" + item.Target;
        }

        private static T ReadStrict<T>(string path)
        {
            JObject token = ReadJObjectStrict(path);
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                DateParseHandling = DateParseHandling.None
            });
            return token.ToObject<T>(serializer);
        }

        private static JObject ReadJObjectStrict(string path)
        {
            string json = File.ReadAllText(path);
            JToken token = JToken.Parse(json, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Load
            });
            if (!(token is JObject result))
                throw new JsonException("JSON 根必须是对象：" + path);
            return result;
        }

        private static string NormalizeRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                return string.Empty;
            try
            {
                return Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string MakeRelativePath(string root, string path)
        {
            string rootUriText = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var rootUri = new Uri(rootUriText);
            var pathUri = new Uri(path);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('\\', '/');
        }

        private static void ValidateStableId(
            string value,
            string field,
            string sourcePath,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (!StableIdPattern.IsMatch(Trim(value)))
                diagnostics.Add(Error("invalid-stable-id", field + " 必须是小写稳定 ID：" + value, sourcePath, field));
        }

        private static void ValidateStableExtensionId(
            string value,
            string field,
            string source,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (!StableIdPattern.IsMatch(Trim(value)))
                diagnostics.Add(Error("invalid-extension-id", source + " 的 " + field + " 无效：" + value));
        }

        private static void RequireText(
            string value,
            string field,
            string sourcePath,
            ICollection<ProjectAtlasDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(value))
                diagnostics.Add(Error("missing-required-text", field + " 不能为空。", sourcePath, field));
        }

        private static string Trim(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string[] Clean(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Select(Trim)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToArray();
        }

        private static ProjectAtlasDiagnostic Error(string code, string message, string sourcePath = null, string fieldPath = null)
        {
            return new ProjectAtlasDiagnostic(ProjectAtlasDiagnosticSeverity.Error, code, message, sourcePath, fieldPath);
        }

        private static ProjectAtlasDiagnostic Warning(string code, string message, string sourcePath = null, string fieldPath = null)
        {
            return new ProjectAtlasDiagnostic(ProjectAtlasDiagnosticSeverity.Warning, code, message, sourcePath, fieldPath);
        }

        [Serializable]
        private sealed class RootData
        {
            public int schemaVersion;
            public ProjectData project;
            public string[] sources;
            public CoverageExclusionData[] coverageExclusions;
        }

        [Serializable]
        private sealed class ProjectData
        {
            public string id;
            public string displayName;
            public string summary;
            public string rootAgentRule;
        }

        [Serializable]
        private sealed class CoverageExclusionData
        {
            public string kind;
            public string target;
            public string reason;
        }

        [Serializable]
        private sealed class FragmentData
        {
            public int schemaVersion;
            public ReferenceData[] references;
            public SystemData[] systems;
        }

        [Serializable]
        private sealed class ReferenceData
        {
            public string id;
            public string kind;
            public string target;
            public string displayName;
            public bool required;
            public string coverageOwnerSystemId;
        }

        [Serializable]
        private sealed class SystemData
        {
            public string id;
            public string displayName;
            public string summary;
            public string category;
            public int order;
            public string[] keywords;
            public string[] ownerRoles;
            public string lifecycle;
            public string ownership;
            public TeamData team;
            public ProgramData program;
            public AgentData agent;
            public RelationData[] relations;
        }

        [Serializable]
        private sealed class TeamData
        {
            public string purpose;
            public string[] audiences;
            public string[] workflows;
            public string configurationMode;
            public string configurationReason;
            public string[] configurationRefs;
            public string[] diagnosticRefs;
        }

        [Serializable]
        private sealed class ProgramData
        {
            public string[] entryRefs;
            public string[] structureRefs;
            public string[] dataFlow;
            public string[] verificationRefs;
        }

        [Serializable]
        private sealed class AgentData
        {
            public string[] readFirstRefs;
            public string changeBoundary;
            public string[] verificationRefs;
            public string[] updateTriggers;
        }

        [Serializable]
        private sealed class RelationData
        {
            public string kind;
            public string targetSystemId;
        }
    }
}
