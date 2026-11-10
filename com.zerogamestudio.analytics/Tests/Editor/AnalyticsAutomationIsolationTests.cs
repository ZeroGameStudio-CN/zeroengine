using System;
using System.Reflection;
using NUnit.Framework;

namespace ZGS.Analytics.Tests.Editor
{
    // Core contract: isolated startup never opens the user's persistent queues.
    public class AnalyticsAutomationIsolationTests
    {
        private static void InvokeStartup(string method)
        {
            typeof(AnalyticsBootstrap).GetMethod(method,
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }

        [SetUp]
        public void SetUp() => InvokeStartup("ResetStartup");

        [TearDown]
        public void TearDown() => InvokeStartup("ResetStartup");

        [Test]
        public void Isolation_DefaultsOffAndLatchesBeforeBootstrap()
        {
            Assert.IsFalse(AnalyticsBootstrap.IsAutomationIsolated);
            AnalyticsBootstrap.DisableForAutomation();
            InvokeStartup("AutoInitialize");
            Assert.IsTrue(AnalyticsBootstrap.IsAutomationIsolated);
            Assert.Throws<InvalidOperationException>(AnalyticsBootstrap.DisableForAutomation);
        }

        [Test]
        public void Isolation_RefusesFeedbackAndDoesNotCreateStorage()
        {
            AnalyticsBootstrap.DisableForAutomation();
            Assert.IsFalse(AnalyticsConfig.IsConfigured);
            Assert.IsFalse(AnalyticsConfig.IsUploadConfigured);
            FeedbackUploadQueue.Initialize();
            FeedbackUploadQueue.ClearQueue();
            FeedbackUploadQueue.StartBackgroundProcessing();
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
            Assert.IsFalse(FeedbackUploadQueue.TryEnqueue("missing.zip", "fixture", "fixture"));
            Assert.IsFalse(FeedbackUploadQueue.ProcessPendingUploads().MoveNext());
            Assert.Throws<InvalidOperationException>(() => { _ = FeedbackUploadQueue.FeedbackDirectory; });
            bool? uploaded = null;
            Assert.IsFalse(FeedbackUploadQueue.UploadWithRetry("missing.zip", "fixture", "fixture",
                value => uploaded = value).MoveNext());
            Assert.AreEqual(false, uploaded);
        }
    }
}
