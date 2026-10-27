using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Render.Urp;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class SpriteRimLightTests
    {
        [Test]
        public void ConeSelection_UsesTheVisibleCenterAndRejectsBehindTheLight()
        {
            var host = new GameObject("Synthetic cone");
            try
            {
                var light = host.AddComponent<SpriteRimLight>();
                light.Shape = LightType.Spot; light.Radius = Vector2.one * 2;
                Assert.That(light.TryGetInfluence(Vector3.down, out var away, out float score), Is.True);
                Assert.That(score, Is.EqualTo(.5f).Within(.00001f));
                Assert.That(away, Is.EqualTo(Vector2.down));
                Assert.That(light.TryGetInfluence(Vector3.up, out _, out _), Is.False);
                light.enabled = false;
                Assert.That(light.TryGetInfluence(Vector3.down, out _, out _), Is.False);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void RendererAssembly_DoesNotReferenceConsumerGameplay()
        {
            var assembly = typeof(SpriteRimLighting).Assembly;
            Assert.That(assembly.GetName().Name, Is.EqualTo("ZeroEngine.Render.Urp"));
            foreach (var reference in assembly.GetReferencedAssemblies())
                Assert.That(reference.Name.StartsWith("POB"), Is.False, reference.FullName);
        }
    }
}
