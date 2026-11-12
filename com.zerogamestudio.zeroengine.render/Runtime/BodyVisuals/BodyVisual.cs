using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render
{
    /// <summary>Caller-clocked Sprite/Spine overlay. Owns only its view, material and copied pose.</summary>
    public sealed class BodyVisual : IDisposable
    {
        public const int MaximumSmallParticles = 24;
        private static readonly int Clock = Shader.PropertyToID("_BodyClock");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int BodyBounds = Shader.PropertyToID("_BodyBounds");
        private static readonly int PixelSize = Shader.PropertyToID("_BodyPixelSize");
        private static readonly int WorldToBody = Shader.PropertyToID("_BodyWorldToLocal");
        private static readonly int Presence = Shader.PropertyToID("_BodyPresence");
        private static readonly int Retiring = Shader.PropertyToID("_BodyRetiring");
        private readonly Transform origin;
        private readonly IRenderPoseSource source;
        private readonly BodyVisualSettings settings;
        private readonly GameObject root;
        private readonly SpriteRenderer style;
        private readonly Material material;
        private readonly RenderPoseSnapshot snapshot = new();
        private readonly RenderPoseRenderer view;
        private readonly RenderTexture silhouette;
        private readonly CommandBuffer maskCommands;
        private readonly List<Renderer> maskRenderers = new();
        private readonly GameObject atmosphere;
        private readonly Mesh atmosphereMesh;
        private readonly MeshRenderer atmosphereRenderer;
        private readonly Material atmosphereMaterial;
        private float capturedCycle = -1f;
        private Vector3 pulsePivot;
        private bool disposed;
        private float sourcePixelSize;
        private readonly BodyStatusParticles particles;
        private readonly BodyStatusFire localizedFire;
        private readonly BodyStatusSpecks specks;
        private float compositionOpacity = 1f;
        private float accentOpacity = 1f;
        private int compositionOrder, particleBudget = MaximumSmallParticles;
        private float presence = -1f;
        private bool retiring;
        public bool HasSmallParticles => specks != null && specks.HasParticles;
        private readonly List<Vector2> sourceUvs = new();
        private readonly List<int> sourceIndices = new();
        private readonly MaterialPropertyBlock sourceTextureProperties = new();

        public BodyVisual(Transform origin, IRenderPoseSource source, Shader shader, BodyVisualSettings settings, BodyVisualResources resources = default)
            : this(origin, source, settings != null && settings.AppearanceTemplate ? settings.AppearanceTemplate.shader : shader,
                settings, settings?.AppearanceTemplate, resources) { }

        /// <summary>Optional authored look for explicit previews/callers; the template is cloned and never changed.</summary>
        public BodyVisual(Transform origin, IRenderPoseSource source, Shader shader, BodyVisualSettings settings,
            Material appearanceTemplate, BodyVisualResources resources = default)
        {
            if (!origin) throw new ArgumentNullException(nameof(origin));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!shader || !shader.isSupported) throw new ArgumentException("Body visual shader is unavailable.", nameof(shader));
            if (appearanceTemplate && appearanceTemplate.shader != shader)
                throw new ArgumentException("The appearance template must use the requested shader.", nameof(appearanceTemplate));
            if (settings == null || !settings.IsValid) throw new ArgumentException("Invalid body visual settings.", nameof(settings));
            this.origin = origin;
            this.source = source;
            this.settings = settings.Copy();
            material = appearanceTemplate ? new Material(appearanceTemplate) : new Material(shader);
            material.name = "BodyVisualRuntime";
            material.hideFlags = HideFlags.HideAndDontSave;
            material.SetFloat("_Mode", (int)settings.Kind);
            material.SetColor("_Tint", settings.Color);
            material.SetFloat("_Intensity", settings.Intensity);
            material.SetFloat("_PatternScale", settings.PatternScale);
            material.SetFloat("_MotionSpeed", settings.MotionSpeed);
            root = new GameObject("BodyVisual_" + settings.Kind) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(origin, false);
            style = root.AddComponent<SpriteRenderer>();
            style.sharedMaterial = material;
            style.enabled = false;
            view = new RenderPoseRenderer(root.transform, style);
            if ((int)settings.Kind >= (int)BodyVisualKind.Frozen)
            {
                // Union the actual posed layers on the GPU; no texture readability/import changes.
                silhouette = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32)
                {
                    name = "BodyVisualSilhouette", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                silhouette.Create();
                material.SetTexture("_BodyMask", silhouette);
                maskCommands = new CommandBuffer { name = "BodyVisualSilhouette" };
                atmosphereMaterial = new Material(material) { name = "BodyVisualAtmosphere", hideFlags = HideFlags.HideAndDontSave };
                atmosphereMaterial.SetFloat("_BodyLayer", 1f);
                atmosphere = new GameObject("BodyVisualAtmosphere") { hideFlags = HideFlags.DontSave };
                atmosphere.transform.SetParent(origin, false);
                atmosphereMesh = new Mesh { name = "BodyVisualAtmosphere", hideFlags = HideFlags.HideAndDontSave };
                atmosphereMesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
                };
                atmosphereMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                atmosphereMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                atmosphereMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                atmosphereMesh.RecalculateBounds();
                atmosphere.AddComponent<MeshFilter>().sharedMesh = atmosphereMesh;
                atmosphereRenderer = atmosphere.AddComponent<MeshRenderer>();
                atmosphereRenderer.sharedMaterial = atmosphereMaterial;
                atmosphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
                atmosphereRenderer.receiveShadows = false;
                atmosphereRenderer.enabled = false;
                if (settings.HasSmallParticles &&
                    SystemInfo.supportsAsyncGPUReadback)
                {
                    var specksShader = resources.Specks;
                    if (specksShader && specksShader.isSupported) specks = new BodyStatusSpecks(origin, specksShader, settings);
                }
                if (settings.Kind == BodyVisualKind.Burning && material.HasProperty("_UseLocalizedFire") &&
                    material.GetFloat("_UseLocalizedFire") > 0.5f)
                    localizedFire = new BodyStatusFire(origin, material);
                else if (settings.Kind == BodyVisualKind.Burning && material.HasProperty("_UseParticles") &&
                    material.GetFloat("_UseParticles") > 0.5f)
                {
                    if (SystemInfo.supportsAsyncGPUReadback) particles = new BodyStatusParticles(origin, material);
                    else atmosphereMaterial.SetFloat("_UseParticles", 0f); // Keep a visible GPU-only fallback.
                }
            }
        }

        public void SetPalette(VisualPalette palette)
        {
            palette.Apply(material); palette.Apply(atmosphereMaterial);
            specks?.SetPalette(palette); particles?.SetPalette(palette); localizedFire?.SetPalette(palette);
        }

        /// <summary>Composition is per entity; authored templates and source renderers remain read-only.</summary>
        public void SetComposition(float opacity, int sortingOffset, int smallParticleBudget)
        {
            compositionOpacity = BodyVisualSettings.InRange(opacity, 0f, 1f) ? opacity : 1f;
            accentOpacity = Mathf.Sqrt(compositionOpacity);
            compositionOrder = Mathf.Clamp(sortingOffset, 0, 64);
            particleBudget = Mathf.Clamp(smallParticleBudget, 0, MaximumSmallParticles);
        }

        /// <summary>Owner-clocked presence; source lifetime and gameplay state remain with the caller.</summary>
        public void SetPresence(float amount, bool isRetiring)
        {
            presence = BodyVisualSettings.InRange(amount, 0f, 1f) ? amount : 0f;
            retiring = isRetiring;
        }

        private void ApplyPresence(Material target)
        {
            if (target.HasProperty(Presence)) target.SetFloat(Presence, presence);
            if (target.HasProperty(Retiring)) target.SetFloat(Retiring, retiring ? 1f : 0f);
        }

        /// <returns>False when the source is gone/unsupported or a single pulse has ended.</returns>
        public bool Update(float elapsed, Renderer body)
        {
            if (disposed) return false;
            if (!origin || !body || !BodyVisualSettings.InRange(elapsed, 0f, float.MaxValue)) return false;
            bool pulse = settings.Kind == BodyVisualKind.ShadowPulse;
            if (pulse && !settings.Repeat && elapsed >= settings.PulseDuration) return false;
            var sample = pulse ? settings.EvaluatePulse(elapsed) : new BodyPulseSample(true, 1f, 1f);
            view.Hide(); // Unbind every borrowed mesh before the reusable snapshot can change.
            if (atmosphereRenderer) atmosphereRenderer.enabled = false;
            particles?.Hide();
            localizedFire?.Hide();
            specks?.Hide();
            if (!sample.Visible || !body.enabled || !body.gameObject.activeInHierarchy)
            {
                if (retiring) specks?.Clear();
                snapshot.Clear();
                capturedCycle = -1f;
                return true;
            }

            float cycle = pulse && settings.Repeat ? Mathf.Floor(elapsed / settings.PulseInterval) : 0f;
            if (!pulse || capturedCycle != cycle || snapshot.Layers.Count == 0)
            {
                if (!source.TryCapture(snapshot, origin)) return false;
                capturedCycle = cycle;
                pulsePivot = origin.InverseTransformPoint(body.bounds.center);
            }
            if (snapshot.Layers.Count == 0)
            {
                specks?.Clear();
                capturedCycle = -1f;
                return true;
            }
            root.layer = body.gameObject.layer;
            style.gameObject.layer = body.gameObject.layer;
            style.sortingLayerID = body.sortingLayerID;
            style.sortingOrder = body.sortingOrder + (pulse ? -1 : 1 + compositionOrder);
            bool surfaceLook = material.HasProperty("_BodySurface") && material.GetFloat("_BodySurface") > 0.5f;
            float scale = pulse ? sample.Scale : settings.Kind == BodyVisualKind.Glow && !surfaceLook ? 1.035f : 1f;
            // Expand the visual around the body centre, never the feet, Core or collider.
            root.transform.localPosition = pulsePivot * (1f - scale);
            root.transform.localScale = Vector3.one * scale;
            material.SetFloat(Clock, elapsed);
            float fallbackPresence = presence < 0f || material.HasProperty(Presence) ? 1f : Mathf.SmoothStep(0f, 1f, presence);
            material.SetFloat(Opacity, sample.Opacity * compositionOpacity * fallbackPresence);
            ApplyPresence(material);
            // Transform extents as axis contributions, not an AABB diagonal (which cancels under rotation).
            var localBounds = body.localBounds;
            var toOrigin = origin.worldToLocalMatrix * body.localToWorldMatrix;
            var x = toOrigin.MultiplyVector(Vector3.right * localBounds.extents.x);
            var y = toOrigin.MultiplyVector(Vector3.up * localBounds.extents.y);
            var z = toOrigin.MultiplyVector(Vector3.forward * localBounds.extents.z);
            var size = new Vector3(Mathf.Max((Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x)) * 2f, 0.001f),
                Mathf.Max((Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y)) * 2f, 0.001f), 1f);
            var bounds = new Vector4(pulsePivot.x, pulsePivot.y, 1f / size.x, 1f / size.y);
            material.SetVector(BodyBounds, bounds);
            material.SetMatrix(WorldToBody, origin.worldToLocalMatrix);
            view.Apply(snapshot);
            // Every authored surface, including glow/pulse, needs the current sprite frame's private UV mapping.
            if (atmosphereRenderer || material.HasProperty("_BodySourceTexelSize"))
                UpdateSourceMappingAndSilhouette();
            if (atmosphereRenderer)
            {
                if (sourcePixelSize <= 0f) sourcePixelSize = MeasureSourcePixelSize(size.y);
                material.SetFloat(PixelSize, sourcePixelSize);
                atmosphereMaterial.SetFloat(PixelSize, sourcePixelSize);
                // Reserve the actual pixel envelope even for very small/wide actors.
                var padding = material.HasProperty("_EffectPadding") ? material.GetVector("_EffectPadding") : new Vector4(3, 10, 3, 0);
                if (surfaceLook)
                {
                    var required = settings.Kind switch
                    {
                        BodyVisualKind.Burning => new Vector4(4, 9, 2, 0),
                        BodyVisualKind.Poisoned => new Vector4(11, 6, 6, 0),
                        BodyVisualKind.Electrified => new Vector4(3, 3, 2, 0),
                        BodyVisualKind.Bleeding => new Vector4(2, 1, 3, 0),
                        BodyVisualKind.Weakened => new Vector4(2, 24, 1, 0),
                        BodyVisualKind.Resonating => new Vector4(24, 1, 8, 0),
                        BodyVisualKind.FrozenSolid => new Vector4(3, 3, 3, 0),
                        _ => Vector4.zero
                    };
                    padding = Vector4.Max(padding, required);
                }
                padding = new Vector4(Mathf.Clamp(padding.x, 0, 32), Mathf.Clamp(padding.y, 0, 48), Mathf.Clamp(padding.z, 0, 32), 0);
                atmosphere.transform.localPosition = pulsePivot + Vector3.up * ((padding.y - padding.z) * 0.5f * sourcePixelSize);
                atmosphere.transform.localScale = new Vector3(size.x + padding.x * 2f * sourcePixelSize,
                    size.y + (padding.y + padding.z) * sourcePixelSize, 1f);
                atmosphere.layer = body.gameObject.layer;
                atmosphereRenderer.sortingLayerID = body.sortingLayerID;
                atmosphereRenderer.sortingOrder = body.sortingOrder + 2 + compositionOrder;
                atmosphereMaterial.SetFloat(Clock, elapsed);
                atmosphereMaterial.SetFloat(Opacity, sample.Opacity * accentOpacity * fallbackPresence);
                ApplyPresence(atmosphereMaterial);
                atmosphereMaterial.SetVector(BodyBounds, bounds);
                atmosphereMaterial.SetMatrix(WorldToBody, origin.worldToLocalMatrix);
                atmosphereRenderer.enabled = true;
                particles?.Update(elapsed, body, bounds, sourcePixelSize, silhouette);
                localizedFire?.Update(elapsed, body, bounds, sourcePixelSize);
                specks?.Update(elapsed, body, bounds, sourcePixelSize, silhouette, accentOpacity, compositionOrder, particleBudget,
                    !retiring, presence < 0f ? 1f : presence);
                if (particles != null) atmosphereMaterial.SetFloat("_UseParticles", particles.NeedsFallback ? 0f : 1f);
            }
            return true;
        }

        // Emitted accents use a bounded authored texel, never the current pose envelope.
        internal static float EmissionPixelSize(float pixelSize) => Mathf.Clamp(pixelSize, 0.001f, 1f / 16f);

        // A texel keeps its size while the pose bounds change; the shader grid is local to the entity.
        // UV edge lengths also cover Spine without introducing a Spine/Runtime dependency here.
        private float MeasureSourcePixelSize(float fallbackHeight)
        {
            var samples = new List<float>();
            foreach (var layer in snapshot.Layers)
            {
                if (layer.Sprite)
                {
                    samples.Add(Mathf.Abs(layer.Scale.x) / layer.Sprite.pixelsPerUnit);
                    samples.Add(Mathf.Abs(layer.Scale.y) / layer.Sprite.pixelsPerUnit);
                    continue;
                }
                if (!layer.Mesh) continue;
                var vertices = layer.Mesh.vertices;
                var uv = layer.Mesh.uv;
                if (uv.Length != vertices.Length) continue;
                for (int sub = 0; sub < Mathf.Min(layer.Mesh.subMeshCount, layer.Textures.Count); sub++)
                {
                    var texture = layer.Textures[sub];
                    if (!texture) continue;
                    var indices = layer.Mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < Mathf.Min(indices.Length, 24); i += 3)
                    {
                        for (int edge = 0; edge < 3; edge++)
                        {
                            int a = indices[i + edge], b = indices[i + (edge + 1) % 3];
                            float texels = Vector2.Scale(uv[b] - uv[a], new Vector2(texture.width, texture.height)).magnitude;
                            float distance = Vector3.Scale(vertices[b] - vertices[a], layer.Scale).magnitude;
                            if (texels > 0.5f && distance > 0.00001f) samples.Add(distance / texels);
                        }
                    }
                }
            }
            samples.RemoveAll(value => !BodyVisualSettings.InRange(value, 0.00001f, float.MaxValue));
            if (samples.Count == 0) return Mathf.Max(fallbackHeight / 48f, 0.001f);
            samples.Sort();
            return samples[samples.Count / 2];
        }

        private void UpdateSourceMappingAndSilhouette()
        {
            maskRenderers.Clear();
            root.GetComponentsInChildren(false, maskRenderers);
            if (maskCommands != null)
            {
                maskCommands.Clear();
                maskCommands.SetRenderTarget(silhouette);
                maskCommands.ClearRenderTarget(false, true, Color.clear);
            }
            foreach (var renderer in maskRenderers)
            {
                if (!renderer.enabled) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                int count = filter && filter.sharedMesh ? filter.sharedMesh.subMeshCount : 1;
                for (int i = 0; i < count; i++)
                {
                    if (material.HasProperty("_BodySourceTexelSize"))
                    {
                        sourceTextureProperties.Clear();
                        bool sprite = renderer is SpriteRenderer;
                        if (sprite) renderer.GetPropertyBlock(sourceTextureProperties);
                        else renderer.GetPropertyBlock(sourceTextureProperties, i);
                        var texture = sprite ? ((SpriteRenderer)renderer).sprite.texture : sourceTextureProperties.GetTexture("_MainTex");
                        if (texture)
                        {
                            sourceTextureProperties.SetVector("_BodySourceTexelSize", new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height));
                            if (material.HasProperty("_BodySourceUVRect"))
                            {
                                var rect = new Vector4(0, 0, 1, 1);
                                if (sprite)
                                {
                                    var spriteData = ((SpriteRenderer)renderer).sprite;
                                    // UV extrema also support tightly packed sprite geometry without textureRect exceptions.
                                    var uv = spriteData.uv;
                                    var min = Vector2.one; var max = Vector2.zero;
                                    foreach (var point in uv) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
                                    rect = new Vector4(min.x, min.y, max.x - min.x, max.y - min.y);
                                }
                                else if (filter && filter.sharedMesh)
                                {
                                    filter.sharedMesh.GetUVs(0, sourceUvs);
                                    filter.sharedMesh.GetTriangles(sourceIndices, i);
                                    var min = new Vector2(float.MaxValue, float.MaxValue);
                                    var max = new Vector2(float.MinValue, float.MinValue);
                                    foreach (int index in sourceIndices)
                                        if (index >= 0 && index < sourceUvs.Count)
                                        { min = Vector2.Min(min, sourceUvs[index]); max = Vector2.Max(max, sourceUvs[index]); }
                                    if (max.x > min.x && max.y > min.y)
                                        rect = new Vector4(min.x, min.y, max.x - min.x, max.y - min.y);
                                }
                                sourceTextureProperties.SetVector("_BodySourceUVRect", rect);
                            }
                            if (sprite) renderer.SetPropertyBlock(sourceTextureProperties);
                            else renderer.SetPropertyBlock(sourceTextureProperties, i);
                        }
                    }
                    maskCommands?.DrawRenderer(renderer, material, i, 1);
                }
            }
            if (maskCommands == null) return;
            // This runs before normal camera rendering and must not retain a caller's render target.
            var previous = RenderTexture.active;
            try { Graphics.ExecuteCommandBuffer(maskCommands); }
            finally { RenderTexture.active = previous; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            view.Dispose();
            snapshot.Dispose();
            if (atmosphereRenderer) atmosphereRenderer.enabled = false;
            particles?.Hide();
            localizedFire?.Hide();
            specks?.Hide();
            if (atmosphere) atmosphere.GetComponent<MeshFilter>().sharedMesh = null;
            particles?.Dispose();
            localizedFire?.Dispose();
            specks?.Dispose();
            maskCommands?.Dispose();
            if (silhouette) silhouette.Release();
            DestroyOwned(atmosphere);
            DestroyOwned(atmosphereMesh);
            DestroyOwned(atmosphereMaterial);
            DestroyOwned(silhouette);
            DestroyOwned(root);
            DestroyOwned(material);
        }

        internal static void DestroyOwned(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
