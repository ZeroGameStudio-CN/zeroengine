using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ZGS.Analytics
{
    /// <summary>
    /// 反馈上传队列 - 持久化管理未成功上传的 ZIP 文件
    /// </summary>
    public static class FeedbackUploadQueue
    {
        private const string QueueKey = "zgs_feedback_queue";
        private const int MaxPendingCount = 10;
        private const int MaxPendingAgeDays = 7;
        private const int MaxRetries = 3;
        private const int QueueSchemaVersion = 1;
        private const long NonRetryableRecheckDelaySeconds = 6 * 60 * 60;
        private const string UploadSecretHeaderName = "X-Upload-Secret";
        private const string IdempotencyHeaderName = "Idempotency-Key";
        private const string SubmissionIdHeaderName = "X-Feedback-Submission-Id";
        private static readonly int[] RetryDelays = { 2, 4, 8 }; // 秒
        private static readonly float[] BackgroundRetryDelays = { 60f, 300f, 900f, 1800f };

        private static List<PendingUpload> _pendingUploads;
        private static bool _isProcessing;
        private static bool _backgroundRunning;
        private static int _queueMutationVersion;
        private static Func<string, bool> _persistQueueOverride;
        private static Action<string> _deleteFileOverride;
        private static Func<PendingUpload, Action<bool>, IEnumerator> _tryUploadOverride;
        private static Func<PendingUpload, Action<FeedbackUploadAttempt>, IEnumerator> _tryUploadDetailedOverride;
        private static string _queueKeyOverride;
        private static string _feedbackDirectoryOverride;

        private static string QueueStorageKey =>
            string.IsNullOrEmpty(_queueKeyOverride) ? QueueKey : _queueKeyOverride;

        private static string QueueBackupStorageKey => QueueStorageKey + ".backup";

        /// <summary>
        /// 队列项收到 HTTP 成功响应，并已从持久队列移除后触发。
        /// </summary>
        public static event Action<string> QueuedUploadSucceeded;

        /// <summary>
        /// 带提交 ID 的队列项收到 HTTP 成功响应，并已从持久队列移除后触发。
        /// </summary>
        internal static event Action<FeedbackUploadCompletion> QueuedSubmissionSucceeded;

        internal static event Action<FeedbackUploadFailure> QueuedSubmissionFailed;

        /// <summary>
        /// 待上传项
        /// </summary>
        [Serializable]
        public class PendingUpload
        {
            public string submissionId;
            public string idempotencyKey;
            public string zipPath;
            public string version;
            public string userName;
            public long createdAt;
            public int retryCount;
            public long fileSize;
            public string fileSha256;
            public bool blocked;
            public long blockedUntil;
            public string lastFailureCode;
            public string lastFailureReason;
            public long lastAttemptAt;
        }

        /// <summary>
        /// 队列包装类（用于 JSON 序列化）
        /// </summary>
        [Serializable]
        private class QueueWrapper
        {
            public List<PendingUpload> items = new();
        }

        [Serializable]
        private class QueueEnvelope
        {
            public int schemaVersion;
            public string payload;
            public string checksum;
        }

        /// <summary>
        /// 获取反馈文件存储目录
        /// </summary>
        public static string FeedbackDirectory
        {
            get
            {
                if (AnalyticsBootstrap.IsAutomationIsolated)
                    throw new InvalidOperationException("Feedback storage is unavailable during isolated automation.");
                string dir = _feedbackDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "PendingFeedback");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// 初始化队列（从 PlayerPrefs 加载）
        /// </summary>
        public static void Initialize()
        {
            if (AnalyticsBootstrap.IsAutomationIsolated) return;
            _isProcessing = false;
            _backgroundRunning = false;
            _queueMutationVersion = 0;
            LoadQueue();
            CleanupExpired();
        }

        /// <summary>
        /// 启动后台补传循环。由包内 Bootstrap/Enqueue 调用，游戏项目无需额外接入。
        /// </summary>
        public static void StartBackgroundProcessing()
        {
            if (_backgroundRunning || !AnalyticsConfig.IsUploadConfigured)
                return;

            _backgroundRunning = true;
            CoroutineRunner.Instance.StartCoroutine(BackgroundProcessLoop());
        }

        /// <summary>
        /// 将上传可靠写入持久队列。
        /// </summary>
        public static bool TryEnqueue(string zipPath, string version, string userName)
        {
            return TryEnqueueCore(string.Empty, string.Empty, zipPath, version, userName);
        }

        /// <summary>
        /// 将带稳定提交 ID 的上传可靠写入持久队列。
        /// </summary>
        public static bool TryEnqueue(
            string submissionId,
            string zipPath,
            string version,
            string userName)
        {
            return TryEnqueueCore(submissionId, submissionId, zipPath, version, userName);
        }

        private static bool TryEnqueueCore(
            string submissionId,
            string idempotencyKey,
            string zipPath,
            string version,
            string userName)
        {
            if (AnalyticsBootstrap.IsAutomationIsolated) return false;
            if (_pendingUploads == null)
                LoadQueue();

            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                AnalyticsLog.LogWarning("[FeedbackQueue] 入队失败，文件不存在");
                return false;
            }

            if (_pendingUploads.Exists(p => p.zipPath == zipPath))
                return true;

            if (!TryComputeFileFingerprint(zipPath, out long fileSize, out string fileSha256, out string fingerprintFailure))
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 入队失败，文件校验失败: {SafeLogValue(fingerprintFailure)}");
                return false;
            }

            if (string.IsNullOrEmpty(idempotencyKey))
                idempotencyKey = Guid.NewGuid().ToString("N");

            var candidate = new List<PendingUpload>(_pendingUploads);
            var evicted = new List<PendingUpload>();

            while (candidate.Count >= MaxPendingCount)
            {
                var oldest = candidate[0];
                candidate.RemoveAt(0);
                evicted.Add(oldest);
            }

            var pending = new PendingUpload
            {
                submissionId = submissionId ?? string.Empty,
                idempotencyKey = idempotencyKey,
                zipPath = zipPath,
                version = version,
                userName = userName,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                retryCount = 0,
                fileSize = fileSize,
                fileSha256 = fileSha256,
                blocked = false,
                blockedUntil = 0,
                lastFailureCode = string.Empty,
                lastFailureReason = string.Empty,
                lastAttemptAt = 0
            };

            candidate.Add(pending);
            if (!TryPersistQueue(candidate))
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 入队持久化失败: {Path.GetFileName(zipPath)}");
                return false;
            }

            _pendingUploads = candidate;
            _queueMutationVersion++;

            foreach (var item in evicted)
                DeleteFileBestEffort(item.zipPath);

            AnalyticsLog.Log($"[FeedbackQueue] 已加入队列: {Path.GetFileName(zipPath)}");
            StartBackgroundProcessing();
            return true;
        }

        /// <summary>
        /// 将失败的上传加入队列。保留旧 API 以兼容现有调用方。
        /// </summary>
        public static void Enqueue(string zipPath, string version, string userName)
        {
            TryEnqueue(zipPath, version, userName);
        }

        /// <summary>
        /// 处理所有待上传的文件（启动时调用）
        /// </summary>
        public static IEnumerator ProcessPendingUploads()
        {
            return ProcessPendingUploadsInternal(false);
        }

        private static IEnumerator ProcessPendingUploadsInternal(bool respectBlockedUntil)
        {
            if (AnalyticsBootstrap.IsAutomationIsolated) yield break;
            if (_isProcessing) yield break;
            if (_pendingUploads == null) LoadQueue();
            if (_pendingUploads.Count == 0) yield break;

            _isProcessing = true;

            try
            {
                AnalyticsLog.Log($"[FeedbackQueue] 开始处理 {_pendingUploads.Count} 个待上传文件");
                var processedPaths = new HashSet<string>(StringComparer.Ordinal);

                while (true)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    PendingUpload pending = _pendingUploads.Find(item =>
                        !processedPaths.Contains(item.zipPath) &&
                        (!respectBlockedUntil || IsEligibleForAttempt(item, now)));
                    if (pending == null)
                        break;

                    processedPaths.Add(pending.zipPath);

                    if (!File.Exists(pending.zipPath))
                    {
                        ReportUploadFailure(
                            pending.submissionId,
                            FeedbackFailureCodes.FileRead,
                            "file_missing",
                            attempt: pending.retryCount + 1);
                        TryRemovePersisted(pending);
                        continue;
                    }

                    if (!TryValidatePendingFile(pending, out string validationReason))
                    {
                        ReportUploadFailure(
                            pending.submissionId,
                            FeedbackFailureCodes.FileRead,
                            validationReason,
                            attempt: pending.retryCount + 1);
                        PersistFailedAttempt(
                            pending,
                            new FeedbackUploadAttempt(
                                false,
                                FeedbackFailureCodes.FileRead,
                                reason: validationReason));
                        continue;
                    }

                    FeedbackUploadAttempt attempt = new FeedbackUploadAttempt(false, FeedbackFailureCodes.Unknown);
                    yield return TryUpload(pending, result => attempt = result);

                    if (attempt.Succeeded)
                    {
                        if (!TryRemovePersisted(pending))
                        {
                            ReportUploadFailure(
                                pending.submissionId,
                                FeedbackFailureCodes.QueuePersistence,
                                "queue_ack_persistence_failed",
                                attempt: pending.retryCount + 1);
                            PersistFailedAttempt(
                                pending,
                                new FeedbackUploadAttempt(
                                    false,
                                    FeedbackFailureCodes.QueuePersistence,
                                    reason: "queue_ack_persistence_failed"));
                            continue;
                        }

                        DeleteFileBestEffort(pending.zipPath);
                        AnalyticsLog.Log($"[FeedbackQueue] 上传成功: {Path.GetFileName(pending.zipPath)}");
                        NotifyQueuedUploadSucceeded(pending.zipPath);
                        NotifyQueuedSubmissionSucceeded(pending.submissionId);
                    }
                    else
                    {
                        int retryCount = PersistFailedAttempt(pending, attempt);
                        AnalyticsLog.LogWarning(
                            $"[FeedbackQueue] 上传失败 code={attempt.FailureCode} retryable={attempt.Retryable} 重试次数: {retryCount}");
                    }
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// 带重试的上传（供 ZipAttachmentUploader 调用）
        /// </summary>
        public static IEnumerator UploadWithRetry(string zipPath, string version, string userName, Action<bool> onComplete)
        {
            if (AnalyticsBootstrap.IsAutomationIsolated)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            string idempotencyKey = Guid.NewGuid().ToString("N");
            FeedbackUploadAttempt lastAttempt = new FeedbackUploadAttempt(false, FeedbackFailureCodes.Unknown);

            for (int i = 0; i <= MaxRetries; i++)
            {
                if (i > 0)
                {
                    int delay = RetryDelays[Math.Min(i - 1, RetryDelays.Length - 1)];
                    AnalyticsLog.Log($"[FeedbackQueue] 第 {i} 次重试，等待 {delay} 秒...");
                    yield return new WaitForSecondsRealtime(delay);
                }

                yield return DoUpload(
                    zipPath,
                    version,
                    result => lastAttempt = result,
                    submissionId: string.Empty,
                    idempotencyKey: idempotencyKey,
                    attempt: i + 1);

                if (lastAttempt.Succeeded)
                {
                    AnalyticsLog.Log($"[FeedbackQueue] 上传成功: {Path.GetFileName(zipPath)}");
                    onComplete?.Invoke(true);
                    yield break;
                }

                if (!lastAttempt.Retryable)
                    break;
            }

            // 所有重试都失败，加入队列
            AnalyticsLog.LogWarning($"[FeedbackQueue] 重试 {MaxRetries} 次后仍失败，加入离线队列");
            TryEnqueueCore(string.Empty, idempotencyKey, zipPath, version, userName);
            onComplete?.Invoke(false);
        }

        /// <summary>
        /// 执行单次上传
        /// </summary>
        private static IEnumerator DoUpload(
            string zipPath,
            string version,
            Action<FeedbackUploadAttempt> onComplete,
            string submissionId = null,
            string idempotencyKey = null,
            int attempt = 1)
        {
            if (!AnalyticsConfig.IsUploadConfigured)
            {
                const string code = FeedbackFailureCodes.NotConfigured;
                ReportUploadFailure(submissionId, code, "not_configured", attempt: attempt);
                onComplete?.Invoke(new FeedbackUploadAttempt(false, code, reason: "not_configured"));
                yield break;
            }

            string baseUrl = AnalyticsConfig.ServerUrl.TrimEnd('/');
            string uploadUrl = baseUrl + "/upload";
            string fileName = Path.GetFileName(zipPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            byte[] fileData;
            try
            {
                fileData = File.ReadAllBytes(zipPath);
            }
            catch (Exception e)
            {
                ReportUploadFailure(submissionId, FeedbackFailureCodes.FileRead, e.GetType().Name, attempt: attempt);
                onComplete?.Invoke(new FeedbackUploadAttempt(false, FeedbackFailureCodes.FileRead, reason: e.GetType().Name));
                yield break;
            }

            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation = null;
            try
            {
                request = CreateUploadRequest(
                    uploadUrl,
                    version,
                    timestamp,
                    fileName,
                    fileData,
                    submissionId,
                    idempotencyKey);
                operation = request.SendWebRequest();
            }
            catch (Exception exception)
            {
                request?.Dispose();
                ReportUploadFailure(
                    submissionId,
                    FeedbackFailureCodes.Connection,
                    exception.GetType().Name,
                    attempt: attempt);
            }
            if (operation == null)
            {
                onComplete?.Invoke(new FeedbackUploadAttempt(
                    false,
                    FeedbackFailureCodes.Connection,
                    reason: "request_creation_failed"));
                yield break;
            }

            using (request)
            {
                yield return operation;
                bool success = request.result == UnityWebRequest.Result.Success;
                if (!success)
                {
                    string failureCode = FeedbackFailureCodes.FromTransport(request.result, request.responseCode, request.error);
                    string requestId = request.GetResponseHeader("X-Request-ID") ?? request.GetResponseHeader("CF-RAY");
                    string reason = FeedbackFailureCodes.TransportReason(request.result, request.responseCode, request.error);
                    ReportUploadFailure(
                        submissionId,
                        failureCode,
                        reason,
                        request.responseCode,
                        requestId,
                        attempt);
                    onComplete?.Invoke(new FeedbackUploadAttempt(
                        false,
                        failureCode,
                        request.responseCode,
                        requestId,
                        reason));
                }
                else
                {
                    string requestId = FeedbackFailureCodes.SafeDiagnosticId(request.GetResponseHeader("X-Request-ID") ?? request.GetResponseHeader("CF-RAY"));
                    Debug.Log($"[FeedbackUpload] confirmed submission={FeedbackFailureCodes.SafeDiagnosticId(submissionId)} http={request.responseCode} request={requestId}");
                    onComplete?.Invoke(new FeedbackUploadAttempt(true, httpStatusCode: request.responseCode, requestId: requestId));
                }
            }
        }

        private static UnityWebRequest CreateUploadRequest(
            string uploadUrl,
            string version,
            string timestamp,
            string fileName,
            byte[] fileData,
            string submissionId,
            string idempotencyKey)
        {
            var form = new WWWForm();
            form.AddField("version", version);
            form.AddField("timestamp", timestamp);
            if (!string.IsNullOrEmpty(submissionId))
                form.AddField("submission_id", submissionId);
            form.AddBinaryData("file", fileData, fileName, "application/zip");

            UnityWebRequest request = UnityWebRequest.Post(uploadUrl, form);
            request.SetRequestHeader(UploadSecretHeaderName, AnalyticsConfig.UploadSecret);
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                request.SetRequestHeader(IdempotencyHeaderName, idempotencyKey);
                if (!string.IsNullOrEmpty(submissionId))
                    request.SetRequestHeader(SubmissionIdHeaderName, submissionId);
            }
            request.timeout = 60;
            return request;
        }

        /// <summary>
        /// 尝试上传单个待处理项
        /// </summary>
        private static IEnumerator TryUpload(PendingUpload pending, Action<FeedbackUploadAttempt> onComplete)
        {
            if (_tryUploadDetailedOverride != null)
            {
                yield return _tryUploadDetailedOverride(pending, onComplete);
                yield break;
            }

            if (_tryUploadOverride != null)
            {
                bool success = false;
                yield return _tryUploadOverride(pending, result => success = result);
                onComplete?.Invoke(new FeedbackUploadAttempt(
                    success,
                    success ? null : FeedbackFailureCodes.Unknown,
                    reason: success ? null : "test_or_legacy_override"));
                yield break;
            }

            yield return DoUpload(
                pending.zipPath,
                pending.version,
                onComplete,
                pending.submissionId,
                pending.idempotencyKey,
                pending.retryCount + 1);
        }

        /// <summary>
        /// 后台补传循环：有待传文件时按退避持续尝试，清空后退出，后续 Enqueue 会重新拉起。
        /// </summary>
        private static IEnumerator BackgroundProcessLoop()
        {
            int retryDelayIndex = 0;

            try
            {
                // Let the caller register its local acceptance before a synchronous read/config failure.
                yield return null;
                while (AnalyticsConfig.IsUploadConfigured)
                {
                    if (_pendingUploads == null)
                        LoadQueue();

                    if (_pendingUploads.Count == 0)
                        yield break;

                    if (!HasEligiblePendingUploads())
                    {
                        float blockedDelay = GetNextBlockedDelaySeconds();
                        if (blockedDelay < 0f)
                            yield break;

                        int observedMutationVersion = _queueMutationVersion;
                        yield return WaitForRetryOrQueueMutation(blockedDelay, observedMutationVersion);
                        continue;
                    }

                    int countBefore = _pendingUploads.Count;
                    if (!_isProcessing)
                        yield return ProcessPendingUploadsInternal(true);

                    if (_pendingUploads == null)
                        LoadQueue();

                    if (_pendingUploads.Count == 0)
                        yield break;

                    if (!HasEligiblePendingUploads())
                    {
                        float blockedDelay = GetNextBlockedDelaySeconds();
                        if (blockedDelay < 0f)
                            yield break;

                        int observedMutationVersion = _queueMutationVersion;
                        yield return WaitForRetryOrQueueMutation(blockedDelay, observedMutationVersion);
                        continue;
                    }

                    bool madeProgress = _pendingUploads.Count < countBefore;
                    if (madeProgress)
                        retryDelayIndex = 0;

                    float delay = GetBackgroundRetryDelaySeconds(retryDelayIndex);
                    float nextBlockedDelay = GetNextBlockedDelaySeconds();
                    if (nextBlockedDelay >= 0f)
                        delay = Math.Min(delay, nextBlockedDelay);
                    retryDelayIndex = Math.Min(retryDelayIndex + 1, BackgroundRetryDelays.Length - 1);
                    int retryMutationVersion = _queueMutationVersion;

                    AnalyticsLog.Log($"[FeedbackQueue] 后台补传仍有 {_pendingUploads.Count} 个待上传文件，{delay:0} 秒后重试");
                    yield return WaitForRetryOrQueueMutation(delay, retryMutationVersion);
                }
            }
            finally
            {
                _backgroundRunning = false;
            }
        }

        private static float GetBackgroundRetryDelaySeconds(int retryDelayIndex)
        {
            int index = Math.Max(0, Math.Min(retryDelayIndex, BackgroundRetryDelays.Length - 1));
            return BackgroundRetryDelays[index];
        }

        private static IEnumerator WaitForRetryOrQueueMutation(float delaySeconds, int observedMutationVersion)
        {
            float remaining = delaySeconds;
            while (remaining > 0f && observedMutationVersion == _queueMutationVersion)
            {
                float slice = Math.Min(1f, remaining);
                yield return new WaitForSecondsRealtime(slice);
                remaining -= slice;
            }
        }

        private static bool HasEligiblePendingUploads()
        {
            if (_pendingUploads == null || _pendingUploads.Count == 0)
                return false;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (PendingUpload item in _pendingUploads)
            {
                if (IsEligibleForAttempt(item, now))
                    return true;
            }

            return false;
        }

        private static bool IsEligibleForAttempt(PendingUpload item, long now)
        {
            return item != null && (!item.blocked || item.blockedUntil <= now);
        }

        private static float GetNextBlockedDelaySeconds()
        {
            if (_pendingUploads == null)
                return -1f;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long soonest = long.MaxValue;
            foreach (PendingUpload item in _pendingUploads)
            {
                if (item == null || !item.blocked || item.blockedUntil <= now)
                    continue;

                soonest = Math.Min(soonest, item.blockedUntil);
            }

            return soonest == long.MaxValue ? -1f : Math.Max(0f, soonest - now);
        }

        /// <summary>
        /// 清理过期文件
        /// </summary>
        private static void CleanupExpired()
        {
            if (_pendingUploads == null) return;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long maxAge = MaxPendingAgeDays * 24 * 60 * 60;

            var expired = _pendingUploads.FindAll(p => now - p.createdAt > maxAge);
            if (expired.Count == 0)
                return;

            var candidate = _pendingUploads.FindAll(p => !expired.Contains(p));
            if (!TryPersistQueue(candidate))
            {
                AnalyticsLog.LogWarning("[FeedbackQueue] 过期队列持久化失败，保留原队列");
                return;
            }

            _pendingUploads = candidate;
            foreach (var item in expired)
            {
                AnalyticsLog.Log($"[FeedbackQueue] 清理过期文件: {Path.GetFileName(item.zipPath)}");
                DeleteFileBestEffort(item.zipPath);
            }
        }

        private static bool TryRemovePersisted(PendingUpload item)
        {
            var candidate = new List<PendingUpload>(_pendingUploads);
            int index = candidate.FindIndex(p => p.zipPath == item.zipPath);
            if (index < 0)
                return false;

            candidate.RemoveAt(index);
            if (!TryPersistQueue(candidate))
                return false;

            _pendingUploads = candidate;
            return true;
        }

        private static int PersistFailedAttempt(PendingUpload item, FeedbackUploadAttempt attempt)
        {
            var candidate = ClonePendingUploads(_pendingUploads);
            PendingUpload candidateItem = candidate.Find(p => p.zipPath == item.zipPath);
            if (candidateItem == null)
                return item.retryCount;

            candidateItem.retryCount = Math.Min(int.MaxValue, candidateItem.retryCount + 1);
            candidateItem.lastFailureCode = FeedbackFailureCodes.Normalize(attempt.FailureCode);
            candidateItem.lastFailureReason = FeedbackFailureCodes.SafeDiagnosticId(attempt.Reason);
            candidateItem.lastAttemptAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            candidateItem.blocked = !attempt.Retryable;
            candidateItem.blockedUntil = attempt.Retryable
                ? 0
                : candidateItem.lastAttemptAt + NonRetryableRecheckDelaySeconds;
            if (TryPersistQueue(candidate))
            {
                _pendingUploads = candidate;
                return candidateItem.retryCount;
            }

            return item.retryCount;
        }

        private static List<PendingUpload> ClonePendingUploads(List<PendingUpload> source)
        {
            var clone = new List<PendingUpload>(source.Count);
            foreach (var item in source)
            {
                clone.Add(new PendingUpload
                {
                    submissionId = item.submissionId,
                    idempotencyKey = item.idempotencyKey,
                    zipPath = item.zipPath,
                    version = item.version,
                    userName = item.userName,
                    createdAt = item.createdAt,
                    retryCount = item.retryCount,
                    fileSize = item.fileSize,
                    fileSha256 = item.fileSha256,
                    blocked = item.blocked,
                    blockedUntil = item.blockedUntil,
                    lastFailureCode = item.lastFailureCode,
                    lastFailureReason = item.lastFailureReason,
                    lastAttemptAt = item.lastAttemptAt
                });
            }

            return clone;
        }

        private static bool TryValidatePendingFile(PendingUpload pending, out string reason)
        {
            reason = string.Empty;
            if (!TryComputeFileFingerprint(pending.zipPath, out long fileSize, out string fileSha256, out reason))
                return false;

            if (pending.fileSize > 0 && pending.fileSize != fileSize)
            {
                reason = "file_size_changed";
                return false;
            }

            if (!string.IsNullOrEmpty(pending.fileSha256) &&
                !string.Equals(pending.fileSha256, fileSha256, StringComparison.OrdinalIgnoreCase))
            {
                reason = "file_checksum_changed";
                return false;
            }

            return true;
        }

        private static bool TryComputeFileFingerprint(
            string path,
            out long fileSize,
            out string fileSha256,
            out string reason)
        {
            fileSize = 0;
            fileSha256 = string.Empty;
            reason = string.Empty;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    fileSize = stream.Length;
                    if (fileSize <= 0)
                    {
                        reason = "file_empty";
                        return false;
                    }

                    fileSha256 = ToLowerHex(sha.ComputeHash(stream));
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = exception.GetType().Name;
                return false;
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private static string SafeLogValue(string value)
        {
            return FeedbackFailureCodes.SafeDiagnosticId(value) ?? string.Empty;
        }

        private static void DeleteFileBestEffort(string zipPath)
        {
            try
            {
                if (_deleteFileOverride != null)
                {
                    _deleteFileOverride(zipPath);
                    return;
                }

                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }
            catch (Exception e)
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 删除文件失败: {e.GetType().Name}");
            }
        }

        private static void NotifyQueuedUploadSucceeded(string zipPath)
        {
            Action<string> handlers = QueuedUploadSucceeded;
            if (handlers == null)
                return;

            foreach (Action<string> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(zipPath);
                }
                catch (Exception e)
                {
                    AnalyticsLog.LogWarning($"[FeedbackQueue] 上传成功回调失败: {e.GetType().Name}");
                }
            }
        }

        private static void NotifyQueuedSubmissionSucceeded(string submissionId)
        {
            if (string.IsNullOrEmpty(submissionId))
                return;

            Action<FeedbackUploadCompletion> handlers = QueuedSubmissionSucceeded;
            if (handlers == null)
                return;

            var completion = new FeedbackUploadCompletion(submissionId);
            foreach (Action<FeedbackUploadCompletion> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(completion);
                }
                catch (Exception e)
                {
                    AnalyticsLog.LogWarning($"[FeedbackQueue] 提交成功回调失败: {e.GetType().Name}");
                }
            }
        }

        internal static void ReportUploadFailure(
            string submissionId,
            string code,
            string reason,
            long httpStatusCode = 0,
            string requestId = null,
            int attempt = 0)
        {
            var failure = new FeedbackUploadFailure(
                submissionId,
                code,
                httpStatusCode,
                requestId,
                reason,
                attempt);
            // Always available in release Players; never log URLs, credentials, paths, bodies or player input.
            Debug.LogWarning($"[FeedbackUpload] failed code={failure.FailureCode} retryable={failure.Retryable} submission={FeedbackFailureCodes.SafeDiagnosticId(submissionId)} http={failure.HttpStatusCode} request={failure.RequestId} reason={failure.Reason} attempt={failure.Attempt}");
            LogFailureDiagnostic(failure);
            if (string.IsNullOrEmpty(submissionId))
                return;
            Action<FeedbackUploadFailure> handlers = QueuedSubmissionFailed;
            if (handlers == null)
                return;
            foreach (Action<FeedbackUploadFailure> handler in handlers.GetInvocationList())
            {
                try { handler(failure); }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[FeedbackUpload] failure_callback_failed code={failure.FailureCode} submission={FeedbackFailureCodes.SafeDiagnosticId(submissionId)} reason={exception.GetType().Name}");
                }
            }
        }

        private static void LogFailureDiagnostic(FeedbackUploadFailure failure)
        {
            if (string.IsNullOrEmpty(failure.SubmissionId))
                return;

            var properties = new Dictionary<string, object>
            {
                ["submission_id"] = failure.SubmissionId,
                ["failure_code"] = failure.FailureCode,
                ["retryable"] = failure.Retryable,
                ["attempt"] = failure.Attempt,
                ["reason"] = failure.Reason
            };

            if (failure.HttpStatusCode > 0)
                properties["http_status"] = failure.HttpStatusCode;
            if (!string.IsNullOrEmpty(failure.RequestId))
                properties["request_id"] = failure.RequestId;

            bool accepted = AnalyticsService.TryLogEvent(
                "feedback_upload_failure",
                properties,
                new AnalyticsEventOptions(
                    "feedback.upload.failure." + failure.SubmissionId + "." + failure.Attempt,
                    0,
                    true));
            if (!accepted)
            {
                AnalyticsLog.LogWarning(
                    $"[FeedbackUpload] failure diagnostic not accepted code={failure.FailureCode} submission={FeedbackFailureCodes.SafeDiagnosticId(failure.SubmissionId)}");
            }
        }

        /// <summary>
        /// 从 PlayerPrefs 加载队列
        /// </summary>
        private static void LoadQueue()
        {
            _pendingUploads = new List<PendingUpload>();

            string json = PlayerPrefs.GetString(QueueStorageKey, "");
            if (TryDeserializeQueue(json, out List<PendingUpload> items, out bool legacy))
            {
                _pendingUploads = items;
                if (legacy)
                    TryPersistQueue(_pendingUploads);
                return;
            }

            string backup = PlayerPrefs.GetString(QueueBackupStorageKey, "");
            if (TryDeserializeQueue(backup, out items, out _))
            {
                _pendingUploads = items;
                AnalyticsLog.LogWarning("[FeedbackQueue] 主队列校验失败，已恢复备份队列");
                return;
            }

            if (!string.IsNullOrEmpty(json) || !string.IsNullOrEmpty(backup))
            {
                AnalyticsLog.LogWarning("[FeedbackQueue] 主队列和备份队列均无法通过校验，保留原始数据供诊断");
            }
        }

        /// <summary>
        /// 保存队列到 PlayerPrefs
        /// </summary>
        private static bool TryPersistQueue(List<PendingUpload> items)
        {
            string payload;
            string json;
            try
            {
                var wrapper = new QueueWrapper { items = items };
                payload = JsonUtility.ToJson(wrapper);
                json = JsonUtility.ToJson(new QueueEnvelope
                {
                    schemaVersion = QueueSchemaVersion,
                    payload = payload,
                    checksum = ComputeChecksum(payload)
                });
            }
            catch (Exception e)
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 序列化队列失败: {e.GetType().Name}");
                return false;
            }

            if (_persistQueueOverride != null)
            {
                try
                {
                    return _persistQueueOverride(json);
                }
                catch (Exception e)
                {
                    AnalyticsLog.LogWarning($"[FeedbackQueue] 保存队列失败: {e.GetType().Name}");
                    return false;
                }
            }

            string storageKey = QueueStorageKey;
            bool hadOriginal = PlayerPrefs.HasKey(storageKey);
            string originalJson = PlayerPrefs.GetString(storageKey, string.Empty);
            bool hadBackup = PlayerPrefs.HasKey(QueueBackupStorageKey);
            string originalBackup = PlayerPrefs.GetString(QueueBackupStorageKey, string.Empty);

            try
            {
                PlayerPrefs.SetString(storageKey, json);
                PlayerPrefs.Save();

                string persisted = PlayerPrefs.GetString(storageKey, string.Empty);
                if (!string.Equals(persisted, json, StringComparison.Ordinal) ||
                    !TryDeserializeQueue(persisted, out _, out _))
                    throw new IOException("queue_readback_mismatch");

                PlayerPrefs.SetString(QueueBackupStorageKey, json);
                PlayerPrefs.Save();
                string persistedBackup = PlayerPrefs.GetString(QueueBackupStorageKey, string.Empty);
                if (!string.Equals(persistedBackup, json, StringComparison.Ordinal) ||
                    !TryDeserializeQueue(persistedBackup, out _, out _))
                    throw new IOException("queue_backup_readback_mismatch");

                return true;
            }
            catch (Exception e)
            {
                try
                {
                    if (hadOriginal)
                        PlayerPrefs.SetString(storageKey, originalJson);
                    else
                        PlayerPrefs.DeleteKey(storageKey);
                    if (hadBackup)
                        PlayerPrefs.SetString(QueueBackupStorageKey, originalBackup);
                    else
                        PlayerPrefs.DeleteKey(QueueBackupStorageKey);
                    PlayerPrefs.Save();
                }
                catch
                {
                    // Keep the original persistence error as the actionable diagnostic.
                }

                AnalyticsLog.LogWarning($"[FeedbackQueue] 保存队列失败: {e.GetType().Name}");
                return false;
            }
        }

        private static bool TryDeserializeQueue(
            string json,
            out List<PendingUpload> items,
            out bool legacy)
        {
            items = null;
            legacy = false;
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                QueueEnvelope envelope = JsonUtility.FromJson<QueueEnvelope>(json);
                if (envelope != null && !string.IsNullOrEmpty(envelope.payload))
                {
                    if (envelope.schemaVersion != QueueSchemaVersion ||
                        !string.Equals(envelope.checksum, ComputeChecksum(envelope.payload), StringComparison.OrdinalIgnoreCase))
                        return false;

                    QueueWrapper wrapped = JsonUtility.FromJson<QueueWrapper>(envelope.payload);
                    if (wrapped?.items == null)
                        return false;

                    items = wrapped.items;
                    return true;
                }

                QueueWrapper legacyWrapper = JsonUtility.FromJson<QueueWrapper>(json);
                if (legacyWrapper?.items == null)
                    return false;

                items = legacyWrapper.items;
                legacy = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ComputeChecksum(string value)
        {
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        /// <summary>
        /// 获取待上传数量
        /// </summary>
        public static int PendingCount
        {
            get
            {
                if (AnalyticsBootstrap.IsAutomationIsolated) return 0;
                if (_pendingUploads == null) LoadQueue();
                return _pendingUploads.Count;
            }
        }

        /// <summary>
        /// 清空队列（调试用）
        /// </summary>
        public static void ClearQueue()
        {
            if (AnalyticsBootstrap.IsAutomationIsolated) return;
            if (_pendingUploads == null) LoadQueue();

            foreach (var item in _pendingUploads)
            {
                try
                {
                    if (File.Exists(item.zipPath))
                        File.Delete(item.zipPath);
                }
                catch { }
            }

            _pendingUploads.Clear();
            PlayerPrefs.DeleteKey(QueueStorageKey);
            PlayerPrefs.DeleteKey(QueueBackupStorageKey);
            PlayerPrefs.Save();
            _queueMutationVersion++;
            AnalyticsLog.Log("[FeedbackQueue] 队列已清空");
        }
    }
}
