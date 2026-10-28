using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Render
{
    /// <summary>Caller-timed visual values. Owns copied meshes; borrowed sprites/textures are never destroyed.</summary>
    public sealed class RenderPoseSnapshot : IDisposable
    {
        private readonly List<RenderPoseLayer> layers = new();
        private int sortingOrigin;
        public IReadOnlyList<RenderPoseLayer> Layers => layers;

        public void Clear(int sortingOrderOrigin = 0)
        {
            foreach (var layer in layers) DestroyOwnedMesh(layer.Mesh);
            layers.Clear();
            sortingOrigin = sortingOrderOrigin;
        }

        public bool TryCapture(Renderer source, Transform origin, Sprite spriteOverride = null)
        {
            if (!source || !origin) return false;
            if (source is not SpriteRenderer && source is not MeshRenderer && source is not SkinnedMeshRenderer)
                return false;
            if (!source.enabled || !source.gameObject.activeInHierarchy) return true;
            if (source is SpriteRenderer spriteRenderer)
            {
                var sprite = spriteOverride ? spriteOverride : spriteRenderer.sprite;
                if (sprite) layers.Add(new RenderPoseLayer(spriteRenderer, origin, sprite, sortingOrigin));
                return true;
            }

            Mesh copy;
            if (source is SkinnedMeshRenderer skinned)
            {
                if (!skinned.sharedMesh) return true;
                copy = new Mesh { name = "ShadowVisualPose" };
                try { skinned.BakeMesh(copy); }
                catch (UnityException) { DestroyOwnedMesh(copy); return false; }
            }
            else
            {
                var filter = source.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) return true;
                // Spine mutates/reuses its live meshes. Every history sample needs independent geometry.
                copy = UnityEngine.Object.Instantiate(filter.sharedMesh);
                copy.name = "ShadowVisualPose";
            }
            layers.Add(new RenderPoseLayer(source, origin, copy, sortingOrigin));
            return true;
        }

        public void Dispose() => Clear();

        internal static void DestroyOwnedMesh(Mesh mesh)
        {
            if (!mesh) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    /// <summary>No live Renderer, Animator, skeleton or gameplay object is retained.</summary>
    public readonly struct RenderPoseLayer
    {
        public Sprite Sprite { get; }
        public Mesh Mesh { get; }
        public IReadOnlyList<Texture> Textures { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public bool FlipX { get; }
        public bool FlipY { get; }
        public int SortingOffset { get; }
        public SpriteDrawMode DrawMode { get; }
        public Vector2 Size { get; }

        private RenderPoseLayer(Renderer source, Transform origin, int sortingOrigin) : this()
        {
            Position = origin.InverseTransformPoint(source.transform.position);
            var relative = origin.worldToLocalMatrix * source.transform.localToWorldMatrix;
            Vector3 x = relative.GetColumn(0);
            Vector3 y = relative.GetColumn(1);
            Vector3 z = relative.GetColumn(2);
            // Quaternion subtraction loses reflected ancestry. Reconstruct the signed relative basis.
            Rotation = y.sqrMagnitude > 0f && z.sqrMagnitude > 0f
                ? Quaternion.LookRotation(z, y) : Quaternion.identity;
            Scale = new Vector3(Vector3.Dot(x, Rotation * Vector3.right), y.magnitude, z.magnitude);
            SortingOffset = source.sortingOrder - sortingOrigin;
        }

        internal RenderPoseLayer(SpriteRenderer source, Transform origin, Sprite sprite, int sortingOrigin)
            : this(source, origin, sortingOrigin)
        {
            Sprite = sprite;
            FlipX = source.flipX;
            FlipY = source.flipY;
            DrawMode = source.drawMode;
            Size = source.size;
        }

        internal RenderPoseLayer(Renderer source, Transform origin, Mesh mesh, int sortingOrigin)
            : this(source, origin, sortingOrigin)
        {
            Mesh = mesh;
            var materials = source.sharedMaterials;
            var textures = new Texture[Mathf.Max(1, Mathf.Max(mesh.subMeshCount, materials.Length))];
            for (int i = 0; i < textures.Length; i++)
            {
                var material = materials.Length > 0 ? materials[Mathf.Min(i, materials.Length - 1)] : null;
                textures[i] = material && material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") :
                    material && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            }
            Textures = textures;
        }
    }
}
