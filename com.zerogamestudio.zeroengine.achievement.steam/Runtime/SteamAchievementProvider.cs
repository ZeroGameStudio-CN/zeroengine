using UnityEngine;

#if STEAMWORKS_NET && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX)
using Steamworks;
#endif

namespace ZeroEngine.Achievement.Providers
{
    /// <summary>
    /// Legacy Steam Achievement Provider. The host initializes the Steamworks.NET SDK
    /// and registers the provider explicitly.
    /// </summary>
    public class SteamAchievementProvider : IAchievementProvider
    {
        private bool _isInitialized = false;

        public bool Initialize()
        {
#if STEAMWORKS_NET && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX)
            try
            {
                _isInitialized = SteamUserStats.RequestCurrentStats();
                return _isInitialized;
            }
            catch (System.InvalidOperationException)
            {
                _isInitialized = false;
                Debug.LogWarning("[SteamAchievementProvider] Initialize the Steamworks.NET SDK before registration.");
                return false;
            }
#else
            Debug.LogWarning("[SteamAchievementProvider] Steam achievements require a supported desktop target and Steamworks.NET.");
            return false;
#endif
        }

        public void Unlock(string id)
        {
            if (!_isInitialized) return;

#if STEAMWORKS_NET && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX)
            // Check if already unlocked on Steam to avoid redundant calls
            if (SteamUserStats.GetAchievement(id, out bool achieved))
            {
                if (!achieved)
                {
                    bool success = SteamUserStats.SetAchievement(id);
                    if (success)
                    {
                        SteamUserStats.StoreStats();
                        // Debug.Log($"[SteamAchievementProvider] Unlocked: {id}");
                    }
                    else
                    {
                        Debug.LogWarning($"[SteamAchievementProvider] Failed to set achievement: {id}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[SteamAchievementProvider] Achievement ID not found on Steam: {id}");
            }
#endif
        }

        public void SetProgress(string id, float progress)
        {
            if (!_isInitialized) return;

#if STEAMWORKS_NET && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX)
            // Steam achievements based on stats are handled automatically by Steam when stats change.
            // However, we can use IndicateAchievementProgress to show a notification popup
            // for achievements that have a "Progress Stat" configured in Steamworks.
            // 
            // Note: This API takes (uint current, uint max). 
            // Since we use float (0.0-1.0), we can map it to 0-100 for display generic progress.
            
            uint cur = (uint)(Mathf.Clamp01(progress) * 100);
            uint max = 100;
            
            SteamUserStats.IndicateAchievementProgress(id, cur, max);
#endif
        }
    }
}
