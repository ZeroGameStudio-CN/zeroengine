using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using ZeroEngine.World.Authoring;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldAddressablesBindingValidator
    {
        public static IReadOnlyList<AreaAuthoringIssue> Validate(WorldGraphGraduationProfile profile)
        {
            if (profile == null)
            {
                return Array.Empty<AreaAuthoringIssue>();
            }

            var issues = new List<AreaAuthoringIssue>();
            var groupTexts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in profile.AddressablesGroups)
            {
                ValidateGroup(group, groupTexts, issues);
            }

            foreach (var binding in profile.AddressableAssets)
            {
                ValidateAssetBinding(binding, groupTexts, issues);
            }

            ValidateUniqueAddresses(profile.AddressableAssets, groupTexts, issues);
            return issues;
        }

        private static void ValidateGroup(
            WorldAddressablesGroupContract group,
            IDictionary<string, string> groupTexts,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(group.GroupAssetPath) || !File.Exists(group.GroupAssetPath))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_ADDRESSABLES_GROUP_MISSING",
                    $"Addressables group is missing: {group.GroupName}.",
                    group.GroupAssetPath,
                    group.GroupName));
                return;
            }

            var groupText = File.ReadAllText(group.GroupAssetPath);
            groupTexts[group.GroupAssetPath] = groupText;

            foreach (var schemaPath in group.RequiredSchemaAssetPaths)
            {
                if (string.IsNullOrWhiteSpace(schemaPath) || !File.Exists(schemaPath))
                {
                    issues.Add(WorldGraphGraduationRunner.Error(
                        "WORLD_ADDRESSABLES_GROUP_SCHEMA_MISSING",
                        $"Addressables group {group.GroupName} is missing required schema asset {schemaPath}.",
                        group.GroupAssetPath,
                        group.GroupName));
                    continue;
                }

                var schemaGuid = ReadMetaGuid(schemaPath + ".meta");
                if (string.IsNullOrWhiteSpace(schemaGuid) || !groupText.Contains("guid: " + schemaGuid))
                {
                    issues.Add(WorldGraphGraduationRunner.Error(
                        "WORLD_ADDRESSABLES_GROUP_SCHEMA_UNBOUND",
                        $"Addressables group {group.GroupName} must reference schema asset {schemaPath}.",
                        group.GroupAssetPath,
                        schemaPath));
                }
            }
        }

        private static void ValidateAssetBinding(
            WorldAddressableAssetContract binding,
            IReadOnlyDictionary<string, string> groupTexts,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(binding.AssetPath) || string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(binding.AssetPath)))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_ADDRESSABLES_ASSET_GUID_MISSING",
                    $"Addressable asset guid is missing for {binding.Address}: {binding.AssetPath}.",
                    binding.AssetPath,
                    binding.Address));
                return;
            }

            var assetType = AssetDatabase.GetMainAssetTypeAtPath(binding.AssetPath);
            if (binding.ExpectedAssetType != null
                && (assetType == null || !binding.ExpectedAssetType.IsAssignableFrom(assetType)))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_ADDRESSABLES_ASSET_TYPE_INVALID",
                    $"Addressable asset {binding.Address} must reference {binding.ExpectedAssetType.Name}, but found {assetType?.Name ?? "missing"}.",
                    binding.AssetPath,
                    binding.Address));
                return;
            }

            if (string.IsNullOrWhiteSpace(binding.GroupAssetPath)
                || !groupTexts.TryGetValue(binding.GroupAssetPath, out var groupText))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_ADDRESSABLES_BINDING_GROUP_UNKNOWN",
                    $"Addressables binding group is not available for {binding.Address}: {binding.GroupName}.",
                    binding.GroupAssetPath,
                    binding.Address));
                return;
            }

            var assetGuid = AssetDatabase.AssetPathToGUID(binding.AssetPath);
            if (!groupText.Contains(assetGuid))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_ADDRESSABLES_ENTRY_MISSING",
                    $"Addressables group {binding.GroupName} must contain asset {binding.AssetPath}.",
                    binding.GroupAssetPath,
                    binding.Address));
            }

            if (!ContainsAddress(groupText, binding.Address))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_ADDRESSABLES_ENTRY_ADDRESS_MISMATCH",
                    $"Addressables group {binding.GroupName} must bind {binding.AssetPath} to address {binding.Address}.",
                    binding.GroupAssetPath,
                    binding.Address));
            }
        }

        private static void ValidateUniqueAddresses(
            IEnumerable<WorldAddressableAssetContract> bindings,
            IReadOnlyDictionary<string, string> groupTexts,
            ICollection<AreaAuthoringIssue> issues)
        {
            foreach (var binding in bindings.Where(binding => !string.IsNullOrWhiteSpace(binding.Address)))
            {
                var count = groupTexts.Values.Sum(groupText => CountAddress(groupText, binding.Address));
                if (count == 1)
                {
                    continue;
                }

                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_ADDRESSABLES_ADDRESS_NOT_UNIQUE",
                    $"Addressables address {binding.Address} must be unique across validated groups; found {count} entries.",
                    binding.GroupAssetPath,
                    binding.Address));
            }
        }

        private static bool ContainsAddress(string groupText, string address)
        {
            return CountAddress(groupText, address) > 0;
        }

        private static int CountAddress(string groupText, string address)
        {
            if (string.IsNullOrWhiteSpace(groupText) || string.IsNullOrWhiteSpace(address))
            {
                return 0;
            }

            var count = 0;
            var marker = "m_Address: " + address;
            var index = groupText.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = groupText.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
            }

            return count;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            if (string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath))
            {
                return string.Empty;
            }

            foreach (var line in File.ReadLines(metaPath))
            {
                const string prefix = "guid:";
                if (!line.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                return line.Substring(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length).Trim();
            }

            return string.Empty;
        }
    }
}
