using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public enum ConfigValueDiffKind
    {
        Added,
        Removed,
        Changed
    }

    public sealed class ConfigValueDiff
    {
        internal ConfigValueDiff(string artifactPath, string fieldPath, ConfigValueDiffKind kind)
        {
            ArtifactPath = artifactPath;
            FieldPath = fieldPath;
            Kind = kind;
        }

        public string ArtifactPath { get; }
        public string FieldPath { get; }
        public ConfigValueDiffKind Kind { get; }
    }

    public static class ConfigDocumentDiff
    {
        public static IReadOnlyList<ConfigValueDiff> Compare(
            string artifactPath,
            ConfigNode before,
            ConfigNode after)
        {
            var result = new List<ConfigValueDiff>();
            CompareNode(artifactPath, "$", before, after, result);
            return result;
        }

        private static void CompareNode(
            string artifactPath,
            string path,
            ConfigNode before,
            ConfigNode after,
            List<ConfigValueDiff> result)
        {
            if (before == null || after == null)
            {
                result.Add(new ConfigValueDiff(
                    artifactPath,
                    path,
                    before == null ? ConfigValueDiffKind.Added : ConfigValueDiffKind.Removed));
                return;
            }

            if (before.Kind != after.Kind)
            {
                result.Add(new ConfigValueDiff(artifactPath, path, ConfigValueDiffKind.Changed));
                return;
            }

            if (before is ConfigObjectNode beforeObject && after is ConfigObjectNode afterObject)
            {
                var names = beforeObject.Properties.Select(value => value.Name)
                    .Concat(afterObject.Properties.Select(value => value.Name))
                    .Distinct(StringComparer.Ordinal);
                foreach (string name in names)
                {
                    beforeObject.TryGetValue(name, out ConfigNode beforeValue);
                    afterObject.TryGetValue(name, out ConfigNode afterValue);
                    CompareNode(artifactPath, path + "/" + Escape(name), beforeValue, afterValue, result);
                }

                return;
            }

            if (before is ConfigArrayNode beforeArray && after is ConfigArrayNode afterArray)
            {
                if (TryIndexById(beforeArray, out Dictionary<string, ConfigNode> beforeById) &&
                    TryIndexById(afterArray, out Dictionary<string, ConfigNode> afterById))
                {
                    foreach (string id in beforeById.Keys.Concat(afterById.Keys).Distinct(StringComparer.Ordinal))
                    {
                        beforeById.TryGetValue(id, out ConfigNode beforeValue);
                        afterById.TryGetValue(id, out ConfigNode afterValue);
                        CompareNode(artifactPath, path + "[id=" + id + "]", beforeValue, afterValue, result);
                    }
                }
                else
                {
                    int count = Math.Max(beforeArray.Items.Count, afterArray.Items.Count);
                    for (int index = 0; index < count; index++)
                    {
                        CompareNode(
                            artifactPath,
                            path + "/" + index,
                            index < beforeArray.Items.Count ? beforeArray.Items[index] : null,
                            index < afterArray.Items.Count ? afterArray.Items[index] : null,
                            result);
                    }
                }

                return;
            }

            if (!string.Equals(
                    CanonicalJsonWriter.WriteText(before),
                    CanonicalJsonWriter.WriteText(after),
                    StringComparison.Ordinal))
            {
                result.Add(new ConfigValueDiff(artifactPath, path, ConfigValueDiffKind.Changed));
            }
        }

        private static bool TryIndexById(
            ConfigArrayNode array,
            out Dictionary<string, ConfigNode> values)
        {
            values = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
            foreach (ConfigNode node in array.Items)
            {
                if (!(node is ConfigObjectNode item) ||
                    !item.TryGetValue("id", out ConfigNode idNode) ||
                    !(idNode is ConfigStringNode id) ||
                    values.ContainsKey(id.Value))
                {
                    values = null;
                    return false;
                }

                values.Add(id.Value, node);
            }

            return true;
        }

        private static string Escape(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }
    }
}
