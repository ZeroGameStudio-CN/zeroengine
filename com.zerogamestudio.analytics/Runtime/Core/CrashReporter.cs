using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZGS.Analytics
{
    /// <summary>
    /// Crash/Bug 报告器 - 自动采集上下文并上报
    /// </summary>
    public static class CrashReporter
    {
        private const int MaxLogEntries = 50;
        
        private static readonly Queue<LogEntry> _recentLogs = new();
        private static readonly object _lock = new();
        private static bool _isInitialized;
        private static IAttachmentUploader _attachmentUploader;

        /// <summary>
        /// 初始化 Crash 报告器，订阅 Unity 日志
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            
            Application.logMessageReceived += OnLogMessageReceived;
            _isInitialized = true;
        }

        /// <summary>
        /// 清理
        /// </summary>
        public static void Shutdown()
        {
            if (!_isInitialized) return;
            
            Application.logMessageReceived -= OnLogMessageReceived;
            _isInitialized = false;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                _recentLogs.Enqueue(new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = type.ToString(),
                    Message = condition.Length > 200 ? condition.Substring(0, 200) : condition,
                    StackTrace = type == LogType.Error || type == LogType.Exception 
                        ? (stackTrace.Length > 500 ? stackTrace.Substring(0, 500) : stackTrace) 
                        : null
                });

                while (_recentLogs.Count > MaxLogEntries)
                    _recentLogs.Dequeue();
            }
        }

        /// <summary>
        /// 报告崩溃
        /// </summary>
        public static void ReportCrash(Exception exception, Dictionary<string, object> extraData = null)
        {
            var props = BuildReportProps("crash", exception?.GetType().Name ?? "Unknown", exception?.Message, exception?.StackTrace, extraData);
            AnalyticsService.LogEvent("crash_report", props);
        }

        /// <summary>
        /// 报告 Bug（用户主动反馈，仅结构化数据）
        /// </summary>
        public static void ReportBug(string userMessage, Dictionary<string, object> extraData = null)
        {
            var props = BuildReportProps("bug", "UserReport", userMessage, null, extraData);
            AnalyticsService.LogEvent("bug_report", props);
        }

        /// <summary>
        /// 注册附件上传器（业务项目实现）
        /// </summary>
        public static void RegisterAttachmentUploader(IAttachmentUploader uploader)
        {
            _attachmentUploader = uploader;
        }

        /// <summary>
        /// 报告 Bug 并上传附件（截图、存档等）
        /// </summary>
        /// <param name="request">上传请求</param>
        /// <returns>协程（需要 MonoBehaviour 启动）</returns>
        public static System.Collections.IEnumerator ReportBugWithAttachments(AttachmentUploadRequest request)
        {
            request = request ?? new AttachmentUploadRequest();

            if (_attachmentUploader is ZipAttachmentUploader)
            {
                yield return _attachmentUploader.Upload(request);
                yield break;
            }

            // 自定义旧上传器保持原事件路径，但不再覆盖调用方提供的时间线。
            var props = BuildReportProps("bug", "UserReport", request.UserMessage, null, request.ExtraData);
            AnalyticsService.LogEvent("bug_report", props);

            if (_attachmentUploader == null)
                yield break;

            if (string.IsNullOrEmpty(request.TimelineJson))
                request.TimelineJson = TimelineLogger.ToJson();

            yield return _attachmentUploader.Upload(request);
        }

        /// <summary>
        /// 报告错误日志
        /// </summary>
        public static void ReportError(string errorType, string message, string stackTrace = null)
        {
            var props = BuildReportProps("error", errorType, message, stackTrace, null);
            AnalyticsService.LogEvent("error_report", props);
        }

        private static Dictionary<string, object> BuildReportProps(
            string reportType,
            string errorType, 
            string message, 
            string stackTrace,
            Dictionary<string, object> extraData)
        {
            var props = new Dictionary<string, object>
            {
                ["report_type"] = reportType,
                ["error_type"] = errorType ?? "Unknown",
                ["message"] = message ?? "",
            };

            if (!string.IsNullOrEmpty(stackTrace))
            {
                // 限制 stack trace 长度
                props["stack_trace"] = stackTrace.Length > 2000 
                    ? stackTrace.Substring(0, 2000) 
                    : stackTrace;
            }

            // 附加 Timeline
            var timeline = TimelineLogger.ToList();
            if (timeline.Count > 0)
                props["timeline"] = timeline;

            // 附加最近日志
            var recentLogs = GetRecentLogs();
            if (recentLogs.Count > 0)
                props["recent_logs"] = recentLogs;

            // 附加设备快照
            props["device"] = new Dictionary<string, object>
            {
                ["platform"] = Application.platform.ToString(),
                ["os"] = SystemInfo.operatingSystem,
                ["device_model"] = SystemInfo.deviceModel,
                ["ram_mb"] = SystemInfo.systemMemorySize,
                ["gpu"] = SystemInfo.graphicsDeviceName,
                ["app_version"] = Application.version
            };

            // 附加额外数据
            if (extraData != null)
            {
                foreach (var kv in extraData)
                    props[kv.Key] = kv.Value;
            }

            return props;
        }

        private static List<Dictionary<string, object>> GetRecentLogs()
        {
            lock (_lock)
            {
                var result = new List<Dictionary<string, object>>(_recentLogs.Count);
                foreach (var entry in _recentLogs)
                {
                    var dict = new Dictionary<string, object>
                    {
                        ["ts"] = entry.Timestamp,
                        ["type"] = entry.Type,
                        ["msg"] = entry.Message
                    };
                    if (entry.StackTrace != null)
                        dict["stack"] = entry.StackTrace;
                    result.Add(dict);
                }
                return result;
            }
        }

        private class LogEntry
        {
            public long Timestamp;
            public string Type;
            public string Message;
            public string StackTrace;
        }
    }
}
