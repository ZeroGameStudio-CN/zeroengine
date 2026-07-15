using System;
using System.Collections;
using System.Collections.Generic;
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

        private readonly List<QueueItem> _memoryQueue = new();
        private readonly string _queueKey;
        private readonly int _maxQueueSize;
        private bool _isFlushing;
        private bool _isDirty;
        private float _lastSaveTime;

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

            if (_isFlushing) return;

            var runner = CoroutineRunner.Instance;
            if (runner != null)
                runner.StartCoroutine(FlushCoroutine(serverUrl, secret));
        }

        private IEnumerator FlushCoroutine(string serverUrl, string secret)
        {
            _isFlushing = true;
            
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
                        lock (_memoryQueue)
                        {
                            int sentIndex = _memoryQueue.IndexOf(item);
                            if (sentIndex >= 0)
                                _memoryQueue.RemoveAt(sentIndex);
                            SaveToStorageLocked();
                        }
                    }
                    else
                    {
                        AnalyticsLog.LogWarning($"[ZGS.Analytics] Failed to send event: {request.error}");
                        AnalyticsLog.LogWarning($"[ZGS.Analytics] Response: {request.downloadHandler?.text}");
                        break;
                    }
                }
            }
            
            _isFlushing = false;
        }

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
