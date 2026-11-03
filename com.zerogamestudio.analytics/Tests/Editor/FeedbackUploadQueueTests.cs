using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

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
        private bool _previousRequireReceipt;

        [SetUp]
        public void SetUp()
        {
            _queueKey = "zgs_feedback_queue_tests_" + Guid.NewGuid().ToString("N");
            _root = Path.Combine(Path.GetTempPath(), _queueKey);
            Directory.CreateDirectory(_root);

            _previousServerUrl = AnalyticsConfig.ServerUrl;
            _previousSecret = AnalyticsConfig.Secret;
            _previousUploadSecret = AnalyticsConfig.UploadSecret;
            _previousRequireReceipt = AnalyticsConfig.RequireFeedbackReceipt;
            AnalyticsConfig.RequireFeedbackReceipt = false;
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
            AnalyticsConfig.RequireFeedbackReceipt = _previousRequireReceipt;

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
            StringAssert.Contains("checksum", PlayerPrefs.GetString(_queueKey));
            Assert.AreEqual(
                PlayerPrefs.GetString(_queueKey),
                PlayerPrefs.GetString(_queueKey + ".backup"));

            SetStaticField("_pendingUploads", null);
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
        }

        [TestCase("{\"status\":\"ok\",\"url\":\"https://feedback.example/feedback.zip\"}", false, true)]
        [TestCase("{\"status\":\"ok\",\"url\":\"https://feedback.example/feedback.zip\"}", true, false)]
        [TestCase("{\"status\":\"ok\"}", false, false)]
        [TestCase("{}", false, false)]
        [TestCase("<html>proxy</html>", false, false)]
        [TestCase("{\"receipt_version\":1,\"status\":\"ok\",\"url\":\"https://feedback.example/feedback.zip\"}", false, false)]
        public void UploadAcknowledgement_LegacyCompatibilityDoesNotAcceptUnprovenResponses(string json, bool strict, bool expected)
        {
            AnalyticsConfig.RequireFeedbackReceipt = strict;
            var method = typeof(FeedbackUploadQueue).GetMethod("TryReadUploadAcknowledgement", BindingFlags.NonPublic | BindingFlags.Static);
            object[] args = { json, new FeedbackUploadQueue.PendingUpload { submissionId = "1234567890abcdef1234567890abcdef" }, false };
            Assert.AreEqual(expected, method.Invoke(null, args));
        }

        [Test]
        public void LegacyMode_DoesNotQueryUnsupportedReceiptEndpoint()
        {
            AnalyticsConfig.RequireFeedbackReceipt = false;
            AnalyticsConfig.ServerUrl = "http://127.0.0.1:1";
            AnalyticsConfig.UploadSecret = "synthetic-test-secret";
            var method = typeof(FeedbackUploadQueue).GetMethod("QueryReceipt", BindingFlags.NonPublic | BindingFlags.Static);
            bool called = false;
            var query = (IEnumerator)method.Invoke(null, new object[] { new FeedbackUploadQueue.PendingUpload {
                    submissionId = "1234567890abcdef1234567890abcdef", fileSha256 = new string('a', 64), project = "TEST" },
                (Action<bool, bool>)((received, partial) => { called = true; Assert.IsFalse(received); }) });
            Assert.IsFalse(query.MoveNext(), "Legacy servers must not be probed for the new endpoint.");
            Assert.IsTrue(called);
        }

        [Test]
        public void QueuePersistence_CorruptPrimary_RecoversVerifiedBackup()
        {
            string zipPath = CreateZip("backup-recovery.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(zipPath, "POB_v1", "Tester"));

            PlayerPrefs.SetString(_queueKey, "{corrupt");
            PlayerPrefs.Save();
            SetStaticField("_pendingUploads", null);

            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
            Assert.IsTrue(File.Exists(zipPath));
        }

        [Test]
        public void TryEnqueue_WithSubmissionId_RoundTripsAndPublishesTypedSuccess()
        {
            string zipPath = CreateZip("typed-success.zip");
            const string submissionId = "0123456789abcdef0123456789abcdef";
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(
                submissionId,
                zipPath,
                "POB_v1",
                "Tester"));

            SetStaticField("_pendingUploads", null);
            StringAssert.Contains(submissionId, PlayerPrefs.GetString(_queueKey));

            int typedCount = 0;
            string observedId = null;
            FeedbackUploadQueue.QueuedSubmissionSucceeded += completion =>
            {
                typedCount++;
                observedId = completion.SubmissionId;
            };
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) => CompleteUpload(callback, true)));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(1, typedCount);
            Assert.AreEqual(submissionId, observedId);
        }

        [Test]
        public void DeliveryState_RetryThenReceipt_SurvivesReloadAndKeepsIdentity()
        {
            const string id = "1234567890abcdef1234567890abcdef";
            string path = CreateZip("delivery-state.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(id, path, "POB_v1", "Synthetic"));
            int calls = 0;
            SetStaticField("_tryUploadDetailedOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<FeedbackUploadAttempt>, IEnumerator>)((item, completed) =>
                {
                    Assert.AreEqual(id, item.idempotencyKey);
                    return CompleteUploadAttempt(completed, ++calls == 1
                        ? new FeedbackUploadAttempt(false, FeedbackFailureCodes.Timeout)
                        : new FeedbackUploadAttempt(true, partialAttachments: true));
                }));
            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
            Assert.AreEqual(FeedbackDeliveryState.Retrying, FeedbackUploadQueue.GetRecentSubmissions().Single().State);
            SetStaticField("_pendingUploads", null);
            FeedbackUploadQueue.Initialize();
            Assert.AreEqual(FeedbackDeliveryState.Retrying, FeedbackUploadQueue.GetRecentSubmissions().Single().State);
            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
            FeedbackUploadQueue.MarkSuccessNotified(id);
            SetStaticField("_pendingUploads", null);
            FeedbackSubmissionStatus received = FeedbackUploadQueue.GetRecentSubmissions().Single();
            Assert.AreEqual(FeedbackDeliveryState.Received, received.State);
            Assert.IsTrue(received.PartialAttachments);
            Assert.IsTrue(received.SuccessNotified);
            Assert.AreEqual(id, received.SubmissionId);
            Assert.AreEqual(2, calls);
        }

        [Test]
        public void DeliveryState_LaterRejectionCannotDisproveEarlierUnknownAttempt()
        {
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue("current", CreateZip("unknown-then-rejected.zip"), "POB_v1", "Synthetic"));
            int calls = 0;
            SetStaticField("_tryUploadDetailedOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<FeedbackUploadAttempt>, IEnumerator>)((_, completed) =>
                    CompleteUploadAttempt(completed, ++calls == 1
                        ? new FeedbackUploadAttempt(false, FeedbackFailureCodes.Timeout)
                        : new FeedbackUploadAttempt(false, "F403", 403))));
            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
            FeedbackSubmissionStatus status = FeedbackUploadQueue.GetRecentSubmissions().Single();
            Assert.AreEqual(FeedbackDeliveryState.NeedsAction, status.State);
            Assert.Greater(status.NextAttemptAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Assert.IsFalse(status.AutomaticRetryScheduled, "An unconfigured receiver cannot promise an automatic retry.");
            Assert.AreEqual("F403", status.FailureCode);
        }

        [Test]
        public void DeliveryState_ExpiryStopsRetriesWithoutInventingFailure()
        {
            string path = CreateZip("expired-status.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue("expiring", path, "POB_v1", "Synthetic"));
            var pending = (List<FeedbackUploadQueue.PendingUpload>)GetStaticField("_pendingUploads");
            pending[0].createdAt -= 8 * 24 * 60 * 60;
            pending[0].uncertainDelivery = true;
            int failures = 0;
            FeedbackUploadQueue.QueuedSubmissionFailed += _ => failures++;
            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
            FeedbackSubmissionStatus stopped = FeedbackUploadQueue.GetRecentSubmissions().Single();
            Assert.AreEqual(FeedbackDeliveryState.Stopped, stopped.State);
            Assert.IsFalse(stopped.AutomaticRetryScheduled);
            Assert.AreEqual(0, failures);
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void DeliveryState_DuplicateIdentityCannotCreateAnotherQueueItem()
        {
            const string id = "1234567890abcdef1234567890abcdef";
            string first = CreateZip("first-identity.zip");
            string second = CreateZip("second-identity.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(id, first, "POB_v1", "Synthetic"));
            Assert.IsFalse(FeedbackUploadQueue.TryEnqueue(id, second, "POB_v1", "Synthetic"));
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
            Assert.IsTrue(File.Exists(first));
            Assert.IsTrue(File.Exists(second));
        }

        [Test]
        public void QueuePersistence_UnreadableRecordsCannotBeOverwrittenByNewSubmission()
        {
            PlayerPrefs.SetString(_queueKey, "{damaged-primary");
            PlayerPrefs.SetString(_queueKey + ".backup", "{damaged-backup");
            PlayerPrefs.Save();
            SetStaticField("_pendingUploads", null);
            Assert.IsFalse(FeedbackUploadQueue.TryEnqueue(CreateZip("new-after-damage.zip"), "POB_v1", "Synthetic"));
            Assert.IsFalse(FeedbackUploadQueue.StorageReadable);
            Assert.AreEqual("{damaged-primary", PlayerPrefs.GetString(_queueKey));
            Assert.AreEqual("{damaged-backup", PlayerPrefs.GetString(_queueKey + ".backup"));
        }

        [Test]
        public void LegacyQueueItem_DoesNotPublishTypedSuccess()
        {
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(
                CreateZip("legacy-success.zip"),
                "POB_v1",
                "Tester"));

            int typedCount = 0;
            FeedbackUploadQueue.QueuedSubmissionSucceeded += _ => typedCount++;
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) => CompleteUpload(callback, true)));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(0, typedCount);
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
        public void TryEnqueue_WhenFull_PreservesQueueAndEveryZip()
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
        public void ProcessPendingUploads_NonRetryableFailure_BlocksBackgroundRetryButPreservesQueue()
        {
            const string id = "0123456789abcdef0123456789abcdef";
            string zipPath = CreateZip("forbidden.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(id, zipPath, "POB_v1", "Tester"));

            SetStaticField(
                "_tryUploadDetailedOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<FeedbackUploadAttempt>, IEnumerator>)
                ((_, callback) => CompleteUploadAttempt(
                    callback,
                    new FeedbackUploadAttempt(false, "F403", 403, "synthetic-forbidden", "server_forbidden"))));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            var pending = (List<FeedbackUploadQueue.PendingUpload>)GetStaticField("_pendingUploads");
            Assert.AreEqual(1, pending.Count);
            Assert.IsTrue(pending[0].blocked);
            Assert.AreEqual("F403", pending[0].lastFailureCode);
            Assert.Greater(pending[0].blockedUntil, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Assert.IsTrue(File.Exists(zipPath));
        }

        [Test]
        public void ProcessPendingUploads_WhenRemovalPersistenceFails_KeepsConfirmedSuccessAndOnlyRetriesCleanup()
        {
            string zipPath = CreateZip("removal-save-fails.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue("current-submission", zipPath, "POB_v1", "Tester"));

            int eventCount = 0;
            FeedbackUploadQueue.QueuedUploadSucceeded += _ => eventCount++;
            string failureCode = null;
            int uploadCalls = 0;
            FeedbackUploadQueue.QueuedSubmissionFailed += failure => failureCode = failure.FailureCode;
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) =>
                {
                    uploadCalls++;
                    SetStaticField("_persistQueueOverride", (Func<string, bool>)(_ => false));
                    return CompleteUpload(callback, true);
                }));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
            Assert.AreEqual(1, eventCount);
            Assert.IsNull(failureCode);
            Assert.AreEqual(FeedbackDeliveryState.Received, FeedbackUploadQueue.GetRecentSubmissions().Single().State);
            Assert.IsTrue(File.Exists(zipPath));
            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual(1, uploadCalls);
            SetStaticField("_persistQueueOverride", null);
            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
            Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
            Assert.IsFalse(File.Exists(zipPath));
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

        [Test]
        public void UploadWithRetry_NonRetryableFailure_AttemptsOnceAndPersistsCooldown()
        {
            // Permanent regression: legacy callers must preserve the same retry classification.
            int attempts = 0;
            int callbacks = 0;
            SetStaticField(
                "_tryUploadDetailedOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<FeedbackUploadAttempt>, IEnumerator>)
                ((_, callback) =>
                {
                    attempts++;
                    return CompleteUploadAttempt(callback,
                        new FeedbackUploadAttempt(false, "F403", 403, reason: "server_forbidden"));
                }));

            Exhaust(FeedbackUploadQueue.UploadWithRetry(
                CreateZip("legacy-forbidden.zip"), "TEST_v1", "Synthetic", success =>
                {
                    callbacks++;
                    Assert.IsFalse(success);
                }));

            Assert.AreEqual(1, attempts);
            Assert.AreEqual(1, callbacks);
            SetStaticField("_pendingUploads", null);
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
            var pending = (List<FeedbackUploadQueue.PendingUpload>)GetStaticField("_pendingUploads");
            Assert.IsTrue(pending[0].blocked);
            Assert.AreEqual("F403", pending[0].lastFailureCode);
            Assert.AreEqual(1, pending[0].retryCount);
            Assert.IsNotEmpty(pending[0].idempotencyKey);
        }

        [Test]
        public void UploadWithRetry_RetryableFailure_KeepsOneIdempotencyKeyThroughQueueReload()
        {
            var keys = new List<string>();
            SetStaticField(
                "_tryUploadDetailedOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<FeedbackUploadAttempt>, IEnumerator>)
                ((pending, callback) =>
                {
                    keys.Add(pending.idempotencyKey);
                    return CompleteUploadAttempt(callback,
                        new FeedbackUploadAttempt(false, FeedbackFailureCodes.Connection));
                }));

            Exhaust(FeedbackUploadQueue.UploadWithRetry(
                CreateZip("legacy-offline.zip"), "TEST_v1", "Synthetic", _ => { }));

            Assert.AreEqual(4, keys.Count);
            Assert.IsNotEmpty(keys[0]);
            Assert.IsTrue(keys.TrueForAll(key => key == keys[0]));
            SetStaticField("_pendingUploads", null);
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
            var pendingItems = (List<FeedbackUploadQueue.PendingUpload>)GetStaticField("_pendingUploads");
            Assert.AreEqual(keys[0], pendingItems[0].idempotencyKey);
            Assert.AreEqual(4, pendingItems[0].retryCount);
            Assert.IsFalse(pendingItems[0].blocked);
        }

        [Test]
        public void ProcessPendingUploads_ReadFailure_ReportsInReleaseModeAndPreservesRetryableItem()
        {
            const string id = "0123456789abcdef0123456789abcdef";
            string path = CreateZip("locked.zip");
            SetStaticField("_backgroundRunning", true);
            AnalyticsConfig.ServerUrl = "http://127.0.0.1:1";
            AnalyticsConfig.UploadSecret = "synthetic-test-secret";
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(id, path, "TEST_v1", "Synthetic"));
            bool previousDebugMode = AnalyticsConfig.DebugMode;
            int observed = 0;
            FeedbackUploadQueue.QueuedSubmissionFailed += _ => throw new InvalidOperationException();
            FeedbackUploadQueue.QueuedSubmissionFailed += failure =>
            {
                observed++;
                Assert.AreEqual(id, failure.SubmissionId);
                Assert.AreEqual(FeedbackFailureCodes.FileRead, failure.FailureCode);
            };
            try
            {
                AnalyticsConfig.DebugMode = false;
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("\\[FeedbackUpload\\] failed code=F103.*"));
                        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("\\[FeedbackUpload\\] failure_callback_failed code=F103.*"));
                        Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
                    }
                }
                Assert.AreEqual(2, observed, "A bad observer must not stop later observers or the next retry.");
                Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
                Assert.IsTrue(File.Exists(path));
                SetStaticField("_pendingUploads", null);
                Assert.AreEqual(1, FeedbackUploadQueue.PendingCount, "Failed attempts must survive queue reload.");
            }
            finally { AnalyticsConfig.DebugMode = previousDebugMode; }
        }

        [Test]
        public void BackgroundStart_DefersTransportUntilCallerCanRegisterLocalAcceptance()
        {
            MethodInfo method = typeof(FeedbackUploadQueue).GetMethod("BackgroundProcessLoop", BindingFlags.NonPublic | BindingFlags.Static);
            var loop = (IEnumerator)method.Invoke(null, null);
            try
            {
                Assert.IsTrue(loop.MoveNext());
                Assert.IsNull(loop.Current);
                Assert.IsFalse((bool)GetStaticField("_isProcessing"));
            }
            finally { (loop as IDisposable)?.Dispose(); }
        }

        [UnityTest]
        public IEnumerator HttpFailureThenRecovery_PreservesQueueAndPublishesActualResponse()
            => HttpFailureThenRecovery(true);

        [UnityTest]
        public IEnumerator LegacyHttpFailureThenRecovery_UsesExistingServerWithoutReceiptEndpoint()
            => HttpFailureThenRecovery(false);

        private IEnumerator HttpFailureThenRecovery(bool strict)
        {
            AnalyticsConfig.RequireFeedbackReceipt = strict;
            const string id = "0123456789abcdef0123456789abcdef";
            string path = CreateZip("http-recovery.zip");
            string fileHash = Sha256("zip");
            string receiptId = Sha256("feedback-submission-v1\0TEST\0" + id);
            string previousAppId = AnalyticsConfig.AppId;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task server = Task.Run(() =>
            {
                for (int attempt = 0; attempt < (strict ? 3 : 2); attempt++)
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 5000;
                        var header = new StringBuilder();
                        while (header.Length < 32768 && !header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                        {
                            int value = stream.ReadByte();
                            if (value < 0) throw new IOException("Incomplete synthetic upload header.");
                            header.Append((char)value);
                        }
                        var match = System.Text.RegularExpressions.Regex.Match(header.ToString(), @"(?im)^Content-Length:\s*(\d+)");
                        bool querying = strict && attempt == 1;
                        if (!header.ToString().StartsWith(querying ? "GET /upload/receipt " : "POST /upload ", StringComparison.Ordinal))
                            throw new IOException("Unexpected synthetic request path or method.");
                        if (!match.Success && !querying) throw new IOException("Synthetic upload has no content length.");
                        int remaining = match.Success ? int.Parse(match.Groups[1].Value) : 0;
                        byte[] buffer = new byte[4096];
                        while (remaining > 0)
                        {
                            int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                            if (read == 0) throw new IOException("Incomplete synthetic upload body.");
                            remaining -= read;
                        }
                        string status = attempt == 0 ? "403 Forbidden" : "200 OK";
                        string body = attempt == 0 ? "{}" : !strict
                            ? "{\"status\":\"ok\",\"url\":\"https://feedback.example/feedback.zip\"}"
                            : attempt == 1
                            ? "{\"receipt_version\":1,\"status\":\"unknown\"}"
                            : "{\"receipt_version\":1,\"status\":\"ok\",\"project\":\"TEST\",\"submission_id\":\"" + id +
                              "\",\"original_sha256\":\"" + fileHash + "\",\"receipt_id\":\"" + receiptId + "\"}";
                        byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nContent-Type: application/json\r\nX-Request-ID: synthetic-{attempt}\r\nConnection: close\r\n\r\n{body}");
                        stream.Write(response, 0, response.Length);
                    }
                }
            });
            try
            {
                SetStaticField("_backgroundRunning", true);
                AnalyticsConfig.ServerUrl = "http://127.0.0.1:" + port;
                AnalyticsConfig.AppId = "TEST";
                AnalyticsConfig.UploadSecret = "synthetic-test-secret";
                var failures = new List<FeedbackUploadFailure>();
                int successCount = 0;
                FeedbackUploadQueue.QueuedSubmissionFailed += failures.Add;
                FeedbackUploadQueue.QueuedSubmissionSucceeded += completion => { Assert.AreEqual(id, completion.SubmissionId); successCount++; };
                Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(id, path, "TEST_v1", "Synthetic"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("\\[FeedbackUpload\\] failed code=F403.*"));
                yield return DriveRequest(FeedbackUploadQueue.ProcessPendingUploads());
                Assert.AreEqual(1, failures.Count);
                Assert.AreEqual("F403", failures[0].FailureCode);
                Assert.AreEqual(403, failures[0].HttpStatusCode);
                Assert.AreEqual("synthetic-0", failures[0].RequestId);
                Assert.AreEqual(0, successCount);
                SetStaticField("_pendingUploads", null);
                Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
                Assert.IsTrue(File.Exists(path));
                yield return DriveRequest(FeedbackUploadQueue.ProcessPendingUploads());
                Assert.AreEqual(1, successCount);
                Assert.AreEqual(1, failures.Count);
                SetStaticField("_pendingUploads", null);
                Assert.AreEqual(0, FeedbackUploadQueue.PendingCount);
                Assert.IsFalse(File.Exists(path));
                Assert.IsTrue(server.Wait(1000), "Synthetic receiver did not finish.");
            }
            finally
            {
                listener.Stop();
                AnalyticsConfig.AppId = previousAppId;
                try { server.Wait(1000); } catch (AggregateException) { }
            }
        }

        private static IEnumerator DriveRequest(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            float deadline = Time.realtimeSinceStartup + 15f;
            try
            {
                while (stack.Count > 0)
                {
                    Assert.Less(Time.realtimeSinceStartup, deadline, "Synthetic upload exceeded its deadline.");
                    IEnumerator current = stack.Peek();
                    if (!current.MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    if (current.Current is AsyncOperation operation)
                    {
                        while (!operation.isDone)
                        {
                            Assert.Less(Time.realtimeSinceStartup, deadline, "Synthetic request did not finish.");
                            yield return null;
                        }
                    }
                    else yield return null;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
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

        private static IEnumerator CompleteUploadAttempt(
            Action<FeedbackUploadAttempt> callback,
            FeedbackUploadAttempt result)
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

        private static string Sha256(string text)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private static void ResetHooksAndSubscribers()
        {
            SetStaticField("_persistQueueOverride", null);
            SetStaticField("_deleteFileOverride", null);
            SetStaticField("_tryUploadOverride", null);
            SetStaticField("_tryUploadDetailedOverride", null);
            SetStaticField("QueuedUploadSucceeded", null);
            SetStaticField("QueuedSubmissionSucceeded", null);
            SetStaticField("QueuedSubmissionFailed", null);
            SetStaticField("SubmissionStateChanged", null);
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
