using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.ModSystem
{
    public static class ModLoadOrchestrator
    {
        [Obsolete("Use LoadFromRegisteredSourcesAsync.")]
        public static ModLoadReport LoadFromRegisteredSources(IEnumerable<IModContentImporter> importers)
        {
            return LoadFromSources(ModSourceRegistry.RegisteredSources, importers, null);
        }

        [Obsolete("Use LoadFromSourcesAsync.")]
        public static ModLoadReport LoadFromSources(
            IEnumerable<IModSource> sources,
            IEnumerable<IModContentImporter> importers,
            ModLoadOptions options = null)
        {
            var outcomes = new List<SourceQueryOutcome>();
            int order = 0;
            foreach (var source in sources ?? Array.Empty<IModSource>())
            {
                if (source == null)
                    continue;

                string sourceId = GetSourceId(source);
                try
                {
                    if (!source.IsAvailable)
                    {
                        outcomes.Add(SourceQueryOutcome.WithIssue(
                            order++,
                            new ModLoadIssue(
                                ModIssueSeverity.Warning,
                                "source_unavailable",
                                string.Empty,
                                sourceId,
                                $"Mod source is not available: {sourceId}")));
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    outcomes.Add(SourceQueryOutcome.WithIssue(
                        order++,
                        SourceIssue("source_availability_failed", sourceId, ex.Message)));
                    continue;
                }

                ModSourceQueryResult queryResult = null;
                try
                {
#pragma warning disable CS0618
                    source.QueryInstalledModFolders(result => queryResult = result);
#pragma warning restore CS0618
                }
                catch (Exception ex)
                {
                    outcomes.Add(SourceQueryOutcome.WithIssue(
                        order++,
                        SourceIssue("source_query_exception", sourceId, ex.Message)));
                    continue;
                }

                outcomes.Add(queryResult == null
                    ? SourceQueryOutcome.WithIssue(
                        order++,
                        SourceIssue(
                            "source_query_incomplete",
                            sourceId,
                            $"Mod source did not complete query: {sourceId}"))
                    : SourceQueryOutcome.WithResult(order++, queryResult));
            }

            return BuildReport(outcomes, importers, options, CancellationToken.None);
        }

        public static Task<ModLoadReport> LoadFromRegisteredSourcesAsync(
            IEnumerable<IModContentImporter> importers,
            ModLoadOptions options = null,
            CancellationToken cancellationToken = default)
        {
            return LoadFromSourcesAsync(
                ModSourceRegistry.RegisteredSources,
                importers,
                options,
                cancellationToken);
        }

        public static async Task<ModLoadReport> LoadFromSourcesAsync(
            IEnumerable<IModSource> sources,
            IEnumerable<IModContentImporter> importers,
            ModLoadOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveOptions = options ?? new ModLoadOptions();
            TimeSpan timeout = effectiveOptions.GetSourceQueryTimeout();
            var sourceList = (sources ?? Array.Empty<IModSource>())
                .Where(source => source != null)
                .ToArray();
            var queryTasks = sourceList
                .Select((source, order) => QuerySourceAsync(source, order, timeout, cancellationToken))
                .ToArray();

            SourceQueryOutcome[] outcomes = queryTasks.Length == 0
                ? Array.Empty<SourceQueryOutcome>()
                : await Task.WhenAll(queryTasks);

            return BuildReport(outcomes, importers, effectiveOptions, cancellationToken);
        }

        private static async Task<SourceQueryOutcome> QuerySourceAsync(
            IModSource source,
            int order,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            string sourceId = GetSourceId(source);
            try
            {
                if (!source.IsAvailable)
                {
                    return SourceQueryOutcome.WithIssue(
                        order,
                        new ModLoadIssue(
                            ModIssueSeverity.Warning,
                            "source_unavailable",
                            string.Empty,
                            sourceId,
                            $"Mod source is not available: {sourceId}"));
                }
            }
            catch (Exception ex)
            {
                return SourceQueryOutcome.WithIssue(
                    order,
                    SourceIssue("source_availability_failed", sourceId, ex.Message));
            }

            using var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<ModSourceQueryResult> queryTask;
            try
            {
                queryTask = source is IAsyncModSource asyncSource
                    ? asyncSource.QueryInstalledModFoldersAsync(queryCancellation.Token)
                    : QueryLegacySourceAsync(source, queryCancellation.Token);
                if (queryTask == null)
                {
                    return SourceQueryOutcome.WithIssue(
                        order,
                        SourceIssue("source_query_null_task", sourceId, "Mod source returned a null query task."));
                }
            }
            catch (OperationCanceledException)
            {
                return SourceQueryOutcome.WithIssue(
                    order,
                    SourceIssue("source_cancelled", sourceId, $"Mod source query was cancelled: {sourceId}"));
            }
            catch (Exception ex)
            {
                return SourceQueryOutcome.WithIssue(
                    order,
                    SourceIssue("source_query_exception", sourceId, ex.Message));
            }

            Task deadlineTask = Task.Delay(timeout);
            Task completed = await Task.WhenAny(queryTask, deadlineTask);
            if (completed != queryTask)
            {
                queryCancellation.Cancel();
                _ = queryTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                string reasonCode = cancellationToken.IsCancellationRequested
                    ? "source_cancelled"
                    : "source_timeout";
                string message = cancellationToken.IsCancellationRequested
                    ? $"Mod source query was cancelled: {sourceId}"
                    : $"Mod source query timed out after {timeout.TotalSeconds:0.###} seconds: {sourceId}";
                return SourceQueryOutcome.WithIssue(order, SourceIssue(reasonCode, sourceId, message));
            }

            try
            {
                ModSourceQueryResult result = await queryTask;
                return result == null
                    ? SourceQueryOutcome.WithIssue(
                        order,
                        SourceIssue("source_query_null_result", sourceId, "Mod source returned a null query result."))
                    : SourceQueryOutcome.WithResult(order, result);
            }
            catch (OperationCanceledException)
            {
                return SourceQueryOutcome.WithIssue(
                    order,
                    SourceIssue("source_cancelled", sourceId, $"Mod source query was cancelled: {sourceId}"));
            }
            catch (Exception ex)
            {
                return SourceQueryOutcome.WithIssue(
                    order,
                    SourceIssue("source_query_exception", sourceId, ex.Message));
            }
        }

        private static async Task<ModSourceQueryResult> QueryLegacySourceAsync(
            IModSource source,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<ModSourceQueryResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
            try
            {
#pragma warning disable CS0618
                source.QueryInstalledModFolders(result => completion.TrySetResult(result));
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }

            return await completion.Task;
        }

        private static ModLoadReport BuildReport(
            IEnumerable<SourceQueryOutcome> sourceOutcomes,
            IEnumerable<IModContentImporter> importers,
            ModLoadOptions options,
            CancellationToken cancellationToken)
        {
            var issues = new List<ModLoadIssue>();
            var manifests = new List<ModManifest>();
            var effectiveOptions = options ?? new ModLoadOptions();
            string manifestFileName = effectiveOptions.GetManifestFileName();

            foreach (var outcome in (sourceOutcomes ?? Array.Empty<SourceQueryOutcome>())
                         .OrderBy(item => item.Order))
            {
                if (outcome.Issue != null)
                {
                    issues.Add(outcome.Issue);
                    continue;
                }

                ModSourceQueryResult queryResult = outcome.Result;

                if (!queryResult.Succeeded)
                {
                    issues.Add(SourceIssue(
                        "source_query_failed",
                        queryResult.SourceId,
                        string.IsNullOrWhiteSpace(queryResult.Error)
                            ? "Mod source query failed."
                            : queryResult.Error));
                    continue;
                }

                foreach (string folder in (queryResult.ModFolders ?? Array.Empty<string>())
                             .Where(folder => !string.IsNullOrWhiteSpace(folder))
                             .Distinct(StringComparer.Ordinal)
                             .OrderBy(folder => folder, StringComparer.Ordinal))
                {
                    if (ModManifestReader.TryRead(folder, out var manifest, out var issue, manifestFileName))
                    {
                        manifest.SourceId = queryResult.SourceId ?? string.Empty;
                        if (manifest.Enabled)
                            manifests.Add(manifest);
                    }
                    else if (issue != null)
                    {
                        issues.Add(issue);
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                issues.Add(new ModLoadIssue(
                    ModIssueSeverity.Error,
                    "load_cancelled",
                    string.Empty,
                    string.Empty,
                    "Mod loading was cancelled before import."));
                return new ModLoadReport(Array.Empty<ModManifest>(), issues);
            }

            manifests = FilterDisabledMods(manifests, effectiveOptions.DisabledModIds, issues);

            var sortedManifests = SortByDependencies(manifests, issues);
            var loadableManifests = FilterConflicts(sortedManifests, issues);
            var loadedManifests = new List<ModManifest>();
            var failedImports = new HashSet<string>(StringComparer.Ordinal);

            foreach (var manifest in loadableManifests)
            {
                string failedDependency = (manifest.Dependencies ?? Array.Empty<string>())
                    .FirstOrDefault(failedImports.Contains);
                if (!string.IsNullOrEmpty(failedDependency))
                {
                    failedImports.Add(manifest.Id);
                    issues.Add(new ModLoadIssue(
                        ModIssueSeverity.Error,
                        "dependency_import_failed",
                        manifest.Id,
                        manifest.RootPath,
                        $"Dependency failed to import: {failedDependency}"));
                    continue;
                }

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
                else
                    failedImports.Add(manifest.Id);
            }

            return new ModLoadReport(loadedManifests, issues);
        }

        private static List<ModManifest> FilterDisabledMods(
            List<ModManifest> manifests,
            ISet<string> disabledModIds,
            List<ModLoadIssue> issues)
        {
            if (manifests == null || manifests.Count == 0 || disabledModIds == null || disabledModIds.Count == 0)
                return manifests ?? new List<ModManifest>();

            var excludedIds = new HashSet<string>(disabledModIds, StringComparer.Ordinal);
            foreach (ModManifest manifest in manifests.Where(item => excludedIds.Contains(item.Id)))
            {
                issues.Add(new ModLoadIssue(
                    ModIssueSeverity.Warning,
                    "mod_disabled",
                    manifest.Id,
                    manifest.RootPath,
                    "Mod is disabled and will take effect only after a restart."));
            }

            bool changed;
            do
            {
                changed = false;
                foreach (ModManifest manifest in manifests)
                {
                    if (excludedIds.Contains(manifest.Id))
                        continue;
                    string disabledDependency = (manifest.Dependencies ?? Array.Empty<string>())
                        .FirstOrDefault(excludedIds.Contains);
                    if (string.IsNullOrEmpty(disabledDependency))
                        continue;

                    excludedIds.Add(manifest.Id);
                    issues.Add(new ModLoadIssue(
                        ModIssueSeverity.Warning,
                        "dependency_disabled",
                        manifest.Id,
                        manifest.RootPath,
                        $"Dependency is disabled: {disabledDependency}"));
                    changed = true;
                }
            } while (changed);

            return manifests.Where(manifest => !excludedIds.Contains(manifest.Id)).ToList();
        }

        private static ModLoadIssue SourceIssue(string reasonCode, string sourceId, string message)
        {
            return new ModLoadIssue(
                ModIssueSeverity.Error,
                reasonCode,
                string.Empty,
                sourceId,
                message);
        }

        private static string GetSourceId(IModSource source)
        {
            try
            {
                return string.IsNullOrWhiteSpace(source?.SourceId)
                    ? source?.GetType().FullName ?? string.Empty
                    : source.SourceId;
            }
            catch
            {
                return source?.GetType().FullName ?? string.Empty;
            }
        }

        private sealed class SourceQueryOutcome
        {
            private SourceQueryOutcome(int order, ModSourceQueryResult result, ModLoadIssue issue)
            {
                Order = order;
                Result = result;
                Issue = issue;
            }

            public int Order { get; }
            public ModSourceQueryResult Result { get; }
            public ModLoadIssue Issue { get; }

            public static SourceQueryOutcome WithResult(int order, ModSourceQueryResult result)
            {
                return new SourceQueryOutcome(order, result, null);
            }

            public static SourceQueryOutcome WithIssue(int order, ModLoadIssue issue)
            {
                return new SourceQueryOutcome(order, null, issue);
            }
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
