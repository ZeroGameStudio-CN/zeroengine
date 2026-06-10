using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using ZeroEngine.TCE.Presentation;

namespace ZeroEngine.TCE.Presentation.Tests.Editor
{
    [TestFixture]
    public sealed class TceRendererSnapshotSourceTests
    {
        [Test]
        public void MeshSnapshot_Dispose_DestroysOwnedMesh()
        {
            var mesh = new Mesh { name = "OwnedSnapshotMesh" };
            var snapshot = new TceMeshSnapshot(Matrix4x4.identity, layer: 3, mesh, ownsMesh: true);

            Assert.AreSame(mesh, snapshot.Mesh);
            Assert.AreEqual(Matrix4x4.identity, snapshot.Matrix);
            Assert.AreEqual(3, snapshot.Layer);

            snapshot.Dispose();

            Assert.IsTrue(mesh == null);
        }

        [Test]
        public void RendererSource_ReadableMesh_CopiesMeshDataWithoutSharing()
        {
            var sourceObject = new GameObject(nameof(RendererSource_ReadableMesh_CopiesMeshDataWithoutSharing));
            var sourceMesh = CreateTriangleMesh("ReadableSourceMesh");

            try
            {
                sourceMesh.indexFormat = IndexFormat.UInt32;
                sourceMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                sourceMesh.colors32 = new[]
                {
                    new Color32(255, 0, 0, 255),
                    new Color32(0, 255, 0, 255),
                    new Color32(0, 0, 255, 255)
                };

                sourceObject.AddComponent<MeshRenderer>();
                sourceObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh;

                var source = new TceRendererSnapshotSource(sourceObject);
                bool captured = source.TryCaptureSnapshot(new TceVisualSnapshotRequest(new Vector3(1f, 2f, 3f), Vector3.right, true), out var snapshot);

                Assert.IsTrue(captured);
                var meshSnapshot = snapshot as TceMeshSnapshot;
                Assert.IsNotNull(meshSnapshot);
                Assert.AreNotSame(sourceMesh, meshSnapshot.Mesh);
                Assert.AreEqual(IndexFormat.UInt32, meshSnapshot.Mesh.indexFormat);
                CollectionAssert.AreEqual(sourceMesh.GetTriangles(0), meshSnapshot.Mesh.GetTriangles(0));
                Assert.AreEqual(Vector3.right, meshSnapshot.Mesh.vertices[1]);
                Assert.That(meshSnapshot.Mesh.uv, Has.Length.EqualTo(3));
                Assert.That(meshSnapshot.Mesh.colors32, Has.Length.EqualTo(3));
                Assert.AreEqual(new Vector3(1f, 2f, 3f), (Vector3)meshSnapshot.Matrix.GetColumn(3));

                sourceMesh.vertices = new[] { Vector3.zero, Vector3.left * 9f, Vector3.down * 9f };
                Assert.AreEqual(Vector3.right, meshSnapshot.Mesh.vertices[1]);

                snapshot.Dispose();
            }
            finally
            {
                if (sourceMesh) Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(sourceObject);
            }
        }

        [Test]
        public void RendererSource_UnreadableMesh_ReturnsFalseWithoutThrowing()
        {
            var sourceObject = new GameObject(nameof(RendererSource_UnreadableMesh_ReturnsFalseWithoutThrowing));
            var sourceMesh = CreateTriangleMesh("UnreadableSourceMesh");

            try
            {
                sourceMesh.UploadMeshData(true);
                Assert.IsFalse(sourceMesh.isReadable);

                sourceObject.AddComponent<MeshRenderer>();
                sourceObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh;

                var source = new TceRendererSnapshotSource(sourceObject);

                Assert.DoesNotThrow(() =>
                {
                    bool captured = source.TryCaptureSnapshot(new TceVisualSnapshotRequest(Vector3.zero, Vector3.right, true), out var snapshot);
                    Assert.IsFalse(captured);
                    Assert.IsNull(snapshot);
                });
            }
            finally
            {
                if (sourceMesh) Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(sourceObject);
            }
        }

        [Test]
        public void RendererSource_SpriteRenderer_CapturesSpriteAndSorting()
        {
            var sourceObject = new GameObject(nameof(RendererSource_SpriteRenderer_CapturesSpriteAndSorting));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);

            try
            {
                var renderer = sourceObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = 17;

                var source = new TceRendererSnapshotSource(sourceObject);
                bool captured = source.TryCaptureSnapshot(new TceVisualSnapshotRequest(Vector3.zero, Vector3.right, true), out var snapshot);

                Assert.IsTrue(captured);
                var spriteSnapshot = snapshot as TceSpriteSnapshot;
                Assert.IsNotNull(spriteSnapshot);
                Assert.AreSame(sprite, spriteSnapshot.Sprite);
                Assert.AreEqual(renderer.sortingLayerID, spriteSnapshot.SortingLayerId);
                Assert.AreEqual(17, spriteSnapshot.SortingOrder);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(sourceObject);
            }
        }

        [Test]
        public void RendererSource_SpriteRenderer_AppliesRequestOffset()
        {
            var sourceObject = new GameObject(nameof(RendererSource_SpriteRenderer_AppliesRequestOffset));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);

            try
            {
                sourceObject.transform.position = new Vector3(1f, 2f, 3f);
                var renderer = sourceObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;

                var source = new TceRendererSnapshotSource(sourceObject);
                bool captured = source.TryCaptureSnapshot(new TceVisualSnapshotRequest(new Vector3(4f, 5f, 6f), Vector3.right, true), out var snapshot);

                Assert.IsTrue(captured);
                var spriteSnapshot = snapshot as TceSpriteSnapshot;
                Assert.IsNotNull(spriteSnapshot);
                Assert.AreEqual(new Vector3(5f, 7f, 9f), (Vector3)spriteSnapshot.Matrix.GetColumn(3));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(sourceObject);
            }
        }

        [Test]
        public void MeshSnapshotDispose_UsesRuntimeSafeDestroyBranch()
        {
            string source = System.IO.File.ReadAllText("Packages/com.zerogamestudio.zeroengine.tce.presentation/Runtime/Capture/TceVisualSnapshot.cs");

            StringAssert.Contains("Application.isPlaying", source);
            StringAssert.Contains("Object.Destroy(", source);
            StringAssert.Contains("Object.DestroyImmediate(", source);
        }

        [Test]
        public void RendererSource_SkinnedMeshRenderer_BakesOwnedMesh()
        {
            var sourceObject = new GameObject(nameof(RendererSource_SkinnedMeshRenderer_BakesOwnedMesh));
            var sourceMesh = CreateTriangleMesh("SkinnedSourceMesh");

            try
            {
                sourceObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = sourceMesh;

                var source = new TceRendererSnapshotSource(sourceObject);
                bool captured = source.TryCaptureSnapshot(new TceVisualSnapshotRequest(Vector3.zero, Vector3.right, true), out var snapshot);

                Assert.IsTrue(captured);
                var meshSnapshot = snapshot as TceMeshSnapshot;
                Assert.IsNotNull(meshSnapshot);
                Assert.IsNotNull(meshSnapshot.Mesh);
                Assert.AreNotSame(sourceMesh, meshSnapshot.Mesh);

                Mesh bakedMesh = meshSnapshot.Mesh;
                snapshot.Dispose();
                Assert.IsTrue(bakedMesh == null);
            }
            finally
            {
                if (sourceMesh) Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(sourceObject);
            }
        }

        private static Mesh CreateTriangleMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
