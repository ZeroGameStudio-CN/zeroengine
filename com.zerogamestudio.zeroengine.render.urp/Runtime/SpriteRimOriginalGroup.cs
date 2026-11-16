using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ZeroEngine.Render.Urp
{
    /// <summary>One explicit light set and its receivers. No source-material or MPB writes.</summary>
    internal sealed class SpriteRimOriginalGroup : IDisposable
    {
        internal readonly List<Source> Lights = new();
        internal readonly List<Draw> Draws = new();
        private readonly Dictionary<Renderer, Draw> owned = new();
        private readonly HashSet<SpriteRimLight> seenSources = new();
        private readonly HashSet<UnityEngine.Object> ignoredSources = new();
        private Shader shader;
        private readonly int layer;
        internal SpriteRimOriginalGroup(int layer) => this.layer = layer;

        internal readonly struct Source
        {
            internal readonly Matrix4x4 Matrix, Inverse;
            internal readonly UnityEngine.Object Identity;
            internal readonly Vector3 Position;
            internal readonly Vector2 Direction;
            internal readonly Vector4 Color, Powers;
            internal readonly LightType Shape;
            internal readonly float Angle, Intensity, MinimumExposure;
            internal readonly bool AffectsRim;
            internal readonly int CullingMask;

            internal Source(SpriteRimLight light)
            {
                Identity = light;
                Inverse = light.WorldToLight; Matrix = Inverse.inverse; Position = light.transform.position;
                float rotation = light.transform.eulerAngles.z * Mathf.Deg2Rad;
                Direction = new Vector2(Mathf.Sin(rotation), -Mathf.Cos(rotation));
                Color = (Vector4)light.LightColor * light.Intensity;
                Powers = new Vector4(light.LightPower, light.HorizontalPower, light.VerticalPower, light.RangePower);
                Shape = light.Shape; Angle = light.SpotHalfAngle * Mathf.Deg2Rad;
                Intensity = light.Intensity; MinimumExposure = light.MinimumGlobalExposure;
                AffectsRim = light.AffectsRim; CullingMask = ~0;
            }

            // Native Point/Spot are an input adapter: the original renderer itself used 2D lights.
            internal Source(Light light)
            {
                Identity = light;
                Position = light.transform.position;
                float rotation = light.transform.eulerAngles.z * Mathf.Deg2Rad;
                Direction = new Vector2(Mathf.Sin(rotation), -Mathf.Cos(rotation));
                Matrix = Matrix4x4.TRS(Position, light.transform.rotation, Vector3.one * Mathf.Max(.001f, light.range));
                Inverse = Matrix.inverse; Color = (Vector4)light.color * light.intensity;
                Powers = Vector4.one; Shape = light.type; Angle = light.spotAngle * .5f * Mathf.Deg2Rad;
                Intensity = light.intensity; MinimumExposure = 1; AffectsRim = true; CullingMask = light.cullingMask;
            }

            internal bool Influence(Vector3 position, out Vector2 away, out float score)
            {
                away = new Vector2(position.x - Position.x, position.y - Position.y);
                var local = Inverse.MultiplyVector(new Vector3(away.x, away.y));
                away.Normalize(); score = 0;
                if (!AffectsRim) return false;
                float distance = local.magnitude;
                if (Shape == LightType.Point)
                {
                    if (distance >= 1) return false;
                    score = Mathf.Pow(1 - distance, Powers.x);
                }
                else if (Shape == LightType.Directional)
                {
                    float x = 1 - Mathf.Abs(local.x), y = 1 - Mathf.Abs(local.y);
                    if (x <= 0 || y <= 0) return false;
                    away = Direction;
                    score = Mathf.Pow(x, Powers.y) * Mathf.Pow(y, Powers.z);
                }
                else if (Shape == LightType.Spot)
                {
                    float angle = Mathf.Abs(Mathf.Atan2(local.x, -local.y));
                    if (distance >= 1 || angle > Angle) return false;
                    // The shipped CPU selector uses angle/range. The field shader uses 1-angle/range.
                    // Keep the two distinct, including zero influence at the spot's exact centre line.
                    score = Mathf.Pow(angle / Angle, Powers.w) * Mathf.Pow(1 - distance, Powers.x);
                }
                else return false;
                score = Mathf.Clamp01(score * Intensity);
                return float.IsFinite(score) && score > 0;
            }
        }

        internal int Prepare(Renderer[] targets, SpriteRimLight[] sources, List<SpriteRimNativeSource> native,
            IReadOnlyList<UnityEngine.Object> ignored, SpriteRimOriginalSettings settings, Camera camera, Plane[] planes)
        {
            Lights.Clear(); Draws.Clear();
            ignoredSources.Clear();
            if (ignored != null) foreach (var source in ignored) if (source) ignoredSources.Add(source);
            shader ??= Resources.Load<Shader>("ZeroEngine/SpriteRimOriginal");
            if (!shader || !shader.isSupported || settings == null || targets == null) return 0;
            if (native != null)
            {
                foreach (var source in native)
                    if ((source.Light.cullingMask & (1 << layer)) != 0) Lights.Add(new Source(source.Light));
            }
            else if (sources != null)
            {
                seenSources.Clear();
                foreach (var source in sources)
                    if (source && source.isActiveAndEnabled && float.IsFinite(source.Intensity) && source.Intensity > 0 && seenSources.Add(source))
                        Lights.Add(new Source(source));
            }
            foreach (var target in targets)
            {
                if (!target || target.gameObject.layer != layer || !target.enabled || !target.gameObject.activeInHierarchy ||
                    (camera && ((camera.cullingMask & (1 << target.gameObject.layer)) == 0 || !GeometryUtility.TestPlanesAABB(planes, target.bounds)))) continue;
                if (!owned.TryGetValue(target, out var draw)) owned.Add(target, draw = new Draw(target, shader));
                if (!Draws.Contains(draw) && draw.Update(Lights, ignoredSources, settings)) Draws.Add(draw);
            }
            return Draws.Count;
        }

        internal sealed class Draw : IDisposable
        {
            internal readonly Renderer Renderer;
            internal readonly SpriteRimSilhouette Silhouette;
            internal readonly List<Material> Materials = new();
            private readonly List<Material> sources = new();
            private readonly MaterialPropertyBlock properties = new(), submeshProperties = new();
            private readonly Shader shader;

            internal Draw(Renderer renderer, Shader shader)
            { Renderer = renderer; Silhouette = new SpriteRimSilhouette(renderer); this.shader = shader; }

            internal bool Update(List<Source> lights, HashSet<UnityEngine.Object> ignored, SpriteRimOriginalSettings settings)
            {
                int selected = -1; float strongest = 0; Vector2 direction = default;
                // Original SetRim queries the setter pivot, not renderer.bounds.center.
                for (int i = 0; i < lights.Count; i++)
                {
                    var source = lights[i];
                    var delta = (Vector2)(Renderer.transform.position - source.Position);
                    if (ignored.Contains(source.Identity) || (source.Identity is Component component && ignored.Contains(component.gameObject)) ||
                        source.Inverse.MultiplyVector(new Vector3(delta.x,delta.y)).magnitude > settings.MaximumSelectionDistance) continue;
                    if ((source.CullingMask & (1 << Renderer.gameObject.layer)) != 0 &&
                        source.Influence(Renderer.transform.position, out var away, out float score) && score > strongest)
                    { selected = i; strongest = score; direction = away; }
                }
                if (!Silhouette.Update(6) || !Silhouette.SupportsDepthComposite) return false;
                Renderer.GetSharedMaterials(sources);
                while (Materials.Count < sources.Count) Materials.Add(new Material(shader) { hideFlags = HideFlags.HideAndDontSave });
                while (Materials.Count > sources.Count)
                { int index = Materials.Count - 1; CoreUtils.Destroy(Materials[index]); Materials.RemoveAt(index); }
                Vector3 offset = Renderer.transform.TransformVector(direction * settings.Distance(strongest));
                var outer = settings.OuterColor; var inner = settings.InnerColor;
                float alpha = selected < 0 ? 0 : settings.Alpha(strongest);
                outer.a *= alpha; inner.a *= alpha;
                float exposure = selected < 0 || !settings.UseLocalExposure ? 1 :
                    Mathf.Lerp(1, settings.MinimumLocalExposure * lights[selected].MinimumExposure, strongest);
                bool sprite = Renderer is SpriteRenderer sr && sr.sprite;
                Vector4 frame = Silhouette.ReferenceFrame;
                if (sprite)
                {
                    var image = ((SpriteRenderer)Renderer).sprite;
                    Vector2 center = image.packed ? image.textureRect.min - image.textureRectOffset + image.pivot : image.pivot;
                    Vector2 size = image.packed ? image.textureRect.size : image.rect.size;
                    frame = new Vector4(center.x, center.y, size.x, size.y);
                }
                properties.Clear(); Renderer.GetPropertyBlock(properties);
                for (int i = 0; i < Materials.Count; i++)
                {
                    var material = Materials[i]; var source = sources[i];
                    var texture = Silhouette.GetSourceTexture(i, out var st);
                    material.SetTexture("_RimOriginalTexture", texture);
                    material.SetVector("_RimOriginalST", st);
                    material.SetTexture("_RimOriginalMask", Silhouette.Texture);
                    material.SetVector("_RimOriginalRow0", Silhouette.WorldToMask.GetRow(0));
                    material.SetVector("_RimOriginalRow1", Silhouette.WorldToMask.GetRow(1));
                    material.SetVector("_RimOriginalMaskInfo", Silhouette.Info);
                    material.SetVector("_RimOriginalFrame", frame);
                    material.SetFloat("_RimOriginalSprite", sprite ? 1 : 0);
                    material.SetVector("_RimOriginalBand", new Vector4(offset.x, offset.y, settings.Scale(strongest),
                        texture && GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) ? 1 : 0));
                    material.SetVector("_RimOriginalOuter", outer); material.SetVector("_RimOriginalInner", inner);
                    material.SetVector("_RimOriginalBlend", new Vector4((int)settings.Blend, settings.LightBlend, settings.MaximumLight, 0));
                    material.SetVector("_RimOriginalExposure", new Vector4(settings.Exposure, settings.ExposureThreshold, settings.ExposureOffset, settings.GlobalExposure * exposure));
                    Color tint = source && source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                    int colorId = Shader.PropertyToID("_Color");
                    if (properties.HasColor(colorId)) tint = properties.GetColor(colorId);
                    if (!sprite)
                    {
                        submeshProperties.Clear(); Renderer.GetPropertyBlock(submeshProperties, i);
                        if (submeshProperties.HasColor(colorId)) tint = submeshProperties.GetColor(colorId);
                    }
                    else tint *= ((SpriteRenderer)Renderer).color;
                    material.SetVector("_RimOriginalTint", tint);
                }
                return Materials.Count > 0;
            }

            public void Dispose()
            {
                foreach (var material in Materials) CoreUtils.Destroy(material);
                Materials.Clear(); Silhouette.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var draw in owned.Values) draw.Dispose();
            owned.Clear(); Draws.Clear(); Lights.Clear(); seenSources.Clear(); ignoredSources.Clear();
        }
    }
}
