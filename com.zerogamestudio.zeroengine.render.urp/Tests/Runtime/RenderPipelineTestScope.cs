#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZeroEngine.Render.Tests
{
    [SetUpFixture]
    public sealed class RenderPipelineTestScope
    {
        private RenderPipelineAsset previous;
        private UniversalRenderPipelineAsset ownedPipeline;
        private UniversalRendererData ownedRenderer;

        [OneTimeSetUp]
        public void ProvidePipelineForStandalonePackageTests()
        {
            previous = QualitySettings.renderPipeline;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset) return;

            ownedRenderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            ownedPipeline = UniversalRenderPipelineAsset.Create(ownedRenderer);
            ownedPipeline.supportsHDR = false;
            ownedPipeline.msaaSampleCount = 1;
            QualitySettings.renderPipeline = ownedPipeline;
        }

        [OneTimeTearDown]
        public void RestorePipelineWithoutSavingProjectSettings()
        {
            if (!ownedPipeline) return;
            QualitySettings.renderPipeline = previous;
            Object.DestroyImmediate(ownedPipeline);
            Object.DestroyImmediate(ownedRenderer);
        }
    }
}
#endif
