using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.ModSystem
{
    public static class ModManagementProjection
    {
        public static IReadOnlyList<ModManagementItem> Build(
            ModLoadReport report,
            IReadOnlyCollection<string> disabledModIds)
        {
            var disabledIds = new HashSet<string>(
                disabledModIds ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var itemsById = new Dictionary<string, ModManagementItem>(StringComparer.Ordinal);

            foreach (ModManifest manifest in report?.LoadedManifests ?? Array.Empty<ModManifest>())
            {
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
                    continue;

                bool enabled = !disabledIds.Contains(manifest.Id);
                itemsById[manifest.Id] = new ModManagementItem(
                    manifest.Id,
                    string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name,
                    manifest.Author,
                    manifest.Version,
                    manifest.SourceId,
                    enabled ? ModManagementStatus.Loaded : ModManagementStatus.RestartRequired,
                    enabled ? string.Empty : "restart_required",
                    enabled);
            }

            IEnumerable<IGrouping<string, ModLoadIssue>> issueGroups =
                (report?.Issues ?? Array.Empty<ModLoadIssue>())
                .Where(issue => issue != null && !string.IsNullOrWhiteSpace(issue.ModId))
                .GroupBy(issue => issue.ModId, StringComparer.Ordinal);
            foreach (IGrouping<string, ModLoadIssue> issueGroup in issueGroups)
            {
                if (itemsById.ContainsKey(issueGroup.Key))
                    continue;

                ModLoadIssue issue = issueGroup
                    .OrderByDescending(candidate => candidate.Severity == ModIssueSeverity.Error)
                    .ThenBy(candidate => candidate.ReasonCode, StringComparer.Ordinal)
                    .First();
                bool enabled = !disabledIds.Contains(issue.ModId);
                itemsById.Add(issue.ModId, new ModManagementItem(
                    issue.ModId,
                    issue.ModId,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    ResolveStatus(issue, enabled, disabledIds),
                    issue.ReasonCode,
                    enabled));
            }

            return itemsById.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
        }

        private static ModManagementStatus ResolveStatus(
            ModLoadIssue issue,
            bool enabled,
            ISet<string> disabledIds)
        {
            if (string.Equals(issue.ReasonCode, "mod_disabled", StringComparison.Ordinal))
                return enabled ? ModManagementStatus.RestartRequired : ModManagementStatus.Disabled;
            if (!enabled)
                return ModManagementStatus.RestartRequired;
            if (string.Equals(issue.ReasonCode, "dependency_disabled", StringComparison.Ordinal))
            {
                string dependencyId = GetMessageSuffix(issue.Message);
                return disabledIds.Contains(dependencyId)
                    ? ModManagementStatus.Disabled
                    : ModManagementStatus.RestartRequired;
            }
            if (string.Equals(issue.ReasonCode, "restart_required", StringComparison.Ordinal))
                return ModManagementStatus.RestartRequired;
            return ModManagementStatus.Failed;
        }

        private static string GetMessageSuffix(string message)
        {
            int separator = message?.LastIndexOf(':') ?? -1;
            return separator < 0 ? string.Empty : message.Substring(separator + 1).Trim();
        }
    }
}
