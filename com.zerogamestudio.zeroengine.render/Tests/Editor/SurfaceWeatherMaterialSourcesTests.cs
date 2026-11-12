using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Render.Weather;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class SurfaceWeatherMaterialSourcesTests
    {
        [Test]
        public void RendererNotification_UsesExactTargetAndReleasesSubscriber()
        {
            var root = new GameObject("Synthetic material notification");
            var renderer = root.AddComponent<MeshRenderer>();
            Renderer observed = null;
            int calls = 0;
            void Observe(Renderer value) { observed = value; calls++; }
            SurfaceWeatherMaterialSources.RendererMaterialsChanged += Observe;
            try
            {
                SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(null);
                Assert.That(calls, Is.Zero);
                SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(renderer);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(observed, Is.SameAs(renderer));
                SurfaceWeatherMaterialSources.RendererMaterialsChanged -= Observe;
                SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(renderer);
                Assert.That(calls, Is.EqualTo(1));
            }
            finally
            {
                SurfaceWeatherMaterialSources.RendererMaterialsChanged -= Observe;
                Object.DestroyImmediate(root);
            }
        }
    }
}
