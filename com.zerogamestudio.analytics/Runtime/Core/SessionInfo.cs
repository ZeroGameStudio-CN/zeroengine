using System;
using UnityEngine;

namespace ZGS.Analytics
{
    /// <summary>
    /// 会话与用户信息管理 - 工业级标准
    /// </summary>
    public static class SessionInfo
    {
        private const string UserIdKey = "zgs_analytics_user_id";
        private const string SessionCountKey = "zgs_analytics_session_count";
        private const string FirstOpenKey = "zgs_analytics_first_open";
        
        private static string _sessionId = string.Empty;
        private static string _userId = string.Empty;
        private static int _sessionNumber;
        private static long _firstOpenTime;
        private static long _eventSequence;
        private static DateTime _sessionStartTime;
        private static bool _initialized;
        
        #region Public Properties
        
        /// <summary>是否已初始化</summary>
        public static bool IsInitialized => _initialized;
        
        /// <summary>当前会话ID (每次启动生成)</summary>
        public static string SessionId => _initialized ? _sessionId : string.Empty;
        
        /// <summary>匿名用户ID (本地持久化)</summary>
        public static string UserId => _initialized ? _userId : string.Empty;
        
        /// <summary>第几次启动 (累计)</summary>
        public static int SessionNumber => _sessionNumber;
        
        /// <summary>首次打开时间 (Unix毫秒)</summary>
        public static long FirstOpenTime => _firstOpenTime;
        
        /// <summary>安装天数</summary>
        public static int DaysSinceInstall
        {
            get
            {
                if (_firstOpenTime == 0) return 0;
                var firstOpen = DateTimeOffset.FromUnixTimeMilliseconds(_firstOpenTime);
                return (int)(DateTimeOffset.UtcNow - firstOpen).TotalDays;
            }
        }
        
        /// <summary>当前会话开始时间</summary>
        public static DateTime SessionStartTime => _sessionStartTime;
        
        /// <summary>当前会话持续秒数</summary>
        public static int SessionDurationSeconds => (int)(DateTime.UtcNow - _sessionStartTime).TotalSeconds;
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// 初始化会话信息 (应在 AnalyticsBootstrap 中调用)
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            
            // 生成新的 session ID
            _sessionId = Guid.NewGuid().ToString();
            _sessionStartTime = DateTime.UtcNow;
            _eventSequence = 0;
            
            // 加载或生成 user ID
            _userId = PlayerPrefs.GetString(UserIdKey, string.Empty);
            if (string.IsNullOrEmpty(_userId))
            {
                _userId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(UserIdKey, _userId);
            }
            
            // 增加会话计数
            _sessionNumber = PlayerPrefs.GetInt(SessionCountKey, 0) + 1;
            PlayerPrefs.SetInt(SessionCountKey, _sessionNumber);
            
            // 记录首次打开时间
            var storedFirstOpen = PlayerPrefs.GetString(FirstOpenKey, string.Empty);
            if (string.IsNullOrEmpty(storedFirstOpen) || !long.TryParse(storedFirstOpen, out _firstOpenTime))
            {
                _firstOpenTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                PlayerPrefs.SetString(FirstOpenKey, _firstOpenTime.ToString());
            }
            
            PlayerPrefs.Save();
            
            AnalyticsLog.Log($"[ZGS.Analytics] Session initialized: user={_userId.Substring(0, 8)}..., session={_sessionNumber}, days_since_install={DaysSinceInstall}");
        }
        
        #endregion
        
        #region Helpers

        internal static long NextEventSequence()
        {
            lock (typeof(SessionInfo))
            {
                return ++_eventSequence;
            }
        }

        internal static void ResetEventSequenceForTests()
        {
            lock (typeof(SessionInfo))
            {
                _eventSequence = 0;
            }
        }
        
        /// <summary>
        /// 获取会话属性的字典 (用于事件附加, 不含已在顶层的 session_id/user_id)
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, object> ToDictionary()
        {
            return new System.Collections.Generic.Dictionary<string, object>
            {
                ["session_number"] = SessionNumber,
                ["first_open_time"] = FirstOpenTime,
                ["days_since_install"] = DaysSinceInstall
            };
        }
        
        #endregion
    }
}
