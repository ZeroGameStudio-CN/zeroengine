using UnityEngine;

using Steamworks;

namespace ZeroEngine.Achievement.Providers
{
    /// <summary>
    /// Steam Achievement Provider.
    /// Requires Unity-Steamworks (SteamManager) to be initialized.
    /// </summary>
    public class SteamAchievementProvider : IAchievementProvider
    {
        private bool _isInitialized = false;

        public bool Initialize()
        {
            // Check if SteamManager is present and initialized
            // Note: SteamManager is the standard singleton from Steamworks.NET
            if (SteamManager.Initialized)
            {
                // Request current stats from Steam
                SteamUserStats.RequestCurrentStats();
                _isInitialized = true;
                return true;
            }
            else
            {
                Debug.LogWarning("[SteamAchievementProvider] SteamManager is not initialized.");
                return false;
            }
        }

        public void Unlock(string id)
        {
            if (!_isInitialized) return;

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
        }

        public void SetProgress(string id, float progress)
        {
            if (!_isInitialized) return;

            // Steam achievements based on stats are handled automatically by Steam when stats change.
            // However, we can use IndicateAchievementProgress to show a notification popup
            // for achievements that have a "Progress Stat" configured in Steamworks.
            // 
            // Note: This API takes (uint current, uint max). 
            // Since we use float (0.0-1.0), we can map it to 0-100 for display generic progress.
            
            uint cur = (uint)(Mathf.Clamp01(progress) * 100);
            uint max = 100;
            
            SteamUserStats.IndicateAchievementProgress(id, cur, max);
        }
    }
}
