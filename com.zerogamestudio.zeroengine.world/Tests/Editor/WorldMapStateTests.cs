using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.Map;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    [TestFixture]
    [Category("Unit")]
    [Category("Boundary")]
    public sealed class WorldMapStateTests
    {
        [Test]
        public void MarkerRegistry_MergesProvidersAndFailsClosedOnDuplicateIds()
        {
            var registry = new WorldMapMarkerRegistry();
            registry.RegisterProvider(new StaticMarkerProvider(
                Marker("marker.quest", WorldMapMarkerCategory.Quest, priority: 20),
                Marker("marker.npc", WorldMapMarkerCategory.Npc, priority: 10)));
            registry.RegisterProvider(new StaticMarkerProvider(
                Marker("marker.quest", WorldMapMarkerCategory.Waypoint, priority: 30)));

            var results = new List<WorldMapMarkerDefinition>();
            var collected = registry.TryCollectMarkers(results, out var error, WorldMapMarkerFilter.All);

            Assert.False(collected);
            Assert.That(results, Is.Empty);
            Assert.That(error, Does.Contain("Duplicate world map marker id 'marker.quest'"));
            Assert.That(registry.LastError, Is.EqualTo(error));
        }

        [Test]
        public void MarkerRegistry_FiltersCategoriesAndDiscoveryVisibility()
        {
            var registry = new WorldMapMarkerRegistry();
            registry.RegisterProvider(new StaticMarkerProvider(
                Marker(
                    "marker.quest.visible",
                    WorldMapMarkerCategory.Quest,
                    cellId: "cell.street",
                    visibility: WorldMapMarkerVisibility.DiscoveredOnly),
                Marker(
                    "marker.quest.hidden",
                    WorldMapMarkerCategory.Quest,
                    visibility: WorldMapMarkerVisibility.Hidden),
                Marker(
                    "marker.npc",
                    WorldMapMarkerCategory.Npc,
                    visibility: WorldMapMarkerVisibility.Always)));
            var discovery = new WorldMapDiscoveryState();
            var filter = new WorldMapMarkerFilter(new[] { WorldMapMarkerCategory.Quest });
            var results = new List<WorldMapMarkerDefinition>();

            Assert.True(registry.TryCollectMarkers(results, out _, filter, discovery));
            Assert.That(results, Is.Empty);

            discovery.DiscoverCell("cell.street");
            Assert.True(registry.TryCollectMarkers(results, out _, filter, discovery));

            Assert.That(results.Select(marker => marker.MarkerId), Is.EqualTo(new[] { "marker.quest.visible" }));
        }

        [Test]
        public void DiscoveryState_CapturesAndRestoresSaveSafeSnapshot()
        {
            var discovery = new WorldMapDiscoveryState();
            discovery.DiscoverCell("cell.wild");
            discovery.DiscoverCell("Cell.Invalid");
            discovery.VisitAnchor("anchor.wild.spawn");
            discovery.UnlockFastTravelNode("fast.wild");

            var snapshot = discovery.CaptureSnapshot();
            var restored = new WorldMapDiscoveryState();
            restored.RestoreSnapshot(snapshot);

            Assert.That(restored.DiscoveredCellCount, Is.EqualTo(1));
            Assert.True(restored.IsCellDiscovered("cell.wild"));
            Assert.False(restored.IsCellDiscovered("Cell.Invalid"));
            Assert.True(restored.IsAnchorVisited("anchor.wild.spawn"));
            Assert.True(restored.IsFastTravelNodeUnlocked("fast.wild"));
        }

        [Test]
        public void StableIdUtility_CreatesLowercaseStableIdsForAdapterInputs()
        {
            var stableId = WorldMapStableIdUtility.CreateStableId("p5.minimap", " Story Teller Spawn ");

            Assert.That(stableId, Is.EqualTo("p5.minimap.story-teller-spawn"));
            Assert.True(WorldMapStableIdUtility.IsStableId(stableId));
        }

        [Test]
        public void CoordinateMapper_ProjectsWorldXZBoundsToNormalizedCoordinates()
        {
            var mapper = new WorldMapCoordinateMapper(new Bounds(
                new Vector3(10f, 0f, 20f),
                new Vector3(20f, 4f, 40f)));

            Assert.True(mapper.TryWorldToNormalized(new Vector3(10f, 2f, 20f), out var center));
            Assert.That(center, Is.EqualTo(new Vector2(0.5f, 0.5f)).Using(Vector2Comparer.Instance));

            Assert.True(mapper.TryWorldToNormalized(new Vector3(0f, 0f, 0f), out var min));
            Assert.That(min, Is.EqualTo(Vector2.zero).Using(Vector2Comparer.Instance));

            Assert.False(mapper.TryWorldToNormalized(new Vector3(30f, 0f, 50f), out var outside));
            Assert.That(outside, Is.EqualTo(new Vector2(1.5f, 1.25f)).Using(Vector2Comparer.Instance));
            Assert.That(mapper.WorldToNormalizedClamped(new Vector3(30f, 0f, 50f)), Is.EqualTo(Vector2.one).Using(Vector2Comparer.Instance));
        }

        [Test]
        public void WorldMapState_AppliesRuntimeLocationToDiscovery()
        {
            var state = new WorldMapState("world.old");
            var location = new WorldGraphRuntimeLocation(
                "world.longleji",
                "region.town",
                "cell.street",
                "anchor.street.spawn",
                "Street",
                Vector3.zero,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                Quaternion.identity);

            Assert.True(state.ApplyRuntimeLocation(location));

            Assert.That(state.WorldGraphId, Is.EqualTo("world.longleji"));
            Assert.That(state.ActiveCellId, Is.EqualTo("cell.street"));
            Assert.That(state.ActiveAnchorId, Is.EqualTo("anchor.street.spawn"));
            Assert.True(state.Discovery.IsCellDiscovered("cell.street"));
            Assert.True(state.Discovery.IsAnchorVisited("anchor.street.spawn"));
        }

        [Test]
        public void RuntimeMapSources_DoNotReferenceP5()
        {
            var runtimeMapRoot = FindRuntimeMapRoot();
            var source = string.Join(
                "\n",
                Directory.GetFiles(runtimeMapRoot, "*.cs", SearchOption.TopDirectoryOnly)
                    .Select(File.ReadAllText));

            Assert.That(source, Does.Not.Contain("namespace ZGS"));
            Assert.That(source, Does.Not.Contain("using ZGS"));
            Assert.That(source, Does.Not.Contain("Longleji"));
            Assert.That(source, Does.Not.Contain("ZGSProject_5"));
        }

        private static WorldMapMarkerDefinition Marker(
            string markerId,
            WorldMapMarkerCategory category,
            string cellId = "",
            string anchorId = "",
            int priority = 0,
            WorldMapMarkerVisibility visibility = WorldMapMarkerVisibility.Always)
        {
            return new WorldMapMarkerDefinition(
                markerId,
                category,
                "world.test",
                cellId,
                anchorId,
                markerId,
                Vector3.zero,
                Quaternion.identity,
                priority,
                visibility);
        }

        private static string FindRuntimeMapRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);

            var embeddedPath = Path.Combine(
                projectRoot,
                "Packages",
                "com.zerogamestudio.zeroengine.world",
                "Runtime",
                "Map");
            if (Directory.Exists(embeddedPath))
            {
                return embeddedPath;
            }

            var packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(packageCacheRoot))
            {
                var cachePath = Directory.GetDirectories(packageCacheRoot, "com.zerogamestudio.zeroengine.world*")
                    .Select(path => Path.Combine(path, "Runtime", "Map"))
                    .FirstOrDefault(Directory.Exists);
                if (!string.IsNullOrWhiteSpace(cachePath))
                {
                    return cachePath;
                }
            }

            var packageWorktreePath = Path.Combine(
                projectRoot,
                "com.zerogamestudio.zeroengine.world",
                "Runtime",
                "Map");
            if (Directory.Exists(packageWorktreePath))
            {
                return packageWorktreePath;
            }

            Assert.Fail("Could not locate ZeroEngine world Runtime/Map sources.");
            return string.Empty;
        }

        private sealed class StaticMarkerProvider : IWorldMapMarkerProvider
        {
            private readonly WorldMapMarkerDefinition[] _markers;

            public StaticMarkerProvider(params WorldMapMarkerDefinition[] markers)
            {
                _markers = markers;
            }

            public void CollectMarkers(List<WorldMapMarkerDefinition> results)
            {
                results.AddRange(_markers);
            }
        }

        private sealed class Vector2Comparer : IEqualityComparer<Vector2>
        {
            public static readonly Vector2Comparer Instance = new Vector2Comparer();

            public bool Equals(Vector2 x, Vector2 y)
            {
                return Mathf.Approximately(x.x, y.x) && Mathf.Approximately(x.y, y.y);
            }

            public int GetHashCode(Vector2 obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
