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
        /// 将失败的上传加入队列
        /// </summary>
        public static void Enqueue(string zipPath, string version, string userName)
        {
            if (_pendingUploads == null)
                LoadQueue();

            // 检查是否已存在
            if (_pendingUploads.Exists(p => p.zipPath == zipPath))
                return;

            // 限制队列大小
            while (_pendingUploads.Count >= MaxPendingCount)
            {
                var oldest = _pendingUploads[0];
                RemoveAndDeleteFile(oldest);
            }

            var pending = new PendingUpload
            {
                zipPath = zipPath,
                version = version,
                userName = userName,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                retryCount = 0
            };

            _pendingUploads.Add(pending);
            SaveQueue();

            AnalyticsLog.Log($"[FeedbackQueue] 已加入队列: {Path.GetFileName(zipPath)}");
            StartBackgroundProcessing();
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
            AnalyticsLog.Log($"[FeedbackQueue] 开始处理 {_pendingUploads.Count} 个待上传文件");

            // 复制列表，避免迭代时修改
            var toProcess = new List<PendingUpload>(_pendingUploads);

            foreach (var pending in toProcess)
            {
                // 检查文件是否存在
                if (!File.Exists(pending.zipPath))
                {
                    AnalyticsLog.LogWarning($"[FeedbackQueue] 文件不存在，移除: {pending.zipPath}");
                    _pendingUploads.Remove(pending);
                    continue;
                }

                // 尝试上传
                bool success = false;
                yield return TryUpload(pending, result => success = result);

                if (success)
                {
                    RemoveAndDeleteFile(pending);
                    AnalyticsLog.Log($"[FeedbackQueue] 上传成功: {Path.GetFileName(pending.zipPath)}");
                }
                else
                {
                    pending.retryCount++;
                    AnalyticsLog.LogWarning($"[FeedbackQueue] 上传失败，重试次数: {pending.retryCount}");
                }
            }

            SaveQueue();
            _isProcessing = false;
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

                    AnalyticsLog.Log($"[FeedbackQueue] 后台补传仍有 {_pendingUploads.Count} 个待上传文件，{delay:0} 秒后重试");
                    yield return new WaitForSecondsRealtime(delay);
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

        /// <summary>
        /// 清理过期文件
        /// </summary>
        private static void CleanupExpired()
        {
            if (_pendingUploads == null) return;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long maxAge = MaxPendingAgeDays * 24 * 60 * 60;

            var expired = _pendingUploads.FindAll(p => now - p.createdAt > maxAge);
            foreach (var item in expired)
            {
                AnalyticsLog.Log($"[FeedbackQueue] 清理过期文件: {Path.GetFileName(item.zipPath)}");
                RemoveAndDeleteFile(item);
            }

            if (expired.Count > 0)
                SaveQueue();
        }

        /// <summary>
        /// 从队列移除并删除文件
        /// </summary>
        private static void RemoveAndDeleteFile(PendingUpload item)
        {
            _pendingUploads.Remove(item);

            try
            {
                if (File.Exists(item.zipPath))
                    File.Delete(item.zipPath);
            }
            catch (Exception e)
            {
                AnalyticsLog.LogWarning($"[FeedbackQueue] 删除文件失败: {e.Message}");
            }
        }

        /// <summary>
        /// 从 PlayerPrefs 加载队列
        /// </summary>
        private static void LoadQueue()
        {
            _pendingUploads = new List<PendingUpload>();

            string json = PlayerPrefs.GetString(QueueKey, "");
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
        private static void SaveQueue()
        {
            var wrapper = new QueueWrapper { items = _pendingUploads };
            string json = JsonUtility.ToJson(wrapper);
            PlayerPrefs.SetString(QueueKey, json);
            PlayerPrefs.Save();
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
            PlayerPrefs.DeleteKey(QueueKey);
            PlayerPrefs.Save();
            AnalyticsLog.Log("[FeedbackQueue] 队列已清空");
        }
    }
}
