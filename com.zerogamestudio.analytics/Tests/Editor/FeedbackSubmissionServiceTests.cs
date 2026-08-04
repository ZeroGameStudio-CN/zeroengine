using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ZGS.Analytics.Tests.Editor
{
    [TestFixture]
    public sealed class FeedbackSubmissionServiceTests
    {
        private string _queueKey;
        private string _previousServerUrl;
        private string _previousSecret;
        private string _previousUploadSecret;
        private string _previousAppId;

        [SetUp]
        public void SetUp()
        {
            _queueKey = "zgs_feedback_submission_tests_" + Guid.NewGuid().ToString("N");
            _previousServerUrl = AnalyticsConfig.ServerUrl;
            _previousSecret = AnalyticsConfig.Secret;
            _previousUploadSecret = AnalyticsConfig.UploadSecret;
            _previousAppId = AnalyticsConfig.AppId;

            AnalyticsConfig.ServerUrl = string.Empty;
            AnalyticsConfig.Secret = string.Empty;
            AnalyticsConfig.UploadSecret = string.Empty;
            AnalyticsConfig.AppId = "TEST";
            SetQueueField("_queueKeyOverride", _queueKey);
            SetQueueField("_pendingUploads", null);
            SetQueueField("_persistQueueOverride", null);
            SetQueueField("_tryUploadOverride", null);
            SetQueueField("_backgroundRunning", false);
            SetQueueField("QueuedSubmissionSucceeded", null);
            PlayerPrefs.DeleteKey(_queueKey);
            FeedbackUploadQueue.Initialize();
            InvokeSubmissionReset();
        }

        [TearDown]
        public void TearDown()
        {
            InvokeSubmissionReset();
            SetQueueField("_backgroundRunning", true);
            FeedbackUploadQueue.ClearQueue();
            PlayerPrefs.DeleteKey(_queueKey);
            PlayerPrefs.Save();
            SetQueueField("_queueKeyOverride", null);
            SetQueueField("_pendingUploads", null);
            SetQueueField("_persistQueueOverride", null);
            SetQueueField("_tryUploadOverride", null);
            SetQueueField("QueuedSubmissionSucceeded", null);

            AnalyticsConfig.ServerUrl = _previousServerUrl;
            AnalyticsConfig.Secret = _previousSecret;
            AnalyticsConfig.UploadSecret = _previousUploadSecret;
            AnalyticsConfig.AppId = _previousAppId;
        }

        [Test]
        public void Submit_InvalidRequest_CompletesExactlyOnce()
        {
            int callbackCount = 0;
            FeedbackSubmissionResult observed = default;

            Exhaust(FeedbackSubmissionService.Submit(
                new FeedbackSubmissionRequest { UserMessage = "  " },
                result =>
                {
                    callbackCount++;
                    observed = result;
                }));

            Assert.AreEqual(1, callbackCount);
            Assert.IsFalse(observed.AcceptedLocally);
            Assert.AreEqual(FeedbackSubmissionFailure.InvalidRequest, observed.Failure);
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
        }

        [Test]
        public void Submit_NotConfigured_CompletesExactlyOnce()
        {
            int callbackCount = 0;
            FeedbackSubmissionResult observed = default;

            Exhaust(FeedbackSubmissionService.Submit(
                new FeedbackSubmissionRequest { UserMessage = "Broken" },
                result =>
                {
                    callbackCount++;
                    observed = result;
                }));

            Assert.AreEqual(1, callbackCount);
            Assert.AreEqual(FeedbackSubmissionFailure.NotConfigured, observed.Failure);
        }

        [Test]
        public void Submit_Configured_PersistsQueueBeforeReturningAccepted()
        {
            ConfigureUpload();
            SetQueueField("_backgroundRunning", true);
            int callbackCount = 0;
            FeedbackSubmissionResult observed = default;

            Exhaust(FeedbackSubmissionService.Submit(
                new FeedbackSubmissionRequest { UserMessage = "Broken", UserName = "Tester" },
                result =>
                {
                    callbackCount++;
                    observed = result;
                }));

            Assert.AreEqual(1, callbackCount);
            Assert.IsTrue(observed.AcceptedLocally);
            Assert.AreEqual(32, observed.SubmissionId.Length);
            StringAssert.Contains(observed.SubmissionId, PlayerPrefs.GetString(_queueKey));
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
        }

        [Test]
        public void Submit_ContributorFailure_DoesNotRejectCoreFeedback()
        {
            ConfigureUpload();
            SetQueueField("_backgroundRunning", true);
            FeedbackSubmissionResult observed = default;

            Exhaust(FeedbackSubmissionService.Submit(
                new FeedbackSubmissionRequest
                {
                    UserMessage = "Broken",
                    Contributors = new IFeedbackPackageContributor[] { new ThrowingContributor() }
                },
                result => observed = result));

            Assert.IsTrue(observed.AcceptedLocally);
            StringAssert.Contains("InvalidOperationException", PlayerPrefs.GetString(_queueKey).Length > 0
                ? ReadQueuedManifest()
                : string.Empty);
        }

        [Test]
        public void LegacyDefaultUploader_UsesQueueFirstPathWithoutForegroundUpload()
        {
            ConfigureUpload();
            SetQueueField("_backgroundRunning", true);
            int foregroundUploadCalls = 0;
            SetQueueField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) =>
                {
                    foregroundUploadCalls++;
                    callback(false);
                    return Empty();
                }));

            Exhaust(new ZipAttachmentUploader().Upload(new AttachmentUploadRequest
            {
                UserMessage = "Broken",
                UserName = "Tester"
            }));

            Assert.AreEqual(0, foregroundUploadCalls);
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
        }

        [Test]
        public void TryCreateFeedbackZip_WritesLogicalContributorDataWithoutPrivateManifestValues()
        {
            string sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sav");
            File.WriteAllText(sourcePath, "save");
            string zipPath = null;

            try
            {
                var request = new FeedbackSubmissionRequest
                {
                    UserMessage = "private body",
                    Contact = "private@example.com",
                    UserName = "Tester"
                };
                var collector = new FeedbackPackageCollector();
                collector.AddFile(
                    sourcePath,
                    "SaveData/main.sav",
                    FeedbackAttachmentKind.ProjectState,
                    FeedbackAttachmentPriority.ProjectState);
                collector.AddText("Diagnostics/System.txt", "system", FeedbackAttachmentPriority.Diagnostic);
                collector.AddMetadata("build", "123");
                collector.AddMetadata("auth_token", "secret-value");

                Assert.IsTrue(ZipAttachmentUploader.TryCreateFeedbackZip(
                    request,
                    collector,
                    "0123456789abcdef0123456789abcdef",
                    "{}",
                    out zipPath,
                    out _,
                    out _));

                using ZipArchive zip = ZipFile.OpenRead(zipPath);
                string[] names = zip.Entries.Select(entry => entry.FullName).ToArray();
                Assert.IsTrue(names.Any(name => name.EndsWith("/SaveData/main.sav", StringComparison.Ordinal)));
                Assert.IsTrue(names.Any(name => name.EndsWith("/Diagnostics/System.txt", StringComparison.Ordinal)));
                string manifestName = names.Single(name => name.EndsWith("/UploadManifest.json", StringComparison.Ordinal));
                string manifest = ReadEntry(zip, manifestName);
                StringAssert.Contains("0123456789abcdef0123456789abcdef", manifest);
                StringAssert.Contains("SaveData/main.sav", manifest);
                StringAssert.Contains("<redacted>", manifest);
                StringAssert.DoesNotContain(sourcePath, manifest);
                StringAssert.DoesNotContain("private body", manifest);
                StringAssert.DoesNotContain("private@example.com", manifest);
                StringAssert.DoesNotContain("secret-value", manifest);
            }
            finally
            {
                if (File.Exists(sourcePath))
                    File.Delete(sourcePath);
                if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
                    File.Delete(zipPath);
            }
        }

        private string ReadQueuedManifest()
        {
            var pending = (IList<FeedbackUploadQueue.PendingUpload>)typeof(FeedbackUploadQueue)
                .GetField("_pendingUploads", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            string zipPath = pending.Single().zipPath;
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            string name = zip.Entries.Single(entry =>
                entry.FullName.EndsWith("/UploadManifest.json", StringComparison.Ordinal)).FullName;
            return ReadEntry(zip, name);
        }

        private static string ReadEntry(ZipArchive zip, string name)
        {
            using Stream stream = zip.GetEntry(name).Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void ConfigureUpload()
        {
            AnalyticsConfig.ServerUrl = "https://example.invalid";
            AnalyticsConfig.UploadSecret = "test-secret";
        }

        private static void Exhaust(IEnumerator coroutine)
        {
            while (coroutine.MoveNext())
            {
                if (coroutine.Current is IEnumerator nested)
                    Exhaust(nested);
            }
        }

        private static IEnumerator Empty()
        {
            yield break;
        }

        private static void InvokeSubmissionReset()
        {
            MethodInfo method = typeof(FeedbackSubmissionService).GetMethod(
                "ResetRuntimeState",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            method.Invoke(null, null);
        }

        private static void SetQueueField(string name, object value)
        {
            FieldInfo field = typeof(FeedbackUploadQueue).GetField(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, name);
            field.SetValue(null, value);
        }

        private sealed class ThrowingContributor : IFeedbackPackageContributor
        {
            public void Collect(FeedbackPackageContext context, FeedbackPackageCollector collector)
            {
                throw new InvalidOperationException("private contributor message");
            }
        }
    }
}
