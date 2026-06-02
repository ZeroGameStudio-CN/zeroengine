using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WorldNavigationServiceTests
    {
        [Test]
        public void ValidateTravelLink_WhenBothCellsReady_ReturnsWorldSpaceAnchorRoute()
        {
            var graph = CreateOffsetStreetToInteriorGraph();
            var navigation = new WorldNavigationService(graph);
            navigation.ReplaceReadyCells(new[] { "cell.street", "cell.apothecary" });

            var result = navigation.ValidateTravelLink(
                "link.street.apothecary.enter",
                "anchor.street.apothecary.outside");

            Assert.That(result.Status, Is.EqualTo(WorldNavigationRouteStatus.Succeeded));
            Assert.That(result.From.CellId, Is.EqualTo("cell.street"));
            Assert.That(result.From.AnchorId, Is.EqualTo("anchor.street.apothecary.outside"));
            Assert.That(result.To.CellId, Is.EqualTo("cell.apothecary"));
            Assert.That(result.To.AnchorId, Is.EqualTo("anchor.apothecary.inside"));
            Assert.That(result.To.WorldSpacePosition, Is.EqualTo(new Vector3(5f, 0f, 2f)));
        }

        [Test]
        public void ValidateTravelLink_WhenDestinationCellIsNotReady_ReturnsNavigationUnavailable()
        {
            var graph = CreateOffsetStreetToInteriorGraph();
            var navigation = new WorldNavigationService(graph);
            navigation.RegisterCellNavigationReady("cell.street");

            var result = navigation.ValidateTravelLink(
                "link.street.apothecary.enter",
                "anchor.street.apothecary.outside");

            Assert.That(result.Status, Is.EqualTo(WorldNavigationRouteStatus.NavigationUnavailable));
            Assert.That(result.Message, Does.Contain("cell.apothecary"));
        }

        [Test]
        public void ValidateTravelLink_BidirectionalLinkCanStartFromTargetAnchor()
        {
            var graph = CreateBidirectionalStreetToWildGraph();
            var navigation = new WorldNavigationService(graph);
            navigation.ReplaceReadyCells(new[] { "cell.street", "cell.wild" });

            var result = navigation.ValidateTravelLink(
                "link.street.wild",
                "anchor.wild.entry");

            Assert.That(result.Status, Is.EqualTo(WorldNavigationRouteStatus.Succeeded));
            Assert.That(result.From.AnchorId, Is.EqualTo("anchor.wild.entry"));
            Assert.That(result.To.AnchorId, Is.EqualTo("anchor.street.gate"));
        }

        [Test]
        public void ValidateAnchorRoute_WhenCrossCellAnchorsHaveNoTravelLink_ReturnsRouteNotConnected()
        {
            var graph = CreateUnlinkedCrossCellGraph();
            var navigation = new WorldNavigationService(graph);
            navigation.ReplaceReadyCells(new[] { "cell.street", "cell.wild" });

            var result = navigation.ValidateAnchorRoute(
                "anchor.street.gate",
                "anchor.wild.entry");

            Assert.That(result.Status, Is.EqualTo(WorldNavigationRouteStatus.RouteNotConnected));
        }

        [Test]
        public void ReplaceReadyCells_RemovesStaleNavigationRegistrations()
        {
            var graph = CreateBidirectionalStreetToWildGraph();
            var navigation = new WorldNavigationService(graph);
            navigation.ReplaceReadyCells(new[] { "cell.street", "cell.wild" });

            navigation.ReplaceReadyCells(new[] { "cell.street" });

            Assert.That(navigation.IsCellNavigationReady("cell.street"), Is.True);
            Assert.That(navigation.IsCellNavigationReady("cell.wild"), Is.False);
        }

        private static WorldGraphSO CreateOffsetStreetToInteriorGraph()
        {
            return CreateGraph(
                new[]
                {
                    new WorldCellDefinition(
                        "cell.street",
                        "Street",
                        WorldCellKind.Outdoor,
                        "StreetScene",
                        Vector3.zero,
                        WorldCellLayer.All,
                        1,
                        new[]
                        {
                            new WorldAnchorDefinition(
                                "anchor.street.apothecary.outside",
                                "Apothecary Outside",
                                WorldAnchorKind.InteriorEntry,
                                Vector3.zero,
                                Vector3.forward)
                        },
                        Array.Empty<WorldStreamingBoundaryDefinition>()),
                    new WorldCellDefinition(
                        "cell.apothecary",
                        "Apothecary",
                        WorldCellKind.Interior,
                        "ApothecaryInteriorScene",
                        new Vector3(4f, 0f, 0f),
                        WorldCellLayer.All,
                        1,
                        new[]
                        {
                            new WorldAnchorDefinition(
                                "anchor.apothecary.inside",
                                "Apothecary Inside",
                                WorldAnchorKind.InteriorEntry,
                                new Vector3(1f, 0f, 2f),
                                Vector3.back)
                        },
                        Array.Empty<WorldStreamingBoundaryDefinition>())
                },
                new[]
                {
                    new WorldTravelLinkDefinition(
                        "link.street.apothecary.enter",
                        "anchor.street.apothecary.outside",
                        "anchor.apothecary.inside",
                        WorldTravelMode.SeamlessInterior,
                        false)
                });
        }

        private static WorldGraphSO CreateBidirectionalStreetToWildGraph()
        {
            return CreateGraph(
                new[]
                {
                    new WorldCellDefinition(
                        "cell.street",
                        "Street",
                        WorldCellKind.Outdoor,
                        "StreetScene",
                        WorldCellLayer.All,
                        1,
                        new[]
                        {
                            new WorldAnchorDefinition(
                                "anchor.street.gate",
                                "Street Gate",
                                WorldAnchorKind.RoadExit,
                                Vector3.zero,
                                Vector3.forward)
                        },
                        Array.Empty<WorldStreamingBoundaryDefinition>()),
                    new WorldCellDefinition(
                        "cell.wild",
                        "Wild",
                        WorldCellKind.Outdoor,
                        "WildScene",
                        WorldCellLayer.All,
                        1,
                        new[]
                        {
                            new WorldAnchorDefinition(
                                "anchor.wild.entry",
                                "Wild Entry",
                                WorldAnchorKind.RoadExit,
                                new Vector3(8f, 0f, 0f),
                                Vector3.back)
                        },
                        Array.Empty<WorldStreamingBoundaryDefinition>())
                },
                new[]
                {
                    new WorldTravelLinkDefinition(
                        "link.street.wild",
                        "anchor.street.gate",
                        "anchor.wild.entry",
                        WorldTravelMode.PortalTransition,
                        true)
                });
        }

        private static WorldGraphSO CreateUnlinkedCrossCellGraph()
        {
            return CreateGraph(
                new[]
                {
                    new WorldCellDefinition(
                        "cell.street",
                        "Street",
                        WorldCellKind.Outdoor,
                        "StreetScene",
                        WorldCellLayer.All,
                        1,
                        new[]
                        {
                            new WorldAnchorDefinition(
                                "anchor.street.gate",
                                "Street Gate",
                                WorldAnchorKind.RoadExit,
                                Vector3.zero,
                                Vector3.forward)
                        },
                        Array.Empty<WorldStreamingBoundaryDefinition>()),
                    new WorldCellDefinition(
                        "cell.wild",
                        "Wild",
                        WorldCellKind.Outdoor,
                        "WildScene",
                        WorldCellLayer.All,
                        1,
                        new[]
                        {
                            new WorldAnchorDefinition(
                                "anchor.wild.entry",
                                "Wild Entry",
                                WorldAnchorKind.RoadExit,
                                new Vector3(8f, 0f, 0f),
                                Vector3.back)
                        },
                        Array.Empty<WorldStreamingBoundaryDefinition>())
                },
                Array.Empty<WorldTravelLinkDefinition>());
        }

        private static WorldGraphSO CreateGraph(
            IEnumerable<WorldCellDefinition> cells,
            IEnumerable<WorldTravelLinkDefinition> travelLinks)
        {
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[]
                {
                    new WorldRegionDefinition("region.test", "Test Region", cells)
                },
                travelLinks,
                Array.Empty<WorldFastTravelNodeDefinition>());
            return graph;
        }
    }
}
