using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class PlatformNavigationBootstrapTests
    {
        [Test]
        public void EnsureOn_AddsMissingComponentsAndBindsGraph()
        {
            var host = new GameObject("PlatformNavigationBootstrapTest");
            try
            {
                var components = PlatformNavigationBootstrap.EnsureOn(host);

                Assert.IsNotNull(components.GraphGenerator);
                Assert.IsNotNull(components.JumpLinkCalculator);
                Assert.IsNotNull(components.Pathfinder);
                Assert.AreSame(components.GraphGenerator, components.Pathfinder.GraphGenerator);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
