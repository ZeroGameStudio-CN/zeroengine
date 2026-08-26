using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ZeroEngine.ProjectAtlas
{
    public enum ProjectAtlasDiagnosticSeverity
    {
        Warning,
        Error
    }

    public enum ProjectAtlasResolutionStatus
    {
        Resolved,
        Missing,
        NotApplicable
    }

    public sealed class ProjectAtlasDiagnostic
    {
        public ProjectAtlasDiagnostic(
            ProjectAtlasDiagnosticSeverity severity,
            string code,
            string message,
            string sourcePath = null,
            string fieldPath = null)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
        }

        public ProjectAtlasDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string SourcePath { get; }
        public string FieldPath { get; }
    }

    public sealed class ProjectAtlasReference
    {
        public ProjectAtlasReference(
            string id,
            string kind,
            string target,
            string displayName,
            bool required,
            string coverageOwnerSystemId,
            string sourcePath)
        {
            Id = id ?? string.Empty;
            Kind = kind ?? string.Empty;
            Target = target ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Required = required;
            CoverageOwnerSystemId = coverageOwnerSystemId ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
        }

        public string Id { get; }
        public string Kind { get; }
        public string Target { get; }
        public string DisplayName { get; }
        public bool Required { get; }
        public string CoverageOwnerSystemId { get; }
        public string SourcePath { get; }
    }

    public sealed class ProjectAtlasReferenceResolution
    {
        public ProjectAtlasReferenceResolution(
            ProjectAtlasResolutionStatus status,
            string displayValue,
            string authority,
            string detail = null)
        {
            Status = status;
            DisplayValue = displayValue ?? string.Empty;
            Authority = authority ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public ProjectAtlasResolutionStatus Status { get; }
        public string DisplayValue { get; }
        public string Authority { get; }
        public string Detail { get; }
    }

    public sealed class ProjectAtlasRelation
    {
        public ProjectAtlasRelation(string kind, string targetSystemId)
        {
            Kind = kind ?? string.Empty;
            TargetSystemId = targetSystemId ?? string.Empty;
        }

        public string Kind { get; }
        public string TargetSystemId { get; }
    }

    public sealed class ProjectAtlasTeamProjection
    {
        public ProjectAtlasTeamProjection(
            string purpose,
            IEnumerable<string> audiences,
            IEnumerable<string> workflows,
            string configurationMode,
            string configurationReason,
            IEnumerable<string> configurationRefs,
            IEnumerable<string> diagnosticRefs)
        {
            Purpose = purpose ?? string.Empty;
            Audiences = Freeze(audiences);
            Workflows = Freeze(workflows);
            ConfigurationMode = configurationMode ?? string.Empty;
            ConfigurationReason = configurationReason ?? string.Empty;
            ConfigurationRefs = Freeze(configurationRefs);
            DiagnosticRefs = Freeze(diagnosticRefs);
        }

        public string Purpose { get; }
        public IReadOnlyList<string> Audiences { get; }
        public IReadOnlyList<string> Workflows { get; }
        public string ConfigurationMode { get; }
        public string ConfigurationReason { get; }
        public IReadOnlyList<string> ConfigurationRefs { get; }
        public IReadOnlyList<string> DiagnosticRefs { get; }

        private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
        {
            return Array.AsReadOnly((values ?? Array.Empty<string>()).Where(value => value != null).ToArray());
        }
    }

    public sealed class ProjectAtlasProgramProjection
    {
        public ProjectAtlasProgramProjection(
            IEnumerable<string> entryRefs,
            IEnumerable<string> structureRefs,
            IEnumerable<string> dataFlow,
            IEnumerable<string> verificationRefs)
        {
            EntryRefs = Freeze(entryRefs);
            StructureRefs = Freeze(structureRefs);
            DataFlow = Freeze(dataFlow);
            VerificationRefs = Freeze(verificationRefs);
        }

        public IReadOnlyList<string> EntryRefs { get; }
        public IReadOnlyList<string> StructureRefs { get; }
        public IReadOnlyList<string> DataFlow { get; }
        public IReadOnlyList<string> VerificationRefs { get; }

        public IEnumerable<string> AllReferenceIds => EntryRefs.Concat(StructureRefs).Concat(VerificationRefs);

        private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
        {
            return Array.AsReadOnly((values ?? Array.Empty<string>()).Where(value => value != null).ToArray());
        }
    }

    public sealed class ProjectAtlasAgentProjection
    {
        public ProjectAtlasAgentProjection(
            IEnumerable<string> readFirstRefs,
            string changeBoundary,
            IEnumerable<string> verificationRefs,
            IEnumerable<string> updateTriggers)
        {
            ReadFirstRefs = Freeze(readFirstRefs);
            ChangeBoundary = changeBoundary ?? string.Empty;
            VerificationRefs = Freeze(verificationRefs);
            UpdateTriggers = Freeze(updateTriggers);
        }

        public IReadOnlyList<string> ReadFirstRefs { get; }
        public string ChangeBoundary { get; }
        public IReadOnlyList<string> VerificationRefs { get; }
        public IReadOnlyList<string> UpdateTriggers { get; }

        private static IReadOnlyList<string> Freeze(IEnumerable<string> values)
        {
            return Array.AsReadOnly((values ?? Array.Empty<string>()).Where(value => value != null).ToArray());
        }
    }

    public sealed class ProjectAtlasSystem
    {
        public ProjectAtlasSystem(
            string id,
            string displayName,
            string summary,
            string category,
            int order,
            IEnumerable<string> keywords,
            IEnumerable<string> ownerRoles,
            string lifecycle,
            string ownership,
            ProjectAtlasTeamProjection team,
            ProjectAtlasProgramProjection program,
            ProjectAtlasAgentProjection agent,
            IEnumerable<ProjectAtlasRelation> relations,
            string sourcePath)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Summary = summary ?? string.Empty;
            Category = category ?? string.Empty;
            Order = order;
            Keywords = Array.AsReadOnly((keywords ?? Array.Empty<string>()).ToArray());
            OwnerRoles = Array.AsReadOnly((ownerRoles ?? Array.Empty<string>()).ToArray());
            Lifecycle = lifecycle ?? string.Empty;
            Ownership = ownership ?? string.Empty;
            Team = team;
            Program = program;
            Agent = agent;
            Relations = Array.AsReadOnly((relations ?? Array.Empty<ProjectAtlasRelation>()).ToArray());
            SourcePath = sourcePath ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Summary { get; }
        public string Category { get; }
        public int Order { get; }
        public IReadOnlyList<string> Keywords { get; }
        public IReadOnlyList<string> OwnerRoles { get; }
        public string Lifecycle { get; }
        public string Ownership { get; }
        public ProjectAtlasTeamProjection Team { get; }
        public ProjectAtlasProgramProjection Program { get; }
        public ProjectAtlasAgentProjection Agent { get; }
        public IReadOnlyList<ProjectAtlasRelation> Relations { get; }
        public string SourcePath { get; }
    }

    public sealed class ProjectAtlasCoverageItem
    {
        public ProjectAtlasCoverageItem(string dimensionId, string kind, string target, string displayName)
        {
            DimensionId = dimensionId ?? string.Empty;
            Kind = kind ?? string.Empty;
            Target = target ?? string.Empty;
            DisplayName = displayName ?? target ?? string.Empty;
        }

        public string DimensionId { get; }
        public string Kind { get; }
        public string Target { get; }
        public string DisplayName { get; }
    }

    public sealed class ProjectAtlasCoverageContribution
    {
        private ProjectAtlasCoverageContribution(IEnumerable<ProjectAtlasCoverageItem> items, bool notApplicable, string reason)
        {
            Items = Array.AsReadOnly((items ?? Array.Empty<ProjectAtlasCoverageItem>()).ToArray());
            NotApplicable = notApplicable;
            Reason = reason ?? string.Empty;
        }

        public IReadOnlyList<ProjectAtlasCoverageItem> Items { get; }
        public bool NotApplicable { get; }
        public string Reason { get; }

        public static ProjectAtlasCoverageContribution Required(IEnumerable<ProjectAtlasCoverageItem> items)
        {
            return new ProjectAtlasCoverageContribution(items, false, string.Empty);
        }

        public static ProjectAtlasCoverageContribution NotRequired(string reason)
        {
            return new ProjectAtlasCoverageContribution(Array.Empty<ProjectAtlasCoverageItem>(), true, reason);
        }
    }

    public sealed class ProjectAtlasCoverageExclusion
    {
        public ProjectAtlasCoverageExclusion(string kind, string target, string reason)
        {
            Kind = kind ?? string.Empty;
            Target = target ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public string Kind { get; }
        public string Target { get; }
        public string Reason { get; }
    }

    public sealed class ProjectAtlasProject
    {
        public ProjectAtlasProject(string id, string displayName, string summary, string rootAgentRule)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Summary = summary ?? string.Empty;
            RootAgentRule = rootAgentRule ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Summary { get; }
        public string RootAgentRule { get; }
    }

    public sealed class ProjectAtlasGraph
    {
        internal ProjectAtlasGraph(
            string projectRoot,
            ProjectAtlasProject project,
            IEnumerable<ProjectAtlasSystem> systems,
            IEnumerable<ProjectAtlasReference> references,
            IDictionary<string, ProjectAtlasReferenceResolution> resolutions,
            IEnumerable<ProjectAtlasCoverageItem> coverage,
            IEnumerable<ProjectAtlasDiagnostic> diagnostics,
            IEnumerable<ProjectAtlasCoverageExclusion> exclusions)
        {
            ProjectRoot = projectRoot ?? string.Empty;
            Project = project;
            Systems = Array.AsReadOnly((systems ?? Array.Empty<ProjectAtlasSystem>())
                .OrderBy(system => system.Order)
                .ThenBy(system => system.Id, StringComparer.Ordinal)
                .ToArray());
            References = Array.AsReadOnly((references ?? Array.Empty<ProjectAtlasReference>())
                .OrderBy(reference => reference.Id, StringComparer.Ordinal)
                .ToArray());
            Resolutions = new ReadOnlyDictionary<string, ProjectAtlasReferenceResolution>(
                new Dictionary<string, ProjectAtlasReferenceResolution>(
                    resolutions ?? new Dictionary<string, ProjectAtlasReferenceResolution>(),
                    StringComparer.Ordinal));
            Coverage = Array.AsReadOnly((coverage ?? Array.Empty<ProjectAtlasCoverageItem>())
                .OrderBy(item => item.DimensionId, StringComparer.Ordinal)
                .ThenBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Target, StringComparer.Ordinal)
                .ToArray());
            Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ProjectAtlasDiagnostic>()).ToArray());
            CoverageExclusions = Array.AsReadOnly((exclusions ?? Array.Empty<ProjectAtlasCoverageExclusion>()).ToArray());
        }

        public string ProjectRoot { get; }
        public ProjectAtlasProject Project { get; }
        public IReadOnlyList<ProjectAtlasSystem> Systems { get; }
        public IReadOnlyList<ProjectAtlasReference> References { get; }
        public IReadOnlyDictionary<string, ProjectAtlasReferenceResolution> Resolutions { get; }
        public IReadOnlyList<ProjectAtlasCoverageItem> Coverage { get; }
        public IReadOnlyList<ProjectAtlasDiagnostic> Diagnostics { get; }
        public IReadOnlyList<ProjectAtlasCoverageExclusion> CoverageExclusions { get; }
        public bool HasErrors => Diagnostics.Any(item => item.Severity == ProjectAtlasDiagnosticSeverity.Error);

        public ProjectAtlasSystem FindSystem(string id)
        {
            return Systems.FirstOrDefault(system => string.Equals(system.Id, id, StringComparison.Ordinal));
        }

        public ProjectAtlasReference FindReference(string id)
        {
            return References.FirstOrDefault(reference => string.Equals(reference.Id, id, StringComparison.Ordinal));
        }
    }

    public sealed class ProjectAtlasContext
    {
        public ProjectAtlasContext(string projectRoot, string projectId)
        {
            ProjectRoot = projectRoot ?? string.Empty;
            ProjectId = projectId ?? string.Empty;
        }

        public string ProjectRoot { get; }
        public string ProjectId { get; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ProjectAtlasReferenceResolverAttribute : Attribute
    {
        public ProjectAtlasReferenceResolverAttribute(string projectId, string resolverId, string kind)
        {
            ProjectId = projectId ?? string.Empty;
            ResolverId = resolverId ?? string.Empty;
            Kind = kind ?? string.Empty;
        }

        public string ProjectId { get; }
        public string ResolverId { get; }
        public string Kind { get; }
    }

    public interface IProjectAtlasReferenceResolver
    {
        ProjectAtlasReferenceResolution Resolve(ProjectAtlasContext context, ProjectAtlasReference reference);
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ProjectAtlasCoverageProviderAttribute : Attribute
    {
        public ProjectAtlasCoverageProviderAttribute(string projectId, string providerId, string dimensionId)
        {
            ProjectId = projectId ?? string.Empty;
            ProviderId = providerId ?? string.Empty;
            DimensionId = dimensionId ?? string.Empty;
        }

        public string ProjectId { get; }
        public string ProviderId { get; }
        public string DimensionId { get; }
    }

    public interface IProjectAtlasCoverageProvider
    {
        ProjectAtlasCoverageContribution GetCoverage(ProjectAtlasContext context);
    }
}
