using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.WorldShift.Tests.Editor
{
    public sealed class WorldShiftStencilRendererFeatureSourceTests
    {
        [Test]
        public void StencilMaskShaderWritesStencilWithoutColor()
        {
            string path = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Shaders/WorldShiftStencilMask.shader");
            string source = File.ReadAllText(path);

            StringAssert.Contains("Shader \"Hidden/ZeroEngine/WorldShift/StencilMask\"", source);
            StringAssert.Contains("Name \"ClearWorldShiftStencilBit\"", source);
            StringAssert.Contains("Name \"WriteWorldShiftStencilMask\"", source);
            StringAssert.Contains("ColorMask 0", source);
            StringAssert.Contains("Cull Off", source);
            StringAssert.Contains("WriteMask [_StencilMask]", source);
            StringAssert.Contains("Pass Replace", source);
            StringAssert.Contains("ZEWorldShiftBinaryTargetMask", source);
        }

        [Test]
        public void StencilRendererFeatureUsesLayerMasksAndRenderStateStencil()
        {
            string path = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/URP/WorldShiftStencilRendererFeature.cs");
            string source = File.ReadAllText(path);

            StringAssert.Contains("ScriptableRendererFeature", source);
            StringAssert.Contains("FilteringSettings", source);
            StringAssert.Contains("RenderStateBlock", source);
            StringAssert.Contains("CompareFunction.Equal", source);
            StringAssert.Contains("CompareFunction.NotEqual", source);
            StringAssert.Contains("WorldShiftStencilRuntimeState.HasRegisteredController", source);
            StringAssert.Contains("WorldShiftStencilRuntimeState.TryGetActiveState", source);
            StringAssert.Contains("context.cmd.DrawRendererList", source);
            StringAssert.Contains("builder.UseRendererList", source);
            StringAssert.Contains("builder.SetRenderAttachmentDepth", source);
            StringAssert.Contains("DrawProcedural", source);
            StringAssert.Contains("ScreenScanMaskPassIndex", source);
            StringAssert.Contains("Universal2D", source);
            StringAssert.Contains("RenderQueueRange.opaque", source);
            StringAssert.Contains("RenderQueueRange.transparent", source);
            StringAssert.Contains("RenderPassEvent.BeforeRenderingTransparents", source);
            StringAssert.Contains("SortingCriteria.CommonTransparent", source);
        }

        [Test]
        public void StencilRendererFeatureUsesFullscreenScanMaskInsteadOfVisualGeometryMask()
        {
            string path = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/URP/WorldShiftStencilRendererFeature.cs");
            string source = File.ReadAllText(path);

            StringAssert.Contains("context.cmd.DrawProcedural", source);
            StringAssert.Contains("ScreenScanMaskPassIndex", source);
            StringAssert.DoesNotContain("maskDrawing.overrideMaterial", source);
            StringAssert.DoesNotContain("context.DrawRenderers(renderingData.cullResults, ref maskDrawing", source);
        }

        [Test]
        public void StencilMaskShaderContainsFullscreenScreenScanPass()
        {
            string path = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Shaders/WorldShiftStencilMask.shader");
            string source = File.ReadAllText(path);

            StringAssert.Contains("Name \"WriteWorldShiftScreenScanMask\"", source);
            StringAssert.Contains("_ZEWorldShiftOrigin", source);
            StringAssert.Contains("_ZEWorldShiftRadius", source);
            StringAssert.Contains("_ZEWorldShiftDirection", source);
            StringAssert.Contains("TransformWorldToHClip", source);
            StringAssert.Contains("DrawProcedural", File.ReadAllText(Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/URP/WorldShiftStencilRendererFeature.cs")));
        }

        [Test]
        public void StencilRendererFeatureDoesNotUseDoorPortalMaskPath()
        {
            string featurePath = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/URP/WorldShiftStencilRendererFeature.cs");
            string shaderPath = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Shaders/WorldShiftStencilMask.shader");
            string materialPath = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.world-shift/Runtime/Materials/ZE_WorldShiftStencilMask.mat");

            string feature = File.ReadAllText(featurePath);
            string shader = File.ReadAllText(shaderPath);
            string material = File.ReadAllText(materialPath);

            StringAssert.Contains("TryGetActiveState", feature);
            StringAssert.Contains("context.cmd.DrawProcedural", feature);
            StringAssert.Contains("ScreenScanMaskPassIndex", feature);
            StringAssert.DoesNotContain("WorldShiftTransitionMode.DoorPortal", feature);
            StringAssert.DoesNotContain("DrawPortalMaskRenderers", feature);
            StringAssert.DoesNotContain("_PortalOpenAmount", feature);
            StringAssert.DoesNotContain("Name \"WritePortalStencilMask\"", shader);
            StringAssert.DoesNotContain("_PortalOpenAmount", shader);
            StringAssert.DoesNotContain("input.uv.x", shader);
            StringAssert.DoesNotContain("_PortalOpenAmount", material);
        }
    }
}
