using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    public sealed class TeleportLanding2DTests
    {
        private const int SolidLayer = 0;
        private static readonly Vector2 Origin = new(3000f, 3000f);
        private static readonly LayerMask Solids = 1 << SolidLayer;
        private readonly List<GameObject> objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in objects)
                if (item) Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void TryFind_AllowsAuthoredCrossFloorAndCanRequireLineOfSight()
        {
            var body = Box("Body", new Vector2(0f, 4f), Vector2.one);
            Box("Solid floor", Vector2.zero, new Vector2(30f, .5f));
            Vector2 requested = Origin + Vector2.down * 4f;
            Vector3 start = body.transform.position;
            var options = new TeleportLandingOptions2D(Solids);

            var allow = new TeleportLandingSearchPolicy(2f, terrainMask: Solids);
            Assert.That(TeleportLanding2D.TryFind(body.transform, body, requested, options, allow,
                out var destination, out _), Is.True);
            Assert.That(destination, Is.EqualTo(requested));

            var block = new TeleportLandingSearchPolicy(2f, terrainMask: Solids, requiresLineOfSight: true);
            Assert.That(TeleportLanding2D.TryFind(body.transform, body, requested, options, block,
                out _, out _), Is.False);
            Assert.That(body.transform.position, Is.EqualTo(start));
        }

        [Test]
        public void TryFind_DownwardFloorPolicyBlocksDescentAndAllowsAscent()
        {
            var body = Box("Body", Vector2.up * 4f, Vector2.one);
            Box("Solid floor", Vector2.zero, new Vector2(30f, .5f));
            var options = new TeleportLandingOptions2D(Solids);
            var policy = new TeleportLandingSearchPolicy(1f, downwardFloorMask: Solids);

            Assert.That(TeleportLanding2D.TryFind(body.transform, body, Origin + Vector2.down * 4f,
                options, policy, out _, out _), Is.False);

            body.transform.position = Origin + Vector2.down * 4f;
            Vector2 target = Origin + Vector2.up * 4f;
            Assert.That(TeleportLanding2D.TryFind(body.transform, body, target, options, policy,
                out var landing, out _), Is.True);
            Assert.That(landing, Is.EqualTo(target));
        }

        [Test]
        public void TryFind_AdjustsOnlyOnTheRequestedSideOfTheFloor()
        {
            var body = Box("Body", Vector2.up * 5f, new Vector2(1f, 2f));
            var floor = Box("Solid floor", Vector2.zero, new Vector2(30f, .2f));
            var ceiling = Box("Low ceiling", Vector2.up * 2.4f, new Vector2(30f, 3f));
            var options = new TeleportLandingOptions2D(Solids);
            var policy = new TeleportLandingSearchPolicy(2f, terrainMask: Solids);
            Vector2 requested = Origin + Vector2.up * .45f;
            Vector3 start = body.transform.position;

            Assert.That(TeleportLanding2D.TryFind(body.transform, body, requested, options, policy,
                out _, out _), Is.False);

            ceiling.enabled = false;
            Assert.That(TeleportLanding2D.TryFind(body.transform, body, requested, options, policy,
                out var destination, out _), Is.True);
            Assert.That(destination.x, Is.EqualTo(requested.x).Within(.002f));
            Assert.That(destination.y - body.bounds.extents.y, Is.GreaterThan(floor.bounds.max.y));
            Assert.That(body.transform.position, Is.EqualTo(start));
        }

        [Test]
        public void TryFind_DoesNotTreatTheMovingBodyAsATerrainObstruction()
        {
            var body = Box("Body on the queried layer", Vector2.zero, Vector2.one);
            Vector2 requested = Origin + Vector2.right * 6f;
            var options = new TeleportLandingOptions2D(Solids);
            var policy = new TeleportLandingSearchPolicy(1f, terrainMask: Solids, requiresLineOfSight: true);

            Assert.That(TeleportLanding2D.TryFind(body.transform, body, requested, options, policy,
                out var destination, out _), Is.True);
            Assert.That(destination, Is.EqualTo(requested));
        }

        [Test]
        public void TrySet_RechecksTheLiveBodyAndPreservesPoseAndVelocityOnFailure()
        {
            var body = Box("Body", Vector2.left * 4f, Vector2.one);
            body.transform.position += Vector3.forward * 2f;
            var rigidbody = body.gameObject.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.linearVelocity = new Vector2(3f, -2f);
            var blocker = Box("Moved wall", Vector2.right * 8f, new Vector2(2f, 3f));
            var options = new TeleportLandingOptions2D(Solids);
            Vector3 start = body.transform.position;
            Vector2 velocity = rigidbody.linearVelocity;

            Assert.That(TeleportLanding2D.TryFind(body.transform, body, Origin, options,
                new TeleportLandingSearchPolicy(1f), out var destination, out _), Is.True);
            blocker.transform.position = Origin;
            Physics2D.SyncTransforms();

            Assert.That(TeleportLanding2D.TrySet(body.transform, body, destination, options, out var reason), Is.False);
            Assert.That(reason, Does.StartWith("blocked_by:"));
            Assert.That(body.transform.position, Is.EqualTo(start));
            Assert.That(rigidbody.linearVelocity, Is.EqualTo(velocity));

            blocker.enabled = false;
            Assert.That(TeleportLanding2D.TrySet(body.transform, body, destination, options, out reason), Is.True, reason);
            Assert.That((Vector2)body.transform.position, Is.EqualTo(destination));
            Assert.That(body.transform.position.z, Is.EqualTo(start.z));
        }

        [UnityTest]
        public IEnumerator TryFind_UsesTheBodyPhysicsSceneForItsProbeAndObstacleQueries()
        {
            yield return new EnterPlayMode();
            var previous = SceneManager.GetActiveScene();
            Scene isolated = SceneManager.CreateScene("TeleportLanding2D_" + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            AsyncOperation closing = null;
            try
            {
                var body = Box("Isolated body", Vector2.up * 3f, Vector2.one);
                var floor = Box("Isolated floor", Vector2.down, new Vector2(30f, 2f));
                var unrelated = Box("Blocker in the original scene", Vector2.zero, new Vector2(20f, 20f));
                SceneManager.MoveGameObjectToScene(body.gameObject, isolated);
                SceneManager.MoveGameObjectToScene(floor.gameObject, isolated);
                SceneManager.MoveGameObjectToScene(unrelated.gameObject, previous);
                var options = new TeleportLandingOptions2D(Solids);
                Vector2 requested = Origin + Vector2.up * .1f;

                Assert.That(TeleportLanding2D.TryFind(body.transform, body, requested, options,
                    new TeleportLandingSearchPolicy(2f, terrainMask: Solids), out var destination, out _), Is.True);
                Assert.That(destination.x, Is.EqualTo(requested.x).Within(.002f));
                Assert.That(destination.y, Is.GreaterThan(requested.y));
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                closing = SceneManager.UnloadSceneAsync(isolated);
            }

            if (closing != null) yield return closing;
            yield return new ExitPlayMode();
        }

        private BoxCollider2D Box(string name, Vector2 offset, Vector2 size)
        {
            var item = new GameObject(name) { layer = SolidLayer };
            objects.Add(item);
            item.transform.position = Origin + offset;
            var collider = item.AddComponent<BoxCollider2D>();
            collider.size = size;
            return collider;
        }
    }
}
