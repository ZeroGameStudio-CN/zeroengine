using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Render.Urp
{
    [MovedFrom(true, "POB", "POB.Rendering", "SpriteRimReferenceRendererFeature")]
    public sealed class SpriteRimRendererFeature : ScriptableRendererFeature
    {
        private RimPass pass;
        public override void Create() => pass ??= new RimPass();
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!Application.isPlaying || renderingData.cameraData.cameraType != CameraType.Game ||
                renderingData.cameraData.renderType != CameraRenderType.Base) return;
            pass ??= new RimPass();
            if (pass.Prepare(renderer, renderingData.cameraData.camera, renderingData.lightData)) renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) { pass?.Dispose(); pass = null; }

        private sealed class RimPass : ScriptableRenderPass
        {
            private readonly List<SpriteRimDraw> draws = new();
            private readonly List<SpriteRimNativeSource> nativeSources = new();
            private readonly Plane[] planes = new Plane[6];
            private static readonly int SceneColorId = Shader.PropertyToID("_RimEnhanceSceneColor");
            private static readonly int SceneScaleId = Shader.PropertyToID("_RimEnhanceSceneScaleBias");
            private static readonly int ShadowDistanceSqId = Shader.PropertyToID("_RimEnhanceAdditionalShadowDistanceSq");

            internal RimPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques + 1;
                ConfigureInput(ScriptableRenderPassInput.Depth);
                requiresIntermediateTexture = true;
            }
            internal bool Prepare(ScriptableRenderer renderer, Camera camera, LightData lightData)
            {
                nativeSources.Clear();
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
                    nativeSources.Add(new SpriteRimNativeSource(light, source, shadowIndex));
                }
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                SpriteRimRegistry.Collect(camera, planes, nativeSources, draws);
                return draws.Count > 0;
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

            private sealed class PassData
            {
                internal TextureHandle Source, Destination;
                internal List<SpriteRimDraw> Draws;
                internal Rect FullViewport, ActorViewport;
                internal float AdditionalShadowDistanceSq;
            }
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "Pixel rim camera color"; desc.clearBuffer = false;
                var destination = renderGraph.CreateTexture(desc);
                var size = desc.CalculateFinalDimensions(); var rect = camera.camera.rect;
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Pixel rim: multi-light composite", out var data))
                {
                    data.Source = source; data.Destination = destination; data.Draws = new List<SpriteRimDraw>(draws);
                    data.FullViewport = new Rect(0, 0, size.x, size.y);
                    data.ActorViewport = new Rect(rect.x * size.x, rect.y * size.y, rect.width * size.x, rect.height * size.y);
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    var shadowAtlas = resources.additionalShadowsTexture;
                    if (shadowAtlas.IsValid())
                    {
                        data.AdditionalShadowDistanceSq = camera.maxShadowDistance * camera.maxShadowDistance;
                        builder.UseTexture(shadowAtlas, AccessFlags.Read);
                    }
                    foreach (var draw in data.Draws) builder.UseTexture(renderGraph.ImportTexture(draw.Silhouette.ReferenceHandle), AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        RTHandle handle = data.Source;
                        Vector2 scale = handle.useScaling ? new Vector2(handle.rtHandleProperties.rtHandleScale.x, handle.rtHandleProperties.rtHandleScale.y) : Vector2.one;
                        bool flip = context.GetTextureUVOrigin(in data.Source) != context.GetTextureUVOrigin(in data.Destination);
                        var bias = flip ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0);
                        context.cmd.SetViewport(data.FullViewport);
                        Blitter.BlitTexture(context.cmd, handle, bias, 0f, false);
                        context.cmd.SetGlobalTexture(SceneColorId, data.Source); context.cmd.SetGlobalVector(SceneScaleId, bias);
                        context.cmd.SetGlobalFloat(ShadowDistanceSqId, data.AdditionalShadowDistanceSq);
                        context.cmd.SetViewport(data.ActorViewport);
                        foreach (var draw in data.Draws)
                            if (draw.Renderer && draw.Renderer.enabled)
                                for (int i = 0; i < draw.Materials.Count; i++) context.cmd.DrawRenderer(draw.Renderer, draw.Materials[i], i, 0);
                    });
                }
                resources.cameraColor = destination;
            }
            internal void Dispose()
            {
                nativeSources.Clear();
                draws.Clear();
            }
        }
    }
}
