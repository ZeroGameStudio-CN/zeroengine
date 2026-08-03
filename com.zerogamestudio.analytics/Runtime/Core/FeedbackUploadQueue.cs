using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        private const string UploadSecretHeaderName = "X-Upload-Secret";
        private static readonly int[] RetryDelays = { 2, 4, 8 }; // 秒
        private static readonly float[] BackgroundRetryDelays = { 60f, 300f, 900f, 1800f };

        private static List<PendingUpload> _pendingUploads;
        private static bool _isProcessing;
        private static bool _backgroundRunning;
        private static int _queueMutationVersion;
        private static Func<string, bool> _persistQueueOverride;
        private static Action<string> _deleteFileOverride;
        private static Func<PendingUpload, Action<bool>, IEnumerator> _tryUploadOverride;
        private static string _queueKeyOverride;

        private static string QueueStorageKey =>
            string.IsNullOrEmpty(_queueKeyOverride) ? QueueKey : _queueKeyOverride;

        /// <summary>
        /// 队列项收到 HTTP 成功响应，并已从持久队列移除后触发。
        /// </summary>
        public static event Action<string> QueuedUploadSucceeded;

        /// <summary>
        /// 待上传项
        /// </summary>
        [Serializable]
        public class PendingUpload
        {
            public string zipPath;
            public string version;
            public string userName;
            public long createdAt;
            public int retryCount;
        }

        /// <summary>
        /// 队列包装类（用于 JSON 序列化）
        /// </summary>
        [Serializable]
        private class QueueWrapper
        {
            public List<PendingUpload> items = new();
        }

        /// <summary>
        /// 获取反馈文件存储目录
        /// </summary>
        public static string FeedbackDirectory
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "PendingFeedback");
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
            if (_pendingUploads == null)
                LoadQueue();

            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 入队失败，文件不存在: {zipPath}");
                return false;
            }

            if (_pendingUploads.Exists(p => p.zipPath == zipPath))
                return true;

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
                zipPath = zipPath,
                version = version,
                userName = userName,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                retryCount = 0
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
                    PendingUpload pending = _pendingUploads.Find(item => !processedPaths.Contains(item.zipPath));
                    if (pending == null)
                        break;

                    processedPaths.Add(pending.zipPath);

                    if (!File.Exists(pending.zipPath))
                    {
                        AnalyticsLog.LogWarning($"[FeedbackQueue] 文件不存在，移除: {pending.zipPath}");
                        TryRemovePersisted(pending);
                        continue;
                    }

                    bool success = false;
                    yield return TryUpload(pending, result => success = result);

                    if (success)
                    {
                        if (!TryRemovePersisted(pending))
                        {
                            AnalyticsLog.LogWarning($"[FeedbackQueue] 上传成功但队列移除持久化失败: {Path.GetFileName(pending.zipPath)}");
                            continue;
                        }

                        DeleteFileBestEffort(pending.zipPath);
                        AnalyticsLog.Log($"[FeedbackQueue] 上传成功: {Path.GetFileName(pending.zipPath)}");
                        NotifyQueuedUploadSucceeded(pending.zipPath);
                    }
                    else
                    {
                        int retryCount = TryIncrementRetryCount(pending);
                        AnalyticsLog.LogWarning($"[FeedbackQueue] 上传失败，重试次数: {retryCount}");
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
            bool success = false;

            for (int i = 0; i <= MaxRetries; i++)
            {
                if (i > 0)
                {
                    int delay = RetryDelays[Math.Min(i - 1, RetryDelays.Length - 1)];
                    AnalyticsLog.Log($"[FeedbackQueue] 第 {i} 次重试，等待 {delay} 秒...");
                    yield return new WaitForSecondsRealtime(delay);
                }

                yield return DoUpload(zipPath, version, result => success = result);

                if (success)
                {
                    AnalyticsLog.Log($"[FeedbackQueue] 上传成功: {Path.GetFileName(zipPath)}");
                    onComplete?.Invoke(true);
                    yield break;
                }
            }

            // 所有重试都失败，加入队列
            AnalyticsLog.LogWarning($"[FeedbackQueue] 重试 {MaxRetries} 次后仍失败，加入离线队列");
            Enqueue(zipPath, version, userName);
            onComplete?.Invoke(false);
        }

        /// <summary>
        /// 执行单次上传
        /// </summary>
        private static IEnumerator DoUpload(string zipPath, string version, Action<bool> onComplete)
        {
            if (!AnalyticsConfig.IsUploadConfigured)
            {
                onComplete?.Invoke(false);
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
                AnalyticsLog.LogWarning($"[FeedbackQueue] 读取文件失败: {e.Message}");
                onComplete?.Invoke(false);
                yield break;
            }

            using var request = CreateUploadRequest(uploadUrl, version, timestamp, fileName, fileData);

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;
            if (!success)
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 上传失败: {request.error}");
            }

            onComplete?.Invoke(success);
        }

        private static UnityWebRequest CreateUploadRequest(
            string uploadUrl,
            string version,
            string timestamp,
            string fileName,
            byte[] fileData)
        {
            var form = new WWWForm();
            form.AddField("version", version);
            form.AddField("timestamp", timestamp);
            form.AddBinaryData("file", fileData, fileName, "application/zip");

            UnityWebRequest request = UnityWebRequest.Post(uploadUrl, form);
            request.SetRequestHeader(UploadSecretHeaderName, AnalyticsConfig.UploadSecret);
            request.timeout = 60;
            return request;
        }

        /// <summary>
        /// 尝试上传单个待处理项
        /// </summary>
        private static IEnumerator TryUpload(PendingUpload pending, Action<bool> onComplete)
        {
            if (_tryUploadOverride != null)
            {
                yield return _tryUploadOverride(pending, onComplete);
                yield break;
            }

            yield return DoUpload(pending.zipPath, pending.version, onComplete);
        }

        /// <summary>
        /// 后台补传循环：有待传文件时按退避持续尝试，清空后退出，后续 Enqueue 会重新拉起。
        /// </summary>
        private static IEnumerator BackgroundProcessLoop()
        {
            int retryDelayIndex = 0;

            try
            {
                while (AnalyticsConfig.IsUploadConfigured)
                {
                    if (_pendingUploads == null)
                        LoadQueue();

                    if (_pendingUploads.Count == 0)
                        yield break;

                    int countBefore = _pendingUploads.Count;
                    if (!_isProcessing)
                        yield return ProcessPendingUploads();

                    if (_pendingUploads == null)
                        LoadQueue();

                    if (_pendingUploads.Count == 0)
                        yield break;

                    bool madeProgress = _pendingUploads.Count < countBefore;
                    if (madeProgress)
                        retryDelayIndex = 0;

                    float delay = GetBackgroundRetryDelaySeconds(retryDelayIndex);
                    retryDelayIndex = Math.Min(retryDelayIndex + 1, BackgroundRetryDelays.Length - 1);
                    int observedMutationVersion = _queueMutationVersion;

                    AnalyticsLog.Log($"[FeedbackQueue] 后台补传仍有 {_pendingUploads.Count} 个待上传文件，{delay:0} 秒后重试");
                    yield return WaitForRetryOrQueueMutation(delay, observedMutationVersion);
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

        private static int TryIncrementRetryCount(PendingUpload item)
        {
            var candidate = ClonePendingUploads(_pendingUploads);
            PendingUpload candidateItem = candidate.Find(p => p.zipPath == item.zipPath);
            if (candidateItem == null)
                return item.retryCount;

            candidateItem.retryCount++;
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
                    zipPath = item.zipPath,
                    version = item.version,
                    userName = item.userName,
                    createdAt = item.createdAt,
                    retryCount = item.retryCount
                });
            }

            return clone;
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
                AnalyticsLog.LogWarning($"[FeedbackQueue] 删除文件失败: {e.Message}");
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
                    AnalyticsLog.LogWarning($"[FeedbackQueue] 上传成功回调失败: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 从 PlayerPrefs 加载队列
        /// </summary>
        private static void LoadQueue()
        {
            _pendingUploads = new List<PendingUpload>();

            string json = PlayerPrefs.GetString(QueueStorageKey, "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var wrapper = JsonUtility.FromJson<QueueWrapper>(json);
                if (wrapper?.items != null)
                    _pendingUploads = wrapper.items;
            }
            catch (Exception e)
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 加载队列失败: {e.Message}");
                _pendingUploads = new List<PendingUpload>();
            }
        }

        /// <summary>
        /// 保存队列到 PlayerPrefs
        /// </summary>
        private static bool TryPersistQueue(List<PendingUpload> items)
        {
            string json;
            try
            {
                var wrapper = new QueueWrapper { items = items };
                json = JsonUtility.ToJson(wrapper);
            }
            catch (Exception e)
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 序列化队列失败: {e.Message}");
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
                    AnalyticsLog.LogWarning($"[FeedbackQueue] 保存队列失败: {e.Message}");
                    return false;
                }
            }

            string storageKey = QueueStorageKey;
            bool hadOriginal = PlayerPrefs.HasKey(storageKey);
            string originalJson = PlayerPrefs.GetString(storageKey, string.Empty);

            try
            {
                PlayerPrefs.SetString(storageKey, json);
                PlayerPrefs.Save();
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
                    PlayerPrefs.Save();
                }
                catch
                {
                    // Keep the original persistence error as the actionable diagnostic.
                }

                AnalyticsLog.LogWarning($"[FeedbackQueue] 保存队列失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取待上传数量
        /// </summary>
        public static int PendingCount
        {
            get
            {
                if (_pendingUploads == null) LoadQueue();
                return _pendingUploads.Count;
            }
        }

        /// <summary>
        /// 清空队列（调试用）
        /// </summary>
        public static void ClearQueue()
        {
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
            PlayerPrefs.Save();
            _queueMutationVersion++;
            AnalyticsLog.Log("[FeedbackQueue] 队列已清空");
        }
    }
}
