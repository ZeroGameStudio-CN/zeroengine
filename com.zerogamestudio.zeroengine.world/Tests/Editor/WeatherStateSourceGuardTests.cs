using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;
using ZeroEngine.EnvironmentSystem;

namespace ZeroEngine.Tests.World
{
    [TestFixture]
    [Category("Boundary")]
    public sealed class WeatherStateSourceGuardTests
    {
        [Test]
        public void WeatherState_DoesNotOwnPresentationSideEffects()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(WeatherState).Assembly);
            Assert.NotNull(packageInfo);
            string source = File.ReadAllText(Path.Combine(packageInfo.resolvedPath, "Runtime", "Environment", "WeatherState.cs"));

            Assert.That(source, Does.Not.Contain("RenderSettings"));
            Assert.That(source, Does.Not.Contain("AudioSource"));
            Assert.That(source, Does.Not.Contain("Instantiate"));
            Assert.That(source, Does.Not.Contain("Destroy"));
            Assert.That(source, Does.Not.Contain("StartCoroutine"));
        }
    }
}
