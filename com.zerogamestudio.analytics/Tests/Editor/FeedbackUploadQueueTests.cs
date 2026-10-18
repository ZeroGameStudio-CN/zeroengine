using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            StringAssert.Contains("checksum", PlayerPrefs.GetString(_queueKey));
            Assert.AreEqual(
                PlayerPrefs.GetString(_queueKey),
                PlayerPrefs.GetString(_queueKey + ".backup"));

            SetStaticField("_pendingUploads", null);
            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
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
        public void ProcessPendingUploads_WhenRemovalPersistenceFails_ReportsCodeWithoutDeletingOrNotifyingSuccess()
        {
            string zipPath = CreateZip("removal-save-fails.zip");
            Assert.IsTrue(FeedbackUploadQueue.TryEnqueue("current-submission", zipPath, "POB_v1", "Tester"));

            int eventCount = 0;
            FeedbackUploadQueue.QueuedUploadSucceeded += _ => eventCount++;
            string failureCode = null;
            FeedbackUploadQueue.QueuedSubmissionFailed += failure => failureCode = failure.FailureCode;
            SetStaticField(
                "_tryUploadOverride",
                (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                ((_, callback) => CompleteUpload(callback, true)));
            SetStaticField("_persistQueueOverride", (Func<string, bool>)(_ => false));

            Exhaust(FeedbackUploadQueue.ProcessPendingUploads());

            Assert.AreEqual(1, FeedbackUploadQueue.PendingCount);
            Assert.AreEqual(0, eventCount);
            Assert.AreEqual("F102", failureCode);
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
        {
            const string id = "0123456789abcdef0123456789abcdef";
            string path = CreateZip("http-recovery.zip");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task server = Task.Run(() =>
            {
                for (int attempt = 0; attempt < 2; attempt++)
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
                        if (!match.Success) throw new IOException("Synthetic upload has no content length.");
                        int remaining = int.Parse(match.Groups[1].Value);
                        byte[] buffer = new byte[4096];
                        while (remaining > 0)
                        {
                            int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                            if (read == 0) throw new IOException("Incomplete synthetic upload body.");
                            remaining -= read;
                        }
                        string status = attempt == 0 ? "403 Forbidden" : "200 OK";
                        byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 2\r\nContent-Type: application/json\r\nX-Request-ID: synthetic-{attempt}\r\nConnection: close\r\n\r\n{{}}");
                        stream.Write(response, 0, response.Length);
                    }
                }
            });
            try
            {
                SetStaticField("_backgroundRunning", true);
                AnalyticsConfig.ServerUrl = "http://127.0.0.1:" + port;
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

        private static void ResetHooksAndSubscribers()
        {
            SetStaticField("_persistQueueOverride", null);
            SetStaticField("_deleteFileOverride", null);
            SetStaticField("_tryUploadOverride", null);
            SetStaticField("_tryUploadDetailedOverride", null);
            SetStaticField("QueuedUploadSucceeded", null);
            SetStaticField("QueuedSubmissionSucceeded", null);
            SetStaticField("QueuedSubmissionFailed", null);
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
