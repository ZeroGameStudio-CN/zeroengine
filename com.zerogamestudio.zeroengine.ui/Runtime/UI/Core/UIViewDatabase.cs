using System;
using System.Collections.Generic;
using UnityEngine;

#if ZEROENGINE_ADDRESSABLES
using UnityEngine.AddressableAssets;
#endif

namespace ZeroEngine.UI
{
    /// <summary>
    /// UI View 配置数据库
    /// 集中管理所有 UI Prefabs 的配置
    /// </summary>
    [CreateAssetMenu(fileName = "UIViewDatabase", menuName = "ZeroEngine/UI/View Database")]
    public class UIViewDatabase : ScriptableObject
    {
        [Header("View Configurations")]
        public List<UIViewEntry> views = new();

        /// <summary>
        /// 获取所有配置
        /// </summary>
        public IEnumerable<UIViewConfig> GetAllConfigs()
        {
            foreach (var entry in views)
            {
                yield return entry.ToConfig();
            }
        }

        /// <summary>
        /// 获取指定 View 的配置
        /// </summary>
        public UIViewConfig GetConfig(string viewName)
        {
            foreach (var entry in views)
            {
                if (entry.viewName == viewName)
                    return entry.ToConfig();
            }
            return null;
        }

        /// <summary>
        /// 注册到 UIManager
        /// </summary>
        public void RegisterToManager(UIManager manager)
        {
            if (manager == null)
            {
                Debug.LogError("[UIViewDatabase] UIManager is null!");
                return;
            }

            manager.RegisterViews(GetAllConfigs());
            Debug.Log($"[UIViewDatabase] Registered {views.Count} views to UIManager");
        }

        /// <summary>
        /// 注册到单例 UIManager
        /// </summary>
        public void RegisterToManager()
        {
            RegisterToManager(UIManager.Instance);
        }

#if UNITY_EDITOR
        [ContextMenu("Validate Entries")]
        private void ValidateEntries()
        {
            int errors = 0;
            foreach (var entry in views)
            {
                if (string.IsNullOrEmpty(entry.viewName))
                {
                    Debug.LogError("[UIViewDatabase] Found entry with empty viewName!");
                    errors++;
                }

#if ZEROENGINE_ADDRESSABLES
                if (entry.prefabRef == null || !entry.prefabRef.RuntimeKeyIsValid())
                {
                    if (entry.prefab == null)
                    {
                        Debug.LogWarning($"[UIViewDatabase] Entry '{entry.viewName}' has no prefab assigned!");
                        errors++;
                    }
                }
#else
                if (entry.prefab == null)
                {
                    Debug.LogWarning($"[UIViewDatabase] Entry '{entry.viewName}' has no prefab assigned!");
                    errors++;
                }
#endif
            }

            if (errors == 0)
            {
                Debug.Log($"[UIViewDatabase] All {views.Count} entries are valid!");
            }
            else
            {
                Debug.LogWarning($"[UIViewDatabase] Found {errors} issues in {views.Count} entries");
            }
        }

        [ContextMenu("Sort by Layer")]
        private void SortByLayer()
        {
            views.Sort((a, b) =>
            {
                int layerCompare = ((int)a.layer).CompareTo((int)b.layer);
                if (layerCompare != 0) return layerCompare;
                return string.Compare(a.viewName, b.viewName, StringComparison.Ordinal);
            });

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[UIViewDatabase] Sorted entries by layer");
        }

        [ContextMenu("Auto-Find Prefabs")]
        private void AutoFindPrefabs()
        {
            string[] searchPaths = { "Assets/Prefabs/UI", "Assets/Resources/UI", "Assets/UI" };
            int found = 0;

            foreach (var entry in views)
            {
                if (entry.prefab != null) continue;

                foreach (var searchPath in searchPaths)
                {
                    string path = $"{searchPath}/{entry.viewName}.prefab";
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        entry.prefab = prefab;
                        found++;
                        break;
                    }
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[UIViewDatabase] Found {found} prefabs");
        }
#endif
    }

    /// <summary>
    /// UI View 配置条目
    /// </summary>
    [Serializable]
    public class UIViewEntry
    {
        [Tooltip("View的唯一标识名称，通常与类名一致")]
        public string viewName;

        [Tooltip("View的Prefab引用")]
        public GameObject prefab;

#if ZEROENGINE_ADDRESSABLES
        [Tooltip("Addressables引用（优先于直接引用）")]
        public AssetReferenceGameObject prefabRef;
#endif

        [Tooltip("UI所在层级")]
        public UILayer layer = UILayer.Screen;

        [Tooltip("显示模式")]
        public UIShowMode showMode = UIShowMode.Normal;

        [Tooltip("关闭模式")]
        public UICloseMode closeMode = UICloseMode.Hide;

        [Tooltip("是否显示遮罩")]
        public bool showMask = false;

        [Tooltip("遮罩颜色")]
        public Color maskColor = new Color(0, 0, 0, 0.5f);

        [Tooltip("点击遮罩是否关闭")]
        public bool maskClickClose = true;

        [Tooltip("是否阻挡输入")]
        public bool blockInput = true;

        [Tooltip("是否允许ESC键关闭")]
        public bool allowESCClose = true;

        [Tooltip("打开时是否暂停游戏")]
        public bool pauseGame = false;

        [Tooltip("打开动画类型")]
        public UIAnimationType openAnimation = UIAnimationType.Fade;

        [Tooltip("关闭动画类型")]
        public UIAnimationType closeAnimation = UIAnimationType.Fade;

        [Tooltip("动画持续时间")]
        public float animationDuration = 0.2f;

        [Tooltip("是否缓存实例")]
        public bool cache = true;

        /// <summary>
        /// 转换为 UIViewConfig
        /// </summary>
        public UIViewConfig ToConfig()
        {
            var config = new UIViewConfig
            {
                viewName = viewName,
                prefab = prefab,
                layer = layer,
                showMode = showMode,
                closeMode = closeMode,
                showMask = showMask,
                maskColor = maskColor,
                maskClickClose = maskClickClose,
                blockInput = blockInput,
                allowESCClose = allowESCClose,
                pauseGame = pauseGame,
                openAnimation = openAnimation,
                closeAnimation = closeAnimation,
                animationDuration = animationDuration,
                cache = cache
            };

#if ZEROENGINE_ADDRESSABLES
            config.prefabReference = prefabRef;
#endif

            return config;
        }
    }
}
