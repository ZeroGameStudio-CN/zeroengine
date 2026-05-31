using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZeroEngine.World.Authoring
{
    public readonly struct AreaAuthoringYamlComponentBlock
    {
        public AreaAuthoringYamlComponentBlock(string source)
        {
            Source = source ?? string.Empty;
        }

        public string Source { get; }

        public bool Contains(string text)
        {
            return !string.IsNullOrEmpty(text) && Source.Contains(text);
        }

        public string GetScalar(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }

            var match = Regex.Match(Source, Regex.Escape(fieldName) + @":\s*(?<value>[^\r\n]+)");
            if (!match.Success)
            {
                return string.Empty;
            }

            var value = match.Groups["value"].Value.Trim();
            return value == "\"\"" ? string.Empty : value.Trim('"');
        }
    }

    public static class AreaAuthoringYamlScanner
    {
        public static IReadOnlyList<AreaAuthoringYamlComponentBlock> ExtractComponentBlocks(string yaml, string scriptGuid)
        {
            if (string.IsNullOrEmpty(yaml) || string.IsNullOrWhiteSpace(scriptGuid) || !yaml.Contains(scriptGuid))
            {
                return Array.Empty<AreaAuthoringYamlComponentBlock>();
            }

            return yaml.Split(new[] { "--- !u!" }, StringSplitOptions.None)
                .Where(block => block.Contains(scriptGuid))
                .Select(block => new AreaAuthoringYamlComponentBlock(block))
                .ToArray();
        }

        public static IReadOnlyList<AreaAuthoringIssue> ValidateForbiddenScriptGuids(
            string assetPath,
            string yaml,
            IEnumerable<string> forbiddenScriptGuids,
            bool directComponentsAreErrors,
            string code = "DIRECT_RUNTIME_COMPONENT_IN_AREA_SCENE")
        {
            var issues = new List<AreaAuthoringIssue>();
            if (string.IsNullOrEmpty(yaml))
            {
                issues.Add(Error("AREA_SCENE_EMPTY", "Area scene YAML is empty.", assetPath));
                return issues;
            }

            if (forbiddenScriptGuids == null)
            {
                return issues;
            }

            var severity = directComponentsAreErrors
                ? AreaAuthoringIssueSeverity.Error
                : AreaAuthoringIssueSeverity.Warning;

            foreach (var guid in forbiddenScriptGuids.Where(guid => !string.IsNullOrWhiteSpace(guid)))
            {
                if (!yaml.Contains(guid))
                {
                    continue;
                }

                issues.Add(new AreaAuthoringIssue(
                    severity,
                    code,
                    $"Area scene serializes forbidden runtime script guid {guid}.",
                    assetPath,
                    guid));
            }

            return issues;
        }

        public static IReadOnlyList<AreaAuthoringIssue> ValidateForbiddenScriptGuid(
            string assetPath,
            string yaml,
            string scriptGuid,
            string code,
            string message)
        {
            if (string.IsNullOrEmpty(yaml) || string.IsNullOrWhiteSpace(scriptGuid) || !yaml.Contains(scriptGuid))
            {
                return Array.Empty<AreaAuthoringIssue>();
            }

            return new[]
            {
                Error(code, message, assetPath, scriptGuid)
            };
        }

        private static AreaAuthoringIssue Error(string code, string message, string assetPath = null, string contextId = null)
        {
            return new AreaAuthoringIssue(AreaAuthoringIssueSeverity.Error, code, message, assetPath, contextId);
        }
    }
}
