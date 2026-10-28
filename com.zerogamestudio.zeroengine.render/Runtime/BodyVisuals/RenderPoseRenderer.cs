using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render
{
    /// <summary>Pure presentation. The caller retains the displayed snapshot until Hide or the next Apply.</summary>
    public sealed class RenderPoseRenderer : IDisposable
    {
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int RendererColor = Shader.PropertyToID("_RendererColor");
        private static readonly int Flip = Shader.PropertyToID("_Flip");
        private readonly Transform parent;
        private readonly SpriteRenderer style;
        private readonly bool originalStyleEnabled;
        private readonly Vector3 offset;
        private readonly List<LayerView> views = new();
        private readonly MaterialPropertyBlock properties = new();
        private bool disposed;

        private sealed class LayerView
        {
            public Renderer Renderer;
            public MeshFilter Filter;
            public Material[] Materials;
        }

        public RenderPoseRenderer(Transform parent, SpriteRenderer style, Vector3 localOffset = default)
        {
            this.parent = parent ? parent : throw new ArgumentNullException(nameof(parent));
            this.style = style ? style : throw new ArgumentNullException(nameof(style));
            originalStyleEnabled = style.enabled;
            offset = localOffset;
        }

        public void Apply(RenderPoseSnapshot snapshot)
        {
            if (disposed) throw new ObjectDisposedException(nameof(RenderPoseRenderer));
            Hide();
            if (!parent || !style || snapshot == null) return;
            style.enabled = false;
            for (int i = 0; i < snapshot.Layers.Count; i++)
            {
                var frame = snapshot.Layers[i];
                bool sprite = frame.Sprite;
                if (!sprite && !frame.Mesh) continue;
                while (views.Count <= i) views.Add(null);
                var view = views[i];
                if (view == null || !view.Renderer || (view.Renderer is SpriteRenderer) != sprite)
                {
                    DestroyView(view);
                    var layer = new GameObject("RenderPoseLayer");
                    layer.transform.SetParent(parent, false);
                    view = new LayerView();
                    if (sprite) view.Renderer = layer.AddComponent<SpriteRenderer>();
                    else
                    {
                        view.Filter = layer.AddComponent<MeshFilter>();
                        view.Renderer = layer.AddComponent<MeshRenderer>();
                    }
                    view.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                    view.Renderer.receiveShadows = false;
                    views[i] = view;
                }

                var target = view.Renderer;
                target.gameObject.layer = style.gameObject.layer;
                target.transform.localPosition = frame.Position + offset;
                target.transform.localRotation = frame.Rotation;
                target.transform.localScale = frame.Scale;
                target.sortingLayerID = style.sortingLayerID;
                target.sortingOrder = style.sortingOrder + frame.SortingOffset;
                if (target is SpriteRenderer spriteTarget)
                {
                    spriteTarget.sharedMaterial = style.sharedMaterial;
                    spriteTarget.color = style.color;
                    spriteTarget.maskInteraction = style.maskInteraction;
                    spriteTarget.sprite = frame.Sprite;
                    spriteTarget.flipX = frame.FlipX;
                    spriteTarget.flipY = frame.FlipY;
                    spriteTarget.drawMode = frame.DrawMode;
                    spriteTarget.size = frame.Size;
                }
                else ApplyMesh(view, frame);
                target.enabled = true;
            }
        }

        private void ApplyMesh(LayerView view, RenderPoseLayer frame)
        {
            view.Filter.sharedMesh = frame.Mesh;
            int count = frame.Textures.Count;
            if (view.Materials == null || view.Materials.Length != count) view.Materials = new Material[count];
            var material = style.sharedMaterial;
            for (int i = 0; i < count; i++) view.Materials[i] = material;
            view.Renderer.sharedMaterials = view.Materials;
            for (int i = 0; i < count; i++)
            {
                properties.Clear();
                var texture = frame.Textures[i] ? frame.Textures[i] : Texture2D.whiteTexture;
                properties.SetTexture(MainTex, texture);
                properties.SetTexture(BaseMap, texture);
                properties.SetVector(Flip, Vector4.one);
                properties.SetColor(RendererColor, Color.white);
                if (material && material.HasProperty(BaseColor))
                    properties.SetColor(BaseColor, material.GetColor(BaseColor) * style.color);
                else if (material && material.HasProperty(ColorProperty))
                    properties.SetColor(ColorProperty, material.GetColor(ColorProperty) * style.color);
                else properties.SetColor(RendererColor, style.color);
                view.Renderer.SetPropertyBlock(properties, i);
            }
        }

        /// <summary>Unbinds borrowed geometry before a history entry is recycled and restores the template.</summary>
        public void Hide()
        {
            foreach (var view in views)
            {
                if (view == null || !view.Renderer) continue;
                view.Renderer.enabled = false;
                if (view.Filter) view.Filter.sharedMesh = null;
                if (view.Renderer is SpriteRenderer sprite) sprite.sprite = null;
            }
            if (style) style.enabled = originalStyleEnabled;
        }

        public void Dispose()
        {
            if (disposed) return;
            Hide();
            foreach (var view in views) DestroyView(view);
            views.Clear();
            disposed = true;
        }

        private static void DestroyView(LayerView view)
        {
            if (view == null || !view.Renderer) return;
            view.Renderer.enabled = false;
            if (view.Filter) view.Filter.sharedMesh = null;
            if (Application.isPlaying) UnityEngine.Object.Destroy(view.Renderer.gameObject);
            else UnityEngine.Object.DestroyImmediate(view.Renderer.gameObject);
        }
    }
}
