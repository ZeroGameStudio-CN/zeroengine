using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ZGS.Analytics
{
    public enum FeedbackDeliveryState
    {
        Queued, Uploading, Retrying, Received, Failed, NeedsAction, Stopped
    }

    /// <summary>Delivery evidence, independent of an individual upload attempt.</summary>
    public readonly struct FeedbackSubmissionStatus
    {
        public string SubmissionId { get; }
        public FeedbackDeliveryState State { get; }
        public string FailureCode { get; }
        public string Reason { get; }
        public long CreatedAt { get; }
        public long UpdatedAt { get; }
        public long NextAttemptAt { get; }
        public int Attempts { get; }
        public bool PartialAttachments { get; }
        public bool SuccessNotified { get; }
        public bool AutomaticRetryScheduled { get; }

        internal FeedbackSubmissionStatus(FeedbackUploadQueue.PendingUpload item)
        {
            SubmissionId = item.submissionId;
            State = item.hasServerReceipt ? FeedbackDeliveryState.Received : item.deliveryState;
            FailureCode = item.lastFailureCode;
            Reason = item.lastFailureReason;
            CreatedAt = item.createdAt;
            UpdatedAt = item.updatedAt;
            NextAttemptAt = item.nextAttemptAt;
            Attempts = Math.Max(item.retryCount, item.attemptCount);
            PartialAttachments = item.partialAttachments;
            SuccessNotified = item.successNotified;
            AutomaticRetryScheduled = AnalyticsConfig.IsUploadConfigured && (State == FeedbackDeliveryState.Queued ||
                State == FeedbackDeliveryState.Uploading || State == FeedbackDeliveryState.Retrying ||
                (State == FeedbackDeliveryState.NeedsAction && NextAttemptAt > 0));
        }
    }

    public static partial class FeedbackUploadQueue
    {
        private static List<PendingUpload> _recentSubmissions = new();
        public static event Action<FeedbackSubmissionStatus> SubmissionStateChanged;
        public static bool IsProcessing => _isProcessing;
        public static bool IsFull => PendingCount >= MaxPendingCount;
        public static bool StorageReadable
        {
            get
            {
                if (AnalyticsBootstrap.IsAutomationIsolated) return true;
                if (_pendingUploads == null) LoadQueue();
                return !_queueReadFailed;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDeliveryObservers()
        {
            SubmissionStateChanged = null;
            _recentSubmissions = new List<PendingUpload>();
        }

        public static IReadOnlyList<FeedbackSubmissionStatus> GetRecentSubmissions()
        {
            if (AnalyticsBootstrap.IsAutomationIsolated) return Array.Empty<FeedbackSubmissionStatus>();
            if (_pendingUploads == null) LoadQueue();
            var records = new Dictionary<string, PendingUpload>(StringComparer.Ordinal);
            foreach (PendingUpload item in _pendingUploads)
                if (!string.IsNullOrEmpty(item.submissionId)) records[item.submissionId] = item;
            foreach (PendingUpload item in _recentSubmissions)
                if (!string.IsNullOrEmpty(item.submissionId)) records[item.submissionId] = item;
            var sorted = new List<PendingUpload>(records.Values);
            sorted.Sort((left, right) => right.createdAt.CompareTo(left.createdAt));
            var result = new List<FeedbackSubmissionStatus>(sorted.Count);
            foreach (PendingUpload item in sorted) result.Add(new FeedbackSubmissionStatus(item));
            return result;
        }

        /// <summary>Retries the existing identity without constructing another ZIP.</summary>
        public static bool RetrySubmission(string submissionId)
        {
            if (AnalyticsBootstrap.IsAutomationIsolated || _isProcessing) return false;
            if (_pendingUploads == null) LoadQueue();
            PendingUpload item = _pendingUploads.Find(p => p.submissionId == submissionId);
            if (item == null || item.hasServerReceipt) return false;
            item.blocked = false;
            item.blockedUntil = 0;
            item.nextAttemptAt = 0;
            SetDeliveryState(item, FeedbackDeliveryState.Retrying);
            TryPersistQueue(_pendingUploads);
            _queueMutationVersion++;
            StartBackgroundProcessing();
            return true;
        }

        public static void MarkSuccessNotified(string submissionId)
        {
            if (AnalyticsBootstrap.IsAutomationIsolated) return;
            if (_pendingUploads == null) LoadQueue();
            if (_queueReadFailed) return;
            bool changed = false;
            foreach (PendingUpload item in _pendingUploads)
                if (item.submissionId == submissionId && item.hasServerReceipt && !item.successNotified)
                { item.successNotified = true; changed = true; }
            foreach (PendingUpload item in _recentSubmissions)
                if (item.submissionId == submissionId && item.hasServerReceipt && !item.successNotified)
                { item.successNotified = true; changed = true; }
            if (changed) TryPersistQueue(_pendingUploads);
        }

        private static void SetDeliveryState(PendingUpload item, FeedbackDeliveryState state)
        {
            if (item.hasServerReceipt && state != FeedbackDeliveryState.Received) return;
            bool changed = item.deliveryState != state || item.updatedAt == 0;
            item.deliveryState = state;
            item.updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (changed) PublishDeliveryState(item);
        }

        private static void PublishDeliveryState(PendingUpload item)
        {
            if (string.IsNullOrEmpty(item.submissionId)) return;
            var snapshot = new FeedbackSubmissionStatus(item);
            Debug.Log($"[FeedbackSubmission] state={snapshot.State} submission={FeedbackFailureCodes.SafeDiagnosticId(item.submissionId)} attempt={item.retryCount} next_attempt_utc={item.nextAttemptAt}");
            bool failedAttempt = snapshot.State == FeedbackDeliveryState.Retrying ||
                snapshot.State == FeedbackDeliveryState.NeedsAction || snapshot.State == FeedbackDeliveryState.Failed;
            AnalyticsService.TryLogEvent("feedback_submission_state", new Dictionary<string, object>
            {
                ["submission_id"] = item.submissionId,
                ["delivery_state"] = snapshot.State.ToString(),
                ["observed_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["upload_attempts"] = snapshot.Attempts,
                ["next_attempt_utc"] = item.nextAttemptAt,
                ["failure_code"] = failedAttempt ? item.lastFailureCode ?? string.Empty : string.Empty,
                ["reason"] = failedAttempt ? item.lastFailureReason ?? string.Empty : string.Empty,
                ["server_confirmed"] = item.hasServerReceipt
            }, new AnalyticsEventOptions("feedback.delivery." + item.submissionId + "." + snapshot.State +
                "." + snapshot.Attempts + "." + item.updatedAt, 0, true));
            var handlers = SubmissionStateChanged;
            if (handlers == null) return;
            foreach (Action<FeedbackSubmissionStatus> handler in handlers.GetInvocationList())
            {
                try { handler(snapshot); }
                catch (Exception exception)
                {
                    AnalyticsLog.LogWarning($"[FeedbackSubmission] state observer failed: {exception.GetType().Name}");
                }
            }
        }

        private static void RememberResult(PendingUpload pending)
        {
            if (string.IsNullOrEmpty(pending.submissionId)) return;
            PendingUpload record = pending.Copy();
            record.zipPath = null;
            record.userName = null;
            record.version = null;
            _recentSubmissions.RemoveAll(item => item.submissionId == record.submissionId);
            _recentSubmissions.Add(record);
            while (_recentSubmissions.Count > MaxPendingCount) _recentSubmissions.RemoveAt(0);
        }

        private static void CompleteReceived(PendingUpload pending, bool partial = false)
        {
            bool firstConfirmation = !pending.hasServerReceipt;
            pending.hasServerReceipt = true;
            pending.partialAttachments |= partial;
            pending.nextAttemptAt = 0;
            SetDeliveryState(pending, FeedbackDeliveryState.Received);
            RememberResult(pending);
            // Delivery is proven; a local acknowledgement write cannot undo it.
            if (TryRemovePersisted(pending)) DeleteFileBestEffort(pending.zipPath);
            else AnalyticsLog.LogWarning($"[FeedbackQueue] confirmed cleanup pending code=F102 submission={FeedbackFailureCodes.SafeDiagnosticId(pending.submissionId)}");
            if (firstConfirmation)
            {
                NotifyQueuedUploadSucceeded(pending.zipPath);
                NotifyQueuedSubmissionSucceeded(pending.submissionId);
            }
        }

        private static List<PendingUpload> ReadRecentSubmissions(string json)
        {
            QueueEnvelope envelope = JsonUtility.FromJson<QueueEnvelope>(json);
            QueueWrapper wrapper = JsonUtility.FromJson<QueueWrapper>(
                string.IsNullOrEmpty(envelope?.payload) ? json : envelope.payload);
            return wrapper?.recent ?? new List<PendingUpload>();
        }

        private static string ProjectIdentity(PendingUpload item)
        {
            string project = string.IsNullOrEmpty(item.project) ? AnalyticsConfig.AppId : item.project;
            if (string.IsNullOrWhiteSpace(project)) project = (item.version ?? string.Empty).Split('_')[0];
            project = (project ?? string.Empty).Trim().ToUpperInvariant();
            if (project == "LONGEVITYCODEX") return "LC";
            if (project == "IDLEGARDEN") return "IG";
            return FeedbackFailureCodes.SafeDiagnosticId(project);
        }

        [Serializable]
        private sealed class UploadReceipt
        {
            public int receipt_version;
            public string status;
            public string project;
            public string submission_id;
            public string original_sha256;
            public string receipt_id;
            public bool partial;
        }

        private static bool TryReadReceipt(string json, PendingUpload pending, out bool partial)
        {
            partial = false;
            try
            {
                UploadReceipt receipt = JsonUtility.FromJson<UploadReceipt>(json);
                if (receipt == null || receipt.receipt_version != 1 || receipt.status != "ok" ||
                    receipt.submission_id != pending.submissionId || receipt.project != ProjectIdentity(pending) ||
                    !string.Equals(receipt.original_sha256, pending.fileSha256, StringComparison.OrdinalIgnoreCase) ||
                    receipt.receipt_id != ComputeChecksum("feedback-submission-v1\0" +
                        ProjectIdentity(pending) + "\0" + pending.submissionId)) return false;
                partial = receipt.partial;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static IEnumerator QueryReceipt(PendingUpload pending, Action<bool, bool> completed)
        {
            if (!AnalyticsConfig.IsUploadConfigured || string.IsNullOrEmpty(pending.submissionId) ||
                string.IsNullOrEmpty(pending.fileSha256) || string.IsNullOrEmpty(ProjectIdentity(pending)))
            {
                completed(false, false);
                yield break;
            }
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation = null;
            try
            {
                request = UnityWebRequest.Get(AnalyticsConfig.ServerUrl.TrimEnd('/') + "/upload/receipt");
                request.SetRequestHeader(UploadSecretHeaderName, AnalyticsConfig.UploadSecret);
                request.SetRequestHeader(SubmissionIdHeaderName, pending.submissionId);
                request.SetRequestHeader("X-Feedback-Project", ProjectIdentity(pending));
                request.SetRequestHeader("X-Feedback-Content-Sha256", pending.fileSha256);
                request.timeout = 10;
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                request?.Dispose();
                request = null;
            }
            if (operation == null)
            {
                completed(false, false);
                yield break;
            }
            using (request)
            {
                yield return operation;
                bool partial = false;
                bool received = request.result == UnityWebRequest.Result.Success &&
                    TryReadReceipt(request.downloadHandler.text, pending, out partial);
                completed(received, partial);
            }
        }
    }
}
