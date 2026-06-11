using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.ModSystem
{
    /// <summary>
    /// Mod热重载组件。
    /// 监听窗口焦点变化，自动检测并重新加载变化的mod内容。
    /// 添加到场景中的GameObject上即可使用。
    /// </summary>
    public class ModHotReloader : MonoBehaviour
    {
        [SerializeField]
        private bool _enableHotReload = true;
        
#pragma warning disable 0414 // Field is assigned but never used
        [SerializeField]
        [Tooltip("热重载检测间隔（秒），仅在聚焦时生效")]
        private float _checkInterval = 0.5f;
        
        [SerializeField]
        [Tooltip("只在编辑器模式下启用热重载")]
        private bool _editorOnly = true;
#pragma warning restore 0414
        
        private bool _wasApplicationFocused = true;
        private float _lastCheckTime;
        private List<string> _pendingReloads = new();
        
        /// <summary>
        /// Mod重载完成事件
        /// </summary>
        public event Action OnModsReloaded;
        
        /// <summary>
        /// 单个Mod重载事件
        /// </summary>
        public event Action<string> OnModReloaded;
        
        public bool EnableHotReload
        {
            get => _enableHotReload;
            set => _enableHotReload = value;
        }
        
        private void Awake()
        {
#if !UNITY_EDITOR
            if (_editorOnly)
            {
                enabled = false;
                return;
            }
#endif
        }
        
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!_enableHotReload) return;
            
            if (hasFocus && !_wasApplicationFocused)
            {
                // 从失焦恢复聚焦时检查文件变化
                CheckAndReloadMods();
            }
            
            _wasApplicationFocused = hasFocus;
        }
        
        private void Update()
        {
            if (!_enableHotReload) return;
            if (!Application.isFocused) return;
            
            // 处理待处理的重载（延迟一帧以避免文件锁定问题）
            if (_pendingReloads.Count > 0)
            {
                foreach (var modId in _pendingReloads)
                {
                    try
                    {
                        ModLoader.Instance.ReloadMod(modId);
                        OnModReloaded?.Invoke(modId);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ModHotReloader] Failed to reload {modId}: {ex.Message}");
                    }
                }
                
                if (_pendingReloads.Count > 0)
                {
                    OnModsReloaded?.Invoke();
                    Debug.Log($"[ModHotReloader] Reloaded {_pendingReloads.Count} mod(s)");
                }
                
                _pendingReloads.Clear();
            }
        }
        
        /// <summary>
        /// 检查并重载变化的Mods
        /// </summary>
        public void CheckAndReloadMods()
        {
            if (!_enableHotReload) return;
            
            var changedMods = ModLoader.Instance.DetectChangedMods();
            
            if (changedMods.Any())
            {
                Debug.Log($"[ModHotReloader] Detected changes in {changedMods.Count} mod(s): {string.Join(", ", changedMods)}");
                _pendingReloads.AddRange(changedMods);
            }
        }
        
        /// <summary>
        /// 强制重载所有Mods
        /// </summary>
        public void ForceReloadAllMods()
        {
            foreach (var modId in ModLoader.Instance.LoadedMods.Keys.ToList())
            {
                try
                {
                    ModLoader.Instance.ReloadMod(modId);
                    OnModReloaded?.Invoke(modId);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModHotReloader] Failed to reload {modId}: {ex.Message}");
                }
            }
            
            OnModsReloaded?.Invoke();
        }
        
        /// <summary>
        /// 强制重载指定Mod
        /// </summary>
        public void ForceReloadMod(string modId)
        {
            try
            {
                ModLoader.Instance.ReloadMod(modId);
                OnModReloaded?.Invoke(modId);
                OnModsReloaded?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModHotReloader] Failed to reload {modId}: {ex.Message}");
            }
        }
    }
}
