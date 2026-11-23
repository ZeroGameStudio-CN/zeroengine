using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Collects the marked native sources using the active URP camera's light and shadow ordering.</summary>
    public sealed class SpriteRimLightFrame
    {
        internal readonly List<SpriteRimNativeSource> Sources = new();
        public int Count => Sources.Count;

        public void Prepare(ScriptableRenderer renderer, LightData lightData)
        {
            Sources.Clear();
            NativeArray<VisibleLight> lights = lightData.visibleLights;
            bool useShadowIndices = HasPackedShadowIndices(renderer);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i].light;
                if (!light || !light.isActiveAndEnabled || !float.IsFinite(light.intensity) || light.intensity <= 0 ||
                    (light.type != LightType.Point && light.type != LightType.Spot)) continue;
                if (!light.TryGetComponent<SpriteRimLightSource>(out var source) || !source.isActiveAndEnabled) continue;
                // URP packs forward shadow parameters in visible-light order, skipping the main light.
                int index = i - (lightData.mainLightIndex >= 0 && i > lightData.mainLightIndex ? 1 : 0);
                int shadowIndex = useShadowIndices && i != lightData.mainLightIndex &&
                    index < lightData.additionalLightsCount && light.shadows != LightShadows.None && light.shadowStrength > 0
                    ? index : -1;
                Sources.Add(new SpriteRimNativeSource(light, source, shadowIndex));
            }
        }

        private static bool HasPackedShadowIndices(ScriptableRenderer renderer)
        {
            var asset = UniversalRenderPipeline.asset;
            if (!asset || !asset.supportsAdditionalLightShadows) return false;
            var renderers = asset.renderers;
            var data = asset.rendererDataList;
            for (int i = 0; i < renderers.Length && i < data.Length; i++)
            {
                if (renderers[i] != renderer || data[i] is not UniversalRendererData universal) continue;
                // Deferred without transparent shadow receiving packs only shadow casters.
                return universal.renderingMode == RenderingMode.Forward ||
                    universal.renderingMode == RenderingMode.ForwardPlus || universal.shadowTransparentReceive;
            }
            return false;
        }

    }

    /// <summary>
    /// Supplies the standard ZE rim inputs to a caller that already projects its own sprite and tests scene depth.
    /// The caller binds the mask as a Render Graph read and owns the original renderer and material properties.
    /// </summary>
    public sealed class SpriteRimProjectedReceiver : IDisposable
    {
        private static readonly int LightCountId = Shader.PropertyToID("_RimEnhanceLightCount");
        private static readonly int ShadowDistanceSqId = Shader.PropertyToID("_RimEnhanceAdditionalShadowDistanceSq");
        private SpriteRimDraw draw;

        public SpriteRimProjectedReceiver(SpriteRenderer source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            draw = new SpriteRimDraw(source);
        }

        public RTHandle Mask => draw?.Silhouette.ReferenceHandle;

        public bool Prepare(SpriteRimLightFrame frame, SpriteRimLightingProfile profile,
            MaterialPropertyBlock properties, float maximumShadowDistance)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            properties.SetInt(LightCountId, 0);
            if (draw == null || frame == null || frame.Count == 0 || !profile || profile.Strength <= 0)
                return false;

            if (!draw.SelectLights(frame.Sources) || !draw.Update(profile, properties)) return false;

            properties.SetFloat(ShadowDistanceSqId, maximumShadowDistance * maximumShadowDistance);
            return true;
        }

        public void Dispose()
        {
            draw?.Dispose();
            draw = null;
        }
    }
}
