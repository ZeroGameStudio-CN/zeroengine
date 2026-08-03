using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ZGS.Analytics.Tests.Editor
{
    [TestFixture]
    public sealed class FeedbackUploadQueueTests
    {
        private string _queueKey;
        private string _root;
        private string _previousServerUrl;
        private string _previousSecret;
        private string _previousUploadSecret;
        private int _uploadCalls;

        [SetUp]
        public void SetUp()
        {
            _queueKey = "zgs_feedback_queue_tests_" + Guid.NewGuid().ToString("N");
            _root = Path.Combine(Path.GetTempPath(), _queueKey);
            Directory.CreateDirectory(_root);

            _previousServerUrl = AnalyticsConfig.ServerUrl;
            _previousSecret = AnalyticsConfig.Secret;
            _previousUploadSecret = AnalyticsConfig.UploadSecret;
            AnalyticsConfig.ServerUrl = string.Empty;
            AnalyticsConfig.Secret = string.Empty;
            AnalyticsConfig.UploadSecret = string.Empty;

            SetStaticField("_queueKeyOverride", _queueKey);
            ResetHooksAndSubscribers();
            PlayerPrefs.DeleteKey(_queueKey);
            PlayerPrefs.Save();
            FeedbackUploadQueue.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            ResetHooksAndSubscribers();
            FeedbackUploadQueue.ClearQueue();
            PlayerPrefs.DeleteKey(_queueKey);
            PlayerPrefs.Save();
            SetStaticField("_queueKeyOverride", null);

            AnalyticsConfig.ServerUrl = _previousServerUrl;
            AnalyticsConfig.Secret = _previousSecret;
            AnalyticsConfig.UploadSecret = _previousUploadSecret;

            try
            {
                Directory.Delete(_root, true);
            }
            catch
            {
                // Ignore cleanup failures from transient file locks.
            }
        }

        [Test]
        public void TryEnqueue_WithExistingZip_PersistsBeforeReturningTrue()
        {
            string zipPath = CreateZip("accepted.zip");

            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(zipPath, "POB_v1", "Tester"));
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);

            SetStaticField("_pendingUploads", null);
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
        }

        [Test]
        public void TryEnqueue_WithMissingZipOrPersistenceFailure_LeavesQueueUnchanged()
        {
            Assert.IsFalse(FeedbackUploadQueue.TryEnqueue(
                Path.Combine(_root, "missing.zip"),
                "POB_v1",
                "Tester"));

            string zipPath = CreateZip("save-fails.zip");
            SetStaticField("_persistQueueOverride", (Func<string, bool>)(_ => false));

            Assert.IsFalse(FeedbackUploadQueue.TryEnqueue(zipPath, "POB_v1", "Tester"));
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
            Assert.IsTrue(File.Exists(zipPath));
            Assert.IsFalse(PlayerPrefs.HasKey(_queueKey));
        }

        [Test]
        public void TryEnqueue_WhenFullAndPersistenceFails_PreservesQueueAndEveryZip()
        {
            var originalPaths = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                string path = CreateZip($"queued-{i}.zip");
                originalPaths.Add(path);
                Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(path, "POB_v1", "Tester"));
            }

            string originalJson = PlayerPrefs.GetString(_queueKey);
            string candidatePath = CreateZip("candidate.zip");
            SetStaticField("_persistQueueOverride", (Func<string, bool>)(_ => false));

            Assert.IsFalse(FeedbackUploadQueue.TryEnqueue(candidatePath, "POB_v1", "Tester"));
            Assert.AreEqual(10, FeedbackUploadQueue.PendingCount);
            Assert.AreEqual(originalJson, PlayerPrefs.GetString(_queueKey));
            Assert.IsTrue(File.Exists(candidatePath));
            Assert.IsTrue(originalPaths.TrueForAll(File.Exists));
        }

        [Test]
        public void ProcessPendingUploads_OnSuccess_PersistsRemovalBeforeDeleteAndEvent()
        {
            string zipPath = CreateZip("success.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(zipPath, "POB_v1", "Tester"));

            int eventCount = 0;
            FeedbackUploadQueue.QueuedUploadSucceeded += _ => eventCount++;
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) => CompleteUpload(callback, true)));
            SetStaticField("_deleteFileOverride", (Action<string>)(_ => throw new IOException("delete failed")));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
            Assert.AreEqual(1, eventCount);
            Assert.IsTrue(File.Exists(zipPath));

            SetStaticField("_pendingUploads", null);
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
        }

        [Test]
        public void ProcessPendingUploads_WhenRemovalPersistenceFails_DoesNotDeleteOrNotify()
        {
            string zipPath = CreateZip("removal-save-fails.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(zipPath, "POB_v1", "Tester"));

            int eventCount = 0;
            FeedbackUploadQueue.QueuedUploadSucceeded += _ => eventCount++;
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) => CompleteUpload(callback, true)));
            SetStaticField("_persistQueueOverride", (Func<string, bool>)(_ => false));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
            Assert.AreEqual(0, eventCount);
            Assert.IsTrue(File.Exists(zipPath));
        }

        [Test]
        public void ProcessPendingUploads_IsolatesSubscriberFailureAndContinuesBatch()
        {
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(CreateZip("first.zip"), "POB_v1", "Tester"));
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(CreateZip("second.zip"), "POB_v1", "Tester"));

            int eventCount = 0;
            FeedbackUploadQueue.QueuedUploadSucceeded += _ => throw new InvalidOperationException("UI failed");
            FeedbackUploadQueue.QueuedUploadSucceeded += _ => eventCount++;
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) => CompleteUpload(callback, true)));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(2, eventCount);
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
            Assert.IsFalse((bool)GetStaticField("_isProcessing"));
        }

        [Test]
        public void ProcessPendingUploads_ProcessesItemEnqueuedDuringActiveBatchBeforeReturning()
        {
            string secondPath = CreateZip("second-during-batch.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(CreateZip("first-active.zip"), "POB_v1", "Tester"));

            _uploadCalls = 0;
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) => CompleteUploadWithEnqueue(callback, secondPath)));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(2, _uploadCalls);
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
        }

        [Test]
        public void RetryWait_WhenNewItemArrives_StopsAfterAtMostOneSecondSlice()
        {
            int observedVersion = (int)GetStaticField("_queueMutationVersion");
            MethodInfo method = typeof(FeedbackUploadQueue).GetMethod(
                "WaitForRetryOrQueueMutation",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            var wait = (IEnumerator)method.Invoke(null, new object[] { 60f, observedVersion });

            Assert.IsTrue(wait.MoveNext());
            var slice = wait.Current as WaitForSecondsRealtime;
            Assert.IsNotNull(slice);
            Assert.LessOrEqual(slice.waitTime, 1f);

            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(
                CreateZip("wake.zip"),
                "POB_v1",
                "Tester"));
            Assert.IsFalse(wait.MoveNext());
        }

        [Test]
        public void UploadWithRetry_DoesNotPublishQueuedSuccessEvent()
        {
            int eventCount = 0;
            FeedbackUploadQueue.QueuedUploadSucceeded += _ => eventCount++;

            Exhaust(FeedbackUploadQueue.UploadWithRetry(
                CreateZip("legacy-retry.zip"),
                "POB_v1",
                "Tester",
                _ => { }));

            Assert.AreEqual(0, eventCount);
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
        }

        private IEnumerator CompleteUploadWithEnqueue(
            Action<bool> callback,
            string secondPath)
        {
            _uploadCalls++;
            if (_uploadCalls == 1)
                Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(secondPath, "POB_v1", "Tester"));

            callback(true);
            yield break;
        }

        private static IEnumerator CompleteUpload(Action<bool> callback, bool result)
        {
            callback(result);
            yield break;
        }

        private string CreateZip(string fileName)
        {
            string path = Path.Combine(_root, fileName);
            File.WriteAllText(path, "zip");
            return path;
        }

        private static void Exhaust(IEnumerator coroutine)
        {
            while (coroutine.MoveNext())
            {
                if (coroutine.Current is IEnumerator nested)
                    Exhaust(nested);
            }
        }

        private static void ResetHooksAndSubscribers()
        {
            SetStaticField("_persistQueueOverride", null);
            SetStaticField("_deleteFileOverride", null);
            SetStaticField("_tryUploadOverride", null);
            SetStaticField("QueuedUploadSucceeded", null);
            SetStaticField("_backgroundRunning", false);
            SetStaticField("_isProcessing", false);
            SetStaticField("_pendingUploads", null);
        }

        private static object GetStaticField(string fieldName)
        {
            FieldInfo field = typeof(FeedbackUploadQueue).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(null);
        }

        private static void SetStaticField(string fieldName, object value)
        {
            FieldInfo field = typeof(FeedbackUploadQueue).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(null, value);
        }
    }
}
