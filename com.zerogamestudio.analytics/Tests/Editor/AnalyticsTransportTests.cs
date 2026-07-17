using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace ZGS.Analytics.Tests.Editor
{
    [TestFixture]
    public class AnalyticsTransportTests
    {
        private const string QueueKey = "zgs_analytics_transport_tests";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(QueueKey);
            PlayerPrefs.Save();
            SessionInfo.Initialize();
            SessionInfo.ResetEventSequenceForTests();
            AnalyticsService.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AnalyticsService.ResetForTests();
            PlayerPrefs.DeleteKey(QueueKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void TryLogEvent_DurableEnvelope_FreezesTopLevelIdentityTimeAndSequence()
        {
            var queue = new OfflineQueue(10, QueueKey);
            var provider = new ZGSServerProvider("https://example.invalid", "secret", "POB", queue);
            const string eventId = "pob2.0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            const long occurredAt = 1784102400123L;
            var parameters = new Dictionary<string, object>
            {
                ["event_id"] = "must-not-leak",
                ["session_event_sequence"] = 999,
                ["damage"] = 12.5f,
                ["won"] = true
            };

            bool accepted = provider.TryLogEvent(
                "game_end",
                parameters,
                new AnalyticsEventOptions(eventId, occurredAt, durable: true));

            Assert.IsTrue(accepted);
            string json = queue.GetPendingJsonSnapshot()[0];
            StringAssert.StartsWith($"{{\"event_id\":\"{eventId}\",", json);
            StringAssert.Contains($"\"ts\":{occurredAt}", json);
            StringAssert.Contains("\"damage\":12.5", json);
            StringAssert.Contains("\"won\":true", json);
            StringAssert.Contains("\"session_event_sequence\":1", json);

            string props = json.Substring(json.IndexOf("\"props\"", StringComparison.Ordinal));
            Assert.IsFalse(props.Contains("\"event_id\""));
            Assert.AreEqual("must-not-leak", parameters["event_id"]);
            Assert.AreEqual(999, parameters["session_event_sequence"]);

            var reloaded = new OfflineQueue(10, QueueKey);
            Assert.AreEqual(json, reloaded.GetPendingJsonSnapshot()[0]);
            Assert.IsTrue(reloaded.GetDurabilitySnapshot()[0]);
        }

        [Test]
        public void TryLogEvent_TwoEvents_UsesStrictlyIncreasingReservedSequence()
        {
            var queue = new OfflineQueue(10, QueueKey);
            var provider = new ZGSServerProvider("https://example.invalid", "secret", "POB", queue);

            Assert.IsTrue(provider.TryLogEvent("first", null, default));
            Assert.IsTrue(provider.TryLogEvent("second", null, default));

            string[] events = queue.GetPendingJsonSnapshot();
            long first = ExtractSequence(events[0]);
            long second = ExtractSequence(events[1]);
            Assert.AreEqual(first + 1, second);
        }

        [Test]
        public void TryLogEvent_InvalidExplicitEventId_ReturnsFalseWithoutEnqueue()
        {
            var queue = new OfflineQueue(10, QueueKey);
            var provider = new ZGSServerProvider("https://example.invalid", "secret", "POB", queue);

            bool accepted = provider.TryLogEvent(
                "game_end",
                null,
                new AnalyticsEventOptions("invalid id with spaces", durable: true));

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void Enqueue_DurableAfterBuffered_PersistsBothInOriginalOrder()
        {
            var queue = new OfflineQueue(3, QueueKey);
            Assert.IsTrue(queue.Enqueue("{\"event\":\"buffered\"}"));
            Assert.AreEqual(0, new OfflineQueue(3, QueueKey).Count);

            Assert.IsTrue(queue.Enqueue("{\"event\":\"durable\"}", durable: true));

            var reloaded = new OfflineQueue(3, QueueKey);
            CollectionAssert.AreEqual(
                new[] { "{\"event\":\"buffered\"}", "{\"event\":\"durable\"}" },
                reloaded.GetPendingJsonSnapshot());
            CollectionAssert.AreEqual(new[] { false, true }, reloaded.GetDurabilitySnapshot());
        }

        [Test]
        public void Enqueue_FullQueue_EvictsBufferedBeforeDurable()
        {
            var queue = new OfflineQueue(2, QueueKey);
            Assert.IsTrue(queue.Enqueue("{\"event\":\"buffered\"}"));
            Assert.IsTrue(queue.Enqueue("{\"event\":\"durable-1\"}", durable: true));

            Assert.IsTrue(queue.Enqueue("{\"event\":\"durable-2\"}", durable: true));

            CollectionAssert.AreEqual(
                new[] { "{\"event\":\"durable-1\"}", "{\"event\":\"durable-2\"}" },
                queue.GetPendingJsonSnapshot());
            CollectionAssert.AreEqual(new[] { true, true }, queue.GetDurabilitySnapshot());
        }

        [Test]
        public void Enqueue_FullDurableQueue_RejectsNewDurableWithoutDroppingExisting()
        {
            var queue = new OfflineQueue(2, QueueKey);
            Assert.IsTrue(queue.Enqueue("{\"event\":\"durable-1\"}", durable: true));
            Assert.IsTrue(queue.Enqueue("{\"event\":\"durable-2\"}", durable: true));

            Assert.IsFalse(queue.Enqueue("{\"event\":\"durable-3\"}", durable: true));

            CollectionAssert.AreEqual(
                new[] { "{\"event\":\"durable-1\"}", "{\"event\":\"durable-2\"}" },
                queue.GetPendingJsonSnapshot());
        }

        [Test]
        public void Enqueue_SerializationFailure_ReturnsFalse()
        {
            var queue = new OfflineQueue(2, QueueKey);

            Assert.IsFalse(queue.Enqueue(new ThrowingEvent(), durable: true));
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void AnalyticsService_WithoutUploadProvider_ReturnsFalse()
        {
            Assert.IsFalse(AnalyticsService.TryLogEvent("game_end", null, default));
        }

        [Test]
        public void AnalyticsService_LegacyProvider_RemainsCompatibleButCannotClaimEnqueue()
        {
            var provider = new LegacyProvider();
            AnalyticsService.AddProvider(provider);

            bool accepted = AnalyticsService.TryLogEvent("legacy_event", null, default);

            Assert.IsFalse(accepted);
            Assert.AreEqual(1, provider.LogEventCalls);
        }

        [Test]
        public void AnalyticsService_Flush_InvokesOnlyFlushCapableProviders()
        {
            var legacyProvider = new LegacyProvider();
            var flushProvider = new FlushProvider();
            AnalyticsService.AddProvider(legacyProvider);
            AnalyticsService.AddProvider(flushProvider);

            AnalyticsService.Flush();

            Assert.AreEqual(0, legacyProvider.FlushCalls);
            Assert.AreEqual(1, flushProvider.FlushCalls);
        }

        private static long ExtractSequence(string json)
        {
            Match match = Regex.Match(json, "\\\"session_event_sequence\\\":(?<value>[0-9]+)");
            Assert.IsTrue(match.Success, json);
            return long.Parse(match.Groups["value"].Value);
        }

        private sealed class ThrowingEvent : ISerializableEvent
        {
            public string ToJson()
            {
                throw new InvalidOperationException("serialization failed");
            }
        }

        private sealed class LegacyProvider : IAnalyticsProvider
        {
            public int LogEventCalls { get; private set; }
            public int FlushCalls { get; private set; }

            public void Initialize(string userId) { }

            public void LogEvent(string eventName, Dictionary<string, object> parameters = null)
            {
                LogEventCalls++;
            }

            public void SetUserProperty(string key, object value) { }
            public void TrackScreen(string screenName) { }
            public void LogError(string error, string stackTrace) { }
            public void Flush() { FlushCalls++; }
        }

        private sealed class FlushProvider : IAnalyticsFlushProvider
        {
            public int FlushCalls { get; private set; }

            public void Initialize(string userId) { }
            public void LogEvent(string eventName, Dictionary<string, object> parameters = null) { }
            public void SetUserProperty(string key, object value) { }
            public void TrackScreen(string screenName) { }
            public void LogError(string error, string stackTrace) { }
            public void Flush() { FlushCalls++; }
        }
    }
}
