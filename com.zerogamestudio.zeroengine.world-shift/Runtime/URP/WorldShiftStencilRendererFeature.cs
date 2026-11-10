using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace ZeroEngine.WorldShift.URP
{
    public sealed class WorldShiftStencilRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            public LayerMask overworldLayerMask;
            public LayerMask underworldLayerMask;
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            public Material stencilMaskMaterial;
            [Range(1, 128)] public int stencilBit = 64;
        }

        [SerializeField] private Settings settings = new();

        private WorldShiftStencilPass opaquePass;
        private WorldShiftStencilPass transparentPass;

        public Settings FeatureSettings => settings;

        public override void Create()
        {
            opaquePass = new WorldShiftStencilPass(settings, RenderQueueRange.opaque, true)
            {
                renderPassEvent = settings.renderPassEvent
            };
            transparentPass = new WorldShiftStencilPass(settings, RenderQueueRange.transparent, false)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.stencilMaskMaterial == null || !WorldShiftStencilRuntimeState.HasRegisteredController)
            {
                return;
            }

            opaquePass ??= new WorldShiftStencilPass(settings, RenderQueueRange.opaque, true);
            opaquePass.renderPassEvent = settings.renderPassEvent;
            opaquePass.Setup();
            renderer.EnqueuePass(opaquePass);

            transparentPass ??= new WorldShiftStencilPass(settings, RenderQueueRange.transparent, false);
            transparentPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            transparentPass.Setup();
            renderer.EnqueuePass(transparentPass);
        }

        private sealed class WorldShiftStencilPass : ScriptableRenderPass
        {
            private const int ClearStencilPassIndex = 0;
            private const int ScreenScanMaskPassIndex = 1;

            private static readonly List<ShaderTagId> ShaderTags = new()
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("Universal2D"),
                new ShaderTagId("SRPDefaultUnlit")
            };
#if UNITY_6000_0_OR_NEWER
            private static readonly ShaderTagId[] RendererListShaderTags = ShaderTags.ToArray();

            private sealed class PassData
            {
                public Material stencilMaskMaterial;
                public int stencilBit;
                public bool drawScanMask;
                public RendererListHandle firstWorld;
                public RendererListHandle secondWorld;
            }
#endif

            private readonly Settings settings;
            private readonly RenderQueueRange renderQueueRange;
            private readonly bool useOpaqueSorting;
            private FilteringSettings overworldFiltering;
            private FilteringSettings underworldFiltering;
            private RenderStateBlock stencilEqual;
            private RenderStateBlock stencilNotEqual;
            private int stencilBit;

            public WorldShiftStencilPass(Settings settings, RenderQueueRange renderQueueRange, bool useOpaqueSorting)
            {
                this.settings = settings;
                this.renderQueueRange = renderQueueRange;
                this.useOpaqueSorting = useOpaqueSorting;
            }

            public void Setup()
            {
                overworldFiltering = new FilteringSettings(renderQueueRange, settings.overworldLayerMask.value);
                underworldFiltering = new FilteringSettings(renderQueueRange, settings.underworldLayerMask.value);
                stencilBit = SanitizeStencilBit(settings.stencilBit);

                stencilEqual = CreateStencilState(CompareFunction.Equal, stencilBit);
                stencilNotEqual = CreateStencilState(CompareFunction.NotEqual, stencilBit);
            }

