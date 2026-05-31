using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.World.Authoring
{
    public static class AreaAuthoringComponentValidator
    {
        public static IReadOnlyCollection<string> ExtractStableIds(
            string yaml,
            string scriptGuid,
            string stableIdFieldName)
        {
            var ids = new HashSet<string>();
            foreach (var block in AreaAuthoringYamlScanner.ExtractComponentBlocks(yaml, scriptGuid))
            {
                var stableId = block.GetScalar(stableIdFieldName);
                if (!string.IsNullOrWhiteSpace(stableId))
                {
                    ids.Add(stableId);
                }
            }

            return ids;
        }

        public static IReadOnlyList<AreaAuthoringIssue> ValidateStableIds(
            string assetPath,
            string yaml,
            string scriptGuid,
            string stableIdFieldName,
            bool stableIdsAreRequired,
            string emptyCode,
            string duplicateCode,
            string displayName)
        {
            var issues = new List<AreaAuthoringIssue>();
            var stableIds = new HashSet<string>();
            var blocks = AreaAuthoringYamlScanner.ExtractComponentBlocks(yaml, scriptGuid);

            foreach (var block in blocks)
            {
                var stableId = block.GetScalar(stableIdFieldName);
                if (stableIdsAreRequired && string.IsNullOrWhiteSpace(stableId))
                {
                    issues.Add(Error(emptyCode, $"{displayName} must have a stable id.", assetPath));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(stableId) && !stableIds.Add(stableId))
                {
                    issues.Add(Error(duplicateCode, $"Duplicate stable id '{stableId}' found.", assetPath, stableId));
                }
            }

            return issues;
        }

        public static IReadOnlyList<AreaAuthoringIssue> ValidateRequiredReferences(
            string assetPath,
            string yaml,
            string scriptGuid,
            string referenceFieldName,
            string contextFieldName,
            string missingCode,
            string message)
        {
            var issues = new List<AreaAuthoringIssue>();
            foreach (var block in AreaAuthoringYamlScanner.ExtractComponentBlocks(yaml, scriptGuid))
            {
                if (HasSerializedReference(block, referenceFieldName))
                {
                    continue;
                }

                issues.Add(Error(missingCode, message, assetPath, block.GetScalar(contextFieldName)));
            }

            return issues;
        }

        private static bool HasSerializedReference(AreaAuthoringYamlComponentBlock block, string referenceFieldName)
        {
            var value = block.GetScalar(referenceFieldName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!value.Contains("fileID:", StringComparison.Ordinal))
            {
                return false;
            }

            return !value.Contains("fileID: 0", StringComparison.Ordinal);
        }

        private static AreaAuthoringIssue Error(string code, string message, string assetPath = null, string contextId = null)
        {
            return new AreaAuthoringIssue(AreaAuthoringIssueSeverity.Error, code, message, assetPath, contextId);
        }
    }
}
