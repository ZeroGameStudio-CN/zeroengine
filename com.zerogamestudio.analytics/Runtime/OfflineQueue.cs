using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ZGS.Analytics
{
    /// <summary>
    /// 可序列化事件接口
    /// </summary>
    public interface ISerializableEvent
    {
        string ToJson();
    }

    /// <summary>
    /// 离线队列 - 使用 PlayerPrefs 缓存未发送的事件
    /// </summary>
    public class OfflineQueue
    {
        private const string DefaultQueueKey = "zgs_analytics_queue";
        internal const int DefaultMaxQueueSize = 500;
        private const float SaveInterval = 5f; // 批量保存间隔（秒）
        private const string DurablePrefix = "D\t";
        private const string BufferedPrefix = "B\t";
        private const int MaximumRejectedRecords = 8;
        private const int MaximumRejectedBytes = 128 * 1024;

        private readonly List<QueueItem> _memoryQueue = new();
        private readonly string _queueKey;
        private readonly int _maxQueueSize;
        private bool _isFlushing;
        private bool _isDirty;
        private float _lastSaveTime;
        private double _notBefore;
        private int _failures;

        public OfflineQueue()
            : this(DefaultMaxQueueSize, DefaultQueueKey)
        {
        }

        internal OfflineQueue(int maxQueueSize, string queueKey)
        {
            if (maxQueueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxQueueSize));
            if (string.IsNullOrEmpty(queueKey))
                throw new ArgumentException("Queue key is required.", nameof(queueKey));

            _maxQueueSize = maxQueueSize;
            _queueKey = queueKey;
            LoadFromStorage();
            _lastSaveTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 将事件加入队列
        /// </summary>
        public bool Enqueue(ISerializableEvent payload, bool durable = false)
        {
            if (payload == null)
                return false;

            try
            {
                return Enqueue(payload.ToJson(), durable);
            }
            catch (Exception exception)
            {
                AnalyticsLog.LogWarning($"[ZGS.Analytics] Failed to serialize event: {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将 JSON 字符串加入队列
        /// </summary>
        public bool Enqueue(string json, bool durable = false)
        {
            if (string.IsNullOrEmpty(json))
                return false;

            lock (_memoryQueue)
            {
                List<QueueItem> previousItems = durable
                    ? new List<QueueItem>(_memoryQueue)
                    : null;

                while (_memoryQueue.Count >= _maxQueueSize)
                {
                    if (!durable || !TryEvictOldestBufferedLocked())
                    {
                        AnalyticsLog.LogWarning(
                            $"[ZGS.Analytics] Offline queue full; rejected {(durable ? "durable" : "buffered")} event.");
                        return false;
                    }
                }

                _memoryQueue.Add(new QueueItem(json, durable));
                _isDirty = true;

                if (durable)
                {
                    if (!SaveToStorageLocked())
                    {
                        _memoryQueue.Clear();
                        _memoryQueue.AddRange(previousItems);
                        return false;
                    }

                    _isDirty = false;
                    _lastSaveTime = Time.realtimeSinceStartup;
                }
                else
                {
                    TrySaveIfNeededLocked();
                }
            }

            return true;
        }

        /// <summary>
        /// 检查是否需要保存（防抖机制）
        /// </summary>
        private void TrySaveIfNeededLocked()
        {
            if (!_isDirty) return;

            float now = Time.realtimeSinceStartup;
            if (now - _lastSaveTime >= SaveInterval)
            {
                if (SaveToStorageLocked())
                {
                    _isDirty = false;
                    _lastSaveTime = now;
                }
            }
        }

        /// <summary>
        /// 刷新所有事件到服务器
        /// </summary>
        public void FlushAll(string serverUrl, string secret)
        {
            // Flush 前强制保存未持久化的数据
            if (_isDirty)
            {
                lock (_memoryQueue)
                {
                    if (SaveToStorageLocked())
                    {
                        _isDirty = false;
                        _lastSaveTime = Time.realtimeSinceStartup;
                    }
                }
            }

            if (_isFlushing || Time.realtimeSinceStartupAsDouble < _notBefore) return;

            var runner = CoroutineRunner.Instance;
            if (runner != null)
                runner.StartCoroutine(FlushCoroutine(serverUrl, secret));
        }

        internal IEnumerator FlushCoroutine(string serverUrl, string secret)
        {
            _isFlushing = true;
            try
            {
            while (true)
            {
                QueueItem item;
                lock (_memoryQueue)
                {
                    if (_memoryQueue.Count == 0) break;
                    item = _memoryQueue[0];
                }
                
                string body = $"{{\"secret\":\"{secret}\",\"body\":{item.Json}}}";
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                
                using (var request = new UnityWebRequest(serverUrl, "POST"))
                {
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.timeout = 10;
                    
                    yield return request.SendWebRequest();
                    
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        _failures = 0;
                        _notBefore = 0;
                        lock (_memoryQueue)
                        {
                            int sentIndex = _memoryQueue.IndexOf(item);
                            if (sentIndex >= 0)
                                _memoryQueue.RemoveAt(sentIndex);
                            SaveToStorageLocked();
                        }
                    }
                    else if (IsPermanentEventFailure(request.responseCode) && TryQuarantine(item, request.responseCode))
                    {
                        _failures = 0;
                        _notBefore = 0;
                        AnalyticsLog.LogWarning($"[ZGS.Analytics] Rejected event quarantined (HTTP {request.responseCode}); delivery continues.");
                    }
                    else
                    {
                        // Server bodies may contain credentials or submitted player data.
                        _failures = Math.Min(_failures + 1, 10);
                        _notBefore = Time.realtimeSinceStartupAsDouble + Math.Min(300, Math.Pow(2, _failures));
                        AnalyticsLog.LogWarning($"[ZGS.Analytics] Event retained after upload failure (HTTP {request.responseCode}).");
                        break;
                    }
                }
            }
            
            }
            finally { _isFlushing = false; }
        }

        // Authentication, routing, throttling and server/network failures may recover without
        // changing the event. Only event-specific permanent rejections leave the send queue.
        internal static bool IsPermanentEventFailure(long status) => status is 400 or 409 or 413 or 415 or 422;

        [Serializable] private sealed class RejectedRecord
        {
            public long httpStatus;
            public string sha256, json;
            public bool payloadOmitted;
        }

        private bool TryQuarantine(QueueItem item, long status)
        {
            lock (_memoryQueue)
            {
                int index = _memoryQueue.IndexOf(item);
                if (index < 0) return true;
                string storageKey = _queueKey + ".rejected.v1";
                string previous = PlayerPrefs.GetString(storageKey, "");
                try
                {
                    using var sha = SHA256.Create();
                    byte[] bytes = Encoding.UTF8.GetBytes(item.Json);
                    var record = new RejectedRecord
                    {
                        httpStatus = status,
                        sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(),
                        json = bytes.Length <= MaximumRejectedBytes / 2 ? item.Json : null,
                        payloadOmitted = bytes.Length > MaximumRejectedBytes / 2
                    };
                    var records = new List<string>(previous.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
                    string serialized = JsonUtility.ToJson(record);
                    if (Encoding.UTF8.GetByteCount(serialized) > MaximumRejectedBytes)
                    {
                        record.json = null; record.payloadOmitted = true;
                        serialized = JsonUtility.ToJson(record);
                    }
                    records.Add(serialized);
                    while (records.Count > MaximumRejectedRecords || Encoding.UTF8.GetByteCount(string.Join("\n", records)) > MaximumRejectedBytes)
                        records.RemoveAt(0);
                    PlayerPrefs.SetString(storageKey, string.Join("\n", records));
                    _memoryQueue.RemoveAt(index);
                    if (SaveToStorageLocked()) return true;
                    _memoryQueue.Insert(index, item);
                }
                catch (Exception)
                {
                    // Never discard the only copy when quarantine persistence fails.
                    if (!_memoryQueue.Contains(item)) _memoryQueue.Insert(index, item);
                }
                if (string.IsNullOrEmpty(previous)) PlayerPrefs.DeleteKey(storageKey);
                else PlayerPrefs.SetString(storageKey, previous);
                return false;
            }
        }

        internal string GetRejectedSnapshot() => PlayerPrefs.GetString(_queueKey + ".rejected.v1", "");

        private bool TryEvictOldestBufferedLocked()
        {
            for (int i = 0; i < _memoryQueue.Count; i++)
            {
                if (_memoryQueue[i].Durable)
                    continue;

                _memoryQueue.RemoveAt(i);
                AnalyticsLog.LogWarning("[ZGS.Analytics] Evicted oldest buffered event to admit a durable event.");
                return true;
            }

            return false;
        }

        private bool SaveToStorageLocked()
        {
            try
            {
                if (_memoryQueue.Count == 0)
                {
                    PlayerPrefs.DeleteKey(_queueKey);
                }
                else
                {
                    var lines = new string[_memoryQueue.Count];
                    for (int i = 0; i < _memoryQueue.Count; i++)
                    {
                        QueueItem item = _memoryQueue[i];
                        lines[i] = (item.Durable ? DurablePrefix : BufferedPrefix) + item.Json;
                    }

                    PlayerPrefs.SetString(_queueKey, string.Join("\n", lines));
                }

                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                AnalyticsLog.LogWarning($"[ZGS.Analytics] Failed to persist offline queue: {exception.Message}");
                return false;
            }
        }

        private void LoadFromStorage()
        {
            var stored = PlayerPrefs.GetString(_queueKey, "");
            if (string.IsNullOrEmpty(stored)) return;
            
            var lines = stored.Split('\n');
            int skipped = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                
                // 跳过损坏的 JSON (包含 C# 类型名)
                if (line.Contains("System.Collections.Generic"))
                {
                    skipped++;
                    continue;
                }
                
                bool durable = false;
                string json = line;
                if (line.StartsWith(DurablePrefix, StringComparison.Ordinal))
                {
                    durable = true;
                    json = line.Substring(DurablePrefix.Length);
                }
                else if (line.StartsWith(BufferedPrefix, StringComparison.Ordinal))
                {
                    json = line.Substring(BufferedPrefix.Length);
                }

                _memoryQueue.Add(new QueueItem(json, durable));
            }
            
            if (skipped > 0)
            {
                AnalyticsLog.LogWarning($"[ZGS.Analytics] Skipped {skipped} corrupted events from storage");
                SaveToStorageLocked(); // 保存清理后的队列
            }
            
            if (_memoryQueue.Count > 0)
                AnalyticsLog.Log($"[ZGS.Analytics] Loaded {_memoryQueue.Count} pending events from storage");
        }

        /// <summary>
        /// 清空队列 (用于清除损坏的数据)
        /// </summary>
        public void ClearQueue()
        {
            lock (_memoryQueue)
            {
                _memoryQueue.Clear();
            }
            _isDirty = false;
            PlayerPrefs.DeleteKey(_queueKey);
            PlayerPrefs.Save();
            AnalyticsLog.Log("[ZGS.Analytics] Queue cleared");
        }

        internal string[] GetPendingJsonSnapshot()
        {
            lock (_memoryQueue)
            {
                var result = new string[_memoryQueue.Count];
                for (int i = 0; i < _memoryQueue.Count; i++)
                    result[i] = _memoryQueue[i].Json;
                return result;
            }
        }

        internal bool[] GetDurabilitySnapshot()
        {
            lock (_memoryQueue)
            {
                var result = new bool[_memoryQueue.Count];
                for (int i = 0; i < _memoryQueue.Count; i++)
                    result[i] = _memoryQueue[i].Durable;
                return result;
            }
        }

        public int Count
        {
            get
            {
                lock (_memoryQueue)
                    return _memoryQueue.Count;
            }
        }

        private sealed class QueueItem
        {
            public QueueItem(string json, bool durable)
            {
                Json = json;
                Durable = durable;
            }

            public string Json { get; }
            public bool Durable { get; }
        }
    }

    /// <summary>
    /// 协程运行器 - 线程安全单例
    /// </summary>
    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;
        private static readonly object Lock = new();
        private double nextFlush;

        private void Update()
        {
            if (AnalyticsBootstrap.IsAutomationIsolated || Time.realtimeSinceStartupAsDouble < nextFlush) return;
            nextFlush = Time.realtimeSinceStartupAsDouble + 5;
            AnalyticsService.Flush();
        }
        
        public static CoroutineRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;
                
                lock (Lock)
                {
                    if (_instance != null) return _instance;
                    
                    var go = new GameObject("[ZGS.Analytics.CoroutineRunner]");
                    _instance = go.AddComponent<CoroutineRunner>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
