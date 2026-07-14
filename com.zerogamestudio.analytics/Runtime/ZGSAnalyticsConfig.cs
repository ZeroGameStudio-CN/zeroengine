using UnityEngine;

namespace ZGS.Analytics
{
    [CreateAssetMenu(fileName = "ZGSAnalyticsConfig", menuName = "ZGS/Analytics Config")]
    public class ZGSAnalyticsConfig : ScriptableObject
    {
        [Header("General Settings")]
        [Tooltip("Enable automated analytics")]
        public bool EnableAnalytics = true;
        
        [Tooltip("Application/Game Identifier for multi-game analytics")]
        public string appId = "";

        [Tooltip("Enable debug logging in Editor")]
        public bool debugMode = true;

        [Header("ZGS Server Settings")]
        [Tooltip("Your FastAPI analytics server URL (events + uploads)")]
        public string zgsServerUrl = "";
        [Tooltip("Secret key for authentication")]
        public string zgsSecret = "";

        [Tooltip("Optional feedback upload secret. Falls back to the event secret when empty.")]
        public string zgsUploadSecret = "";
    }
}

