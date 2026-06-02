using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.Editor.WorldGraph;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldGraphValidationKernelTests
    {
        [Test]
        public void WorldPosition_WithAnchor_PreservesStableIdentity()
        {
            var position = new WorldPosition(
                "world.longleji",
                "region.longleji",
                "cell.street",
                "anchor.apothecary.door.outside",
                new Vector3(1f, 0f, 2f),
                Quaternion.identity);

            Assert.AreEqual("world.longleji", position.WorldGraphId);
            Assert.AreEqual("region.longleji", position.RegionId);
            Assert.AreEqual("cell.street", position.CellId);
            Assert.AreEqual("anchor.apothecary.door.outside", position.AnchorId);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), position.CellLocalPosition);
            Assert.IsTrue(position.HasAnchor);
        }

        [Test]
        public void WorldCellLayer_DefaultGraduationMask_IncludesCoreRuntimeLayers()
        {
            var mask = WorldCellLayer.Geometry
                       | WorldCellLayer.Collision
                       | WorldCellLayer.Navigation
                       | WorldCellLayer.GameplayMarkers
                       | WorldCellLayer.LightingAndVolumes
                       | WorldCellLayer.Audio;

            Assert.IsTrue(mask.HasFlag(WorldCellLayer.Geometry));
            Assert.IsTrue(mask.HasFlag(WorldCellLayer.Collision));
            Assert.IsTrue(mask.HasFlag(WorldCellLayer.Navigation));
            Assert.IsTrue(mask.HasFlag(WorldCellLayer.GameplayMarkers));
            Assert.IsTrue(mask.HasFlag(WorldCellLayer.LightingAndVolumes));
            Assert.IsTrue(mask.HasFlag(WorldCellLayer.Audio));
        }

        [Test]
        public void WorldGraphSO_FindCellAndAnchor_ResolveStableIds()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Longleji Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.Geometry | WorldCellLayer.Collision,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.spawn",
                                            "Street Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                Assert.NotNull(graph.FindRegion("region.longleji"));
                Assert.NotNull(graph.FindCell("cell.street"));
                Assert.NotNull(graph.FindAnchor("anchor.street.spawn"));
                Assert.IsNull(graph.FindCell("cell.missing"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphSO_FindMethods_IgnoreNullAuthoredEntries()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new WorldRegionDefinition[]
                    {
                        null,
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new WorldCellDefinition[]
                            {
                                null,
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Longleji Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new WorldAnchorDefinition[]
                                    {
                                        null,
                                        new WorldAnchorDefinition(
                                            "anchor.street.spawn",
                                            "Street Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                Assert.DoesNotThrow(() => graph.FindRegion("region.longleji"));
                Assert.DoesNotThrow(() => graph.FindCell("cell.street"));
                Assert.DoesNotThrow(() => graph.FindAnchor("anchor.street.spawn"));
                Assert.IsNull(graph.FindCell("cell.missing"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsDuplicateCellIdsAndBrokenLinks()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street A",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    Array.Empty<WorldStreamingBoundaryDefinition>()),
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street B",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_02",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.broken",
                            "anchor.missing.a",
                            "anchor.missing.b",
                            WorldTravelMode.PortalTransition,
                            true)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_CELL_ID_DUPLICATE"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_TRAVEL_LINK_ANCHOR_MISSING"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsMissingStreamingBoundaryTargets()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    new[]
                                    {
                                        new WorldStreamingBoundaryDefinition(
                                            "boundary.street.to_missing",
                                            new[] { "cell.missing" })
                                    })
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_TARGET_MISSING"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsDuplicateSceneAddressesInStrictProduction()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_AllInOne",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    Array.Empty<WorldStreamingBoundaryDefinition>()),
                                new WorldCellDefinition(
                                    "cell.wild",
                                    "Wild",
                                    WorldCellKind.Outdoor,
                                    "Longleji_AllInOne",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_CELL_SCENE_ADDRESS_DUPLICATE"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsTravelLinkThatLoopsToSameAnchor()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.loop",
                                            "Street Loop",
                                            WorldAnchorKind.RoadExit,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.loop",
                            "anchor.street.loop",
                            "anchor.street.loop",
                            WorldTravelMode.PortalTransition,
                            false)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_TRAVEL_LINK_SELF_ANCHOR"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsTravelLinkInsideSameCell()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.a",
                                            "Street A",
                                            WorldAnchorKind.RoadExit,
                                            Vector3.zero,
                                            Vector3.forward),
                                        new WorldAnchorDefinition(
                                            "anchor.street.b",
                                            "Street B",
                                            WorldAnchorKind.RoadExit,
                                            Vector3.right,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.same_cell",
                            "anchor.street.a",
                            "anchor.street.b",
                            WorldTravelMode.PortalTransition,
                            false)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_TRAVEL_LINK_SAME_CELL"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsTravelLinkWithAnchorKindThatDoesNotMatchMode()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.spawn",
                                            "Street Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>()),
                                new WorldCellDefinition(
                                    "cell.wild",
                                    "Wild",
                                    WorldCellKind.Outdoor,
                                    "Longleji_WildBank_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.wild.entry",
                                            "Wild Entry",
                                            WorldAnchorKind.RoadExit,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.wild.invalid_kind",
                            "anchor.street.spawn",
                            "anchor.wild.entry",
                            WorldTravelMode.SeamlessWalk,
                            false)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_TRAVEL_LINK_ANCHOR_KIND_MISMATCH"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsInvalidSerializedEnumValues()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    (WorldCellKind)999,
                                    "Longleji_Street_01",
                                    WorldCellLayer.Geometry | (WorldCellLayer)(1 << 12),
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.spawn",
                                            "Street Spawn",
                                            (WorldAnchorKind)999,
                                            Vector3.zero,
                                            Vector3.forward),
                                        new WorldAnchorDefinition(
                                            "anchor.street.exit",
                                            "Street Exit",
                                            WorldAnchorKind.RoadExit,
                                            Vector3.right,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.street.invalid_mode",
                            "anchor.street.spawn",
                            "anchor.street.exit",
                            (WorldTravelMode)999,
                            false)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_CELL_KIND_INVALID"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_CELL_LAYERS_INVALID"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_ANCHOR_KIND_INVALID"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_TRAVEL_LINK_MODE_INVALID"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsInvalidStableIdFormats()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "World Longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region/longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "Cell.longleji.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.longleji.street spawn",
                                            "Street Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward),
                                        new WorldAnchorDefinition(
                                            "anchor.longleji.street.exit",
                                            "Street Exit",
                                            WorldAnchorKind.RoadExit,
                                            Vector3.right,
                                            Vector3.forward)
                                    },
                                    new[]
                                    {
                                        new WorldStreamingBoundaryDefinition(
                                            "boundary.longleji.",
                                            new[] { "cell.wild" })
                                    })
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.longleji#bad",
                            "anchor.longleji.street spawn",
                            "anchor.longleji.street.exit",
                            WorldTravelMode.PortalTransition,
                            false)
                    },
                    new[]
                    {
                        new WorldFastTravelNodeDefinition(
                            "fast Longleji",
                            "anchor.longleji.street.exit",
                            "world.longleji unlock")
                    });

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_GRAPH_ID_INVALID_FORMAT"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_REGION_ID_INVALID_FORMAT"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_CELL_ID_INVALID_FORMAT"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_ANCHOR_ID_INVALID_FORMAT"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_ID_INVALID_FORMAT"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_TRAVEL_LINK_ID_INVALID_FORMAT"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_FAST_TRAVEL_NODE_ID_INVALID_FORMAT"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_FAST_TRAVEL_UNLOCK_INVALID_FORMAT"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsInvalidSpatialAuthoringValues()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    new Vector3(float.NaN, 0f, 0f),
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.bad_position",
                                            "Bad Position",
                                            WorldAnchorKind.Spawn,
                                            new Vector3(float.PositiveInfinity, 0f, 0f),
                                            Vector3.forward),
                                        new WorldAnchorDefinition(
                                            "anchor.street.bad_forward",
                                            "Bad Forward",
                                            WorldAnchorKind.RoadExit,
                                            Vector3.zero,
                                            Vector3.zero)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_CELL_ORIGIN_INVALID"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_ANCHOR_POSITION_INVALID"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_ANCHOR_FORWARD_INVALID"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsInvalidStreamingBoundaryIdentityAndTargets()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    new[]
                                    {
                                        new WorldStreamingBoundaryDefinition(
                                            "boundary.duplicate",
                                            Array.Empty<string>()),
                                        new WorldStreamingBoundaryDefinition(
                                            "boundary.duplicate",
                                            new[] { "cell.street", "cell.wild", "cell.wild", string.Empty })
                                    }),
                                new WorldCellDefinition(
                                    "cell.wild",
                                    "Wild",
                                    WorldCellKind.Outdoor,
                                    "Longleji_WildBank_01",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_ID_DUPLICATE"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_TARGETS_EMPTY"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_TARGET_EMPTY"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_TARGET_DUPLICATE"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_TARGET_SELF"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsDuplicateStreamingTargetAcrossSourceCellBoundaries()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    new[]
                                    {
                                        new WorldStreamingBoundaryDefinition(
                                            "boundary.street.to_wild_a",
                                            new[] { "cell.wild" }),
                                        new WorldStreamingBoundaryDefinition(
                                            "boundary.street.to_wild_b",
                                            new[] { "cell.wild" })
                                    }),
                                new WorldCellDefinition(
                                    "cell.wild",
                                    "Wild",
                                    WorldCellKind.Outdoor,
                                    "Longleji_WildBank_01",
                                    WorldCellLayer.All,
                                    1,
                                    Array.Empty<WorldAnchorDefinition>(),
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_STREAMING_BOUNDARY_TARGET_CELL_DUPLICATE"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsInteriorWithoutReturnLinkAndInvalidBudget()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.apothecary.interior",
                                    "Apothecary",
                                    WorldCellKind.Interior,
                                    "Longleji_Apothecary_Interior",
                                    WorldCellLayer.Geometry,
                                    0,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.apothecary.exit",
                                            "Exit",
                                            WorldAnchorKind.InteriorExit,
                                            Vector3.zero,
                                            Vector3.back)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_CELL_BUDGET_WEIGHT_INVALID"), Is.True);
                Assert.That(issues.Any(issue => issue.Code == "WORLD_INTERIOR_RETURN_LINK_MISSING"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsInteriorExitThatOnlyLinksToAnotherInteriorCell()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.room_a",
                                    "Room A",
                                    WorldCellKind.Interior,
                                    "Longleji_Room_A",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.room_a.exit",
                                            "Room A Exit",
                                            WorldAnchorKind.InteriorExit,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>()),
                                new WorldCellDefinition(
                                    "cell.room_b",
                                    "Room B",
                                    WorldCellKind.Interior,
                                    "Longleji_Room_B",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.room_b.exit",
                                            "Room B Exit",
                                            WorldAnchorKind.InteriorExit,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.room_a.room_b",
                            "anchor.room_a.exit",
                            "anchor.room_b.exit",
                            WorldTravelMode.SeamlessInterior,
                            true)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_INTERIOR_RETURN_LINK_MISSING"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsInteriorExitThatOnlyLinksThroughFastTravel()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.room",
                                    "Room",
                                    WorldCellKind.Interior,
                                    "Longleji_Room",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.room.exit",
                                            "Room Exit",
                                            WorldAnchorKind.InteriorExit,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>()),
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.return",
                                            "Street Return",
                                            WorldAnchorKind.FastTravel,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.room.street.fast_return",
                            "anchor.room.exit",
                            "anchor.street.return",
                            WorldTravelMode.FastTravel,
                            false)
                    },
                    new[]
                    {
                        new WorldFastTravelNodeDefinition(
                            "fast.street.return",
                            "anchor.street.return",
                            "world.longleji.street.fast_return.unlocked")
                    });

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_INTERIOR_RETURN_LINK_MISSING"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsFastTravelLinkWithInvalidAnchorKind()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.kiln",
                                    "Kiln",
                                    WorldCellKind.Outdoor,
                                    "Longleji_KilnRuins_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.kiln.spawn",
                                            "Kiln Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>()),
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.return",
                                            "Street Return",
                                            WorldAnchorKind.FastTravel,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.kiln.street.fast_return",
                            "anchor.kiln.spawn",
                            "anchor.street.return",
                            WorldTravelMode.FastTravel,
                            false)
                    },
                    new[]
                    {
                        new WorldFastTravelNodeDefinition(
                            "fast.street.return",
                            "anchor.street.return",
                            "world.longleji.street.fast_return.unlocked")
                    });

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_TRAVEL_LINK_ANCHOR_KIND_MISMATCH"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsFastTravelLinkWithoutRegisteredDestinationNode()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.kiln",
                                    "Kiln",
                                    WorldCellKind.Outdoor,
                                    "Longleji_KilnRuins_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.kiln.return",
                                            "Kiln Return",
                                            WorldAnchorKind.FastTravel,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>()),
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.return",
                                            "Street Return",
                                            WorldAnchorKind.FastTravel,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    new[]
                    {
                        new WorldTravelLinkDefinition(
                            "link.kiln.street.fast_return",
                            "anchor.kiln.return",
                            "anchor.street.return",
                            WorldTravelMode.FastTravel,
                            false)
                    },
                    Array.Empty<WorldFastTravelNodeDefinition>());

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_FAST_TRAVEL_LINK_NODE_MISSING"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsDuplicateFastTravelNodeAnchors()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.return",
                                            "Street Return",
                                            WorldAnchorKind.FastTravel,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    new[]
                    {
                        new WorldFastTravelNodeDefinition(
                            "fast.longleji.street.a",
                            "anchor.street.return",
                            "world.longleji.street.fast_travel.unlocked.a"),
                        new WorldFastTravelNodeDefinition(
                            "fast.longleji.street.b",
                            "anchor.street.return",
                            "world.longleji.street.fast_travel.unlocked.b")
                    });

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_FAST_TRAVEL_NODE_ANCHOR_DUPLICATE"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void WorldGraphValidator_ReportsFastTravelNodeAnchorWithWrongKind()
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            try
            {
                graph.ConfigureForTests(
                    "world.longleji",
                    new[]
                    {
                        new WorldRegionDefinition(
                            "region.longleji",
                            "Longleji",
                            new[]
                            {
                                new WorldCellDefinition(
                                    "cell.street",
                                    "Street",
                                    WorldCellKind.Outdoor,
                                    "Longleji_Street_01",
                                    WorldCellLayer.All,
                                    1,
                                    new[]
                                    {
                                        new WorldAnchorDefinition(
                                            "anchor.street.spawn",
                                            "Street Spawn",
                                            WorldAnchorKind.Spawn,
                                            Vector3.zero,
                                            Vector3.forward)
                                    },
                                    Array.Empty<WorldStreamingBoundaryDefinition>())
                            })
                    },
                    Array.Empty<WorldTravelLinkDefinition>(),
                    new[]
                    {
                        new WorldFastTravelNodeDefinition(
                            "fast.longleji.street",
                            "anchor.street.spawn",
                            "world.longleji.street.fast_travel.unlocked")
                    });

                var issues = WorldGraphValidator.Validate(graph, WorldGraphValidationOptions.StrictProduction);

                Assert.That(issues.Any(issue => issue.Code == "WORLD_FAST_TRAVEL_NODE_ANCHOR_KIND_INVALID"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }
    }
}
