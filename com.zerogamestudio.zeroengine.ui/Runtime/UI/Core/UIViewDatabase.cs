using System;
using System.Collections.Generic;
using UnityEngine;

#if ZEROENGINE_ADDRESSABLES
using UnityEngine.AddressableAssets;
#endif

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
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
#if ODIN_INSPECTOR
        [Title("View Configurations")]
        [ListDrawerSettings(ShowIndexLabels = true, ShowFoldout = true)]
#else
        [Header("View Configurations")]
#endif
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
#if ODIN_INSPECTOR
        [Title("Editor Tools")]
        [Button("Validate Entries", ButtonSizes.Medium), GUIColor(0.5f, 0.8f, 1f)]
#endif
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

#if ODIN_INSPECTOR
        [Button("Sort by Layer", ButtonSizes.Medium), GUIColor(0.3f, 0.9f, 0.6f)]
#endif
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

#if ODIN_INSPECTOR
        [Button("Auto-Find Prefabs in Project", ButtonSizes.Medium), GUIColor(0.9f, 0.7f, 0.3f)]
#endif
        [ContextMenu("Auto-Find Prefabs")]
        private void AutoFindPrefabs()
        {
            int found = 0;

            foreach (var entry in views)
            {
                if (entry.prefab != null) continue;

                var guids = UnityEditor.AssetDatabase.FindAssets($"{entry.viewName} t:Prefab");
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && string.Equals(prefab.name, entry.viewName, StringComparison.Ordinal))
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
#if ODIN_INSPECTOR
        [HorizontalGroup("Main", 0.3f)]
        [LabelWidth(80)]
#endif
        [Tooltip("View的唯一标识名称，通常与类名一致")]
        public string viewName;

#if ODIN_INSPECTOR
        [HorizontalGroup("Main")]
        [LabelWidth(50)]
#endif
        [Tooltip("View的Prefab引用")]
        public GameObject prefab;

#if ZEROENGINE_ADDRESSABLES
#if ODIN_INSPECTOR
        [HorizontalGroup("Main")]
        [LabelWidth(80)]
#endif
        [Tooltip("Addressables引用（优先于直接引用）")]
        public AssetReferenceGameObject prefabRef;
#endif

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings")]
#endif
        [Tooltip("UI所在层级")]
        public UILayer layer = UILayer.Screen;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings")]
#endif
        [Tooltip("显示模式")]
        public UIShowMode showMode = UIShowMode.Normal;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings")]
#endif
        [Tooltip("关闭模式")]
        public UICloseMode closeMode = UICloseMode.Hide;

#if ODIN_INSPECTOR
        [FoldoutGroup("Display")]
#endif
        [Tooltip("是否显示遮罩")]
        public bool showMask = false;

#if ODIN_INSPECTOR
        [FoldoutGroup("Display")]
#endif
        [Tooltip("遮罩颜色")]
        public Color maskColor = new Color(0, 0, 0, 0.5f);

#if ODIN_INSPECTOR
        [FoldoutGroup("Display")]
#endif
        [Tooltip("点击遮罩是否关闭")]
        public bool maskClickClose = true;

#if ODIN_INSPECTOR
        [FoldoutGroup("Display")]
#endif
        [Tooltip("是否阻挡输入")]
        public bool blockInput = true;

#if ODIN_INSPECTOR
        [FoldoutGroup("Display")]
#endif
        [Tooltip("是否允许ESC键关闭")]
        public bool allowESCClose = true;

#if ODIN_INSPECTOR
        [FoldoutGroup("Display")]
#endif
        [Tooltip("打开时是否暂停游戏")]
        public bool pauseGame = false;

#if ODIN_INSPECTOR
        [FoldoutGroup("Animation")]
#endif
        [Tooltip("打开动画类型")]
        public UIAnimationType openAnimation = UIAnimationType.Fade;

#if ODIN_INSPECTOR
        [FoldoutGroup("Animation")]
#endif
        [Tooltip("关闭动画类型")]
        public UIAnimationType closeAnimation = UIAnimationType.Fade;

#if ODIN_INSPECTOR
        [FoldoutGroup("Animation")]
        [Range(0.05f, 1f)]
#endif
        [Tooltip("动画持续时间")]
        public float animationDuration = 0.2f;

#if ODIN_INSPECTOR
        [FoldoutGroup("Cache")]
#endif
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
