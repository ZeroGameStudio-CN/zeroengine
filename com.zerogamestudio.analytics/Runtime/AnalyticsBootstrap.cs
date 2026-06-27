using UnityEngine;

namespace ZGS.Analytics
{
    public static class AnalyticsBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
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

            // 监听退出事件以释放资源
            Application.quitting += AnalyticsService.Shutdown;
        }
    }
}
