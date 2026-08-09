using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace ZGS.Analytics.Tests.Editor
{
    [TestFixture]
    public sealed class FeedbackTimelineSerializerTests
    {
        [Test]
        public void Create_BoundsEntriesBytesAndValuesWhileKeepingNewestEvents()
        {
            var entries = new TimelineLogger.TimelineEntry[FeedbackTimelineSerializer.MaxEntries + 5];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new TimelineLogger.TimelineEntry
                {
                    Timestamp = i,
                    Event = "event_" + i,
                    Data = new Dictionary<string, object>
                    {
                        ["value"] = new string('x', FeedbackTimelineSerializer.MaxValueChars + 20)
                    }
                };
            }

            FeedbackTimelineSnapshot snapshot = FeedbackTimelineSerializer.Create(entries);

            Assert.LessOrEqual(snapshot.StructuredEvents.Count, FeedbackTimelineSerializer.MaxEntries);
            Assert.AreEqual("event_5", snapshot.StructuredEvents[0]["event"]);
            Assert.AreEqual("event_84", snapshot.StructuredEvents[^1]["event"]);
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(snapshot.Json), FeedbackTimelineSerializer.MaxJsonBytes);

            var firstData = (Dictionary<string, object>)snapshot.StructuredEvents[0]["data"];
            Assert.AreEqual(FeedbackTimelineSerializer.MaxValueChars, firstData["value"].ToString().Length);
            StringAssert.EndsWith("...", firstData["value"].ToString());
        }

        [Test]
        public void Create_RedactsSensitiveKeysInZipAndStructuredRepresentations()
        {
            var entries = new[]
            {
                new TimelineLogger.TimelineEntry
                {
                    Timestamp = 1,
                    Event = "submit",
                    Data = new Dictionary<string, object>
                    {
                        ["authToken"] = "do-not-keep",
                        ["email"] = "player@example.com",
                        ["screen"] = "menu"
                    }
                }
            };

            FeedbackTimelineSnapshot snapshot = FeedbackTimelineSerializer.Create(entries);

            StringAssert.DoesNotContain("do-not-keep", snapshot.Json);
            StringAssert.DoesNotContain("player@example.com", snapshot.Json);
            StringAssert.Contains("<redacted>", snapshot.Json);
            StringAssert.Contains("menu", snapshot.Json);

            var data = (Dictionary<string, object>)snapshot.StructuredEvents[0]["data"];
            Assert.AreEqual("<redacted>", data["authToken"]);
            Assert.AreEqual("<redacted>", data["email"]);
            Assert.AreEqual("menu", data["screen"]);
        }

        [Test]
        public void BoundLegacyText_UsesUtf8ByteLimit()
        {
            string bounded = FeedbackTimelineSerializer.BoundLegacyText(
                new string('界', FeedbackTimelineSerializer.MaxJsonBytes));

            Assert.LessOrEqual(
                Encoding.UTF8.GetByteCount(bounded),
                FeedbackTimelineSerializer.MaxJsonBytes);
            Assert.Greater(bounded.Length, 0);
        }
    }
}
