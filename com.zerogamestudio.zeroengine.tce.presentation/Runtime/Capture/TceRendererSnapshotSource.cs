using UnityEngine;

namespace ZeroEngine.TCE.Presentation
{
    public sealed class TceRendererSnapshotSource : ITcePresentationSource
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private readonly GameObject target;

        public TceRendererSnapshotSource(GameObject target)
        {
            this.target = target;
        }

        public bool TryCaptureSnapshot(TceVisualSnapshotRequest request, out TceVisualSnapshot snapshot)
        {
            snapshot = null;
            if (!target)
                return false;

            if (target.TryGetComponent<SkinnedMeshRenderer>(out var skinnedRenderer))
                return TryCaptureSkinnedMesh(skinnedRenderer, request, out snapshot);

            if (target.TryGetComponent<MeshFilter>(out var filter) &&
                target.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                return TryCaptureMesh(filter, meshRenderer, request, out snapshot);
            }

            if (target.TryGetComponent<SpriteRenderer>(out var spriteRenderer))
                return TryCaptureSprite(spriteRenderer, request, out snapshot);

            return false;
        }

        private static bool TryCaptureSkinnedMesh(SkinnedMeshRenderer renderer, TceVisualSnapshotRequest request, out TceVisualSnapshot snapshot)
        {
            snapshot = null;
            if (!renderer || !renderer.sharedMesh)
                return false;

            var mesh = new Mesh { name = "TceSkinnedSnapshotMesh" };
            renderer.BakeMesh(mesh);
            mesh.MarkDynamic();
            snapshot = CreateMeshSnapshot(mesh, renderer, renderer.transform.localToWorldMatrix, renderer.gameObject.layer, request, ownsMesh: true);
            return true;
        }

        private static bool TryCaptureMesh(MeshFilter filter, MeshRenderer renderer, TceVisualSnapshotRequest request, out TceVisualSnapshot snapshot)
        {
            snapshot = null;
            var source = filter ? filter.sharedMesh : null;
            if (!source || !source.isReadable)
                return false;

            var mesh = new Mesh { name = "TceMeshSnapshot" };
            CopyMesh(source, mesh);
            mesh.MarkDynamic();
            snapshot = CreateMeshSnapshot(mesh, renderer, renderer.transform.localToWorldMatrix, renderer.gameObject.layer, request, ownsMesh: true);
            return true;
        }

        private static bool TryCaptureSprite(SpriteRenderer renderer, TceVisualSnapshotRequest request, out TceVisualSnapshot snapshot)
        {
            snapshot = null;
            if (!renderer || !renderer.sprite)
                return false;

            snapshot = new TceSpriteSnapshot(
                ApplyOffset(renderer.transform.localToWorldMatrix, request.Offset),
                renderer.gameObject.layer,
                renderer.sprite,
                renderer.sortingLayerID,
                renderer.sortingOrder);
            return true;
        }

        private static TceMeshSnapshot CreateMeshSnapshot(
            Mesh mesh,
            Renderer renderer,
            Matrix4x4 matrix,
            int layer,
            TceVisualSnapshotRequest request,
            bool ownsMesh)
        {
            matrix = ApplyOffset(matrix, request.Offset);
            var snapshot = new TceMeshSnapshot(matrix, layer, mesh, ownsMesh);
            var material = renderer ? renderer.sharedMaterial : null;
            if (request.CopyMainTexture && material && material.HasProperty(MainTexId))
                snapshot.MainTexture = material.GetTexture(MainTexId);
            return snapshot;
        }

        private static Matrix4x4 ApplyOffset(Matrix4x4 matrix, Vector3 offset)
        {
            if (offset == Vector3.zero)
                return matrix;

            Vector4 column = matrix.GetColumn(3);
            column.x += offset.x;
            column.y += offset.y;
            column.z += offset.z;
            matrix.SetColumn(3, column);
            return matrix;
        }

        private static void CopyMesh(Mesh source, Mesh target)
        {
            target.Clear();
            target.indexFormat = source.indexFormat;
            target.vertices = source.vertices;
            target.subMeshCount = source.subMeshCount;
            for (int i = 0; i < source.subMeshCount; i++)
                target.SetTriangles(source.GetTriangles(i), i);

            if (source.uv is { Length: > 0 }) target.uv = source.uv;
            if (source.normals is { Length: > 0 }) target.normals = source.normals;
            if (source.tangents is { Length: > 0 }) target.tangents = source.tangents;
            if (source.colors is { Length: > 0 }) target.colors = source.colors;
            if (source.colors32 is { Length: > 0 }) target.colors32 = source.colors32;

            target.RecalculateBounds();
        }
    }
}
