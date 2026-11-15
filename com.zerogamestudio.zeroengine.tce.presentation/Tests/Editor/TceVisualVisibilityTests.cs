using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.TCE.Presentation.Tests
{
    public sealed class TceVisualVisibilityTests
    {
        private GameObject root;
        [TearDown] public void Cleanup() { if(root) Object.DestroyImmediate(root); }

        [Test]
        public void SuppressionPairsRendererFlagsAndLightMasksWithoutChangingEnabledState()
        {
            root=new GameObject("Borrowed source");root.SetActive(false);
            var renderer=root.AddComponent<SpriteRenderer>();var light=root.AddComponent<Light>();
            light.cullingMask=37; light.enabled=false;
            var visibility=new TceVisualVisibility(root);
            visibility.SetHidden(true);
            Assert.That(renderer.forceRenderingOff,Is.True);
            Assert.That(light.cullingMask,Is.Zero);Assert.That(light.enabled,Is.False);
            visibility.SetHidden(false);
            Assert.That(renderer.forceRenderingOff,Is.False);
            Assert.That(light.cullingMask,Is.EqualTo(37));Assert.That(light.enabled,Is.False);
        }

        [Test]
        public void SourceAndDisplayHaveExplicitlyDifferentInitialSuppressionOwnership()
        {
            root=new GameObject("Initially suppressed source");root.SetActive(false);
            var renderer=root.AddComponent<SpriteRenderer>();renderer.forceRenderingOff=true;
            var source=new TceVisualVisibility(root);
            source.SetHidden(true);source.SetHidden(false);
            Assert.That(renderer.forceRenderingOff,Is.True);
            var display=new TceVisualVisibility(root,preserveRendererSuppression:false);
            display.SetHidden(true);display.SetHidden(false);
            Assert.That(renderer.forceRenderingOff,Is.False);
            display.Restore();Assert.That(renderer.forceRenderingOff,Is.True);
        }

        [Test]
        public void DestroyedNodesDoNotBreakLateRestoration()
        {
            root=new GameObject("Retiring graph");root.SetActive(false);
            root.AddComponent<SpriteRenderer>();root.AddComponent<Light>();
            var visibility=new TceVisualVisibility(root);visibility.SetHidden(true);
            Object.DestroyImmediate(root);
            Assert.DoesNotThrow(()=>visibility.SetHidden(false));
        }
    }
}
