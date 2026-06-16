using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.TCE;

namespace ZeroEngine.TCE.Presentation.Tests.Editor
{
    [TestFixture]
    public sealed class TcePresentationBudgetTests
    {
        [Test]
        public void Runner_DestroysOwnedMeshesAfterDuration()
        {
            var runnerObject = new GameObject(nameof(Runner_DestroysOwnedMeshesAfterDuration));
            var meshes = new List<Mesh>();

            try
            {
                var clock = new FakeClock(0f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();

                for (int i = 0; i < 64; i++)
                {
                    var mesh = CreateTriangleMesh($"BudgetSnapshotMesh_{i}");
                    meshes.Add(mesh);
                    runner.Play(
                        new TceMeshSnapshot(Matrix4x4.Translate(new Vector3(i, 0f, 0f)), 0, mesh, ownsMesh: true),
                        new TcePresentationPlaybackSettings { Duration = 1f },
                        clock);
                }

                clock.Now = 1.1f;
                InvokeLateUpdate(runner);

                foreach (Mesh mesh in meshes)
                    Assert.IsTrue(mesh == null);
            }
            finally
            {
                foreach (Mesh mesh in meshes)
                {
                    if (mesh)
                        Object.DestroyImmediate(mesh);
                }

                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_CleansTemporarySpriteLayersAfterDurationAndExplicitDispose()
        {
            var runnerObject = new GameObject(nameof(Runner_CleansTemporarySpriteLayersAfterDurationAndExplicitDispose));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);

            try
            {
                var clock = new FakeClock(0f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                int before = CountSnapshotSpriteRenderers();

                runner.Play(CreateLayerSnapshot(sprite, Vector3.zero), new TcePresentationPlaybackSettings { Duration = 1f }, clock);
                TcePresentationHandle handle = runner.Play(CreateLayerSnapshot(sprite, Vector3.right), new TcePresentationPlaybackSettings { Duration = 1f }, clock);

                Assert.AreEqual(before + 4, CountSnapshotSpriteRenderers());

                handle.Dispose();
                Assert.AreEqual(before + 2, CountSnapshotSpriteRenderers());

                clock.Now = 1.1f;
                InvokeLateUpdate(runner);

                Assert.AreEqual(before, CountSnapshotSpriteRenderers());
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_OnDestroy_CleansActiveSnapshotsAndAllowsHandleDispose()
        {
            var runnerObject = new GameObject(nameof(Runner_OnDestroy_CleansActiveSnapshotsAndAllowsHandleDispose));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            var mesh = CreateTriangleMesh("RunnerDestroySnapshotMesh");

            try
            {
                var clock = new FakeClock(0f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                int before = CountSnapshotSpriteRenderers();

                runner.Play(
                    new TceMeshSnapshot(Matrix4x4.identity, 0, mesh, ownsMesh: true),
                    new TcePresentationPlaybackSettings { Duration = 10f },
                    clock);
                TcePresentationHandle handle = runner.Play(
                    CreateLayerSnapshot(sprite, Vector3.zero),
                    new TcePresentationPlaybackSettings { Duration = 10f },
                    clock);

                Assert.AreEqual(before + 2, CountSnapshotSpriteRenderers());

                InvokeOnDestroy(runner);
                Object.DestroyImmediate(runnerObject);

                Assert.IsTrue(mesh == null);
                Assert.AreEqual(before, CountSnapshotSpriteRenderers());
                Assert.DoesNotThrow(() => handle.Dispose());
            }
            finally
            {
                if (mesh)
                    Object.DestroyImmediate(mesh);

                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);

                if (runnerObject)
                    Object.DestroyImmediate(runnerObject);
            }
        }

        private static TceSpriteLayerSnapshot CreateLayerSnapshot(Sprite sprite, Vector3 position)
        {
            return new TceSpriteLayerSnapshot(
                Matrix4x4.Translate(position),
                0,
                new[]
                {
                    new TceSpriteLayerFrame(sprite, true, 0, 10, Vector3.zero, Quaternion.identity, Vector3.one),
                    new TceSpriteLayerFrame(sprite, true, 0, 11, Vector3.right * 0.1f, Quaternion.identity, Vector3.one)
                });
        }

        private static int CountSnapshotSpriteRenderers()
        {
            int count = 0;
            foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer.transform.root.name == "Tce Sprite Layer Snapshot")
                    count++;
            }

            return count;
        }

        private static void InvokeOnDestroy(TcePresentationRunner runner)
        {
            typeof(TcePresentationRunner)
                .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(runner, null);
        }

        private static void InvokeLateUpdate(TcePresentationRunner runner)
        {
            typeof(TcePresentationRunner)
                .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(runner, null);
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
