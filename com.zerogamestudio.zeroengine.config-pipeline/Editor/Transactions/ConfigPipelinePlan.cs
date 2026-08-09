using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public enum ConfigPlanAction
    {
        Unchanged,
        Create,
        Update,
        Delete
    }

    public sealed class ConfigPlanEntry
    {
        internal ConfigPlanEntry(
            string relativePath,
            ConfigPlanAction action,
            string existingHash,
            string plannedHash)
        {
            RelativePath = relativePath;
            Action = action;
            ExistingHash = existingHash;
            PlannedHash = plannedHash;
        }

        public string RelativePath { get; }

        public ConfigPlanAction Action { get; }

        public string ExistingHash { get; }

        public string PlannedHash { get; }
    }

    public sealed class ConfigPipelinePlan
    {
        private readonly ReadOnlyDictionary<string, string> inputHashes;
        private readonly ReadOnlyCollection<ConfigPlanEntry> entries;

        internal ConfigPipelinePlan(
            string planId,
            string configSetId,
            string packageIdentity,
            IDictionary<string, string> inputHashes,
            IEnumerable<ConfigPlanEntry> entries)
        {
            PlanId = planId;
            ConfigSetId = configSetId;
            PackageIdentity = packageIdentity;
            this.inputHashes = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(inputHashes, StringComparer.Ordinal));
            this.entries = new List<ConfigPlanEntry>(entries).AsReadOnly();
        }

        public string PlanId { get; }

        public string ConfigSetId { get; }

        public string PackageIdentity { get; }

        public IReadOnlyDictionary<string, string> InputHashes => inputHashes;

        public IReadOnlyList<ConfigPlanEntry> Entries => entries;

        public bool IsCurrent => entries.All(entry => entry.Action == ConfigPlanAction.Unchanged);

        public byte[] ToJson()
        {
            var inputs = new List<ConfigProperty>();
            foreach (KeyValuePair<string, string> input in inputHashes.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                inputs.Add(new ConfigProperty(input.Key, new ConfigStringNode(input.Value)));
            }

            var plannedEntries = new List<ConfigNode>();
            foreach (ConfigPlanEntry entry in entries)
            {
                plannedEntries.Add(new ConfigObjectNode(new[]
                {
                    new ConfigProperty("path", new ConfigStringNode(entry.RelativePath)),
                    new ConfigProperty(
                        "action",
                        new ConfigStringNode(entry.Action.ToString().ToLowerInvariant())),
                    new ConfigProperty(
                        "existingHash",
                        new ConfigStringNode(entry.ExistingHash ?? string.Empty)),
                    new ConfigProperty(
                        "plannedHash",
                        new ConfigStringNode(entry.PlannedHash ?? string.Empty))
                }));
            }

            return CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(new[]
            {
                new ConfigProperty("formatVersion", new ConfigIntegerNode(1)),
                new ConfigProperty("planId", new ConfigStringNode(PlanId)),
                new ConfigProperty("configSetId", new ConfigStringNode(ConfigSetId)),
                new ConfigProperty("packageIdentity", new ConfigStringNode(PackageIdentity)),
                new ConfigProperty("inputs", new ConfigObjectNode(inputs)),
                new ConfigProperty("entries", new ConfigArrayNode(plannedEntries))
            }));
        }
    }
}
