using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public enum ConfigPipelineMode
    {
        Plan,
        Check,
        Apply,
        Compile,
        ExportCandidate,
        RefreshCandidate,
        UpgradeCandidate
    }

    public sealed class ConfigPipelineCommandResult
    {
        internal ConfigPipelineCommandResult(
            ConfigPipelineMode mode,
            string configSetId,
            bool success,
            bool current,
            string planId,
            string summary,
            byte[] machineJson)
        {
            Mode = mode;
            ConfigSetId = configSetId;
            Success = success;
            Current = current;
            PlanId = planId;
            Summary = summary;
            MachineJson = machineJson;
        }

        public ConfigPipelineMode Mode { get; }
        public string ConfigSetId { get; }
        public bool Success { get; }
        public bool Current { get; }
        public string PlanId { get; }
        public string Summary { get; }
        public byte[] MachineJson { get; }
    }

    public static class ConfigPipelineBatch
    {
        public static ConfigPipelineCommandResult Run(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity,
            ConfigPipelineMode mode,
            string candidateOutputDirectory = null,
            string targetScope = null,
            string nextProfileRelativePath = null)
        {
            var service = new ConfigPipelineService();
            ConfigPipelinePreparedPlan prepared = null;
            bool current;
            string detail;
            string planId = null;
            switch (mode)
            {
                case ConfigPipelineMode.Plan:
                    prepared = service.Plan(
                        projectRoot, profileRelativePath, configSetId, packageIdentity);
                    current = prepared.Plan.IsCurrent;
                    planId = prepared.Plan.PlanId;
                    detail = planId + ":" + prepared.ValueDiffs.Count + " field changes";
                    break;
                case ConfigPipelineMode.Check:
                    current = service.Check(projectRoot, profileRelativePath, configSetId, packageIdentity);
                    detail = current ? "current" : "stale";
                    break;
                case ConfigPipelineMode.Apply:
                case ConfigPipelineMode.Compile:
                    ConfigApplyResult applied = service.Apply(
                        projectRoot, profileRelativePath, configSetId, packageIdentity);
                    current = true;
                    planId = applied.PlanId;
                    detail = planId + ":" + applied.ChangedFileCount;
                    break;
                case ConfigPipelineMode.ExportCandidate:
                    if (string.IsNullOrWhiteSpace(candidateOutputDirectory) || string.IsNullOrWhiteSpace(targetScope))
                    {
                        throw new ArgumentException("Candidate output directory and target scope are required.");
                    }

                    ConfigImportConflictResult candidate = service.ExportJsonCandidate(
                        projectRoot,
                        profileRelativePath,
                        configSetId,
                        targetScope,
                        candidateOutputDirectory);
                    current = false;
                    detail = candidate.DiagnosticCode;
                    break;
                case ConfigPipelineMode.RefreshCandidate:
                    if (string.IsNullOrWhiteSpace(candidateOutputDirectory))
                    {
                        throw new ArgumentException("Candidate output directory is required.");
                    }

                    ConfigWorkbookRefreshCandidateResult refresh =
                        service.ExportWorkbookRefreshCandidate(
                            projectRoot,
                            profileRelativePath,
                            configSetId,
                            candidateOutputDirectory);
                    current = false;
                    detail = refresh.SourceHash + ":" + refresh.CandidateFileCount;
                    break;
                case ConfigPipelineMode.UpgradeCandidate:
                    if (string.IsNullOrWhiteSpace(candidateOutputDirectory) ||
                        string.IsNullOrWhiteSpace(nextProfileRelativePath))
                    {
                        throw new ArgumentException(
                            "Candidate output directory and next profile are required.");
                    }

                    ConfigSchemaUpgradeCandidateResult upgrade =
                        service.ExportSchemaUpgradeCandidate(
                            projectRoot,
                            profileRelativePath,
                            nextProfileRelativePath,
                            configSetId,
                            packageIdentity,
                            candidateOutputDirectory);
                    current = false;
                    detail = upgrade.SourceVersion + "->" + upgrade.TargetVersion +
                             ":" + upgrade.CandidateFileCount;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return CreateResult(mode, configSetId, current, detail, planId, prepared);
        }

        public static ConfigPipelineCommandResult ApplyExpectedPlan(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity,
            string expectedPlanId)
        {
            ConfigApplyResult applied = new ConfigPipelineService().ApplyExpectedPlan(
                projectRoot,
                profileRelativePath,
                configSetId,
                packageIdentity,
                expectedPlanId);
            return CreateResult(
                ConfigPipelineMode.Apply,
                configSetId,
                true,
                applied.PlanId + ":" + applied.ChangedFileCount,
                applied.PlanId,
                null);
        }

        private static ConfigPipelineCommandResult CreateResult(
            ConfigPipelineMode mode,
            string configSetId,
            bool current,
            string detail,
            string planId,
            ConfigPipelinePreparedPlan prepared)
        {
            bool success = mode != ConfigPipelineMode.Check || current;
            string summary = mode + " " + configSetId + ": " + detail;
            IEnumerable<ConfigNode> allowedWrites = prepared == null
                ? Array.Empty<ConfigNode>()
                : prepared.Plan.Entries
                    .Where(value => value.Action != ConfigPlanAction.Unchanged)
                    .Select(value => (ConfigNode)new ConfigObjectNode(new[]
                    {
                        new ConfigProperty("path", new ConfigStringNode(value.RelativePath)),
                        new ConfigProperty("action", new ConfigStringNode(value.Action.ToString().ToLowerInvariant()))
                    }));
            IEnumerable<ConfigNode> valueDiffs = prepared == null
                ? Array.Empty<ConfigNode>()
                : prepared.ValueDiffs.Select(value => (ConfigNode)new ConfigObjectNode(new[]
                {
                    new ConfigProperty("artifact", new ConfigStringNode(value.ArtifactPath)),
                    new ConfigProperty("fieldPath", new ConfigStringNode(value.FieldPath)),
                    new ConfigProperty("kind", new ConfigStringNode(value.Kind.ToString().ToLowerInvariant()))
                }));
            byte[] json = CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(new[]
            {
                new ConfigProperty("formatVersion", new ConfigIntegerNode(1)),
                new ConfigProperty("mode", new ConfigStringNode(mode.ToString().ToLowerInvariant())),
                new ConfigProperty("configSetId", new ConfigStringNode(configSetId)),
                new ConfigProperty("success", new ConfigBooleanNode(success)),
                new ConfigProperty("current", new ConfigBooleanNode(current)),
                new ConfigProperty("planId", new ConfigStringNode(planId ?? string.Empty)),
                new ConfigProperty("detail", new ConfigStringNode(detail)),
                new ConfigProperty("allowedWrites", new ConfigArrayNode(allowedWrites)),
                new ConfigProperty("valueDiffs", new ConfigArrayNode(valueDiffs))
            }));
            return new ConfigPipelineCommandResult(
                mode,
                configSetId,
                success,
                current,
                planId,
                summary,
                json);
        }

        public static void Execute()
        {
            Dictionary<string, string> arguments = ParseArguments(Environment.GetCommandLineArgs());
            string projectRoot = Required(arguments, "--config-project-root");
            string resultOutput = ResolveResultOutput(
                projectRoot,
                Required(arguments, "--config-result-output"));
            bool resultWritten = false;
            try
            {
                var mode = (ConfigPipelineMode)Enum.Parse(
                    typeof(ConfigPipelineMode),
                    Required(arguments, "--config-mode"),
                    true);
                string packageIdentity = RequiresPackageIdentity(mode)
                    ? Required(arguments, "--config-package-identity")
                    : Optional(arguments, "--config-package-identity");
                ConfigPipelineCommandResult result = Run(
                    projectRoot,
                    Required(arguments, "--config-profile"),
                    Required(arguments, "--config-set"),
                    packageIdentity,
                    mode,
                    Optional(arguments, "--config-candidate-output"),
                    Optional(arguments, "--config-target-scope"),
                    Optional(arguments, "--config-next-profile"));
                WriteMachineResult(resultOutput, result.MachineJson);
                resultWritten = true;
                Debug.Log(result.Summary);
                Debug.Log(System.Text.Encoding.UTF8.GetString(result.MachineJson));
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Summary);
                }
            }
            catch (Exception exception)
            {
                if (!resultWritten)
                {
                    byte[] failure = CreateFailureMachineJson(exception);
                    WriteMachineResult(resultOutput, failure);
                    Debug.LogError(System.Text.Encoding.UTF8.GetString(failure));
                }

                throw;
            }
        }

        public static bool RequiresPackageIdentity(ConfigPipelineMode mode)
        {
            return mode != ConfigPipelineMode.ExportCandidate &&
                   mode != ConfigPipelineMode.RefreshCandidate;
        }

        internal static byte[] CreateFailureMachineJson(Exception exception)
        {
            return CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(new[]
            {
                new ConfigProperty("formatVersion", new ConfigIntegerNode(1)),
                new ConfigProperty("success", new ConfigBooleanNode(false)),
                new ConfigProperty("current", new ConfigBooleanNode(false)),
                new ConfigProperty("errorType", new ConfigStringNode(exception.GetType().FullName)),
                new ConfigProperty("error", new ConfigStringNode(exception.Message))
            }));
        }

        internal static void WriteMachineResult(string path, byte[] content)
        {
            string absolute = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(absolute);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Machine result output requires a parent directory.");
            }

            Directory.CreateDirectory(directory);
            string temporary = absolute + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(true);
                }

                if (File.Exists(absolute))
                {
                    File.Replace(temporary, absolute, null);
                }
                else
                {
                    File.Move(temporary, absolute);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static string ResolveResultOutput(string projectRoot, string path)
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static Dictionary<string, string> ParseArguments(IReadOnlyList<string> args)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < args.Count - 1; index++)
            {
                if (args[index].StartsWith("--config-", StringComparison.Ordinal))
                {
                    result[args[index]] = args[++index];
                }
            }

            return result;
        }

        private static string Required(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing command argument " + key + ".");
            }

            return value;
        }

        private static string Optional(IReadOnlyDictionary<string, string> values, string key)
        {
            values.TryGetValue(key, out string value);
            return value;
        }
    }
}
