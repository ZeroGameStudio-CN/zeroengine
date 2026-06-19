using UnityEngine;

namespace ZGS.Analytics
{
    public static class AnalyticsBootstrap
    {
        private static bool initialized;

        public static bool IsInitialized => initialized;

        public static void Initialize(ZGSAnalyticsConfig config)
        {
            if (config == null)
            {
                AnalyticsLog.LogWarning("[ZGS.Analytics] Analytics config is null. Analytics disabled.");
                return;
            }

            if (!config.EnableAnalytics) return;
            if (initialized) return;

            // 设置 Debug 模式
            AnalyticsConfig.DebugMode = config.debugMode;

            // ZGS Server Provider
            if (!string.IsNullOrEmpty(config.zgsServerUrl))
            {
                AnalyticsConfig.ServerUrl = config.zgsServerUrl;
                AnalyticsConfig.Secret = config.zgsSecret;
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

            // 配置附件上传 - 统一使用 zgsSecret
            if (AnalyticsConfig.IsConfigured)
            {
                CrashReporter.RegisterAttachmentUploader(new ZipAttachmentUploader());

                // 初始化反馈上传队列并处理待上传文件
                FeedbackUploadQueue.Initialize();
                if (FeedbackUploadQueue.PendingCount > 0)
                {
                    AnalyticsLog.Log($"[ZGS.Analytics] 发现 {FeedbackUploadQueue.PendingCount} 个待上传的反馈文件");
                    CoroutineRunner.Instance.StartCoroutine(FeedbackUploadQueue.ProcessPendingUploads());
                }
            }

            // 初始化所有 Provider (会触发 SessionInfo.Initialize)
            AnalyticsService.Initialize();
            initialized = true;

            // 监听退出事件以释放资源
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        public static void Shutdown()
        {
            if (!initialized) return;
            initialized = false;
            Application.quitting -= Shutdown;
            AnalyticsService.Shutdown();
        }
    }
}
