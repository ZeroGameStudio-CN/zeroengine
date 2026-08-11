using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ZeroEngine.Editor.Dashboard
{
    internal enum DashboardSourceKind
    {
        Package,
        Project
    }

    internal enum DashboardDiagnosticSeverity
    {
        Warning,
        Error
    }

    internal enum DashboardEntryKind
    {
        Window,
        Command
    }

    internal enum DashboardEntrySafety
    {
        Navigation,
        ReadOnly,
        ProjectWrite,
        Destructive
    }

    internal enum DashboardEntryAvailability
    {
        Always,
        EditMode,
        PlayMode
    }

    internal enum DashboardModuleScope
    {
        Universal,
        Project
    }

    internal enum DashboardEntryVisibility
    {
        Primary,
        Advanced,
        Maintenance
    }

    internal enum DashboardEntryExecutionKind
    {
        LegacyMenu,
        Provider
    }

    [Serializable]
    internal sealed class DashboardDescriptorData
    {
        public int schemaVersion;
        public string moduleId;
        public string displayName;
        public string description;
        public int order;
        public string documentationPath;
        public string documentationUrl;
        public string scope;
        public string projectId;
        public string projectDisplayName;
        public DashboardEntryData[] entries;
        public DashboardPanelData[] panels;
    }

    [Serializable]
    internal sealed class DashboardEntryData
    {
        public string id;
        public string displayName;
        public string description;
        public string usage;
        public string mountModuleId;
        public string category;
        public string kind;
        public string menuPath;
        public int order;
        public string safety;
        public string confirmation;
        public string availability;
        public string[] replaces;
        public string section;
        public string surfaceId;
        public string surfaceDisplayName;
        public string surfaceActionLabel;
        public bool surfaceDefault;
        public string visibility;
        public string executionKind;
        public string providerId;
        public string actionId;
        public string[] legacyKeywords;
        public string documentationPath;
        public string documentationUrl;
    }

    [Serializable]
    internal sealed class DashboardPanelData
    {
        public string id;
        public string displayName;
        public string description;
        public string usage;
        public string section;
        public string providerId;
        public int order;
        public string safety;
        public string availability;
    }

    internal sealed class DashboardDescriptorSource
    {
        internal DashboardDescriptorSource(
            DashboardSourceKind kind,
            string sourcePath,
            string rootPath,
            string packageName,
            string packageVersion,
            string json,
            string readError = null,
            string projectRootPath = null)
        {
            Kind = kind;
            SourcePath = sourcePath ?? string.Empty;
            RootPath = rootPath ?? string.Empty;
            PackageName = packageName ?? string.Empty;
            PackageVersion = packageVersion ?? string.Empty;
            Json = json;
            ReadError = readError;
            ProjectRootPath = projectRootPath ?? RootPath;
        }

        internal DashboardSourceKind Kind { get; }
        internal string SourcePath { get; }
        internal string RootPath { get; }
        internal string PackageName { get; }
        internal string PackageVersion { get; }
        internal string Json { get; }
        internal string ReadError { get; }
        internal string ProjectRootPath { get; }
    }

    internal sealed class DashboardInstalledPackage
    {
        internal DashboardInstalledPackage(string name, string version, string resolvedPath)
        {
            Name = name ?? string.Empty;
            Version = version ?? string.Empty;
            ResolvedPath = resolvedPath ?? string.Empty;
        }

        internal string Name { get; }
        internal string Version { get; }
        internal string ResolvedPath { get; }
    }

    internal sealed class DashboardDiagnostic
    {
        internal DashboardDiagnostic(
            DashboardDiagnosticSeverity severity,
            string code,
            string message,
            string sourcePath,
            string moduleId = null,
            string entryId = null,
            string menuPath = null)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            ModuleId = moduleId ?? string.Empty;
            EntryId = entryId ?? string.Empty;
            MenuPath = menuPath ?? string.Empty;
        }

        internal DashboardDiagnosticSeverity Severity { get; }
        internal string Code { get; }
        internal string Message { get; }
        internal string SourcePath { get; }
        internal string ModuleId { get; }
        internal string EntryId { get; }
        internal string MenuPath { get; }
    }

    internal sealed class DashboardEntry
    {
        internal DashboardEntry(
            string moduleId,
            string id,
            string displayName,
            string description,
            string category,
            DashboardEntryKind kind,
            string menuPath,
            int order,
            DashboardEntrySafety safety,
            string confirmation,
            DashboardEntryAvailability availability,
            IReadOnlyList<string> replaces,
            string mountModuleId,
            string sourcePath,
            string section,
            string surfaceId,
            string surfaceDisplayName,
            string surfaceActionLabel,
            bool surfaceDefault,
            string usage,
            DashboardEntryExecutionKind executionKind = DashboardEntryExecutionKind.LegacyMenu,
            string providerId = null,
            string actionId = null,
            DashboardEntryVisibility visibility = DashboardEntryVisibility.Primary,
            IReadOnlyList<string> legacyKeywords = null,
            string documentationPath = null,
            string documentationUrl = null)
        {
            ModuleId = moduleId;
            Id = id;
            FullId = moduleId + "/" + id;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            Category = category;
            Kind = kind;
            MenuPath = menuPath ?? string.Empty;
            Order = order;
            Safety = safety;
            Confirmation = confirmation ?? string.Empty;
            Availability = availability;
            Replaces = replaces ?? Array.Empty<string>();
            MountModuleId = mountModuleId ?? string.Empty;
            SourcePath = sourcePath;
            Section = string.IsNullOrEmpty(section) ? "常规" : section;
            SurfaceId = surfaceId ?? string.Empty;
            SurfaceDisplayName = surfaceDisplayName ?? string.Empty;
            SurfaceActionLabel = surfaceActionLabel ?? string.Empty;
            SurfaceDefault = surfaceDefault;
            Usage = usage ?? string.Empty;
            ExecutionKind = executionKind;
            ProviderId = providerId ?? string.Empty;
            ActionId = actionId ?? string.Empty;
            Visibility = visibility;
            LegacyKeywords = legacyKeywords ?? Array.Empty<string>();
            DocumentationPath = documentationPath ?? string.Empty;
            DocumentationUrl = documentationUrl ?? string.Empty;
        }

        internal string ModuleId { get; }
        internal string Id { get; }
        internal string FullId { get; }
        internal string DisplayName { get; }
        internal string Description { get; }
        internal string Category { get; }
        internal DashboardEntryKind Kind { get; }
        internal string MenuPath { get; }
        internal int Order { get; }
        internal DashboardEntrySafety Safety { get; }
        internal string Confirmation { get; }
        internal DashboardEntryAvailability Availability { get; }
        internal IReadOnlyList<string> Replaces { get; }
        internal string MountModuleId { get; }
        internal string DisplayModuleId => string.IsNullOrEmpty(MountModuleId) ? ModuleId : MountModuleId;
        internal string SourcePath { get; }
        internal string Section { get; }
        internal string SurfaceId { get; }
        internal string SurfaceDisplayName { get; }
        internal string SurfaceActionLabel { get; }
        internal bool SurfaceDefault { get; }
        internal string Usage { get; }
        internal DashboardEntryExecutionKind ExecutionKind { get; }
        internal string ProviderId { get; }
        internal string ActionId { get; }
        internal DashboardEntryVisibility Visibility { get; }
        internal IReadOnlyList<string> LegacyKeywords { get; }
        internal string DocumentationPath { get; }
        internal string DocumentationUrl { get; }
        internal bool IsLegacy => ExecutionKind == DashboardEntryExecutionKind.LegacyMenu;
        internal bool Isolated { get; set; }
        internal bool HiddenByReplacement { get; set; }
        internal bool SurfaceGroupingRejected { get; set; }
        internal string EffectiveSurfaceId => string.IsNullOrEmpty(SurfaceId) ? FullId : SurfaceId;
    }

    internal sealed class DashboardSurface
    {
        internal DashboardSurface(IReadOnlyList<DashboardEntry> entries)
        {
            Entries = entries
                .OrderBy(entry => entry.Order)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FullId, StringComparer.Ordinal)
                .ToArray();
            DashboardEntry primary = Entries.FirstOrDefault(entry => entry.SurfaceDefault) ?? Entries[0];
            DefaultEntry = primary;
            SurfaceId = primary.EffectiveSurfaceId;
            Section = primary.Section;
            DisplayName = Entries.Select(entry => entry.SurfaceDisplayName)
                              .FirstOrDefault(value => !string.IsNullOrEmpty(value)) ??
                          primary.DisplayName;
            Description = primary.Description;
            Usage = primary.Usage;
            Order = Entries.Min(entry => entry.Order);
        }

        internal string SurfaceId { get; }
        internal string Section { get; }
        internal string DisplayName { get; }
        internal string Description { get; }
        internal string Usage { get; }
        internal int Order { get; }
        internal DashboardEntry DefaultEntry { get; }
        internal IReadOnlyList<DashboardEntry> Entries { get; }
    }

    internal sealed class DashboardModule
    {
        internal DashboardModule(
            string moduleId,
            string displayName,
            string description,
            int order,
            string documentationPath,
            string documentationUrl,
            DashboardDescriptorSource source,
            IReadOnlyList<DashboardEntry> entries,
            IReadOnlyList<DashboardEntry> visibleEntries = null,
            IReadOnlyList<DashboardPanel> panels = null,
            int schemaVersion = 1,
            DashboardModuleScope scope = DashboardModuleScope.Universal,
            string projectId = null,
            string projectDisplayName = null)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            Order = order;
            DocumentationPath = documentationPath ?? string.Empty;
            DocumentationUrl = documentationUrl ?? string.Empty;
            Source = source;
            Entries = entries ?? Array.Empty<DashboardEntry>();
            _visibleEntries = visibleEntries ?? Entries;
            Panels = panels ?? Array.Empty<DashboardPanel>();
            SchemaVersion = schemaVersion;
            Scope = scope;
            ProjectId = projectId ?? string.Empty;
            ProjectDisplayName = projectDisplayName ?? string.Empty;
        }

        internal string ModuleId { get; }
        internal string DisplayName { get; }
        internal string Description { get; }
        internal int Order { get; }
        internal string DocumentationPath { get; }
        internal string DocumentationUrl { get; }
        internal DashboardDescriptorSource Source { get; }
        internal IReadOnlyList<DashboardEntry> Entries { get; }
        internal IReadOnlyList<DashboardPanel> Panels { get; }
        internal int SchemaVersion { get; }
        internal DashboardModuleScope Scope { get; }
        internal string ProjectId { get; }
        internal string ProjectDisplayName { get; }
        internal bool IsLegacy => SchemaVersion == 1;
        internal IReadOnlyList<DashboardEntry> OwnedVisibleEntries =>
            Entries.Where(entry => !entry.Isolated && !entry.HiddenByReplacement).ToArray();
        internal IReadOnlyList<DashboardEntry> VisibleEntries =>
            _visibleEntries.Where(entry => !entry.Isolated && !entry.HiddenByReplacement).ToArray();
        internal IReadOnlyList<DashboardSurface> VisibleSurfaces => VisibleEntries
            .GroupBy(entry => entry.SurfaceGroupingRejected ? entry.FullId : entry.EffectiveSurfaceId, StringComparer.Ordinal)
            .Select(group => new DashboardSurface(group.ToArray()))
            .OrderBy(surface => surface.Order)
            .ThenBy(surface => surface.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(surface => surface.SurfaceId, StringComparer.Ordinal)
            .ToArray();

        private readonly IReadOnlyList<DashboardEntry> _visibleEntries;
    }

    internal sealed class DashboardPanel
    {
        internal DashboardPanel(
            string moduleId,
            string id,
            string displayName,
            string description,
            string usage,
            string section,
            string providerId,
            int order,
            DashboardEntrySafety safety,
            DashboardEntryAvailability availability,
            string sourcePath)
        {
            ModuleId = moduleId;
            Id = id;
            FullId = moduleId + "/" + id;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            Usage = usage ?? string.Empty;
            Section = string.IsNullOrEmpty(section) ? "常规" : section;
            ProviderId = providerId;
            Order = order;
            Safety = safety;
            Availability = availability;
            SourcePath = sourcePath;
        }

        internal string ModuleId { get; }
        internal string Id { get; }
        internal string FullId { get; }
        internal string DisplayName { get; }
        internal string Description { get; }
        internal string Usage { get; }
        internal string Section { get; }
        internal string ProviderId { get; }
        internal int Order { get; }
        internal DashboardEntrySafety Safety { get; }
        internal DashboardEntryAvailability Availability { get; }
        internal string SourcePath { get; }
    }

    internal sealed class DashboardCatalog
    {
        internal static readonly DashboardCatalog Empty = new DashboardCatalog(
            Array.Empty<DashboardModule>(),
            Array.Empty<DashboardInstalledPackage>(),
            Array.Empty<DashboardDiagnostic>());

        internal DashboardCatalog(
            IReadOnlyList<DashboardModule> modules,
            IReadOnlyList<DashboardInstalledPackage> installedPackages,
            IReadOnlyList<DashboardDiagnostic> diagnostics)
        {
            Modules = modules ?? Array.Empty<DashboardModule>();
            InstalledPackages = installedPackages ?? Array.Empty<DashboardInstalledPackage>();
            Diagnostics = diagnostics ?? Array.Empty<DashboardDiagnostic>();
        }

        internal IReadOnlyList<DashboardModule> Modules { get; }
        internal IReadOnlyList<DashboardInstalledPackage> InstalledPackages { get; }
        internal IReadOnlyList<DashboardDiagnostic> Diagnostics { get; }
        internal IReadOnlyList<DashboardModule> VisibleModules => Modules.Where(module => module.VisibleEntries.Count > 0).ToArray();
        internal IReadOnlyList<DashboardModule> VisibleWorkspaceModules => Modules.Where(module => module.Panels.Count > 0).ToArray();
    }

    internal static class DashboardCatalogBuilder
    {
        private static readonly Regex ModuleIdPattern = new Regex(
            "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex EntryIdPattern = new Regex(
            "^[a-z0-9]+(?:-[a-z0-9]+)*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex FullEntryIdPattern = new Regex(
            "^[a-z0-9]+(?:[.-][a-z0-9]+)*/[a-z0-9]+(?:-[a-z0-9]+)*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex EntriesFieldPattern = new Regex(
            "\\\"entries\\\"\\s*:",
            RegexOptions.CultureInvariant);

        private static readonly Regex ShortcutSuffixPattern = new Regex(
            "\\s[%#&_][^\\s]*$",
            RegexOptions.CultureInvariant);

        private static readonly HashSet<string> LegacyCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "authoring",
            "diagnostics",
            "setup",
            "documentation"
        };

        private static readonly HashSet<string> CategoriesV2 = new HashSet<string>(StringComparer.Ordinal)
        {
            "authoring",
            "data-localization",
            "assets-build",
            "diagnostics",
            "test-release",
            "system-setup"
        };

        internal static DashboardCatalog Build(
            IReadOnlyList<DashboardDescriptorSource> sources,
            IReadOnlyList<DashboardInstalledPackage> installedPackages)
        {
            sources = sources ?? Array.Empty<DashboardDescriptorSource>();
            installedPackages = installedPackages ?? Array.Empty<DashboardInstalledPackage>();

            var diagnostics = new List<DashboardDiagnostic>();
            var installedNames = new HashSet<string>(
                installedPackages.Select(package => package.Name),
                StringComparer.Ordinal);
            var parsedModules = new List<DashboardModule>();

            foreach (DashboardDescriptorSource source in sources.OrderBy(item => item.SourcePath, StringComparer.Ordinal))
            {
                DashboardModule module = Parse(source, installedNames, diagnostics);
                if (module != null)
                    parsedModules.Add(module);
            }

            var duplicateModuleIds = new HashSet<string>(
                parsedModules.GroupBy(module => module.ModuleId, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.Ordinal);

            foreach (DashboardModule module in parsedModules.Where(module => duplicateModuleIds.Contains(module.ModuleId)))
            {
                diagnostics.Add(Error(
                    "duplicate-module-id",
                    "Multiple descriptors declare moduleId '" + module.ModuleId + "'; all are isolated.",
                    module.Source.SourcePath,
                    module.ModuleId));
            }

            List<DashboardModule> activeModules = parsedModules
                .Where(module => !duplicateModuleIds.Contains(module.ModuleId))
                .ToList();
            List<DashboardEntry> entries = activeModules.SelectMany(module => module.Entries).ToList();

            IsolateEntryConflicts(entries, diagnostics);
            IsolateActionBindingConflicts(entries, diagnostics);
            ValidateMountTargets(activeModules, entries, diagnostics);
            ApplyReplacements(activeModules, entries, installedNames, diagnostics);
            ValidateSurfaceGroups(entries, diagnostics);

            DashboardEntry[] displayEntries = entries.Where(entry => !entry.Isolated).ToArray();

            DashboardModule[] orderedModules = activeModules
                .OrderBy(module => module.Order)
                .ThenBy(module => module.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(module => module.ModuleId, StringComparer.Ordinal)
                .Select(module => new DashboardModule(
                    module.ModuleId,
                    module.DisplayName,
                    module.Description,
                    module.Order,
                    module.DocumentationPath,
                    module.DocumentationUrl,
                    module.Source,
                    module.Entries
                        .Where(entry => !entry.Isolated)
                        .OrderBy(entry => entry.Order)
                        .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(entry => entry.FullId, StringComparer.Ordinal)
                        .ToArray(),
                    displayEntries
                        .Where(entry => string.Equals(entry.DisplayModuleId, module.ModuleId, StringComparison.Ordinal))
                        .OrderBy(entry => entry.Order)
                        .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(entry => entry.FullId, StringComparer.Ordinal)
                        .ToArray(),
                    module.Panels
                        .OrderBy(panel => panel.Order)
                        .ThenBy(panel => panel.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(panel => panel.FullId, StringComparer.Ordinal)
                        .ToArray(),
                    module.SchemaVersion,
                    module.Scope,
                    module.ProjectId,
                    module.ProjectDisplayName))
                .ToArray();

            DashboardInstalledPackage[] orderedPackages = installedPackages
                .OrderBy(package => package.Name, StringComparer.Ordinal)
                .ToArray();
            DashboardDiagnostic[] orderedDiagnostics = diagnostics
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.EntryId, StringComparer.Ordinal)
                .ToArray();

            return new DashboardCatalog(orderedModules, orderedPackages, orderedDiagnostics);
        }

        private static DashboardModule Parse(
            DashboardDescriptorSource source,
            HashSet<string> installedPackageNames,
            List<DashboardDiagnostic> diagnostics)
        {
            var errors = new List<string>();
            if (!string.IsNullOrEmpty(source.ReadError))
            {
                diagnostics.Add(Error("descriptor-read-failed", source.ReadError, source.SourcePath));
                return null;
            }

            if (string.IsNullOrWhiteSpace(source.Json))
            {
                diagnostics.Add(Error("descriptor-empty", "Descriptor is empty.", source.SourcePath));
                return null;
            }

            DashboardDescriptorData data;
            try
            {
                data = JsonUtility.FromJson<DashboardDescriptorData>(source.Json);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("descriptor-json-invalid", exception.Message, source.SourcePath));
                return null;
            }

            if (data == null)
            {
                diagnostics.Add(Error("descriptor-json-invalid", "Descriptor JSON did not produce an object.", source.SourcePath));
                return null;
            }

            string moduleId = Trim(data.moduleId);
            string displayName = Trim(data.displayName);
            bool isV2 = data.schemaVersion == 2;

            if (data.schemaVersion != 1 && !isV2)
                errors.Add("schemaVersion must be 1 or 2.");
            if (!ModuleIdPattern.IsMatch(moduleId))
                errors.Add("moduleId must be a lowercase ASCII stable ID.");
            if (string.IsNullOrEmpty(displayName))
                errors.Add("displayName is required.");
            if (ContainsMarkup(data.description))
                errors.Add("description must not contain markup.");
            if (!EntriesFieldPattern.IsMatch(source.Json) || data.entries == null)
                errors.Add("entries must be present as an array.");

            if (source.Kind == DashboardSourceKind.Package &&
                !string.Equals(moduleId, source.PackageName, StringComparison.Ordinal))
            {
                errors.Add("Package descriptor moduleId must equal PackageInfo.name.");
            }

            if (source.Kind == DashboardSourceKind.Project && installedPackageNames.Contains(moduleId))
                errors.Add("Project descriptor moduleId must not impersonate an installed package.");

            DashboardModuleScope scope = DashboardModuleScope.Universal;
            string projectId = string.Empty;
            string projectDisplayName = string.Empty;
            if (isV2)
            {
                if (!TryParseScope(data.scope, out scope))
                    errors.Add("scope must be universal or project.");
                projectId = Trim(data.projectId);
                projectDisplayName = Trim(data.projectDisplayName);
                if (scope == DashboardModuleScope.Project)
                {
                    if (!ModuleIdPattern.IsMatch(projectId))
                        errors.Add("projectId must be a lowercase ASCII stable ID for project scope.");
                    if (string.IsNullOrEmpty(projectDisplayName) || ContainsMarkup(projectDisplayName))
                        errors.Add("projectDisplayName is required and must not contain markup for project scope.");
                }
                else if (!string.IsNullOrEmpty(projectId) || !string.IsNullOrEmpty(projectDisplayName))
                {
                    errors.Add("universal scope must not declare projectId or projectDisplayName.");
                }
            }

            string documentationRoot = isV2 && source.Kind == DashboardSourceKind.Project
                ? source.ProjectRootPath
                : source.RootPath;
            string documentationPath = ValidateDocumentationPath(data.documentationPath, documentationRoot, errors);
            string documentationUrl = ValidateDocumentationUrl(data.documentationUrl, errors);
            var entries = new List<DashboardEntry>();
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            var panels = new List<DashboardPanel>();
            var panelIds = new HashSet<string>(StringComparer.Ordinal);

            if (data.entries != null)
            {
                for (int index = 0; index < data.entries.Length; index++)
                {
                    DashboardEntry entry = ParseEntry(
                        data.entries[index],
                        moduleId,
                        source.SourcePath,
                        documentationRoot,
                        data.schemaVersion,
                        index,
                        errors);
                    if (entry == null)
                        continue;
                    if (!entryIds.Add(entry.Id))
                        errors.Add("entries contains duplicate id '" + entry.Id + "'.");
                    entries.Add(entry);
                }
            }

            if (data.panels != null)
            {
                for (int index = 0; index < data.panels.Length; index++)
                {
                    DashboardPanel panel = ParsePanel(data.panels[index], moduleId, source.SourcePath, index, errors);
                    if (panel == null)
                        continue;
                    if (!panelIds.Add(panel.Id))
                        errors.Add("panels contains duplicate id '" + panel.Id + "'.");
                    panels.Add(panel);
                }
            }

            if (errors.Count > 0)
            {
                diagnostics.Add(Error(
                    "descriptor-invalid",
                    string.Join(" ", errors.Distinct()),
                    source.SourcePath,
                    moduleId));
                return null;
            }

            if (!isV2)
            {
                diagnostics.Add(new DashboardDiagnostic(
                    DashboardDiagnosticSeverity.Warning,
                    "legacy-schema-v1",
                    "Schema v1 entry execution is deprecated and supported only through Dashboard 4.x.",
                    source.SourcePath,
                    moduleId));
            }

            return new DashboardModule(
                moduleId,
                displayName,
                data.description ?? string.Empty,
                data.order,
                documentationPath,
                documentationUrl,
                source,
                entries,
                panels: panels,
                schemaVersion: data.schemaVersion,
                scope: scope,
                projectId: projectId,
                projectDisplayName: projectDisplayName);
        }

        private static DashboardEntry ParseEntry(
            DashboardEntryData data,
            string moduleId,
            string sourcePath,
            string documentationRoot,
            int schemaVersion,
            int index,
            List<string> errors)
        {
            if (data == null)
            {
                errors.Add("entries[" + index + "] must be an object.");
                return null;
            }

            string prefix = "entries[" + index + "] ";
            string id = Trim(data.id);
            string displayName = Trim(data.displayName);
            string category = Trim(data.category);
            string menuPath = Trim(data.menuPath);
            string confirmation = Trim(data.confirmation);
            string mountModuleId = Trim(data.mountModuleId);
            string section = Trim(data.section);
            string surfaceId = Trim(data.surfaceId);
            string surfaceDisplayName = Trim(data.surfaceDisplayName);
            string surfaceActionLabel = Trim(data.surfaceActionLabel);
            string usage = Trim(data.usage);
            string providerId = Trim(data.providerId);
            string actionId = Trim(data.actionId);
            bool isV2 = schemaVersion == 2;

            if (!EntryIdPattern.IsMatch(id))
                errors.Add(prefix + "id must be lowercase kebab-case.");
            if (string.IsNullOrEmpty(displayName))
                errors.Add(prefix + "displayName is required.");
            if (ContainsMarkup(data.description))
                errors.Add(prefix + "description must not contain markup.");
            if (ContainsMarkup(usage))
                errors.Add(prefix + "usage must not contain markup.");
            if (!string.IsNullOrEmpty(mountModuleId) && !ModuleIdPattern.IsMatch(mountModuleId))
                errors.Add(prefix + "mountModuleId must be a lowercase ASCII stable ID.");
            if (!string.IsNullOrEmpty(surfaceId) && !EntryIdPattern.IsMatch(surfaceId))
                errors.Add(prefix + "surfaceId must be lowercase kebab-case.");
            if (ContainsMarkup(section))
                errors.Add(prefix + "section must not contain markup.");
            if (ContainsMarkup(surfaceDisplayName))
                errors.Add(prefix + "surfaceDisplayName must not contain markup.");
            if (ContainsMarkup(surfaceActionLabel))
                errors.Add(prefix + "surfaceActionLabel must not contain markup.");
            if (!(isV2 ? CategoriesV2 : LegacyCategories).Contains(category))
                errors.Add(prefix + "category is invalid.");
            if (!TryParseKind(data.kind, out DashboardEntryKind kind))
                errors.Add(prefix + "kind is invalid.");
            if (!TryParseSafety(data.safety, out DashboardEntrySafety safety))
                errors.Add(prefix + "safety is invalid.");
            if (!TryParseAvailability(data.availability, out DashboardEntryAvailability availability))
                errors.Add(prefix + "availability is invalid.");
            if (kind == DashboardEntryKind.Window && safety != DashboardEntrySafety.Navigation)
                errors.Add(prefix + "window entries must use navigation safety.");
            if ((safety == DashboardEntrySafety.ProjectWrite || safety == DashboardEntrySafety.Destructive) &&
                string.IsNullOrEmpty(confirmation))
            {
                errors.Add(prefix + "confirmation is required for write-capable entries.");
            }

            DashboardEntryExecutionKind executionKind = DashboardEntryExecutionKind.LegacyMenu;
            DashboardEntryVisibility visibility = DashboardEntryVisibility.Primary;
            if (isV2)
            {
                if (!string.IsNullOrEmpty(menuPath))
                    errors.Add(prefix + "menuPath is not allowed in schema v2.");
                if (!string.Equals(Trim(data.executionKind), "provider", StringComparison.Ordinal))
                    errors.Add(prefix + "executionKind must be provider in schema v2.");
                else
                    executionKind = DashboardEntryExecutionKind.Provider;
                if (!ModuleIdPattern.IsMatch(providerId))
                    errors.Add(prefix + "providerId must be a lowercase ASCII stable ID.");
                if (!EntryIdPattern.IsMatch(actionId))
                    errors.Add(prefix + "actionId must be lowercase kebab-case.");
                if (!TryParseVisibility(data.visibility, out visibility))
                    errors.Add(prefix + "visibility is invalid.");
                if (safety == DashboardEntrySafety.Destructive && visibility != DashboardEntryVisibility.Maintenance)
                    errors.Add(prefix + "destructive entries must use maintenance visibility.");
                if (safety == DashboardEntrySafety.ProjectWrite && visibility == DashboardEntryVisibility.Primary)
                    errors.Add(prefix + "project-write entries must use advanced or maintenance visibility.");
            }
            else if (!IsValidMenuPath(menuPath))
            {
                errors.Add(prefix + "menuPath must be a full path without a shortcut suffix.");
            }

            var legacyKeywords = new List<string>();
            if (data.legacyKeywords != null)
            {
                foreach (string rawKeyword in data.legacyKeywords)
                {
                    string keyword = Trim(rawKeyword);
                    if (string.IsNullOrEmpty(keyword) || ContainsMarkup(keyword))
                    {
                        errors.Add(prefix + "legacyKeywords must contain non-empty text without markup.");
                        continue;
                    }
                    if (!legacyKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                        legacyKeywords.Add(keyword);
                }
            }

            var documentationErrors = new List<string>();
            string documentationPath = isV2
                ? ValidateDocumentationPath(data.documentationPath, documentationRoot, documentationErrors)
                : string.Empty;
            string documentationUrl = isV2
                ? ValidateDocumentationUrl(data.documentationUrl, documentationErrors)
                : string.Empty;
            foreach (string documentationError in documentationErrors)
                errors.Add(prefix + documentationError);

            var replacements = new List<string>();
            if (data.replaces != null)
            {
                foreach (string rawReplacement in data.replaces)
                {
                    string replacement = Trim(rawReplacement);
                    if (!FullEntryIdPattern.IsMatch(replacement))
                    {
                        errors.Add(prefix + "replaces contains invalid full entry ID '" + replacement + "'.");
                        continue;
                    }

                    if (!replacements.Contains(replacement, StringComparer.Ordinal))
                        replacements.Add(replacement);
                }
            }

            return new DashboardEntry(
                moduleId,
                id,
                displayName,
                data.description,
                category,
                kind,
                menuPath,
                data.order,
                safety,
                confirmation,
                availability,
                replacements,
                mountModuleId,
                sourcePath,
                section,
                surfaceId,
                surfaceDisplayName,
                surfaceActionLabel,
                data.surfaceDefault,
                usage,
                executionKind,
                providerId,
                actionId,
                visibility,
                legacyKeywords,
                documentationPath,
                documentationUrl);
        }

        private static DashboardPanel ParsePanel(
            DashboardPanelData data,
            string moduleId,
            string sourcePath,
            int index,
            List<string> errors)
        {
            if (data == null)
            {
                errors.Add("panels[" + index + "] must be an object.");
                return null;
            }

            string prefix = "panels[" + index + "] ";
            string id = Trim(data.id);
            string displayName = Trim(data.displayName);
            string description = Trim(data.description);
            string usage = Trim(data.usage);
            string section = Trim(data.section);
            string providerId = Trim(data.providerId);

            if (!EntryIdPattern.IsMatch(id))
                errors.Add(prefix + "id must be lowercase kebab-case.");
            if (string.IsNullOrEmpty(displayName))
                errors.Add(prefix + "displayName is required.");
            if (ContainsMarkup(displayName) || ContainsMarkup(description) || ContainsMarkup(usage) || ContainsMarkup(section))
                errors.Add(prefix + "display text must not contain markup.");
            if (!ModuleIdPattern.IsMatch(providerId))
                errors.Add(prefix + "providerId must be a lowercase ASCII stable ID.");
            if (!TryParseSafety(data.safety, out DashboardEntrySafety safety))
                errors.Add(prefix + "safety is invalid.");
            if (!TryParseAvailability(data.availability, out DashboardEntryAvailability availability))
                errors.Add(prefix + "availability is invalid.");

            return new DashboardPanel(
                moduleId,
                id,
                displayName,
                description,
                usage,
                section,
                providerId,
                data.order,
                safety,
                availability,
                sourcePath);
        }

        private static void ValidateSurfaceGroups(
            IReadOnlyList<DashboardEntry> entries,
            List<DashboardDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, DashboardEntry> group in entries
                         .Where(entry => !entry.Isolated && !entry.HiddenByReplacement && !string.IsNullOrEmpty(entry.SurfaceId))
                         .GroupBy(entry => entry.DisplayModuleId + "/" + entry.SurfaceId, StringComparer.Ordinal))
            {
                DashboardEntry[] members = group.ToArray();
                if (members.Length < 2)
                    continue;

                DashboardEntry first = members[0];
                string[] names = members.Select(entry => entry.SurfaceDisplayName)
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                bool valid = members.All(entry => string.Equals(entry.Category, first.Category, StringComparison.Ordinal)) &&
                             members.All(entry => string.Equals(entry.Section, first.Section, StringComparison.Ordinal)) &&
                             names.Length <= 1 &&
                             members.Count(entry => entry.SurfaceDefault) <= 1;
                if (valid)
                    continue;

                foreach (DashboardEntry entry in members)
                {
                    entry.SurfaceGroupingRejected = true;
                    diagnostics.Add(EntryError(
                        "surface-contract-conflict",
                        "Surface '" + group.Key + "' has incompatible category, section, display name, or defaults; entries are shown separately.",
                        entry));
                }
            }
        }

        private static void ValidateMountTargets(
            IReadOnlyList<DashboardModule> modules,
            IReadOnlyList<DashboardEntry> entries,
            List<DashboardDiagnostic> diagnostics)
        {
            var activeModuleIds = new HashSet<string>(
                modules.Select(module => module.ModuleId),
                StringComparer.Ordinal);

            foreach (DashboardEntry entry in entries.Where(entry =>
                         !entry.Isolated &&
                         !string.IsNullOrEmpty(entry.MountModuleId) &&
                         !activeModuleIds.Contains(entry.MountModuleId)))
            {
                entry.Isolated = true;
                diagnostics.Add(new DashboardDiagnostic(
                    DashboardDiagnosticSeverity.Warning,
                    "mount-target-missing",
                    "Mount target module '" + entry.MountModuleId + "' is missing or isolated; the entry is hidden.",
                    entry.SourcePath,
                    entry.ModuleId,
                    entry.Id,
                    entry.MenuPath));
            }
        }

        private static void IsolateEntryConflicts(
            IReadOnlyList<DashboardEntry> entries,
            List<DashboardDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, DashboardEntry> group in entries.GroupBy(entry => entry.FullId, StringComparer.Ordinal))
            {
                if (group.Count() < 2)
                    continue;
                foreach (DashboardEntry entry in group)
                {
                    entry.Isolated = true;
                    diagnostics.Add(EntryError(
                        "duplicate-entry-id",
                        "Duplicate full entry ID '" + entry.FullId + "'; all conflicting entries are isolated.",
                        entry));
                }
            }

            foreach (IGrouping<string, DashboardEntry> group in entries
                         .Where(entry => !entry.Isolated && entry.IsLegacy && !string.IsNullOrEmpty(entry.MenuPath))
                         .GroupBy(entry => entry.MenuPath, StringComparer.Ordinal))
            {
                if (group.Count() < 2)
                    continue;
                foreach (DashboardEntry entry in group)
                {
                    entry.Isolated = true;
                    diagnostics.Add(EntryError(
                        "duplicate-menu-path",
                        "Menu path '" + entry.MenuPath + "' is declared by multiple entries; all are isolated.",
                        entry));
                }
            }
        }

        private static void IsolateActionBindingConflicts(
            IReadOnlyList<DashboardEntry> entries,
            List<DashboardDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, DashboardEntry> group in entries
                         .Where(entry => !entry.Isolated && entry.ExecutionKind == DashboardEntryExecutionKind.Provider)
                         .GroupBy(entry => entry.ProviderId + "/" + entry.ActionId, StringComparer.Ordinal))
            {
                if (group.Count() < 2)
                    continue;
                foreach (DashboardEntry entry in group)
                {
                    entry.Isolated = true;
                    diagnostics.Add(EntryError(
                        "duplicate-action-binding",
                        "Provider action '" + group.Key + "' is bound by multiple entries; all are isolated.",
                        entry));
                }
            }
        }

        private static void ApplyReplacements(
            IReadOnlyList<DashboardModule> modules,
            IReadOnlyList<DashboardEntry> entries,
            HashSet<string> installedPackageNames,
            List<DashboardDiagnostic> diagnostics)
        {
            var activeEntries = entries
                .Where(entry => !entry.Isolated)
                .ToDictionary(entry => entry.FullId, entry => entry, StringComparer.Ordinal);
            var knownModules = new HashSet<string>(modules.Select(module => module.ModuleId), StringComparer.Ordinal);
            knownModules.UnionWith(installedPackageNames);
            var edges = new List<ReplacementEdge>();

            foreach (DashboardEntry entry in activeEntries.Values.OrderBy(item => item.FullId, StringComparer.Ordinal))
            {
                foreach (string targetId in entry.Replaces.OrderBy(item => item, StringComparer.Ordinal))
                {
                    string targetModuleId = targetId.Substring(0, targetId.IndexOf('/', StringComparison.Ordinal));
                    if (!knownModules.Contains(targetModuleId))
                        continue;

                    if (!activeEntries.TryGetValue(targetId, out DashboardEntry target))
                    {
                        diagnostics.Add(new DashboardDiagnostic(
                            DashboardDiagnosticSeverity.Warning,
                            "replacement-target-missing",
                            "Replacement target '" + targetId + "' is missing or isolated.",
                            entry.SourcePath,
                            entry.ModuleId,
                            entry.Id,
                            entry.MenuPath));
                        continue;
                    }

                    edges.Add(new ReplacementEdge(entry, target));
                }
            }

            foreach (IGrouping<string, ReplacementEdge> group in edges.GroupBy(edge => edge.Target.FullId, StringComparer.Ordinal))
            {
                DashboardEntry[] replacers = group.Select(edge => edge.From).Distinct().ToArray();
                if (replacers.Length < 2)
                    continue;
                foreach (DashboardEntry replacer in replacers)
                {
                    replacer.Isolated = true;
                    diagnostics.Add(EntryError(
                        "multiple-replacers",
                        "Multiple entries replace '" + group.Key + "'; all replacers are isolated.",
                        replacer));
                }
            }

            edges = edges.Where(edge => !edge.From.Isolated && !edge.Target.Isolated).ToList();
            HashSet<DashboardEntry> cycleNodes = FindCycleNodes(edges);
            foreach (DashboardEntry entry in cycleNodes.OrderBy(item => item.FullId, StringComparer.Ordinal))
            {
                entry.Isolated = true;
                diagnostics.Add(EntryError(
                    "replacement-cycle",
                    "Entry participates in a replacement cycle and is isolated.",
                    entry));
            }

            foreach (ReplacementEdge edge in edges.Where(edge => !edge.From.Isolated && !edge.Target.Isolated))
                edge.Target.HiddenByReplacement = true;
        }

        private static HashSet<DashboardEntry> FindCycleNodes(IReadOnlyList<ReplacementEdge> edges)
        {
            var outgoing = edges
                .GroupBy(edge => edge.From)
                .ToDictionary(group => group.Key, group => group.Select(edge => edge.Target).ToArray());
            var state = new Dictionary<DashboardEntry, int>();
            var stack = new List<DashboardEntry>();
            var cycleNodes = new HashSet<DashboardEntry>();

            foreach (DashboardEntry entry in edges.SelectMany(edge => new[] { edge.From, edge.Target }).Distinct())
                Visit(entry, outgoing, state, stack, cycleNodes);
            return cycleNodes;
        }

        private static void Visit(
            DashboardEntry entry,
            IReadOnlyDictionary<DashboardEntry, DashboardEntry[]> outgoing,
            IDictionary<DashboardEntry, int> state,
            IList<DashboardEntry> stack,
            ISet<DashboardEntry> cycleNodes)
        {
            if (state.TryGetValue(entry, out int currentState))
            {
                if (currentState != 1)
                    return;
                int index = stack.IndexOf(entry);
                for (int i = Math.Max(index, 0); i < stack.Count; i++)
                    cycleNodes.Add(stack[i]);
                return;
            }

            state[entry] = 1;
            stack.Add(entry);
            if (outgoing.TryGetValue(entry, out DashboardEntry[] targets))
            {
                foreach (DashboardEntry target in targets)
                    Visit(target, outgoing, state, stack, cycleNodes);
            }
            stack.RemoveAt(stack.Count - 1);
            state[entry] = 2;
        }

        private static string ValidateDocumentationPath(string value, string rootPath, List<string> errors)
        {
            value = Trim(value);
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (Path.IsPathRooted(value) || value.Split('/', '\\').Any(segment => segment == ".."))
            {
                errors.Add("documentationPath must stay below its descriptor root.");
                return string.Empty;
            }

            try
            {
                string root = Path.GetFullPath(rootPath);
                string candidate = Path.GetFullPath(Path.Combine(root, value));
                string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("documentationPath escapes its descriptor root.");
                    return string.Empty;
                }
                return candidate;
            }
            catch (Exception)
            {
                errors.Add("documentationPath is invalid.");
                return string.Empty;
            }
        }

        private static string ValidateDocumentationUrl(string value, List<string> errors)
        {
            value = Trim(value);
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("documentationUrl must use https.");
                return string.Empty;
            }
            return uri.AbsoluteUri;
        }

        private static bool IsValidMenuPath(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf('/') > 0 &&
                   !value.StartsWith("/", StringComparison.Ordinal) &&
                   !value.EndsWith("/", StringComparison.Ordinal) &&
                   !value.Contains("//") &&
                   !ShortcutSuffixPattern.IsMatch(value);
        }

        private static bool TryParseKind(string value, out DashboardEntryKind result)
        {
            switch (Trim(value))
            {
                case "window": result = DashboardEntryKind.Window; return true;
                case "command": result = DashboardEntryKind.Command; return true;
                default: result = default; return false;
            }
        }

        private static bool TryParseSafety(string value, out DashboardEntrySafety result)
        {
            switch (Trim(value))
            {
                case "navigation": result = DashboardEntrySafety.Navigation; return true;
                case "read-only": result = DashboardEntrySafety.ReadOnly; return true;
                case "project-write": result = DashboardEntrySafety.ProjectWrite; return true;
                case "destructive": result = DashboardEntrySafety.Destructive; return true;
                default: result = default; return false;
            }
        }

        private static bool TryParseAvailability(string value, out DashboardEntryAvailability result)
        {
            switch (Trim(value))
            {
                case "always": result = DashboardEntryAvailability.Always; return true;
                case "edit-mode": result = DashboardEntryAvailability.EditMode; return true;
                case "play-mode": result = DashboardEntryAvailability.PlayMode; return true;
                default: result = default; return false;
            }
        }

        private static bool TryParseScope(string value, out DashboardModuleScope result)
        {
            switch (Trim(value))
            {
                case "universal": result = DashboardModuleScope.Universal; return true;
                case "project": result = DashboardModuleScope.Project; return true;
                default: result = default; return false;
            }
        }

        private static bool TryParseVisibility(string value, out DashboardEntryVisibility result)
        {
            switch (Trim(value))
            {
                case "primary": result = DashboardEntryVisibility.Primary; return true;
                case "advanced": result = DashboardEntryVisibility.Advanced; return true;
                case "maintenance": result = DashboardEntryVisibility.Maintenance; return true;
                default: result = default; return false;
            }
        }

        private static bool ContainsMarkup(string value)
        {
            return !string.IsNullOrEmpty(value) && (value.IndexOf('<') >= 0 || value.IndexOf('>') >= 0);
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static DashboardDiagnostic Error(
            string code,
            string message,
            string sourcePath,
            string moduleId = null)
        {
            return new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Error,
                code,
                message,
                sourcePath,
                moduleId);
        }

        private static DashboardDiagnostic EntryError(string code, string message, DashboardEntry entry)
        {
            return new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Error,
                code,
                message,
                entry.SourcePath,
                entry.ModuleId,
                entry.Id,
                entry.MenuPath);
        }

        private sealed class ReplacementEdge
        {
            internal ReplacementEdge(DashboardEntry from, DashboardEntry target)
            {
                From = from;
                Target = target;
            }

            internal DashboardEntry From { get; }
            internal DashboardEntry Target { get; }
        }
    }
}
