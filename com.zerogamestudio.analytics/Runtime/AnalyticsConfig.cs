namespace ZGS.Analytics
{
    /// <summary>SDK 全局配置</summary>
    public static class AnalyticsConfig
    {
        /// <summary>是否启用 Debug 日志</summary>
        public static bool DebugMode { get; set; } = true;

        /// <summary>服务器 URL（事件 + 上传）</summary>
        public static string ServerUrl { get; set; }

        /// <summary>应用/游戏标识（用于事件与反馈上传路由）</summary>
        public static string AppId { get; set; }

        /// <summary>认证密钥</summary>
        public static string Secret { get; set; }

        /// <summary>是否已配置服务器</summary>
        public static bool IsConfigured =>
            !string.IsNullOrEmpty(ServerUrl) && !string.IsNullOrEmpty(Secret);
    }
}
