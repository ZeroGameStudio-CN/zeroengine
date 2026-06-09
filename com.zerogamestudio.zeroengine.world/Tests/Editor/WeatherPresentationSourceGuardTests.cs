using System.IO;
using NUnit.Framework;

namespace ZeroEngine.Tests.World
{
    [TestFixture]
    [Category("Boundary")]
    public sealed class WeatherPresentationSourceGuardTests
    {
        private const string EnvironmentRoot =
            @"D:\unity\projects\ZeroEngine\Packages\com.zerogamestudio.zeroengine.world\Runtime\Environment";

        [Test]
        public void WeatherManager_DoesNotOwnPresentationSideEffects()
        {
            string source = File.ReadAllText(Path.Combine(EnvironmentRoot, "WeatherManager.cs"));

            Assert.That(source, Does.Not.Contain("RenderSettings"));
            Assert.That(source, Does.Not.Contain("AudioSource"));
            Assert.That(source, Does.Not.Contain("StartCoroutine"));
            Assert.That(source, Does.Not.Contain("Instantiate("));
            Assert.That(source, Does.Not.Match(@"\bDestroy\s*\("));
            Assert.That(source, Does.Not.Contain("_activeVfx"));
            Assert.That(source, Does.Not.Contain("_followTarget"));
            Assert.That(source, Does.Not.Contain("LateUpdate"));
            Assert.That(source, Does.Not.Contain("DefaultWeatherPresentationAdapter"));
        }

        [Test]
        public void DefaultWeatherPresentationAdapter_OwnsLegacyPresentationSideEffects()
        {
            string source = File.ReadAllText(Path.Combine(EnvironmentRoot, "DefaultWeatherPresentationAdapter.cs"));

            Assert.That(source, Does.Contain("RenderSettings"));
            Assert.That(source, Does.Contain("AudioSource"));
            Assert.That(source, Does.Contain("StartCoroutine"));
            Assert.That(source, Does.Contain("Instantiate("));
            Assert.That(source, Does.Match(@"\bDestroy\s*\("));
            Assert.That(source, Does.Contain("IWeatherFollowTargetAdapter"));
        }

        [Test]
        public void WeatherState_DoesNotOwnPresentationSideEffects()
        {
            string source = File.ReadAllText(Path.Combine(EnvironmentRoot, "WeatherState.cs"));

            Assert.That(source, Does.Not.Contain("RenderSettings"));
            Assert.That(source, Does.Not.Contain("AudioSource"));
            Assert.That(source, Does.Not.Contain("StartCoroutine"));
            Assert.That(source, Does.Not.Contain("Instantiate("));
            Assert.That(source, Does.Not.Match(@"\bDestroy\s*\("));
            Assert.That(source, Does.Not.Contain("GameObject"));
            Assert.That(source, Does.Not.Contain("MonoBehaviour"));
        }
    }
}
