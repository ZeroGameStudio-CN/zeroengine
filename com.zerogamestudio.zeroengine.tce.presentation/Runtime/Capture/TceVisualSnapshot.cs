using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.TCE.Presentation
{
    public abstract class TceVisualSnapshot : IDisposable
    {
        protected TceVisualSnapshot(Matrix4x4 matrix, int layer)
        {
            Matrix = matrix;
            Layer = layer;
        }

        public Matrix4x4 Matrix { get; }
        public int Layer { get; }

        public virtual void Dispose()
        {
        }
    }

    public sealed class TceMeshSnapshot : TceVisualSnapshot
    {
        private readonly bool ownsMesh;

        public TceMeshSnapshot(Matrix4x4 matrix, int layer, Mesh mesh, bool ownsMesh)
            : base(matrix, layer)
        {
            Mesh = mesh;
            this.ownsMesh = ownsMesh;
        }

        public Mesh Mesh { get; private set; }
        public Texture MainTexture { get; set; }

        public override void Dispose()
        {
            if (ownsMesh && Mesh)
                DestroyOwnedObject(Mesh);

            Mesh = null;
            MainTexture = null;
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (!target)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }

    public sealed class TceSpriteSnapshot : TceVisualSnapshot
    {
        public TceSpriteSnapshot(Matrix4x4 matrix, int layer, Sprite sprite, int sortingLayerId, int sortingOrder)
            : base(matrix, layer)
        {
            Sprite = sprite;
            SortingLayerId = sortingLayerId;
            SortingOrder = sortingOrder;
        }

        public Sprite Sprite { get; }
        public int SortingLayerId { get; }
        public int SortingOrder { get; }
    }

    public readonly struct TceSpriteLayerFrame
    {
        public TceSpriteLayerFrame(
            Sprite sprite,
            bool enabled,
            int sortingLayerId,
            int sortingOrder,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            Sprite = sprite;
            Enabled = enabled && sprite;
            SortingLayerId = sortingLayerId;
            SortingOrder = sortingOrder;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            LocalScale = localScale == Vector3.zero ? Vector3.one : localScale;
        }

        public Sprite Sprite { get; }
        public bool Enabled { get; }
        public int SortingLayerId { get; }
        public int SortingOrder { get; }
        public Vector3 LocalPosition { get; }
        public Quaternion LocalRotation { get; }
        public Vector3 LocalScale { get; }
    }

    public sealed class TceSpriteLayerSnapshot : TceVisualSnapshot
    {
        public TceSpriteLayerSnapshot(Matrix4x4 matrix, int layer, IReadOnlyList<TceSpriteLayerFrame> layers)
            : base(matrix, layer)
        {
            Layers = layers ?? Array.Empty<TceSpriteLayerFrame>();
        }

        public IReadOnlyList<TceSpriteLayerFrame> Layers { get; }
    }
}
