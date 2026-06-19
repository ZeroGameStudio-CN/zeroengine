using NUnit.Framework;
using UnityEngine;

namespace ZGS.Analytics.Editor.Tests
{
    public sealed class AnalyticsBootstrapTests
    {
        [SetUp]
        public void SetUp()
        {
            AnalyticsBootstrap.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            AnalyticsBootstrap.Shutdown();
        }

        [Test]
        public void InitializeWithNullConfigDoesNotInitialize()
        {
            AnalyticsBootstrap.Initialize(null);

            Assert.IsFalse(AnalyticsBootstrap.IsInitialized);
        }

        [Test]
        public void InitializeWithDisabledConfigDoesNotInitialize()
        {
            var config = ScriptableObject.CreateInstance<ZGSAnalyticsConfig>();
            try
            {
                config.EnableAnalytics = false;

                AnalyticsBootstrap.Initialize(config);

                Assert.IsFalse(AnalyticsBootstrap.IsInitialized);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
