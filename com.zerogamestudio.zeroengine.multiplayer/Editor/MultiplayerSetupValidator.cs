using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Multiplayer.Editor
{
    public enum MultiplayerSetupIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class MultiplayerSetupIssue
    {
        public MultiplayerSetupIssue(
            MultiplayerSetupIssueSeverity severity,
            string code,
            string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public MultiplayerSetupIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
    }

    public static class MultiplayerSetupValidator
    {
        private const string SteamOwnerType = "ZeroEngine.Multiplayer.Steam.SteamRuntimeOwner";
        private const string FishNetManagerType = "FishNet.Managing.NetworkManager";
        private const string FishNetDriverType = "ZeroEngine.Multiplayer.FishNet.FishNetConnectionDriver";
        private const string FishySteamworksType = "FishySteamworks.FishySteamworks";
        private const string TugboatType = "FishNet.Transporting.Tugboat.Tugboat";

        public static IReadOnlyList<MultiplayerSetupIssue> Validate(
            MultiplayerSessionConfig config,
            TransportMode transportMode,
            bool scanSteamLifecycleSources = true)
        {
            List<MultiplayerSetupIssue> issues = new List<MultiplayerSetupIssue>();
            ValidateConfig(config, issues);

            MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            Dictionary<string, int> typeCounts = CountLoadedSceneTypes(behaviours);
            RequireExactlyOne(typeCounts, FishNetManagerType, "FishNet NetworkManager", issues);
            RequireExactlyOne(typeCounts, FishNetDriverType, "FishNetConnectionDriver", issues);

            if (transportMode == TransportMode.SteamP2P)
            {
                RequireExactlyOne(typeCounts, SteamOwnerType, "SteamRuntimeOwner", issues);
                RequireAtLeastOne(typeCounts, FishySteamworksType, "FishySteamworks transport", issues);

                int legacySteamManagers = GetCount(typeCounts, "SteamManager");
                if (legacySteamManagers > 0)
                {
                    issues.Add(new MultiplayerSetupIssue(
                        MultiplayerSetupIssueSeverity.Error,
                        "multiplayer.setup.legacy_steam_manager_present",
                        "A legacy SteamManager is loaded beside SteamRuntimeOwner. Remove or explicitly adapt it before enabling Steam multiplayer."));
                }

                if (scanSteamLifecycleSources)
                {
                    ValidateSteamLifecycleSources(issues);
                }
            }
            else
            {
                RequireAtLeastOne(typeCounts, TugboatType, "Tugboat local transport", issues);
            }

            ValidateActiveSceneInBuildSettings(issues);
            return issues.AsReadOnly();
        }

        public static IReadOnlyList<MultiplayerSetupIssue> ValidateConfig(
            MultiplayerSessionConfig config)
        {
            List<MultiplayerSetupIssue> issues = new List<MultiplayerSetupIssue>();
            ValidateConfig(config, issues);
            return issues.AsReadOnly();
        }

        [MenuItem("Window/ZeroEngine/Multiplayer/Validate Setup")]
        private static void ValidateFromMenu()
        {
            MultiplayerSessionConfig config = FindSingleConfig();
            TransportMode mode = config == null ? TransportMode.LocalDirect : config.DefaultTransport;
            IReadOnlyList<MultiplayerSetupIssue> issues = Validate(config, mode);
            if (issues.Count == 0)
            {
                Debug.Log("[ZeroEngine.Multiplayer] Setup validation passed.");
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                MultiplayerSetupIssue issue = issues[i];
                string message = "[ZeroEngine.Multiplayer] " + issue.Code + ": " + issue.Message;
                if (issue.Severity == MultiplayerSetupIssueSeverity.Error)
                {
                    Debug.LogError(message);
                }
                else if (issue.Severity == MultiplayerSetupIssueSeverity.Warning)
                {
                    Debug.LogWarning(message);
                }
                else
                {
                    Debug.Log(message);
                }
            }
        }

        private static void ValidateConfig(
            MultiplayerSessionConfig config,
            ICollection<MultiplayerSetupIssue> issues)
        {
            if (config == null)
            {
                issues.Add(new MultiplayerSetupIssue(
                    MultiplayerSetupIssueSeverity.Error,
                    "multiplayer.setup.config_missing",
                    "Assign a MultiplayerSessionConfig."));
                return;
            }

            IReadOnlyList<string> errors = config.ValidateConfiguration();
            for (int i = 0; i < errors.Count; i++)
            {
                issues.Add(new MultiplayerSetupIssue(
                    MultiplayerSetupIssueSeverity.Error,
                    errors[i],
                    "MultiplayerSessionConfig contains an invalid value."));
            }
        }

        private static Dictionary<string, int> CountLoadedSceneTypes(IEnumerable<MonoBehaviour> behaviours)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null || behaviour.gameObject == null || !behaviour.gameObject.scene.IsValid())
                {
                    continue;
                }

                Type type = behaviour.GetType();
                Increment(counts, type.FullName ?? type.Name);
                Increment(counts, type.Name);
            }

            return counts;
        }

        private static void ValidateSteamLifecycleSources(ICollection<MultiplayerSetupIssue> issues)
        {
            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            for (int i = 0; i < scriptGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
                if (string.IsNullOrWhiteSpace(assetPath) ||
                    assetPath.EndsWith("SteamRuntimeOwner.cs", StringComparison.Ordinal) ||
                    !File.Exists(assetPath))
                {
                    continue;
                }

                string source;
                try
                {
                    source = File.ReadAllText(assetPath);
                }
                catch (IOException)
                {
                    continue;
                }

                if (source.IndexOf("SteamAPI.Init(", StringComparison.Ordinal) >= 0 ||
                    source.IndexOf("SteamAPI.RunCallbacks(", StringComparison.Ordinal) >= 0 ||
                    source.IndexOf("SteamAPI.Shutdown(", StringComparison.Ordinal) >= 0)
                {
                    issues.Add(new MultiplayerSetupIssue(
                        MultiplayerSetupIssueSeverity.Error,
                        "multiplayer.setup.external_steam_lifecycle_call",
                        "Steam lifecycle call found outside SteamRuntimeOwner: " + assetPath));
                }
            }
        }

        private static void ValidateActiveSceneInBuildSettings(ICollection<MultiplayerSetupIssue> issues)
        {
            string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if (string.IsNullOrWhiteSpace(activeScene))
            {
                return;
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled && string.Equals(scenes[i].path, activeScene, StringComparison.Ordinal))
                {
                    return;
                }
            }

            issues.Add(new MultiplayerSetupIssue(
                MultiplayerSetupIssueSeverity.Warning,
                "multiplayer.setup.active_scene_not_in_build",
                "The active multiplayer scene is not enabled in Build Settings: " + activeScene));
        }

        private static MultiplayerSessionConfig FindSingleConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:MultiplayerSessionConfig");
            if (guids.Length == 0)
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<MultiplayerSessionConfig>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void RequireExactlyOne(
            IDictionary<string, int> counts,
            string typeName,
            string displayName,
            ICollection<MultiplayerSetupIssue> issues)
        {
            int count = GetCount(counts, typeName);
            if (count != 1)
            {
                issues.Add(new MultiplayerSetupIssue(
                    MultiplayerSetupIssueSeverity.Error,
                    "multiplayer.setup.component_count_invalid",
                    displayName + " count must be exactly one, found " + count + "."));
            }
        }

        private static void RequireAtLeastOne(
            IDictionary<string, int> counts,
            string typeName,
            string displayName,
            ICollection<MultiplayerSetupIssue> issues)
        {
            int count = GetCount(counts, typeName);
            if (count < 1)
            {
                issues.Add(new MultiplayerSetupIssue(
                    MultiplayerSetupIssueSeverity.Error,
                    "multiplayer.setup.component_missing",
                    displayName + " is missing from the loaded scene."));
            }
        }

        private static int GetCount(IDictionary<string, int> counts, string key)
        {
            int count;
            return counts.TryGetValue(key, out count) ? count : 0;
        }

        private static void Increment(IDictionary<string, int> counts, string key)
        {
            int current;
            counts[key] = counts.TryGetValue(key, out current) ? current + 1 : 1;
        }
    }
}
