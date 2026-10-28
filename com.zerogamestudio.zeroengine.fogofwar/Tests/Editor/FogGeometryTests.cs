using System;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.FogOfWar.Unity;
using Object = UnityEngine.Object;

namespace ZeroEngine.FogOfWar.Tests
{
    public sealed class FogGeometryTests
    {
        [Test]
        public void SubCellWallBlocksCurrentSightWhileExplorationRemainsMemory()
        {
            var sight = new SightPolygon2D();
            var grid = new ExplorationGrid2D(new Rect(-10, -10, 20, 20), 4, 4, new bool[16], new bool[16]);
            var wall = new[] { new SightPolygon2D.Segment(new Vector2(.2f, -10), new Vector2(.2f, 10)) };
            sight.Rebuild(Vector2.zero, new Vector2(8, 8), 1, 0, wall);
            grid.SetCurrentSight(sight);
            Assert.That(grid.IsVisible(new Vector2(.1f, 0)), Is.True);
            Assert.That(grid.IsVisible(new Vector2(.3f, 0)), Is.False);
            sight.Rebuild(new Vector2(2, 0), new Vector2(8, 8), 1, 0, wall);
            Assert.That(grid.IsVisible(new Vector2(.3f, 0)), Is.True);
            Assert.That(grid.IsVisible(new Vector2(.1f, 0)), Is.False);
        }

        [Test]
        public void RestoredMemoryNeverRestoresCurrentSightAndInvalidPayloadIsAtomic()
        {
            var grid = new ExplorationGrid2D(new Rect(0, 0, 8, 8), 8, 8, new bool[64], new bool[64]);
            grid.Reveal(new Vector2(4, 4), new Vector2(3, 3), .1f);
            string explored = grid.ExportRevealed(), terrain = grid.ExportRememberedTerrain();
            Assert.That(grid.ExploredCellCount, Is.GreaterThan(0));
            var invalidTerrain = Convert.FromBase64String(terrain); invalidTerrain[7] = 123;
            Assert.That(grid.TryRestoreMemory(new byte[64], invalidTerrain), Is.False);
            Assert.That(grid.ExportRevealed(), Is.EqualTo(explored));
            Assert.That(grid.IsVisible(new Vector2(4, 4)), Is.True);
            Assert.That(grid.TryRestoreMemory(Convert.FromBase64String(explored), Convert.FromBase64String(terrain)), Is.True);
            Assert.That(grid.IsVisible(new Vector2(4, 4)), Is.False);
            Assert.That(grid.IsExplored(new Vector2(4, 4)), Is.True);
        }

        [Test]
        public void RegisteredPresentationReleasesOnlyItsOverridesAndPreservesAuthoredState()
        {
            var root = new GameObject("Synthetic registered fog target");
            var enabled = root.AddComponent<SpriteRenderer>();
            var disabledObject = new GameObject("Authored hidden renderer");
            disabledObject.transform.SetParent(root.transform);
            var suppressed = disabledObject.AddComponent<SpriteRenderer>(); suppressed.forceRenderingOff = true;
            var canvasObject = new GameObject("Authored disabled canvas"); canvasObject.transform.SetParent(root.transform);
            var canvas = canvasObject.AddComponent<Canvas>(); canvas.enabled = false;
            try
            {
                using var world = new VisibilityWorld();
                using var target = new PresentationVisibilityTarget2D(root.transform, new Renderer[] { enabled, suppressed }, new[] { canvas });
                var handle = world.Register(target);
                world.Evaluate(new HiddenQuery());
                Assert.That(enabled.forceRenderingOff, Is.True);
                Assert.That(enabled.enabled, Is.True);
                Assert.That(root.activeSelf, Is.True);
                world.Unregister(handle);
                Assert.That(enabled.forceRenderingOff, Is.False);
                Assert.That(suppressed.forceRenderingOff, Is.True);
                Assert.That(canvas.enabled, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AssembliesKeepEngineFreeCoreAndNoConsumerOrPipelineDependency()
        {
            foreach (var reference in typeof(VisibilityWorld).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Does.Not.StartWith("Unity"));
            foreach (var assembly in new[] { typeof(VisibilityWorld).Assembly, typeof(SightPolygon2D).Assembly })
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    Assert.That(reference.Name, Does.Not.StartWith("POB"));
                    Assert.That(reference.Name, Does.Not.Contain("RenderPipelines"));
                }
        }

        private sealed class HiddenQuery : IFogVisibilityQuery { public bool IsVisible(FogPosition position) => false; }
    }
}
