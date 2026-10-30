using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ZGS.Analytics
{
    /// <summary>Explicit, project-owned diagnostic data. Call on the Unity main thread.</summary>
    public sealed class DiagnosticReport
    {
        public string IncidentId;
        public string Kind;
        public int Revision;
        public string Fingerprint;
        public long OccurredAtUnixMs;
        public string SnapshotJson;
        public Dictionary<string, object> Context = new();
    }

    public enum DiagnosticSubmissionFailure
    {
        None, InvalidReport, SnapshotTooLarge, RateLimited, IdentityConflict, QueueRejected
    }

    public readonly struct DiagnosticSubmissionResult
    {
        public string EventId { get; }
        public bool AcceptedLocally { get; }
        public bool Duplicate { get; }
        public DiagnosticSubmissionFailure Failure { get; }

        internal DiagnosticSubmissionResult(string eventId, bool accepted, DiagnosticSubmissionFailure failure, bool duplicate = false)
        {
            EventId = eventId;
            AcceptedLocally = accepted;
            Failure = failure;
            Duplicate = duplicate;
        }
    }

    /// <summary>
    /// Sends a bounded JSON snapshot atomically with its searchable summary through the existing
    /// durable event queue. Acceptance means local persistence, never server delivery.
    /// Projects must supply allowlisted, sanitized data; no logs, files or saves are collected here.
    /// </summary>
    public static class DiagnosticReporter
    {
        public const string EventName = "diagnostic_incident";
        public const int MaximumSnapshotBytes = 128 * 1024;
        public const int MaximumCompressedBytes = 7 * 1024;
        private const int MaximumSessionEvents = 16;
        private static readonly Dictionary<string, string> accepted = new(StringComparer.Ordinal);

        [Serializable] private sealed class JsonProbe { public int schema; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset() => accepted.Clear();

        public static DiagnosticSubmissionResult TryReport(DiagnosticReport report)
        {
            string eventId = null;
            try
            {
                if (report == null || !Guid.TryParseExact(report.IncidentId, "N", out var id) ||
                    report.Revision < 0 || report.Revision > 15 || !SafeToken(report.Kind, 96) ||
                    !SafeToken(report.Fingerprint, 128) || string.IsNullOrEmpty(report.SnapshotJson))
                    return Reject(eventId, DiagnosticSubmissionFailure.InvalidReport);
                eventId = "diag." + id.ToString("N") + "." + report.Revision.ToString(CultureInfo.InvariantCulture);
                int bytes = Encoding.UTF8.GetByteCount(report.SnapshotJson);
                if (bytes > MaximumSnapshotBytes)
                    return Reject(eventId, DiagnosticSubmissionFailure.SnapshotTooLarge);
                string trimmed = report.SnapshotJson.Trim();
                if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
                    return Reject(eventId, DiagnosticSubmissionFailure.InvalidReport);
                JsonUtility.FromJson<JsonProbe>(trimmed);
                byte[] compressed;
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                    {
                        byte[] raw = Encoding.UTF8.GetBytes(report.SnapshotJson);
                        gzip.Write(raw, 0, raw.Length);
                    }
                    compressed = output.ToArray();
                }
                if (compressed.Length > MaximumCompressedBytes)
                    return Reject(eventId, DiagnosticSubmissionFailure.SnapshotTooLarge);

                var context = new SortedDictionary<string, object>(StringComparer.Ordinal);
                if (report.Context?.Count > 32) return Reject(eventId, DiagnosticSubmissionFailure.InvalidReport);
                int contextBytes = 0;
                if (report.Context != null)
                    foreach (var pair in report.Context)
                    {
                        if (!SafeToken(pair.Key, 64) || !IsScalar(pair.Value))
                            return Reject(eventId, DiagnosticSubmissionFailure.InvalidReport);
                        context.Add(pair.Key, pair.Value);
                        string value = Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                        foreach (char c in value) if (char.IsControl(c)) return Reject(eventId, DiagnosticSubmissionFailure.InvalidReport);
                        contextBytes += Encoding.UTF8.GetByteCount(pair.Key) + Encoding.UTF8.GetByteCount(value);
                    }
                if (contextBytes > 512) return Reject(eventId, DiagnosticSubmissionFailure.InvalidReport);

                string snapshotHash = Sha256(report.SnapshotJson);
                var identity = new StringBuilder(report.Kind).Append('|').Append(report.Fingerprint)
                    .Append('|').Append(report.OccurredAtUnixMs).Append('|').Append(snapshotHash);
                foreach (var pair in context)
                    identity.Append('|').Append(pair.Key).Append(':').Append(pair.Value?.GetType().Name)
                        .Append(':').Append(Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
                string identityHash = Sha256(identity.ToString());
                if (accepted.TryGetValue(eventId, out string prior))
                    return prior == identityHash
                        ? new DiagnosticSubmissionResult(eventId, true, DiagnosticSubmissionFailure.None, true)
                        : Reject(eventId, DiagnosticSubmissionFailure.IdentityConflict);
                if (accepted.Count >= MaximumSessionEvents)
                    return Reject(eventId, DiagnosticSubmissionFailure.RateLimited);

                var fields = new Dictionary<string, object>
                {
                    ["schema_version"] = 1,
                    ["diagnostic_id"] = id.ToString("N"),
                    ["diagnostic_kind"] = report.Kind,
                    ["diagnostic_revision"] = report.Revision,
                    ["diagnostic_fingerprint"] = report.Fingerprint,
                    ["snapshot_encoding"] = "gzip-base64",
                    ["snapshot_data"] = Convert.ToBase64String(compressed),
                    ["snapshot_sha256"] = snapshotHash,
                    ["snapshot_bytes"] = bytes,
                    ["context"] = new Dictionary<string, object>(context)
                };
                if (!AnalyticsService.TryLogEvent(EventName, fields,
                        new AnalyticsEventOptions(eventId, report.OccurredAtUnixMs, durable: true)))
                    return Reject(eventId, DiagnosticSubmissionFailure.QueueRejected);
                accepted.Add(eventId, identityHash);
                return new DiagnosticSubmissionResult(eventId, true, DiagnosticSubmissionFailure.None);
            }
            catch (Exception)
            {
                // Diagnostics must never throw into gameplay, or log raw project data.
                return Reject(eventId, DiagnosticSubmissionFailure.InvalidReport);
            }
        }

        private static DiagnosticSubmissionResult Reject(string id, DiagnosticSubmissionFailure failure) => new(id, false, failure);

        /// <summary>Validate and decode a received snapshot without unbounded decompression.</summary>
        public static bool TryDecodeSnapshot(string data, string expectedSha256, out string json)
        {
            json = null;
            try
            {
                if (string.IsNullOrEmpty(data) || data.Length > 4 * ((MaximumCompressedBytes + 2) / 3)) return false;
                using var input = new MemoryStream(Convert.FromBase64String(data));
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                var buffer = new byte[4096]; int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaximumSnapshotBytes) return false;
                    output.Write(buffer, 0, read);
                }
                string decoded = new UTF8Encoding(false, true).GetString(output.ToArray());
                if (!string.Equals(Sha256(decoded), expectedSha256, StringComparison.Ordinal)) return false;
                json = decoded; return true;
            }
            catch (Exception) { return false; }
        }

        private static bool SafeToken(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c is '.' or '_' or '-' or ':'))
                    return false;
            return true;
        }

        private static bool IsScalar(object value) => value == null || value is bool or int or long ||
            value is string s && Encoding.UTF8.GetByteCount(s) <= 1024 ||
            value is float f && !float.IsNaN(f) && !float.IsInfinity(f) ||
            value is double d && !double.IsNaN(d) && !double.IsInfinity(d);

        private static string Sha256(string value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
