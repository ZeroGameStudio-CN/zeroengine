using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Owns only mask/override draws. Never replaces source materials or writes their property blocks.</summary>
    internal sealed class SpriteRimDraw : IDisposable
    {
        public readonly Renderer Renderer;
        public readonly SpriteRimSilhouette Silhouette;
        public readonly List<Material> Materials = new();
        private readonly Shader shader;
        private const int MaximumLights = 4;
        private readonly LightInput[] selected = new LightInput[MaximumLights];
        private readonly Vector4[] rows0 = new Vector4[MaximumLights], rows1 = new Vector4[MaximumLights], rows2 = new Vector4[MaximumLights];
        private readonly Vector4[] colors = new Vector4[MaximumLights], shapes = new Vector4[MaximumLights];
        private readonly Vector4[] powers = new Vector4[MaximumLights], bands = new Vector4[MaximumLights];
        private int selectedCount, uploadedCount;
        private bool hasUpload;
        private Vector4 uploadedWrap;
        private static readonly int SourceId = Shader.PropertyToID("_RimRefActorTexture");
        private static readonly int SourceStId = Shader.PropertyToID("_RimRefActorST");
        private static readonly int CountId = Shader.PropertyToID("_RimEnhanceLightCount");
        private static readonly int WrapId = Shader.PropertyToID("_RimEnhanceInnerWrap");
        private static readonly int Rows0Id = Shader.PropertyToID("_RimEnhanceRows0"), Rows1Id = Shader.PropertyToID("_RimEnhanceRows1"), Rows2Id = Shader.PropertyToID("_RimEnhanceRows2");
        private static readonly int ColorsId = Shader.PropertyToID("_RimEnhanceColors"), ShapesId = Shader.PropertyToID("_RimEnhanceShapes");
        private static readonly int PowersId = Shader.PropertyToID("_RimEnhancePowers"), BandsId = Shader.PropertyToID("_RimEnhanceBands");

        private struct LightInput
        {
            internal UnityEngine.Object Identity;
            internal float Score, MinimumExposure;
            internal Vector2 Away;
            internal Vector4 Row0, Row1, Row2, Color, Shape, Powers;
        }

        internal SpriteRimDraw(Renderer renderer)
        {
            Renderer = renderer; Silhouette = new SpriteRimSilhouette(renderer);
            shader = Resources.Load<Shader>("ZeroEngine/SpriteRimLighting");
        }

        private void Insert(LightInput value)
        {
            for (int i = 0; i < selectedCount; i++) if (selected[i].Identity == value.Identity) return;
            for (int i = 0; i < MaximumLights; i++)
            {
                if (i < selectedCount && value.Score <= selected[i].Score) continue;
                for (int k = MaximumLights - 1; k > i; k--) selected[k] = selected[k - 1];
                selected[i] = value; selectedCount = Mathf.Min(MaximumLights, selectedCount + 1); break;
            }
        }

        internal bool SelectLights(IReadOnlyList<SpriteRimLight> sources)
        {
            selectedCount = 0;
            if (!Renderer || sources == null) return false;
            foreach (var light in sources)
            {
                if (!light || !light.TryGetInfluence(Renderer.bounds.center, out var away, out float score)) continue;
                var matrix = light.WorldToLight;
                var row0 = matrix.GetRow(0); var row1 = matrix.GetRow(1);
                row0.w += row0.z * light.transform.position.z; row0.z = 0;
                row1.w += row1.z * light.transform.position.z; row1.z = 0;
                var color = light.LightColor;
                Insert(new LightInput
                {
                    Identity = light, Score = score, Away = away, MinimumExposure = light.MinimumGlobalExposure,
                    Row0 = row0, Row1 = row1,
                    Color = new Vector4(color.r, color.g, color.b, color.a) * light.Intensity,
                    Shape = new Vector4(light.Shape == LightType.Point ? 0 : light.Shape == LightType.Directional ? 1 : 2,
                        Mathf.Clamp(light.SpotHalfAngle, .1f, 90) * Mathf.Deg2Rad, 0, 0),
                    Powers = new Vector4(Mathf.Max(0, light.LightPower), Mathf.Max(0, light.HorizontalPower),
                        Mathf.Max(0, light.VerticalPower), Mathf.Max(0, light.RangePower))
                });
            }
            return selectedCount > 0;
        }

        internal bool SelectLights(NativeArray<VisibleLight> sources)
        {
            selectedCount = 0;
            if (!Renderer) return false;
            var position = Renderer.bounds.center;
            for (int i = 0; i < sources.Length; i++)
            {
                var light = sources[i].light;
                if (!light || !light.isActiveAndEnabled || !float.IsFinite(light.intensity) || light.intensity <= 0 ||
                    (light.cullingMask & (1 << Renderer.gameObject.layer)) == 0 ||
                    (light.type != LightType.Point && light.type != LightType.Spot)) continue;
                if (!light.TryGetComponent<SpriteRimLightSource>(out var source) || !source.isActiveAndEnabled) continue;
                // Ordinary scene illumination and global daylight do not implicitly create actor rims.
                var toward = light.transform.position - position;
                if (toward.sqrMagnitude < .0000001f) continue;
                var away = new Vector2(-toward.x, -toward.y).normalized;
                if (away.sqrMagnitude < .0000001f) continue;
                float range = Mathf.Max(.001f, light.range);
                float attenuation = Mathf.Pow(Mathf.Clamp01(1 - toward.sqrMagnitude / (range * range)), 2);
                float outer = 0, inner = 1;
                if (light.type == LightType.Spot)
                {
                    outer = Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad);
                    inner = Mathf.Cos(light.innerSpotAngle * .5f * Mathf.Deg2Rad);
                    attenuation *= Mathf.InverseLerp(outer, inner, Vector3.Dot(light.transform.forward, -toward.normalized));
                }
                var color = light.color;
                float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                float score = Mathf.Clamp01(attenuation * light.intensity * peak);
                if (!float.IsFinite(score) || score <= 0) continue;
                var matrix = Matrix4x4.Scale(Vector3.one / range) *
                    Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one).inverse;
                Insert(new LightInput
                {
                    Identity = light, Score = score, Away = away, MinimumExposure = 1,
                    Row0 = matrix.GetRow(0), Row1 = matrix.GetRow(1), Row2 = matrix.GetRow(2),
                    // Keep hue separate from HDR intensity; native coverage is bounded in the shader.
                    Color = new Vector4(color.r / Mathf.Max(1, peak), color.g / Mathf.Max(1, peak), color.b / Mathf.Max(1, peak), light.intensity),
                    Shape = new Vector4(light.type == LightType.Point ? 3 : 4, outer, inner, 0),
                    Powers = Vector4.one
                });
            }
            return selectedCount > 0;
        }

        internal bool Update(SpriteRimLightingProfile profile)
        {
            if (!Renderer || selectedCount == 0 || !shader || !shader.isSupported) return false;
            int padding = Mathf.CeilToInt(Mathf.Max(profile.WeakDistance, profile.StrongDistance) + Mathf.Abs(profile.StrongScale));
            if (!Silhouette.Update(Mathf.Max(1, padding)) || !Silhouette.SupportsDepthComposite) return false;
            int count = Silhouette.SourceMaterialCount;
            int changes = !hasUpload || Materials.Count != count ? -1 : 0;
            while (Materials.Count < count) Materials.Add(new Material(shader) { hideFlags = HideFlags.HideAndDontSave });
            while (Materials.Count > count)
            { int i = Materials.Count - 1; CoreUtils.Destroy(Materials[i]); Materials.RemoveAt(i); }
            var axisX = ((Vector3)Silhouette.WorldToMask.GetRow(0)).normalized;
            var axisY = ((Vector3)Silhouette.WorldToMask.GetRow(1)).normalized;
            float total = 0, weightedExposure = 0;
            for (int i = 0; i < selectedCount; i++)
            {
                var input = selected[i];
                float response = Mathf.Pow(input.Score, Mathf.Max(.001f, profile.ResponsePower));
                float distance = Mathf.Lerp(profile.WeakDistance, profile.StrongDistance, response);
                var away = new Vector3(input.Away.x, input.Away.y, 0);
                UpdateData(rows0, i, input.Row0, 1, ref changes); UpdateData(rows1, i, input.Row1, 2, ref changes);
                UpdateData(rows2, i, input.Row2, 4, ref changes); UpdateData(colors, i, input.Color, 8, ref changes);
                UpdateData(shapes, i, input.Shape, 16, ref changes); UpdateData(powers, i, input.Powers, 32, ref changes);
                UpdateData(bands, i, new Vector4(Vector3.Dot(away, axisX) * distance, Vector3.Dot(away, axisY) * distance,
                    Mathf.Lerp(0, profile.StrongScale, response), response), 64, ref changes);
                total += input.Score; weightedExposure += input.MinimumExposure * input.Score;
            }
            var wrap = new Vector4(Mathf.Clamp(profile.InnerWrapWidth, 1, 4), Mathf.Clamp(profile.InnerWrapStrength, 0, .35f), 0, 0);
            if (!uploadedWrap.Equals(wrap)) { uploadedWrap = wrap; changes |= 128; }
            float exposure = Mathf.Lerp(1, Mathf.Clamp(profile.MinimumExposure * weightedExposure / Mathf.Max(total, .000001f), .01f, 1), selected[0].Score);
            foreach (var material in Materials)
            {
                material.SetTexture("_RimRefMask", Silhouette.Texture);
                material.SetVector("_RimRefRow0", Silhouette.WorldToMask.GetRow(0));
                material.SetVector("_RimRefRow1", Silhouette.WorldToMask.GetRow(1));
                material.SetVector("_RimRefMaskInfo", Silhouette.Info); material.SetVector("_RimRefFrame", Silhouette.ReferenceFrame);
                material.SetVector("_RimRefStyle", new Vector4(exposure, Mathf.Max(.01f, profile.MaximumLight), Mathf.Clamp01(profile.Strength),
                    (profile.GammaBlend && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0) + (profile.LimitAddedHdr ? 2 : 0)));
                var edge = profile.EdgeTint; var inner = profile.InnerTint;
                material.SetVector("_RimRefEdgeTint", new Vector4(edge.r, edge.g, edge.b, edge.a));
                material.SetVector("_RimRefInnerTint", new Vector4(inner.r, inner.g, inner.b, inner.a));
                if (changes == -1 || uploadedCount != selectedCount) material.SetInt(CountId, selectedCount);
                if ((changes & 1) != 0) material.SetVectorArray(Rows0Id, rows0);
                if ((changes & 2) != 0) material.SetVectorArray(Rows1Id, rows1);
                if ((changes & 4) != 0) material.SetVectorArray(Rows2Id, rows2);
                if ((changes & 8) != 0) material.SetVectorArray(ColorsId, colors);
                if ((changes & 16) != 0) material.SetVectorArray(ShapesId, shapes);
                if ((changes & 32) != 0) material.SetVectorArray(PowersId, powers);
                if ((changes & 64) != 0) material.SetVectorArray(BandsId, bands);
                if ((changes & 128) != 0) material.SetVector(WrapId, uploadedWrap);
            }
            hasUpload = count > 0; uploadedCount = selectedCount;
            for (int i = 0; i < count; i++)
            { var texture = Silhouette.GetSourceTexture(i, out var st); Materials[i].SetTexture(SourceId, texture); Materials[i].SetVector(SourceStId, st); }
            return count > 0;
        }

        private static void UpdateData(Vector4[] data, int index, Vector4 value, int flag, ref int changes)
        {
            if (data[index].Equals(value)) return;
            data[index] = value; changes |= flag;
        }

        public void Dispose()
        {
            foreach (var material in Materials) if (material) CoreUtils.Destroy(material);
            Materials.Clear(); Silhouette.Dispose();
        }
    }
}