#if UNITY_6000_0_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (settings.stencilMaskMaterial == null)
                {
                    return;
                }

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (!resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid())
                {
                    return;
                }

                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                SortingCriteria sorting = useOpaqueSorting
                    ? cameraData.defaultOpaqueSortFlags
                    : SortingCriteria.CommonTransparent;
                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, renderingData, cameraData, lightData, sorting);

                bool drawScanMask = WorldShiftStencilRuntimeState.TryGetActiveState(
                    out WorldShiftStencilRuntimeState.State state);
                FilteringSettings firstFiltering = overworldFiltering;
                FilteringSettings secondFiltering = underworldFiltering;
                RenderStateBlock? firstStencil = null;
                RenderStateBlock? secondStencil = null;
                if (drawScanMask)
                {
                    // Draw the previous world outside the scan, then the target world inside it.
                    if (state.TargetLayer != WorldShiftLayer.Underworld)
                    {
                        firstFiltering = underworldFiltering;
                        secondFiltering = overworldFiltering;
                    }

                    firstStencil = stencilNotEqual;
                    secondStencil = stencilEqual;
                }

                RendererListHandle firstWorld = CreateWorldRendererList(
                    renderGraph, renderingData, cameraData, drawing, firstFiltering, firstStencil);
                RendererListHandle secondWorld = CreateWorldRendererList(
                    renderGraph, renderingData, cameraData, drawing, secondFiltering, secondStencil);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    useOpaqueSorting ? "WorldShift Stencil Opaque" : "WorldShift Stencil Transparent", out var passData))
                {
                    passData.stencilMaskMaterial = settings.stencilMaskMaterial;
                    passData.stencilBit = stencilBit;
                    passData.drawScanMask = drawScanMask;
                    passData.firstWorld = firstWorld;
                    passData.secondWorld = secondWorld;

                    builder.UseRendererList(firstWorld);
                    builder.UseRendererList(secondWorld);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    // Keep camera depth and unrelated stencil bits; only the mask shader clears our bit.
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    // These lists draw arbitrary world materials, as does URP's built-in DrawObjects pass.
                    builder.UseAllGlobalTextures(true);
                    if (resources.mainShadowsTexture.IsValid())
                        builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid())
                        builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);

                    // Fullscreen stencil work must still run when both renderer lists are empty.
                    if (drawScanMask)
                        builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        if (data.drawScanMask)
                        {
                            data.stencilMaskMaterial.SetFloat("_StencilRef", data.stencilBit);
                            data.stencilMaskMaterial.SetFloat("_StencilMask", data.stencilBit);
                            context.cmd.DrawProcedural(Matrix4x4.identity, data.stencilMaskMaterial,
                                ClearStencilPassIndex, MeshTopology.Triangles, 3);
                            context.cmd.DrawProcedural(Matrix4x4.identity, data.stencilMaskMaterial,
                                ScreenScanMaskPassIndex, MeshTopology.Triangles, 3);
                        }

                        context.cmd.DrawRendererList(data.firstWorld);
                        context.cmd.DrawRendererList(data.secondWorld);
                    });
                }
            }

            private static RendererListHandle CreateWorldRendererList(
                RenderGraph renderGraph,
                UniversalRenderingData renderingData,
                UniversalCameraData cameraData,
                DrawingSettings drawing,
                FilteringSettings filtering,
                RenderStateBlock? stencil)
            {
                if (!stencil.HasValue)
                {
                    return renderGraph.CreateRendererList(
                        new RendererListParams(renderingData.cullResults, drawing, filtering));
                }

                var description = new RendererListDesc(
                    RendererListShaderTags, renderingData.cullResults, cameraData.camera)
                {
                    stateBlock = stencil
                };
                RendererListParams parameters = RendererListDesc.ConvertToParameters(description);
                // Preserve URP's light, per-object and instancing settings, plus the exact layer/queue filters.
                parameters.drawSettings = drawing;
                parameters.filteringSettings = filtering;
                return renderGraph.CreateRendererList(parameters);
            }
#else
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (settings.stencilMaskMaterial == null)
                {
                    return;
                }

                SortingCriteria sorting = useOpaqueSorting
                    ? renderingData.cameraData.defaultOpaqueSortFlags
                    : SortingCriteria.CommonTransparent;
                DrawingSettings drawing = CreateDrawingSettings(ShaderTags, ref renderingData, sorting);

                if (!WorldShiftStencilRuntimeState.TryGetActiveState(out WorldShiftStencilRuntimeState.State state))
                {
                    DrawVisualLayersNormally(context, ref renderingData, ref drawing);
                    return;
                }

                settings.stencilMaskMaterial.SetFloat("_StencilRef", stencilBit);
                settings.stencilMaskMaterial.SetFloat("_StencilMask", stencilBit);

                DrawScreenScanMask(context);
                DrawMaskedWorlds(context, ref renderingData, ref drawing, state);
            }

            private void DrawScreenScanMask(ScriptableRenderContext context)
            {
                CommandBuffer cmd = CommandBufferPool.Get("WorldShiftStencilScreenScanMask");
                cmd.DrawProcedural(Matrix4x4.identity, settings.stencilMaskMaterial, ClearStencilPassIndex, MeshTopology.Triangles, 3);
                cmd.DrawProcedural(Matrix4x4.identity, settings.stencilMaskMaterial, ScreenScanMaskPassIndex, MeshTopology.Triangles, 3);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            private void DrawMaskedWorlds(
                ScriptableRenderContext context,
                ref RenderingData renderingData,
                ref DrawingSettings drawing,
                WorldShiftStencilRuntimeState.State state)
            {
                if (state.TargetLayer == WorldShiftLayer.Underworld)
                {
                    context.DrawRenderers(renderingData.cullResults, ref drawing, ref overworldFiltering, ref stencilNotEqual);
                    context.DrawRenderers(renderingData.cullResults, ref drawing, ref underworldFiltering, ref stencilEqual);
                    return;
                }

                context.DrawRenderers(renderingData.cullResults, ref drawing, ref underworldFiltering, ref stencilNotEqual);
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref overworldFiltering, ref stencilEqual);
            }

            private void DrawVisualLayersNormally(
                ScriptableRenderContext context,
                ref RenderingData renderingData,
                ref DrawingSettings drawing)
            {
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref overworldFiltering);
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref underworldFiltering);
            }
#endif

            private static RenderStateBlock CreateStencilState(CompareFunction compare, int stencilBit)
            {
                var stencil = new StencilState(
                    enabled: true,
                    readMask: (byte)stencilBit,
                    writeMask: (byte)stencilBit,
                    compareFunction: compare,
                    passOperation: StencilOp.Keep,
                    failOperation: StencilOp.Keep,
                    zFailOperation: StencilOp.Keep);

                return new RenderStateBlock(RenderStateMask.Stencil)
                {
                    stencilReference = stencilBit,
                    stencilState = stencil
                };
            }

            private static int SanitizeStencilBit(int value)
            {
                if (value <= 1) return 1;
                if (value <= 2) return 2;
                if (value <= 4) return 4;
                if (value <= 8) return 8;
                if (value <= 16) return 16;
                if (value <= 32) return 32;
                if (value <= 64) return 64;
                return 128;
            }
        }
    }
}
