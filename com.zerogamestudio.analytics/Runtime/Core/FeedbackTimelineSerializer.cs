using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ZGS.Analytics
{
    internal sealed class FeedbackTimelineSnapshot
    {
        public string Json;
        public List<Dictionary<string, object>> StructuredEvents;
    }

    internal static class FeedbackTimelineSerializer
    {
        internal const int MaxEntries = 80;
        internal const int MaxJsonBytes = 64 * 1024;
        internal const int MaxValueChars = 512;

        private static readonly string[] SensitiveKeyTokens =
        {
            "password",
            "token",
            "secret",
            "auth",
            "authorization",
            "cookie",
            "email",
            "phone",
            "contact",
            "address"
        };

        [Serializable]
        private sealed class Payload
        {
            public int totalEvents;
            public int maxEvents = MaxEntries;
            public int maxBytes = MaxJsonBytes;
            public bool truncated;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public long timestamp;
            public string eventName;
            public List<Data> data = new List<Data>();
        }

        [Serializable]
        private sealed class Data
        {
            public string key;
            public string value;
        }

        internal static FeedbackTimelineSnapshot Create(TimelineLogger.TimelineEntry[] source)
        {
            source = source ?? Array.Empty<TimelineLogger.TimelineEntry>();
            var payload = new Payload { totalEvents = source.Length };
            int start = Math.Max(0, source.Length - MaxEntries);
            payload.truncated = start > 0;

            for (int i = start; i < source.Length; i++)
            {
                TimelineLogger.TimelineEntry sourceEntry = source[i];
                if (sourceEntry == null)
                    continue;

                var entry = new Entry
                {
                    timestamp = sourceEntry.Timestamp,
                    eventName = TruncateValue(sourceEntry.Event)
                };

                if (sourceEntry.Data != null)
                {
                    foreach (KeyValuePair<string, object> pair in sourceEntry.Data.OrderBy(
                                 pair => pair.Key,
                                 StringComparer.Ordinal))
                    {
                        string key = TruncateValue(pair.Key ?? string.Empty);
                        entry.data.Add(new Data
                        {
                            key = key,
                            value = IsSensitiveKey(key)
                                ? "<redacted>"
                                : TruncateValue(pair.Value == null ? "<null>" : pair.Value.ToString())
                        });
                    }
                }

                payload.entries.Add(entry);
            }

            string json = JsonUtility.ToJson(payload);
            while (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes && payload.entries.Count > 0)
            {
                payload.truncated = true;
                payload.entries.RemoveAt(0);
                json = JsonUtility.ToJson(payload);
            }

            if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            {
                payload.truncated = true;
                payload.entries.Clear();
                json = JsonUtility.ToJson(payload);
            }

            return new FeedbackTimelineSnapshot
            {
                Json = json,
                StructuredEvents = BuildStructuredEvents(payload.entries)
            };
        }

        internal static string BoundLegacyText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (Encoding.UTF8.GetByteCount(value) <= MaxJsonBytes)
                return value;

            int low = 0;
            int high = value.Length;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                if (Encoding.UTF8.GetByteCount(value.Substring(0, mid)) <= MaxJsonBytes)
                    low = mid;
                else
                    high = mid - 1;
            }

            return value.Substring(0, low);
        }

        internal static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            for (int i = 0; i < SensitiveKeyTokens.Length; i++)
            {
                if (key.IndexOf(SensitiveKeyTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        internal static string TruncateValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxValueChars)
                return value ?? string.Empty;

            return value.Substring(0, MaxValueChars - 3) + "...";
        }

        private static List<Dictionary<string, object>> BuildStructuredEvents(List<Entry> entries)
        {
            var result = new List<Dictionary<string, object>>(entries.Count);
            foreach (Entry entry in entries)
            {
                var data = new Dictionary<string, object>();
                foreach (Data item in entry.data)
                    data[item.key] = item.value;

                result.Add(new Dictionary<string, object>
                {
                    ["ts"] = entry.timestamp,
                    ["event"] = entry.eventName,
                    ["data"] = data
                });
            }

            return result;
        }
    }
}
