using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZGS.Analytics
{
    /// <summary>
    /// ZGS Server Provider - 工业级事件上报
    /// 自动附加设备/会话信息到所有事件
    /// </summary>
    public class ZGSServerProvider : IAnalyticsEnqueueProvider, IAnalyticsFlushProvider
    {
        private readonly string _serverUrl;
        private readonly string _secret;
        private readonly string _appId;
        private readonly OfflineQueue _queue;

        /// <summary>
        /// 创建 ZGS 服务端 Provider
        /// </summary>
        /// <param name="serverUrl">服务器地址</param>
        /// <param name="secret">密钥</param>
        /// <param name="appId">应用/游戏标识 (如 POB, LLS)</param>
        public ZGSServerProvider(string serverUrl, string secret, string appId = "default")
            : this(serverUrl, secret, appId, new OfflineQueue())
        {
        }

        internal ZGSServerProvider(string serverUrl, string secret, string appId, OfflineQueue queue)
        {
            _serverUrl = serverUrl.TrimEnd('/') + "/events";
            _secret = secret;
            _appId = appId;
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public void Initialize(string userId)
        {
            // 初始化会话信息
            SessionInfo.Initialize();
            
            // 发送 session_start (包含完整设备信息)
            var props = DeviceInfo.ToDictionary();
            foreach (var kv in SessionInfo.ToDictionary())
                props[kv.Key] = kv.Value;
            
            TryLogEvent(
                "session_start",
                props,
                new AnalyticsEventOptions(durable: true));
            
            AnalyticsLog.Log($"[ZGS.Analytics] Initialized: user={SessionInfo.UserId.Substring(0, 8)}..., session #{SessionInfo.SessionNumber}");
        }

        public void LogEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            TryLogEvent(eventName, parameters, default);
        }

        public bool TryLogEvent(
            string eventName,
            Dictionary<string, object> parameters,
            AnalyticsEventOptions options)
        {
            if (string.IsNullOrEmpty(eventName))
                return false;

            if (!options.TryFreezeEnvelope(out var frozenOptions))
            {
                AnalyticsLog.LogWarning("[ZGS.Analytics] Event rejected because event_id is invalid.");
                return false;
            }

            var props = parameters != null
                ? new Dictionary<string, object>(parameters)
                : new Dictionary<string, object>();

            // Envelope-owned fields cannot be overridden or leaked into props.
            props.Remove("event_id");
            props["session_event_sequence"] = frozenOptions.SessionEventSequence;
            
            // 基础属性 (每个事件都带)
            if (!props.ContainsKey("is_editor"))
                props["is_editor"] = DeviceInfo.IsEditor;
            if (!props.ContainsKey("app_version"))
                props["app_version"] = DeviceInfo.AppVersion;
            if (!props.ContainsKey("platform"))
                props["platform"] = DeviceInfo.Platform;
            
            // 附加身份信息 (如果有)
            if (IdentityManager.HasAnyIdentity && !props.ContainsKey("identities"))
            {
                props["identities"] = IdentityManager.ToList();
            }
            
            var payload = new EventPayload
            {
                AppId = _appId,
                Event = eventName,
                UserId = SessionInfo.UserId,
                SessionId = SessionInfo.SessionId,
                EventId = frozenOptions.EventId,
                Timestamp = frozenOptions.OccurredAtUnixMs,
                Props = props
            };
            
            return _queue.Enqueue(payload, frozenOptions.Durable);
        }

        public void SetUserProperty(string key, object value)
        {
            LogEvent("set_user_property", new Dictionary<string, object> { [key] = value });
        }

        public void TrackScreen(string screenName)
        {
            LogEvent("screen_view", new Dictionary<string, object> { ["screen_name"] = screenName });
        }

        public void LogError(string error, string stackTrace)
        {
            LogEvent("app_exception", new Dictionary<string, object>
            {
                ["exception_message"] = error,
                ["exception_stacktrace"] = stackTrace?.Length > 1000 ? stackTrace.Substring(0, 1000) : stackTrace
            });
        }

        public void EndSession(int durationSeconds)
        {
            TryLogEvent(
                "session_end",
                new Dictionary<string, object>
                {
                    ["duration_seconds"] = durationSeconds,
                    ["session_number"] = SessionInfo.SessionNumber
                },
                new AnalyticsEventOptions(durable: true));
            
            Flush();
        }

        public void Flush()
        {
            _queue.FlushAll(_serverUrl, _secret);
        }

        #region EventPayload
        
        /// <summary>
        /// 事件负载 - 实现 ISerializableEvent 接口
        /// </summary>
        private class EventPayload : ISerializableEvent
        {
            public string AppId;
            public string Event;
            public string UserId;
            public string SessionId;
            public string EventId;
            public long Timestamp;
            public Dictionary<string, object> Props;

            public string ToJson()
            {
                var sb = new StringBuilder(512);
                sb.Append("{");
                AppendProperty(sb, "event_id", EventId);
                sb.Append(",");
                AppendProperty(sb, "app_id", AppId);
                sb.Append(",");
                AppendProperty(sb, "event", Event);
                sb.Append(",");
                AppendProperty(sb, "user_id", UserId);
                sb.Append(",");
                AppendProperty(sb, "session_id", SessionId);
                sb.Append(",");
                sb.AppendFormat("\"ts\":{0},", Timestamp);
                sb.Append("\"props\":{");
                
                bool first = true;
                foreach (var kv in Props)
                {
                    if (kv.Value == null) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    
                    sb.AppendFormat("\"{0}\":", EscapeJson(kv.Key));
                    AppendValue(sb, kv.Value);
                }
                
                sb.Append("}}");
                return sb.ToString();
            }

            private static void AppendProperty(StringBuilder sb, string key, string value)
            {
                sb.AppendFormat("\"{0}\":\"{1}\"", key, EscapeJson(value ?? string.Empty));
            }

            private static void AppendValue(StringBuilder sb, object value)
            {
                if (value == null)
                {
                    sb.Append("null");
                    return;
                }
                
                switch (value)
                {
                    case string s:
                        sb.AppendFormat("\"{0}\"", EscapeJson(s));
                        break;
                    case bool b:
                        sb.Append(b ? "true" : "false");
                        break;
                    case float f:
                        sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case double d:
                        sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case int i:
                        sb.Append(i);
                        break;
                    case long l:
                        sb.Append(l);
                        break;
                    case System.Collections.IList list:
                        sb.Append("[");
                        bool firstItem = true;
                        foreach (var item in list)
                        {
                            if (!firstItem) sb.Append(",");
                            firstItem = false;
                            AppendValue(sb, item);
                        }
                        sb.Append("]");
                        break;
                    case System.Collections.IDictionary dict:
                        sb.Append("{");
                        bool firstDict = true;
                        foreach (System.Collections.DictionaryEntry entry in dict)
                        {
                            if (!firstDict) sb.Append(",");
                            firstDict = false;
                            sb.AppendFormat("\"{0}\":", EscapeJson(entry.Key?.ToString() ?? string.Empty));
                            AppendValue(sb, entry.Value);
                        }
                        sb.Append("}");
                        break;
                    default:
                        // 其他类型转为字符串并转义
                        sb.AppendFormat("\"{0}\"", EscapeJson(value.ToString()));
                        break;
                }
            }

            private static string EscapeJson(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;

                var sb = new StringBuilder(s.Length + 10);
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '"': sb.Append("\\\""); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            // 控制字符转义为 \u00XX
                            if (c < 32)
                                sb.AppendFormat("\\u{0:x4}", (int)c);
                            else
                                sb.Append(c);
                            break;
                    }
                }
                return sb.ToString();
            }
        }
        
        #endregion
    }
}
