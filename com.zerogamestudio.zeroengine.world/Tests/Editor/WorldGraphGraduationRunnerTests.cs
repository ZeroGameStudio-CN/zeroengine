using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.World.Editor.WorldGraph;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphGraduationRunnerTests
    {
        private const string TempFolderPath = "Assets/__ZEWorldGraphGraduationRunnerTests";
        private const string QuotedNamesScenePath = TempFolderPath + "/QuotedNames.txt";

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
