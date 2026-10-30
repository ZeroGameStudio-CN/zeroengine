using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZGS.Analytics.Tests.Editor
{
    // Core contract: explicit diagnostics, immutable event identity and durable delivery.
    public sealed class DiagnosticReporterTests
    {
        private string key;
        private OfflineQueue queue;

        [SetUp] public void SetUp()
        {
            key = "zgs_diagnostic_tests_" + Guid.NewGuid().ToString("N");
            queue = new OfflineQueue(32, key);
            AnalyticsService.ResetForTests();
            AnalyticsService.AddProvider(new ZGSServerProvider("https://example.invalid", "fixture", "FIXTURE", queue));
        }

        [TearDown] public void TearDown()
        {
            AnalyticsService.ResetForTests();
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        private static DiagnosticReport Report() => new()
        {
            IncidentId = "11111111111111111111111111111111", Kind = "entity-resource", Revision = 0,
            Fingerprint = "entity-resource.load.failed", OccurredAtUnixMs = 1793318400000,
            SnapshotJson = "{\"schema\":1,\"stage\":\"load.failed\",\"path\":\"合成现场\"}",
            Context = new Dictionary<string, object> { ["room"] = "fixture-room", ["integrity"] = "pending" }
        };

        [Test] public void TryReport_PersistsSummaryAndSnapshotInSameEnvelope()
        {
            var report = Report();
            var result = DiagnosticReporter.TryReport(report);
            Assert.That(result.AcceptedLocally, Is.True);
            Assert.That(result.EventId, Is.EqualTo("diag." + report.IncidentId + ".0"));
            string persisted = new OfflineQueue(32, key).GetPendingJsonSnapshot()[0];
            StringAssert.Contains("\"event\":\"diagnostic_incident\"", persisted);
            StringAssert.Contains("\"diagnostic_id\":\"" + report.IncidentId + "\"", persisted);
            StringAssert.Contains("snapshot_sha256", persisted);
            StringAssert.Contains("\"snapshot_encoding\":\"gzip-base64\"", persisted);
            Assert.That(queue.GetDurabilitySnapshot(), Is.EqualTo(new[] { true }));
        }

        [Test] public void DecodeSnapshot_RoundTripsUnicodeAndRejectsCorruption()
        {
            var capture = new CaptureProvider();
            AnalyticsService.ResetForTests(); AnalyticsService.AddProvider(capture);
            var report = Report();
            Assert.That(DiagnosticReporter.TryReport(report).AcceptedLocally, Is.True);
            string data = (string)capture.Data["snapshot_data"], hash = (string)capture.Data["snapshot_sha256"];
            Assert.That(DiagnosticReporter.TryDecodeSnapshot(data, hash, out string decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(report.SnapshotJson));
            Assert.That(DiagnosticReporter.TryDecodeSnapshot(data, new string('0', 64), out _), Is.False);
            Assert.That(DiagnosticReporter.TryDecodeSnapshot("not-base64", hash, out _), Is.False);
        }

        private sealed class CaptureProvider : IAnalyticsEnqueueProvider
        {
            public Dictionary<string, object> Data;
            public bool TryLogEvent(string name, Dictionary<string, object> data, AnalyticsEventOptions options) { Data = data; return true; }
            public void Initialize(string id) { }
            public void LogEvent(string name, Dictionary<string, object> data = null) { }
            public void SetUserProperty(string key, object value) { }
            public void TrackScreen(string name) { }
            public void LogError(string error, string stack) { }
        }

        [Test] public void TryReport_DuplicateIsIdempotent_ChangedSnapshotConflicts()
        {
            var report = Report();
            Assert.That(DiagnosticReporter.TryReport(report).AcceptedLocally, Is.True);
            Assert.That(DiagnosticReporter.TryReport(report).Duplicate, Is.True);
            report.SnapshotJson = "{\"changed\":true}";
            Assert.That(DiagnosticReporter.TryReport(report).Failure, Is.EqualTo(DiagnosticSubmissionFailure.IdentityConflict));
            Assert.That(queue.Count, Is.EqualTo(1));
        }

        [Test] public void TryReport_CompletedVerificationKeepsIncidentAndHasDistinctEvent()
        {
            var report = Report();
            var initial = DiagnosticReporter.TryReport(report);
            report.Revision = 1;
            report.Context["integrity"] = "verified";
            report.SnapshotJson = "{\"integrity\":\"verified\"}";
            var completed = DiagnosticReporter.TryReport(report);
            Assert.That(completed.AcceptedLocally, Is.True);
            Assert.That(completed.EventId, Is.Not.EqualTo(initial.EventId));
            Assert.That(queue.Count, Is.EqualTo(2));
        }

        [Test] public void TryReport_RejectsOversizeOrNonScalarContext_WithoutEnqueue()
        {
            var report = Report();
            report.SnapshotJson = "{\"data\":\"" + new string('x', DiagnosticReporter.MaximumSnapshotBytes) + "\"}";
            Assert.That(DiagnosticReporter.TryReport(report).Failure, Is.EqualTo(DiagnosticSubmissionFailure.SnapshotTooLarge));
            report = Report(); report.Context["unsafe"] = new object();
            Assert.That(DiagnosticReporter.TryReport(report).Failure, Is.EqualTo(DiagnosticSubmissionFailure.InvalidReport));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test] public void TryReport_RejectedQueueDoesNotConsumeIdentity()
        {
            queue = new OfflineQueue(1, key);
            AnalyticsService.ResetForTests();
            AnalyticsService.AddProvider(new ZGSServerProvider("https://example.invalid", "fixture", "FIXTURE", queue));
            Assert.That(queue.Enqueue("{}", true), Is.True);
            var report = Report();
            Assert.That(DiagnosticReporter.TryReport(report).Failure, Is.EqualTo(DiagnosticSubmissionFailure.QueueRejected));
            queue.ClearQueue();
            Assert.That(DiagnosticReporter.TryReport(report).AcceptedLocally, Is.True);
        }

        [Test] public void TryReport_UsesSeparateDiagnosticQueueAndRespectsSessionBudget()
        {
            string diagnosticKey = key + "_diagnostics";
            try
            {
                var diagnostics = new OfflineQueue(32, diagnosticKey);
                AnalyticsService.ResetForTests();
                AnalyticsService.AddProvider(new ZGSServerProvider("https://example.invalid", "fixture", "FIXTURE", queue, diagnostics));
                for (int i = 0; i < 16; i++)
                {
                    var report = Report(); report.IncidentId = Guid.NewGuid().ToString("N");
                    Assert.That(DiagnosticReporter.TryReport(report).AcceptedLocally, Is.True);
                }
                Assert.That(queue.Count, Is.Zero);
                Assert.That(diagnostics.Count, Is.EqualTo(16));
                Assert.That(DiagnosticReporter.TryReport(Report()).Failure, Is.EqualTo(DiagnosticSubmissionFailure.RateLimited));
            }
            finally { PlayerPrefs.DeleteKey(diagnosticKey); PlayerPrefs.Save(); }
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator Delivery_ConnectionRefused_ReloadQueue_RetryIdenticalEnvelope()
        {
            Assert.That(DiagnosticReporter.TryReport(Report()).AcceptedLocally, Is.True);
            string original = queue.GetPendingJsonSnapshot()[0];
            var portProbe = new TcpListener(IPAddress.Loopback, 0);
            portProbe.Start(); int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
            string url = "http://127.0.0.1:" + port + "/events";
            yield return SendInEditMode(queue.FlushCoroutine(url, "fixture"));
            Assert.That(queue.Count, Is.EqualTo(1), "A network failure must retain the event.");
            var reloaded = new OfflineQueue(32, key);
            Assert.That(reloaded.GetPendingJsonSnapshot(), Is.EqualTo(new[] { original }));
            using var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            Task<string> receive = Task.Run(async () =>
            {
                var request = await listener.GetContextAsync();
                using var reader = new StreamReader(request.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();
                request.Response.StatusCode = 200;
                byte[] result = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                await request.Response.OutputStream.WriteAsync(result, 0, result.Length);
                request.Response.Close();
                return body;
            });
            yield return SendInEditMode(reloaded.FlushCoroutine(url, "fixture"));
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!receive.IsCompleted && DateTime.UtcNow < deadline)
                yield return null;
            Assert.That(receive.IsCompleted, Is.True);
            Assert.That(receive.Result, Is.EqualTo("{\"secret\":\"fixture\",\"body\":" + original + "}"));
            Assert.That(reloaded.Count, Is.Zero);
            Assert.That(new OfflineQueue(32, key).Count, Is.Zero);
        }

        // EditMode test runners do not drive arbitrary engine yield objects like the Player.
        // Keep the real UnityWebRequest alive until its native AsyncOperation has completed.
        private static IEnumerator SendInEditMode(IEnumerator routine)
        {
            try
            {
                while (routine.MoveNext())
                {
                    if (routine.Current is AsyncOperation operation)
                        while (!operation.isDone) yield return null;
                    else yield return null;
                }
            }
            finally { (routine as IDisposable)?.Dispose(); }
        }
    }
}
