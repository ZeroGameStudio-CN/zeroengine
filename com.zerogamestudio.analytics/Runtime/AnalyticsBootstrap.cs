using UnityEngine;

namespace ZGS.Analytics
{
    public static class AnalyticsBootstrap
    {
        public static bool IsAutomationIsolated { get; private set; }
        private static bool started;

        // Configure from AfterAssembliesLoaded, before any SDK service is used.
        public static void DisableForAutomation()
        {
            if (started)
                throw new System.InvalidOperationException("Analytics isolation must precede initialization.");
            IsAutomationIsolated = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStartup()
        {
            IsAutomationIsolated = false;
            started = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            started = true;
            if (IsAutomationIsolated) return;
            var config = Resources.Load<ZGSAnalyticsConfig>("ZGSAnalyticsConfig");
            if (config == null)
            {
                AnalyticsLog.LogWarning("[ZGS.Analytics] Config not found in Resources/ZGSAnalyticsConfig. Analytics disabled.");
                return;
            }

            if (!config.EnableAnalytics) return;

            // 设置 Debug 模式
            AnalyticsConfig.DebugMode = config.debugMode;
            AnalyticsConfig.AppId = config.appId;

            // ZGS Server Provider
            if (!string.IsNullOrEmpty(config.zgsServerUrl))
            {
                AnalyticsConfig.ServerUrl = config.zgsServerUrl;
                AnalyticsConfig.Secret = config.zgsSecret;
                AnalyticsConfig.UploadSecret = config.zgsUploadSecret;
                var zgsProvider = new ZGSServerProvider(config.zgsServerUrl, config.zgsSecret, config.appId);
                AnalyticsService.AddProvider(zgsProvider);
                SessionManager.Instance.StartSession(zgsProvider);
            }

            if (Application.isEditor && config.debugMode)
            {
                AnalyticsService.AddProvider(new DebugProvider());
            }

            // 初始化 CrashReporter (订阅 Unity 日志)
            CrashReporter.Initialize();

            // 配置附件上传
            var uploadConfigured = AnalyticsConfig.IsUploadConfigured;
            if (uploadConfigured)
            {
                CrashReporter.RegisterAttachmentUploader(new ZipAttachmentUploader());
            }

            // 加载反馈队列会发出过期记录的状态事件，先初始化 Provider 和会话身份。
            AnalyticsService.Initialize();

            if (uploadConfigured)
            {
                // 初始化反馈上传队列并处理待上传文件
                FeedbackUploadQueue.Initialize();
                if (FeedbackUploadQueue.PendingCount > 0)
                {
                    AnalyticsLog.Log($"[ZGS.Analytics] 发现 {FeedbackUploadQueue.PendingCount} 个待上传的反馈文件");
                }
                FeedbackUploadQueue.StartBackgroundProcessing();
            }

            AnalyticsService.Flush();

            // 监听退出事件以释放资源
            Application.quitting += AnalyticsService.Shutdown;
        }
    }
}
