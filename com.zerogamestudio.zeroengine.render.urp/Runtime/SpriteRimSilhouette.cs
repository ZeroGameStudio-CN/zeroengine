using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render.Urp
{
    /// <summary>One art-grid texel per mask cell. Draws the posed renderer directly, including all submeshes.</summary>
    internal sealed class SpriteRimSilhouette : IDisposable
    {
        private static readonly int CaptureMatrix = Shader.PropertyToID("_RimCaptureMatrix");
        private static readonly int CaptureTexture = Shader.PropertyToID("_RimCaptureTexture");
        private static readonly int CaptureST = Shader.PropertyToID("_RimCaptureST");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
        private readonly Renderer renderer;
        private readonly Material material;
        private readonly CommandBuffer commands;
        private readonly MaterialPropertyBlock sourceProperties = new();
        private readonly MaterialPropertyBlock submeshProperties = new();
        private readonly List<Material> sourceMaterials = new();
        private Texture measuredTexture;
        private Vector2 pixelStep;
        private Vector2Int measuredMultiplier;
        private Vector2 measuredOverride;
        private float measuredPpu;
        private Vector2 measuredSTScale;
        private RenderTexture mask;
        public UnityEngine.Rendering.RTHandle ReferenceHandle { get; private set; }
        public Vector4 ReferenceFrame { get; private set; }
        public int SourceMaterialCount { get; private set; }
        internal bool SupportsDepthComposite
        {
            get
            {
                if (SourceMaterialCount == 0) return false;
                for (int i = 0; i < SourceMaterialCount; i++)
                    if (!sourceMaterials[i] || sourceMaterials[i].renderQueue > (int)RenderQueue.GeometryLast) return false;
                return true;
            }
        }
        internal Texture GetSourceTexture(int index, out Vector4 st) => GetSource(sourceMaterials[index], index, out st);
        public RenderTexture Texture => mask;
        public Vector2 PixelStep => pixelStep;
        public Matrix4x4 WorldToMask { get; private set; }
        public Vector4 Info { get; private set; }

        public SpriteRimSilhouette(Renderer renderer)
        {
            this.renderer = renderer;
            var shader = Resources.Load<Shader>("ZeroEngine/SpriteRimSilhouette");
            if (!shader || !shader.isSupported) return;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            commands = new CommandBuffer { name = "PixelRimSilhouette" };
        }

        private Texture GetSource(Material source, int submesh, out Vector4 st)
        {
            sourceProperties.Clear(); renderer.GetPropertyBlock(sourceProperties);
            var texture = sourceProperties.GetTexture(MainTex);
            bool customSt = sourceProperties.HasVector(MainTexST);
            st = customSt ? sourceProperties.GetVector(MainTexST) : new Vector4(1, 1, 0, 0);
            if (!(renderer is SpriteRenderer))
            {
                submeshProperties.Clear(); renderer.GetPropertyBlock(submeshProperties, submesh);
                if (submeshProperties.HasTexture(MainTex)) texture = submeshProperties.GetTexture(MainTex);
                if (submeshProperties.HasVector(MainTexST)) { st = submeshProperties.GetVector(MainTexST); customSt = true; }
            }
            if (renderer is SpriteRenderer sprite && sprite.sprite) texture = sprite.sprite.texture;
            if (!texture && source && source.HasProperty(MainTex)) texture = source.GetTexture(MainTex);
            if (!customSt && source && source.HasProperty(MainTex))
            {
                Vector2 scale = source.GetTextureScale("_MainTex"), offset = source.GetTextureOffset("_MainTex");
                st = new Vector4(scale.x, scale.y, offset.x, offset.y);
            }
            return texture;
        }

        public bool Update(int width)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !material || commands == null) return false;
            sourceMaterials.Clear(); renderer.GetSharedMaterials(sourceMaterials);
            var materials = sourceMaterials;
            SourceMaterialCount = materials.Count;
            if (!SupportsDepthComposite) return false;
            if (materials.Count == 0) return false;
            var firstTexture = GetSource(materials[0], 0, out var firstSt);
            var metadata = renderer.GetComponent<SpriteRimPixelGrid>();
            if (!metadata) metadata = renderer.GetComponentInParent<SpriteRimPixelGrid>();
            var multiplier = metadata ? metadata.TextureTexelsPerArtPixel : Vector2Int.one;
            var explicitPpu = metadata ? metadata.PixelsPerUnitOverride : Vector2.zero;
            float spritePpu = renderer is SpriteRenderer sr && sr.sprite ? sr.sprite.pixelsPerUnit : 0f;
            if (pixelStep.x <= 0 || firstTexture != measuredTexture || multiplier != measuredMultiplier ||
                explicitPpu != measuredOverride || spritePpu != measuredPpu || measuredSTScale != new Vector2(firstSt.x,firstSt.y))
            {
                if (!SpriteRimPixelGrid.TryMeasure(renderer, firstTexture, firstSt, out pixelStep)) return false;
                measuredTexture = firstTexture; measuredMultiplier = multiplier; measuredOverride = explicitPpu; measuredPpu = spritePpu;
                measuredSTScale = new Vector2(firstSt.x,firstSt.y);
            }
            var bounds = renderer.localBounds;
            int padding = width + 1;
            Vector2 gridOrigin = renderer is SpriteRenderer aligned && aligned.sprite ? -aligned.sprite.pivot / aligned.sprite.pixelsPerUnit : Vector2.zero;
            if (metadata) gridOrigin += metadata.GridOriginLocal;
            Vector2 min = new Vector2(Mathf.Floor((bounds.min.x - gridOrigin.x) / pixelStep.x) - padding,
                Mathf.Floor((bounds.min.y - gridOrigin.y) / pixelStep.y) - padding);
            Vector2 max = new Vector2(Mathf.Ceil((bounds.max.x - gridOrigin.x) / pixelStep.x) + padding,
                Mathf.Ceil((bounds.max.y - gridOrigin.y) / pixelStep.y) + padding);
            int requiredWidth = Mathf.Max(1, Mathf.CeilToInt(max.x - min.x));
            int requiredHeight = Mathf.Max(1, Mathf.CeilToInt(max.y - min.y));
            // Never silently resample an oversized grid and change what "one pixel" means.
            if (requiredWidth > 2048 || requiredHeight > 2048) return false;
            int w = Mathf.NextPowerOfTwo(Mathf.Max(requiredWidth, mask ? mask.width : 16));
            int h = Mathf.NextPowerOfTwo(Mathf.Max(requiredHeight, mask ? mask.height : 16));
            if (!mask || mask.width != w || mask.height != h)
            {
                ReleaseMask();
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
                mask = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear)
                {
                    name = "PixelRimMask", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1, useMipMap = false
                };
                mask.Create();
                ReferenceHandle = UnityEngine.Rendering.RTHandles.Alloc(mask, transferOwnership: false);
            }
            Vector2 localMin = gridOrigin + Vector2.Scale(min, pixelStep);
            Vector2 size = new Vector2(w * pixelStep.x, h * pixelStep.y);
            WorldToMask = Matrix4x4.Scale(new Vector3(1f / size.x, 1f / size.y, 1f)) *
                Matrix4x4.Translate(new Vector3(-localMin.x, -localMin.y, 0)) * renderer.worldToLocalMatrix;
            Info = new Vector4(1f / w, 1f / h, 0, 0);
            ReferenceFrame = new Vector4(-localMin.x / size.x, -localMin.y / size.y, bounds.size.x / pixelStep.x, bounds.size.y / pixelStep.y);
            var projection = Matrix4x4.Ortho(localMin.x, localMin.x + size.x, localMin.y, localMin.y + size.y, -1000f, 1000f);
            commands.Clear(); commands.SetRenderTarget(mask); commands.ClearRenderTarget(false, true, Color.clear);
            commands.SetGlobalMatrix(CaptureMatrix, GL.GetGPUProjectionMatrix(projection, true) * renderer.worldToLocalMatrix);
            // SpriteRenderer supplies its flip through the native renderer matrix.
            var filter = renderer.GetComponent<MeshFilter>();
            int count = renderer is SpriteRenderer ? 1 : filter && filter.sharedMesh ? filter.sharedMesh.subMeshCount : materials.Count;
            SourceMaterialCount = Mathf.Min(materials.Count, count);
            for (int i = 0; i < Mathf.Min(materials.Count, count); i++)
            {
                var texture = GetSource(materials[i], i, out var st);
                if (!texture) continue;
                commands.SetGlobalTexture(CaptureTexture, texture); commands.SetGlobalVector(CaptureST, st);
                commands.DrawRenderer(renderer, material, i, 0);
            }
            var previous = RenderTexture.active;
            try { Graphics.ExecuteCommandBuffer(commands); }
            finally { RenderTexture.active = previous; }
            return true;
        }

        private void ReleaseMask()
        {
            ReferenceHandle?.Release(); ReferenceHandle = null;
            if (!mask) return;
            mask.Release(); CoreUtils.Destroy(mask); mask = null;
        }
        public void Dispose() { commands?.Dispose(); ReleaseMask(); if (material) CoreUtils.Destroy(material); }
    }
}
