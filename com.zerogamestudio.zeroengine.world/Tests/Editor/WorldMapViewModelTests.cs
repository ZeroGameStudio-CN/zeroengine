using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.World.Map;

namespace ZeroEngine.World.Tests.Editor
{
    [TestFixture]
    [Category("Unit")]
    [Category("Boundary")]
    public sealed class WorldMapViewModelTests
    {
        [Test]
        public void Viewport_ClampsZoomPansCenterAndCreatesWorldBounds()
        {
            var viewport = new WorldMapViewportState(Vector3.zero, zoom: 20f, minZoom: 10f, maxZoom: 40f);

            viewport.SetZoom(100f);
            viewport.Pan(new Vector2(0.25f, -0.5f), aspectRatio: 2f);
            viewport.SetRotationDegrees(-90f);

            Assert.That(viewport.Zoom, Is.EqualTo(40f));
            Assert.That(viewport.CenterWorldPosition, Is.EqualTo(new Vector3(40f, 0f, -40f)).Using(Vector3Comparer.Instance));
            Assert.That(viewport.RotationDegrees, Is.EqualTo(270f));

            var bounds = viewport.CreateWorldBounds(aspectRatio: 2f);
            Assert.That(bounds.size, Is.EqualTo(new Vector3(160f, 1f, 80f)).Using(Vector3Comparer.Instance));
        }

        [Test]
        public void ViewModel_ProjectsVisibleMarkersAndMarksSelection()
        {
            var state = new WorldMapState("world.test");
            state.MarkerRegistry.RegisterProvider(new StaticMarkerProvider(
                Marker("marker.center", WorldMapMarkerCategory.Player, Vector3.zero),
                Marker("marker.quest", WorldMapMarkerCategory.Quest, new Vector3(5f, 0f, 5f)),
                Marker("marker.outside", WorldMapMarkerCategory.Npc, new Vector3(50f, 0f, 0f))));
            var viewport = new WorldMapViewportState(Vector3.zero, zoom: 10f);
            viewport.SelectMarker("marker.quest");
            var viewModel = new WorldMapViewModel(state);

            var built = viewModel.TryBuildSnapshot(viewport, out var snapshot, out var error);

            Assert.True(built, error);
            Assert.That(snapshot.Markers.Select(marker => marker.Marker.MarkerId), Is.EqualTo(new[]
            {
                "marker.center",
                "marker.quest"
            }));
            var questMarker = snapshot.Markers.Single(marker => marker.Marker.MarkerId == "marker.quest");
            Assert.That(questMarker.NormalizedPosition, Is.EqualTo(new Vector2(0.75f, 0.75f)).Using(Vector2Comparer.Instance));
            Assert.True(questMarker.IsSelected);
        }

        [Test]
        public void ViewModel_UsesRegistryFilterAndDiscoveryState()
        {
            var state = new WorldMapState("world.test");
            state.MarkerRegistry.RegisterProvider(new StaticMarkerProvider(
                Marker(
                    "marker.quest",
                    WorldMapMarkerCategory.Quest,
                    Vector3.zero,
                    cellId: "cell.street",
                    visibility: WorldMapMarkerVisibility.DiscoveredOnly),
                Marker(
                    "marker.hidden",
                    WorldMapMarkerCategory.Quest,
                    new Vector3(1f, 0f, 1f),
                    visibility: WorldMapMarkerVisibility.Hidden),
                Marker("marker.npc", WorldMapMarkerCategory.Npc, Vector3.zero)));
            var viewport = new WorldMapViewportState(Vector3.zero, zoom: 10f);
            var viewModel = new WorldMapViewModel(state);
            var filter = new WorldMapMarkerFilter(new[] { WorldMapMarkerCategory.Quest });

            Assert.True(viewModel.TryBuildSnapshot(viewport, out var snapshot, out var error, filter), error);
            Assert.That(snapshot.Markers, Is.Empty);

            state.Discovery.DiscoverCell("cell.street");
            Assert.True(viewModel.TryBuildSnapshot(viewport, out snapshot, out error, filter), error);

            Assert.That(snapshot.Markers.Select(marker => marker.Marker.MarkerId), Is.EqualTo(new[] { "marker.quest" }));
        }

        [Test]
        public void ViewModel_CanIncludeOutOfBoundsMarkersAsClampedData()
        {
            var state = new WorldMapState("world.test");
            state.MarkerRegistry.RegisterProvider(new StaticMarkerProvider(
                Marker("marker.outside", WorldMapMarkerCategory.Npc, new Vector3(50f, 0f, 50f))));
            var viewport = new WorldMapViewportState(Vector3.zero, zoom: 10f);
            var viewModel = new WorldMapViewModel(state);

            Assert.True(viewModel.TryBuildSnapshot(
                viewport,
                out var snapshot,
                out var error,
                includeOutOfBounds: true), error);

            Assert.That(snapshot.Markers.Count, Is.EqualTo(1));
            Assert.False(snapshot.Markers[0].IsInViewport);
            Assert.That(snapshot.Markers[0].NormalizedPosition, Is.EqualTo(Vector2.one).Using(Vector2Comparer.Instance));
        }

        [Test]
        public void RuntimeMapViewSources_DoNotReferenceP5OrUnityUi()
        {
            var runtimeMapRoot = WorldMapSourcePath.FindRuntimeMapRoot();
            var source = string.Join(
                "\n",
                Directory.GetFiles(runtimeMapRoot, "*View*.cs", SearchOption.TopDirectoryOnly)
                    .Concat(new[]
                    {
                        Path.Combine(runtimeMapRoot, "WorldMapViewportState.cs")
                    })
                    .Where(File.Exists)
                    .Select(File.ReadAllText));

            Assert.That(source, Does.Not.Contain("namespace ZGS"));
            Assert.That(source, Does.Not.Contain("using ZGS"));
            Assert.That(source, Does.Not.Contain("UnityEngine.UI"));
            Assert.That(source, Does.Not.Contain("TMPro"));
            Assert.That(source, Does.Not.Contain("Longleji"));
        }

        private static WorldMapMarkerDefinition Marker(
            string markerId,
            WorldMapMarkerCategory category,
            Vector3 position,
            string cellId = "",
            WorldMapMarkerVisibility visibility = WorldMapMarkerVisibility.Always)
        {
            return new WorldMapMarkerDefinition(
                markerId,
                category,
                "world.test",
                cellId,
                string.Empty,
                markerId,
                position,
                Quaternion.identity,
                visibility: visibility);
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

        private sealed class Vector3Comparer : IEqualityComparer<Vector3>
        {
            public static readonly Vector3Comparer Instance = new Vector3Comparer();

            public bool Equals(Vector3 x, Vector3 y)
            {
                return Mathf.Approximately(x.x, y.x)
                       && Mathf.Approximately(x.y, y.y)
                       && Mathf.Approximately(x.z, y.z);
            }

            public int GetHashCode(Vector3 obj)
            {
                return obj.GetHashCode();
            }
        }
    }

    internal static class WorldMapSourcePath
    {
        public static string FindRuntimeMapRoot()
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
    }
}
