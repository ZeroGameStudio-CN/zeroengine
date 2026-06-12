using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.Editor.WorldGraph;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphWorkbenchTests
    {
        [Test]
        public void CreateSnapshot_BuildsDeterministicCellsLinksIssuesAndActions()
        {
            var graph = CreateGraph();
            try
            {
                var profile = CreateProfile(graph);
                var issues = new[]
                {
                    new AreaAuthoringIssue(
                        AreaAuthoringIssueSeverity.Warning,
                        "WORLD_TEST_WARNING",
                        "Warn",
                        "Assets/Scenes/CellB.unity",
                        "cell.b"),
                    new AreaAuthoringIssue(
                        AreaAuthoringIssueSeverity.Error,
                        "WORLD_TEST_ERROR",
                        "Block",
                        "Assets/Scenes/CellA.unity",
                        "cell.a")
                };
                var customAction = WorldGraphWorkbenchAction.CreateProjectCommand(
                    "p5.generate.graph",
                    "Generate Graph",
                    "Generate graph content.",
                    WorldGraphWorkbenchActionRisk.WritesAssets,
                    true,
                    () => { });

                var snapshot = WorldGraphWorkbenchModel.CreateSnapshot(
                    "Test Workbench",
                    profile,
                    issues,
                    new[] { customAction },
                    Array.Empty<WorldGraphWorkbenchRunRecord>());

                Assert.That(snapshot.WorldGraphId, Is.EqualTo("world.test"));
                Assert.That(snapshot.Cells.Select(cell => cell.CellId), Is.EqualTo(new[] { "cell.a", "cell.b" }));
                Assert.That(snapshot.Cells[0].Status, Is.EqualTo(WorldGraphWorkbenchCellStatus.Error));
                Assert.That(snapshot.Cells[1].Status, Is.EqualTo(WorldGraphWorkbenchCellStatus.Warning));
                Assert.That(snapshot.Links.Single().LinkId, Is.EqualTo("link.a.b"));
                Assert.That(snapshot.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { "WORLD_TEST_ERROR", "WORLD_TEST_WARNING" }));
                Assert.That(snapshot.Actions.Any(action => action.ActionId == WorldGraphWorkbenchModel.RunValidationActionId), Is.True);
                Assert.That(snapshot.Actions.Any(action => action.ActionId == WorldGraphWorkbenchModel.RunGraduationActionId), Is.True);
                Assert.That(snapshot.Actions.Any(action => action.ActionId == WorldGraphWorkbenchModel.OpenGraphActionId), Is.True);
                Assert.That(snapshot.Actions.Any(action => action.ActionId == WorldGraphWorkbenchModel.OpenSceneActionPrefix + "cell.a"), Is.True);
                Assert.That(snapshot.Actions.Any(action => action.ActionId == WorldGraphWorkbenchModel.OpenNavigationActionPrefix + "cell.a"), Is.True);
                Assert.That(snapshot.Actions.Any(action => action.ActionId == "p5.generate.graph" && action.RequiresConfirmation), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void Session_RunValidationAndGraduation_RecordHistory()
        {
            var graph = CreateGraph();
            try
            {
                var profile = CreateProfile(graph, requiredTravelMode: WorldTravelMode.FastTravel);
                var session = new WorldGraphWorkbenchSession("Test Workbench", profile);

                session.RunValidation();
                session.RunGraduation();

                Assert.That(session.CurrentSnapshot.History.Count, Is.EqualTo(2));
                Assert.That(session.CurrentSnapshot.History[0].Mode, Is.EqualTo(WorldGraphWorkbenchRunMode.Graduation));
                Assert.That(session.CurrentSnapshot.History[1].Mode, Is.EqualTo(WorldGraphWorkbenchRunMode.Validation));
                Assert.That(session.CurrentSnapshot.Issues.Any(issue => issue.Code == "WORLD_GRADUATION_REQUIRED_TRAVEL_MODE_MISSING"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void Session_ExecutesInjectedProjectCommandWithoutOwningProjectLogic()
        {
            var graph = CreateGraph();
            var executed = false;
            try
            {
                var profile = CreateProfile(graph);
                var action = WorldGraphWorkbenchAction.CreateProjectCommand(
                    "project.rebuild",
                    "Rebuild",
                    "Project-provided rebuild command.",
                    WorldGraphWorkbenchActionRisk.WritesAssets,
                    true,
                    () => executed = true);
                var session = new WorldGraphWorkbenchSession("Test Workbench", profile, new[] { action });

                var result = session.TryExecuteAction("project.rebuild", out var error);

                Assert.True(result, error);
                Assert.True(executed);
                Assert.That(session.CurrentSnapshot.Actions.Any(descriptor => descriptor.ActionId == "project.rebuild"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorkbenchSources_DoNotReferenceP5()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(WorldGraphWorkbenchWindow).Assembly);
            Assert.NotNull(packageInfo);
            var workbenchPath = Path.Combine(packageInfo.resolvedPath, "Editor", "WorldGraph", "Workbench");
            var sources = Directory.GetFiles(workbenchPath, "*.cs")
                .Select(File.ReadAllText)
                .ToArray();

            Assert.That(sources, Is.Not.Empty);
            foreach (var source in sources)
            {
                Assert.That(source, Does.Not.Contain("ZGS."));
                Assert.That(source, Does.Not.Contain("Longleji"));
                Assert.That(source, Does.Not.Contain("P5"));
            }
        }

        private static WorldGraphSO CreateGraph()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[]
                {
                    new WorldRegionDefinition(
                        "region.test",
                        "Test Region",
                        new[]
                        {
                            new WorldCellDefinition(
                                "cell.b",
                                "Cell B",
                                WorldCellKind.Interior,
                                "CellB",
                                WorldCellLayer.Geometry,
                                1,
                                new[]
                                {
                                    new WorldAnchorDefinition(
                                        "anchor.b",
                                        "Anchor B",
                                        WorldAnchorKind.InteriorEntry,
                                        Vector3.zero,
                                        Vector3.forward)
                                },
                                Array.Empty<WorldStreamingBoundaryDefinition>()),
                            new WorldCellDefinition(
                                "cell.a",
                                "Cell A",
                                WorldCellKind.Outdoor,
                                "CellA",
                                WorldCellLayer.Geometry | WorldCellLayer.Navigation,
                                2,
                                new[]
                                {
                                    new WorldAnchorDefinition(
                                        "anchor.a",
                                        "Anchor A",
                                        WorldAnchorKind.InteriorExit,
                                        Vector3.zero,
                                        Vector3.forward)
                                },
                                new[]
                                {
                                    new WorldStreamingBoundaryDefinition("boundary.a.b", new[] { "cell.b" })
                                })
                        })
                },
                new[]
                {
                    new WorldTravelLinkDefinition(
                        "link.a.b",
                        "anchor.a",
                        "anchor.b",
                        WorldTravelMode.SeamlessInterior,
                        true)
                },
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }

        private static WorldGraphGraduationProfile CreateProfile(
            WorldGraphSO graph,
            WorldTravelMode? requiredTravelMode = null)
        {
            return new WorldGraphGraduationProfile(
                graph,
                "Assets/Data/World/WorldGraph_Test.asset",
                "world.test",
                "cell.a",
                "anchor.a",
                requiredTravelMode.HasValue
                    ? new[] { requiredTravelMode.Value }
                    : Array.Empty<WorldTravelMode>(),
                Array.Empty<WorldAddressablesGroupContract>(),
                Array.Empty<WorldAddressableAssetContract>(),
                cell => "Assets/Scenes/" + cell.SceneAddress + ".unity",
                cell => "Assets/Data/Nav/" + cell.CellId + ".asset",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false);
        }
    }
}
