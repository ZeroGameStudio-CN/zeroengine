using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using ZeroEngine.World.Editor.WorldGraph;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphGraduationRunnerTests
    {
        private const string TempFolderPath = "Assets/__ZEWorldGraphGraduationRunnerTests";
        private const string QuotedNamesScenePath = TempFolderPath + "/QuotedNames.txt";
        private const string NavigationScenePath = TempFolderPath + "/NavigationBinding.txt";
        private const string NavigationDataPath = TempFolderPath + "/NavMesh.asset";
        private const string NavigationSourceScriptGuid = "11111111111111111111111111111111";

        [Test]
        public void Validate_NullProfile_ReportsMissingProfile()
        {
            var issues = WorldGraphGraduationRunner.Validate(null);

            Assert.That(issues.Any(issue => issue.Code == "WORLD_GRADUATION_PROFILE_MISSING"), Is.True);
        }

        [Test]
        public void Validate_MissingRequiredTravelMode_ReportsBlockingIssue()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.test",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.test",
                            "Test",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.test",
                                    "Test Cell",
                                    WorldCellKind.Outdoor,
                                    "Test_Cell",
                                    WorldCellLayer.Geometry,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.test.spawn",
                                            "Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var profile = new WorldGraphGraduationProfile(
                    graph,
                    "Assets/Test.asset",
                    "world.test",
                    "cell.test",
                    "anchor.test.spawn",
                    new[] { WorldTravelMode.FastTravel },
                    Array.Empty<WorldAddressablesGroupContract>(),
                    Array.Empty<WorldAddressableAssetContract>(),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false);

                var issues = WorldGraphGraduationRunner.Validate(profile);

                Assert.That(
                    issues.Any(issue => issue.Code == "WORLD_GRADUATION_REQUIRED_TRAVEL_MODE_MISSING"),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void Validate_QuotedUnityYamlNames_AreAccepted()
        {
            EnsureTempFolder();
            File.WriteAllText(
                QuotedNamesScenePath,
                "--- !u!1 &1\n"
                + "GameObject:\n"
                + "  m_Name: '[WorldCell] cell.test'\n"
                + "--- !u!1 &2\n"
                + "GameObject:\n"
                + "  m_Name: '[Layer] Geometry'\n"
                + "--- !u!1 &3\n"
                + "GameObject:\n"
                + "  m_Name: '[Ready] Geometry'\n"
                + "--- !u!1 &4\n"
                + "GameObject:\n"
                + "  m_Name: Anchor_anchor.test.spawn\n");
            AssetDatabase.ImportAsset(QuotedNamesScenePath, ImportAssetOptions.ForceUpdate);

            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.test",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.test",
                            "Test",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.test",
                                    "Test Cell",
                                    WorldCellKind.Outdoor,
                                    "Test_Cell",
                                    WorldCellLayer.Geometry,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.test.spawn",
                                            "Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var profile = new WorldGraphGraduationProfile(
                    graph,
                    "Assets/Test.asset",
                    "world.test",
                    "cell.test",
                    "anchor.test.spawn",
                    Array.Empty<WorldTravelMode>(),
                    Array.Empty<WorldAddressablesGroupContract>(),
                    Array.Empty<WorldAddressableAssetContract>(),
                    _ => QuotedNamesScenePath,
                    null,
                    _ => "[WorldCell] cell.test",
                    _ => "[Layer] Geometry",
                    _ => "[Ready] Geometry",
                    null,
                    null,
                    null,
                    null,
                    null,
                    false);

                var sceneIssues = WorldGraphGraduationRunner
                    .Validate(profile)
                    .Where(issue => issue.Code.StartsWith("WORLD_SCENE_", StringComparison.Ordinal))
                    .ToArray();

                Assert.That(sceneIssues, Is.Empty, string.Join("\n", sceneIssues.Select(issue => issue.ToString())));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
                AssetDatabase.DeleteAsset(QuotedNamesScenePath);
                AssetDatabase.DeleteAsset(TempFolderPath);
            }
        }

        [Test]
        public void Validate_StrictNavigationBindingRequiresNavMeshDataField()
        {
            EnsureTempFolder();
            var navMeshData = new NavMeshData();
            AssetDatabase.CreateAsset(navMeshData, NavigationDataPath);
            AssetDatabase.ImportAsset(NavigationDataPath, ImportAssetOptions.ForceUpdate);
            var navMeshGuid = AssetDatabase.AssetPathToGUID(NavigationDataPath);

            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.test",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.test",
                            "Test",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.test",
                                    "Test Cell",
                                    WorldCellKind.Outdoor,
                                    "Test_Cell",
                                    WorldCellLayer.Navigation,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.test.spawn",
                                            "Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var profile = new WorldGraphGraduationProfile(
                    graph,
                    "Assets/Test.asset",
                    "world.test",
                    "cell.test",
                    "anchor.test.spawn",
                    Array.Empty<WorldTravelMode>(),
                    Array.Empty<WorldAddressablesGroupContract>(),
                    Array.Empty<WorldAddressableAssetContract>(),
                    _ => NavigationScenePath,
                    _ => NavigationDataPath,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    NavigationSourceScriptGuid,
                    "navigation.navmesh",
                    true);

                File.WriteAllText(NavigationScenePath, CreateNavigationComponentYaml(navMeshGuid, bindsNavMeshDataField: false));
                var invalidIssues = WorldNavigationBindingValidator.Validate(profile);
                Assert.That(
                    invalidIssues.Any(issue => issue.Code == "WORLD_NAVIGATION_SCENE_BINDING_MISSING"),
                    Is.True,
                    string.Join("\n", invalidIssues.Select(issue => issue.ToString())));

                File.WriteAllText(NavigationScenePath, CreateNavigationComponentYaml(navMeshGuid, bindsNavMeshDataField: true));
                var validIssues = WorldNavigationBindingValidator.Validate(profile);
                Assert.That(
                    validIssues.Any(issue => issue.Code == "WORLD_NAVIGATION_SCENE_BINDING_MISSING"),
                    Is.False,
                    string.Join("\n", validIssues.Select(issue => issue.ToString())));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
                AssetDatabase.DeleteAsset(NavigationScenePath);
                AssetDatabase.DeleteAsset(NavigationDataPath);
                AssetDatabase.DeleteAsset(TempFolderPath);
            }
        }

        private static string CreateNavigationComponentYaml(string navMeshGuid, bool bindsNavMeshDataField)
        {
            var navMeshDataLine = bindsNavMeshDataField
                ? "  _navMeshData: {fileID: 23800000, guid: " + navMeshGuid + ", type: 2}\n"
                : "  _navMeshData: {fileID: 0}\n"
                  + "  _unrelatedReference: {fileID: 23800000, guid: " + navMeshGuid + ", type: 2}\n";

            return "--- !u!114 &1\n"
                   + "MonoBehaviour:\n"
                   + "  m_Enabled: 1\n"
                   + "  m_Script: {fileID: 11500000, guid: " + NavigationSourceScriptGuid + ", type: 3}\n"
                   + "  _sourceId: navigation.navmesh\n"
                   + "  _requiresBakedNavMeshData: 1\n"
                   + navMeshDataLine;
        }

        private static void EnsureTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                return;
            }

            AssetDatabase.CreateFolder("Assets", "__ZEWorldGraphGraduationRunnerTests");
        }
    }
}
