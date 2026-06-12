using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.Editor.WorldGraph;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphGraduationRunnerTests
    {
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
    }
}
