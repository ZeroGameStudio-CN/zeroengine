using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ZGS.Analytics
{
    public enum FeedbackSubmissionFailure
    {
        None,
        InvalidRequest,
        NotConfigured,
        PackageCreationFailed,
        QueuePersistenceFailed
    }

    public enum FeedbackAttachmentKind
    {
        Log,
        Screenshot,
        ProjectState,
        Generic
    }

    public enum FeedbackAttachmentPriority
    {
        Diagnostic = 10,
        Screenshot = 20,
        ProjectState = 30,
        Generic = 40
    }

    public sealed class FeedbackSubmissionRequest
    {
        public string UserMessage;
        public string Contact;
        public string UserName;
        public string[] FilesToInclude = Array.Empty<string>();
        public Dictionary<string, object> ExtraData = new Dictionary<string, object>();
        public TimelineLogger.TimelineEntry[] TimelineEntries;
        public IFeedbackPackageContributor[] Contributors = Array.Empty<IFeedbackPackageContributor>();

        internal string LegacyTimelineJson;
        internal string[] LegacyDirectoriesToInclude = Array.Empty<string>();
    }

    public readonly struct FeedbackSubmissionResult
    {
        public string SubmissionId { get; }
        public bool AcceptedLocally { get; }
        public FeedbackSubmissionFailure Failure { get; }
        public string FailureCode => FeedbackFailureCodes.FromSubmission(Failure);

        public FeedbackSubmissionResult(
            string submissionId,
            bool acceptedLocally,
            FeedbackSubmissionFailure failure)
        {
            SubmissionId = submissionId ?? string.Empty;
            AcceptedLocally = acceptedLocally;
            Failure = failure;
        }
    }

    public readonly struct FeedbackUploadCompletion
    {
        public string SubmissionId { get; }

        public FeedbackUploadCompletion(string submissionId)
        {
            SubmissionId = submissionId ?? string.Empty;
        }
    }

    /// <summary>A failed attempt; retryable feedback remains in the persistent queue.</summary>
    public readonly struct FeedbackUploadFailure
    {
        public string SubmissionId { get; }
        public string FailureCode { get; }
        public long HttpStatusCode { get; }
        public string RequestId { get; }
        public string Reason { get; }
        public bool Retryable { get; }
        public int Attempt { get; }

        public FeedbackUploadFailure(
            string submissionId,
            string failureCode,
            long httpStatusCode = 0,
            string requestId = null,
            string reason = null,
            int attempt = 0)
        {
            SubmissionId = submissionId ?? string.Empty;
            FailureCode = FeedbackFailureCodes.Normalize(failureCode);
            HttpStatusCode = httpStatusCode;
            RequestId = FeedbackFailureCodes.SafeDiagnosticId(requestId);
            Reason = FeedbackFailureCodes.SafeDiagnosticId(reason);
            Retryable = FeedbackFailureCodes.IsRetryable(FailureCode, httpStatusCode);
            Attempt = Math.Max(0, attempt);
        }
    }

    internal readonly struct FeedbackUploadAttempt
    {
        public bool Succeeded { get; }
        public string FailureCode { get; }
        public long HttpStatusCode { get; }
        public string RequestId { get; }
        public string Reason { get; }
        public bool Retryable { get; }

        public FeedbackUploadAttempt(
            bool succeeded,
            string failureCode = null,
            long httpStatusCode = 0,
            string requestId = null,
            string reason = null)
        {
            Succeeded = succeeded;
            FailureCode = FeedbackFailureCodes.Normalize(failureCode);
            HttpStatusCode = httpStatusCode;
            RequestId = FeedbackFailureCodes.SafeDiagnosticId(requestId);
            Reason = FeedbackFailureCodes.SafeDiagnosticId(reason);
            Retryable = !succeeded && FeedbackFailureCodes.IsRetryable(FailureCode, httpStatusCode);
        }
    }

    public static class FeedbackFailureCodes
    {
        public const string InvalidRequest = "F001";
        public const string NotConfigured = "F002";
        public const string PackageCreation = "F101";
        public const string QueuePersistence = "F102";
        public const string FileRead = "F103";
        public const string Connection = "F201";
        public const string Timeout = "F202";
        public const string Certificate = "F203";
        public const string ResponseProcessing = "F204";
        public const string Unknown = "F299";

        public static string FromSubmission(FeedbackSubmissionFailure failure)
        {
            switch (failure)
            {
                case FeedbackSubmissionFailure.None: return string.Empty;
                case FeedbackSubmissionFailure.InvalidRequest: return InvalidRequest;
                case FeedbackSubmissionFailure.NotConfigured: return NotConfigured;
                case FeedbackSubmissionFailure.PackageCreationFailed: return PackageCreation;
                case FeedbackSubmissionFailure.QueuePersistenceFailed: return QueuePersistence;
                default: return Unknown;
            }
        }

        internal static string FromTransport(UnityWebRequest.Result result, long httpStatusCode, string error)
        {
            if (httpStatusCode >= 400 && httpStatusCode <= 599)
                return "F" + httpStatusCode;
            if (result == UnityWebRequest.Result.DataProcessingError)
                return ResponseProcessing;
            if (Contains(error, "timeout") || Contains(error, "timed out") || Contains(error, "time out"))
                return Timeout;
            if (Contains(error, "certificate") || Contains(error, "ssl") || Contains(error, "tls"))
                return Certificate;
            return result == UnityWebRequest.Result.ConnectionError ? Connection : Unknown;
        }

        public static bool IsRetryable(string code, long httpStatusCode = 0)
        {
            code = Normalize(code);

            if (httpStatusCode == 408 || httpStatusCode == 425 || httpStatusCode == 429 ||
                (httpStatusCode >= 500 && httpStatusCode <= 599))
                return true;

            if (httpStatusCode >= 400 && httpStatusCode <= 499)
                return false;

            switch (code)
            {
                case Connection:
                case Timeout:
                case ResponseProcessing:
                case Unknown:
                    return true;
                case Certificate:
                case InvalidRequest:
                case NotConfigured:
                case PackageCreation:
                case QueuePersistence:
                case FileRead:
                    return false;
                default:
                    return false;
            }
        }

        internal static string Normalize(string code)
        {
            if (code != null && code.Length == 4 && code[0] == 'F' &&
                code[1] >= '0' && code[1] <= '9' && code[2] >= '0' && code[2] <= '9' && code[3] >= '0' && code[3] <= '9')
                return code;
            return Unknown;
        }

        internal static string TransportReason(UnityWebRequest.Result result, long status, string error)
        {
            string code = FromTransport(result, status, error);
            if (code == Timeout) return "timeout";
            if (code == Certificate)
            {
                // Hints reported by the transport, not an independently verified certificate diagnosis.
                if (Contains(error, "certificate has expired") || Contains(error, "certificate expired") ||
                    Contains(error, "not yet valid")) return "certificate_validity_error";
                if (Contains(error, "hostname mismatch") || Contains(error, "host name mismatch") ||
                    Contains(error, "certificate does not match")) return "certificate_hostname_mismatch";
                if (Contains(error, "self signed certificate") || Contains(error, "self-signed certificate") ||
                    Contains(error, "local issuer certificate") || Contains(error, "certificate verify failed") ||
                    Contains(error, "ca certificate error")) return "certificate_trust_failed";
                if (Contains(error, "handshake") || Contains(error, "ssl connect error")) return "tls_handshake_failed";
                return "certificate_tls";
            }
            if (status == 401) return "server_unauthorized";
            if (status == 403) return "server_forbidden";
            if (status == 404) return "server_endpoint_missing";
            if (status == 408) return "server_timeout";
            if (status == 413) return "payload_too_large";
            if (status == 429) return "rate_limited";
            if (status >= 500 && status <= 599) return "server_unavailable";
            if (result == UnityWebRequest.Result.ConnectionError)
            {
                if (Contains(error, "resolve")) return "dns_resolution_failed";
                if (Contains(error, "send")) return "send_failed";
                if (Contains(error, "receive")) return "receive_failed";
                if (Contains(error, "connect")) return "connection_failed";
            }
            return result.ToString();
        }

        internal static int TransportCurlErrorCode(string error)
        {
            // Never retain or print the raw error: it can contain URLs, credentials and player paths.
            var match = System.Text.RegularExpressions.Regex.Match(error ?? string.Empty,
                @"\bcurl error\s*:?\s*(\d{1,3})(?!\d)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return match.Success && int.TryParse(match.Groups[1].Value, out int code) ? code : 0;
        }

        internal static Dictionary<string, object> FailureEnvironment(UnityWebRequest.Result? result, int curlErrorCode)
        {
            string osVersion = "not_collected";
            if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
            {
                try { osVersion = Environment.OSVersion.Version.ToString(); }
                catch (PlatformNotSupportedException) { osVersion = "unavailable"; }
            }
            return new Dictionary<string, object>
            {
                ["diagnostic_schema"] = 1,
                ["observed_utc"] = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["transport_result"] = result.HasValue ? SafeDiagnosticId(result.Value.ToString()) : "not_available",
                ["curl_error"] = curlErrorCode > 0 && curlErrorCode <= 999 ? curlErrorCode : 0,
                ["unity_version"] = SafeDiagnosticId(Application.unityVersion),
                ["app_version"] = SafeDiagnosticId(Application.version?.Replace(' ', '_')),
                ["platform"] = Application.platform.ToString(),
                ["os_version"] = osVersion
            };
        }

        internal static string SafeDiagnosticId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128)
                return string.Empty;
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.'))
                    return string.Empty;
            return value;
        }

        private static bool Contains(string value, string part) =>
            value?.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class FeedbackPackageContext
    {
        public string SubmissionId { get; }
        public string ProductName { get; }
        public string AppVersion { get; }
        public RuntimePlatform Platform { get; }
        public string PersistentDataPath { get; }
        public string TemporaryCachePath { get; }

        internal FeedbackPackageContext(string submissionId)
        {
            SubmissionId = submissionId;
            ProductName = Application.productName;
            AppVersion = Application.version;
            Platform = Application.platform;
            PersistentDataPath = Application.persistentDataPath;
            TemporaryCachePath = Application.temporaryCachePath;
        }
    }

    public interface IFeedbackPackageContributor
    {
        void Collect(FeedbackPackageContext context, FeedbackPackageCollector collector);
    }
}
