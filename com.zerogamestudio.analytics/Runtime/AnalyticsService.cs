using System.Collections.Generic;
using UnityEngine;

namespace ZGS.Analytics
{
    /// <summary>
    /// 统一埋点服务入口
    /// </summary>
    public static class AnalyticsService
    {
        private static readonly List<IAnalyticsProvider> _providers = new();
        private static bool _initialized;

        #region Public API
        
        /// <summary>匿名用户ID</summary>
        public static string UserId => SessionInfo.UserId;
        
        /// <summary>当前会话ID</summary>
        public static string SessionId => SessionInfo.SessionId;
        
        /// <summary>会话次数</summary>
        public static int SessionNumber => SessionInfo.SessionNumber;

        public static void AddProvider(IAnalyticsProvider provider)
        {
            if (!_providers.Contains(provider))
                _providers.Add(provider);
        }

        /// <summary>
        /// 初始化所有 Provider
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            foreach (var provider in _providers)
                provider.Initialize(SessionInfo.UserId);
            
            AnalyticsLog.Log($"[ZGS.Analytics] Initialized with {_providers.Count} provider(s), user={UserId.Substring(0, 8)}...");
        }

        /// <summary>
        /// 关闭服务并释放资源
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            _initialized = false;

            CrashReporter.Shutdown();
            _providers.Clear();
        }

        /// <summary>
        /// 记录自定义事件
        /// </summary>
        public static void LogEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            TryLogEvent(eventName, parameters, default);
        }

        /// <summary>
        /// Builds one immutable event envelope and asks all configured providers to enqueue it.
        /// </summary>
        /// <returns>True when at least one upload provider accepted the envelope.</returns>
        public static bool TryLogEvent(
            string eventName,
            Dictionary<string, object> parameters,
            AnalyticsEventOptions options)
        {
            if (string.IsNullOrEmpty(eventName) || _providers.Count == 0)
                return false;

            if (!options.TryFreezeEnvelope(out var frozenOptions))
            {
                AnalyticsLog.LogWarning("[ZGS.Analytics] Event rejected because event_id is invalid.");
                return false;
            }

            bool accepted = false;
            foreach (var provider in _providers)
            {
                try
                {
                    if (provider is IAnalyticsEnqueueProvider enqueueProvider)
                        accepted |= enqueueProvider.TryLogEvent(eventName, parameters, frozenOptions);
                    else
                        provider.LogEvent(eventName, parameters);
                }
                catch (System.Exception exception)
                {
                    AnalyticsLog.LogWarning(
                        $"[ZGS.Analytics] Provider rejected event with exception: {exception.Message}");
                }
            }

            return accepted;
        }

        /// <summary>
        /// 设置用户属性
        /// </summary>
        public static void SetUserProperty(string key, object value)
        {
            foreach (var provider in _providers)
                provider.SetUserProperty(key, value);
        }

        /// <summary>
        /// 追踪页面
        /// </summary>
        public static void TrackScreen(string screenName)
        {
            foreach (var provider in _providers)
                provider.TrackScreen(screenName);
        }

        /// <summary>
        /// 记录异常
        /// </summary>
        public static void LogError(string error, string stackTrace)
        {
            foreach (var provider in _providers)
                provider.LogError(error, stackTrace);
        }

        /// <summary>
        /// 设置平台身份 (如 Steam, Epic, 公司UID等)
        /// </summary>
        public static void SetIdentity(UserIdentity identity)
        {
            IdentityManager.SetIdentity(identity);
        }

        /// <summary>
        /// 获取指定平台的身份
        /// </summary>
        public static UserIdentity GetIdentity(string platform)
        {
            return IdentityManager.GetIdentity(platform);
        }

        #endregion

        #region Timeline API

        /// <summary>
        /// 记录 Timeline 日志（本地缓存，不立即上传）
        /// </summary>
        public static void LogTimeline(string eventName, Dictionary<string, object> data = null)
        {
            TimelineLogger.Log(eventName, data);
        }

        /// <summary>
        /// 记录 Timeline 日志（便捷方法）
        /// </summary>
        public static void LogTimeline(string eventName, string message)
        {
            // TimelineLogger.Log() 内部会复制 Dictionary，直接创建临时对象即可
            TimelineLogger.Log(eventName, new Dictionary<string, object> { ["msg"] = message });
        }

        /// <summary>
        /// 获取 Timeline JSON
        /// </summary>
        public static string GetTimelineJson() => TimelineLogger.ToJson();

        /// <summary>
        /// 清空 Timeline
        /// </summary>
        public static void ClearTimeline() => TimelineLogger.Clear();

        /// <summary>
        /// 获取 Timeline 条目数
        /// </summary>
        public static int TimelineCount => TimelineLogger.Count;

        /// <summary>
        /// 获取 Timeline 快照 (用于存档)
        /// </summary>
        public static TimelineLogger.TimelineEntry[] GetTimelineSnapshot() => TimelineLogger.GetSnapshot();

        /// <summary>
        /// 还原 Timeline 快照 (用于读档)
        /// </summary>
        public static void RestoreTimelineSnapshot(IEnumerable<TimelineLogger.TimelineEntry> snapshot) => TimelineLogger.RestoreSnapshot(snapshot);

        /// <summary>
        /// 获取所有 Timeline 条目 (只读)
        /// </summary>
        public static IReadOnlyList<TimelineLogger.TimelineEntry> TimelineEvents => TimelineLogger.GetSnapshot();

        #endregion

        #region Crash/Bug Reporting API

        /// <summary>
        /// 报告崩溃
        /// </summary>
        public static void ReportCrash(System.Exception exception, Dictionary<string, object> extraData = null)
        {
            CrashReporter.ReportCrash(exception, extraData);
        }

        /// <summary>
        /// 报告 Bug（用户主动反馈）
        /// </summary>
        public static void ReportBug(string userMessage, Dictionary<string, object> extraData = null)
        {
            CrashReporter.ReportBug(userMessage, extraData);
        }

        /// <summary>
        /// 报告错误
        /// </summary>
        public static void ReportError(string errorType, string message, string stackTrace = null)
        {
            CrashReporter.ReportError(errorType, message, stackTrace);
        }

        /// <summary>
        /// 注册附件上传器（业务项目实现具体的上传逻辑）
        /// </summary>
        public static void RegisterAttachmentUploader(IAttachmentUploader uploader)
        {
            CrashReporter.RegisterAttachmentUploader(uploader);
        }

        /// <summary>
        /// 报告 Bug 并上传附件（截图、存档等）
        /// </summary>
        /// <param name="request">上传请求</param>
        /// <returns>协程（需要 MonoBehaviour 启动）</returns>
        public static System.Collections.IEnumerator ReportBugWithAttachments(AttachmentUploadRequest request)
        {
            return CrashReporter.ReportBugWithAttachments(request);
        }

        #endregion

        internal static void ResetForTests()
        {
            _providers.Clear();
            _initialized = false;
        }
    }
}
