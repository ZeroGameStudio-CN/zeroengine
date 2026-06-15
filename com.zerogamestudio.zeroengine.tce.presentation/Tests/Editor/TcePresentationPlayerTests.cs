using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.TCE;
using ZeroEngine.TCE.Presentation;

namespace ZeroEngine.TCE.Presentation.Tests.Editor
{
    [TestFixture]
    public sealed class TcePresentationPlayerTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce.presentation";

        [Test]
        public void Play_NullSnapshot_ReturnsDisposedHandle()
        {
            var runnerObject = new GameObject(nameof(Play_NullSnapshot_ReturnsDisposedHandle));

            try
            {
                var runner = runnerObject.AddComponent<TcePresentationRunner>();

                var handle = runner.Play(null, new TcePresentationPlaybackSettings(), new FakeClock(0f));

                Assert.IsTrue(handle.IsDisposed);
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Play_MeshSnapshot_DisposeCleansSnapshot()
        {
            var runnerObject = new GameObject(nameof(Play_MeshSnapshot_DisposeCleansSnapshot));
            var mesh = CreateTriangleMesh("PlayableSnapshotMesh");

            try
            {
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceMeshSnapshot(Matrix4x4.identity, 0, mesh, ownsMesh: true);

                var handle = runner.Play(snapshot, new TcePresentationPlaybackSettings { Duration = 1f }, new FakeClock(0f));
                handle.Dispose();

                Assert.IsTrue(mesh == null);
            }
            finally
            {
                if (mesh) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Play_SpriteLayerSnapshot_CreatesTemporaryLayersAndCleansSnapshot()
        {
            var runnerObject = new GameObject(nameof(Play_SpriteLayerSnapshot_CreatesTemporaryLayersAndCleansSnapshot));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);

            try
            {
                int before = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteLayerSnapshot(
                    Matrix4x4.identity,
                    0,
                    new[]
                    {
                        new TceSpriteLayerFrame(sprite, true, 0, 12, Vector3.zero, Quaternion.identity, Vector3.one)
                    });

                var handle = runner.Play(snapshot, new TcePresentationPlaybackSettings { Duration = 1f }, new FakeClock(0f));
                int during = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                Assert.AreEqual(before + 1, during);

                handle.Dispose();

                int after = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                Assert.AreEqual(before, after);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_UsesClockDeltaWithoutDotweenDependency()
        {
            foreach (string file in Directory.GetFiles($"{PackagePath}/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                StringAssert.DoesNotContain("DG.Tweening", source, file);
                StringAssert.DoesNotContain("DOTween", source, file);
            }

            var runnerObject = new GameObject(nameof(Runner_UsesClockDeltaWithoutDotweenDependency));
            var mesh = CreateTriangleMesh("ClockDrivenSnapshotMesh");

            try
            {
                var clock = new FakeClock(10f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                runner.Play(new TceMeshSnapshot(Matrix4x4.identity, 0, mesh, ownsMesh: true), new TcePresentationPlaybackSettings { Duration = 1f }, clock);

                InvokeLateUpdate(runner);
                Assert.IsFalse(mesh == null);

                clock.Now = 11.1f;
                InvokeLateUpdate(runner);
                Assert.IsTrue(mesh == null);
            }
            finally
            {
                if (mesh) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SoulGhostSpriteSnapshot_MovesAlongDirection()
        {
            var runnerObject = new GameObject(nameof(Runner_SoulGhostSpriteSnapshot_MovesAlongDirection));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);

            try
            {
                var clock = new FakeClock(0f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 0);
                var handle = runner.Play(
                    snapshot,
                    new TcePresentationPlaybackSettings
                    {
                        Style = TcePresentationStyle.SoulGhost,
                        Direction = new Vector3(2f, 0f, 0f),
                        Duration = 1f
                    },
                    clock);

                Transform root = FindTransform("Tce Sprite Snapshot");
                Assert.IsNotNull(root);
                Assert.AreEqual(Vector3.zero, root.position);

                clock.Now = 0.5f;
                InvokeLateUpdate(runner);

                Assert.AreEqual(new Vector3(1f, 0f, 0f), root.position);
                handle.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        private static void InvokeLateUpdate(TcePresentationRunner runner)
        {
            typeof(TcePresentationRunner)
                .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(runner, null);
        }

        private static Transform FindTransform(string name)
        {
            foreach (Transform transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (transform.name == name)
                    return transform;
            }

            return null;
        }

        private static Mesh CreateTriangleMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class FakeClock : ITceClock
        {
            public FakeClock(float now)
            {
                Now = now;
            }

            public float Now { get; set; }
        }
    }
}
