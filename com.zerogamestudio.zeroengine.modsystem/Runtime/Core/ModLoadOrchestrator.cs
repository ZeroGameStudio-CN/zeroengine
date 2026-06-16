using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.ModSystem
{
    public static class ModLoadOrchestrator
    {
        public static ModLoadReport LoadFromRegisteredSources(IEnumerable<IModContentImporter> importers)
        {
            return LoadFromSources(ModSourceRegistry.RegisteredSources, importers, null);
        }

        public static ModLoadReport LoadFromSources(
            IEnumerable<IModSource> sources,
            IEnumerable<IModContentImporter> importers,
            ModLoadOptions options = null)
        {
            var issues = new List<ModLoadIssue>();
            var manifests = new List<ModManifest>();
            var effectiveOptions = options ?? new ModLoadOptions();
            string manifestFileName = effectiveOptions.GetManifestFileName();

            foreach (var source in sources ?? Array.Empty<IModSource>())
            {
                if (source == null)
                    continue;

                bool sourceAvailable;
                try
                {
                    sourceAvailable = source.IsAvailable;
                }
                catch (Exception ex)
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Error, string.Empty, source.SourceId, ex.Message));
                    continue;
                }

                if (!sourceAvailable)
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Warning, string.Empty, source.SourceId, $"Mod source is not available: {source.SourceId}"));
                    continue;
                }

                ModSourceQueryResult queryResult = null;
                try
                {
                    source.QueryInstalledModFolders(result => queryResult = result);
                }
                catch (Exception ex)
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Error, string.Empty, source.SourceId, ex.Message));
                    continue;
                }

                if (queryResult == null)
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Error, string.Empty, source.SourceId, $"Mod source did not complete query: {source.SourceId}"));
                    continue;
                }

                if (!queryResult.Succeeded)
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Error, string.Empty, source.SourceId, queryResult.Error));
                    continue;
                }

                foreach (string folder in queryResult.ModFolders ?? Array.Empty<string>())
                {
                    if (ModManifestReader.TryRead(folder, out var manifest, out var issue, manifestFileName))
                    {
                        if (manifest.Enabled)
                            manifests.Add(manifest);
                    }
                    else if (issue != null)
                    {
                        issues.Add(issue);
                    }
                }
            }

            var sortedManifests = SortByDependencies(manifests, issues);
            var loadableManifests = FilterConflicts(sortedManifests, issues);
            var loadedManifests = new List<ModManifest>();

            foreach (var manifest in loadableManifests)
            {
                var context = new ModImportContext(manifest);
                bool importSucceeded = true;
                foreach (var importer in importers ?? Array.Empty<IModContentImporter>())
                {
                    if (importer == null)
                        continue;

                    try
                    {
                        var result = importer.Import(context);
                        if (result != null && !result.Succeeded)
                        {
                            importSucceeded = false;
                            issues.AddRange(result.Issues);
                        }
                    }
                    catch (Exception ex)
                    {
                        importSucceeded = false;
                        issues.Add(new ModLoadIssue(ModIssueSeverity.Error, manifest.Id, manifest.RootPath, ex.Message));
                    }
                }

                if (importSucceeded)
                    loadedManifests.Add(manifest);
            }

            return new ModLoadReport(loadedManifests, issues);
        }

        private static List<ModManifest> SortByDependencies(List<ModManifest> manifests, List<ModLoadIssue> issues)
        {
            var result = new List<ModManifest>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var byId = new Dictionary<string, ModManifest>(StringComparer.Ordinal);
            foreach (var group in manifests
                .Where(manifest => !string.IsNullOrWhiteSpace(manifest.Id))
                .GroupBy(manifest => manifest.Id, StringComparer.Ordinal))
            {
                var first = group.First();
                if (group.Count() > 1)
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Error, first.Id, first.RootPath, $"Duplicate mod Id: {first.Id}"));
                    blocked.Add(first.Id);
                    continue;
                }

                byId.Add(group.Key, first);
            }

            bool Visit(ModManifest manifest)
            {
                if (manifest == null || blocked.Contains(manifest.Id) || visited.Contains(manifest.Id))
                    return manifest != null && !blocked.Contains(manifest.Id);

                if (!visiting.Add(manifest.Id))
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Error, manifest.Id, manifest.RootPath, $"Circular mod dependency involving {manifest.Id}."));
                    blocked.Add(manifest.Id);
                    return false;
                }

                bool dependenciesSatisfied = true;
                foreach (string dependency in manifest.Dependencies ?? Array.Empty<string>())
                {
                    if (byId.TryGetValue(dependency, out var dependencyManifest))
                        dependenciesSatisfied &= Visit(dependencyManifest);
                    else
                    {
                        issues.Add(new ModLoadIssue(ModIssueSeverity.Error, manifest.Id, manifest.RootPath, $"Missing dependency: {dependency}"));
                        dependenciesSatisfied = false;
                    }
                }

                visiting.Remove(manifest.Id);
                visited.Add(manifest.Id);
                if (!dependenciesSatisfied || blocked.Contains(manifest.Id))
                {
                    blocked.Add(manifest.Id);
                    return false;
                }

                manifest.LoadOrder = result.Count;
                result.Add(manifest);
                return true;
            }

            foreach (var manifest in manifests)
                Visit(manifest);

            return result;
        }

        private static List<ModManifest> FilterConflicts(List<ModManifest> manifests, List<ModLoadIssue> issues)
        {
            var loadedIds = new HashSet<string>(StringComparer.Ordinal);
            var blockedByLoaded = new Dictionary<string, string>(StringComparer.Ordinal);
            var result = new List<ModManifest>();

            foreach (var manifest in manifests)
            {
                bool hasConflict = false;
                if (blockedByLoaded.TryGetValue(manifest.Id, out string loadedConflictOwner))
                {
                    issues.Add(new ModLoadIssue(ModIssueSeverity.Error, manifest.Id, manifest.RootPath, $"Conflicts with loaded mod: {loadedConflictOwner}"));
                    hasConflict = true;
                }

                foreach (string conflict in manifest.Conflicts ?? Array.Empty<string>())
                {
                    if (loadedIds.Contains(conflict))
                    {
                        issues.Add(new ModLoadIssue(ModIssueSeverity.Error, manifest.Id, manifest.RootPath, $"Conflicts with loaded mod: {conflict}"));
                        hasConflict = true;
                    }
                }

                if (hasConflict)
                    continue;

                manifest.LoadOrder = result.Count;
                loadedIds.Add(manifest.Id);
                foreach (string conflict in manifest.Conflicts ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(conflict) && !blockedByLoaded.ContainsKey(conflict))
                        blockedByLoaded.Add(conflict, manifest.Id);
                }

                result.Add(manifest);
            }

            return result;
        }
    }
}
