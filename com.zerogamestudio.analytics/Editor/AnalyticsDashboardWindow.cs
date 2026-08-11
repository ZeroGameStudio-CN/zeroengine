using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace ZGS.Analytics.Editor
{
    /// <summary>
    /// ZGS Analytics Dashboard - 编辑器内查看 SDK 状态
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class AnalyticsDashboardWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private GUIStyle _headerStyle;
        private GUIStyle _valueStyle;
        private bool _stylesInitialized;

        public static void ShowWindow()
        {
            var window = GetWindow<AnalyticsDashboardWindow>("Analytics Dashboard");
            window.minSize = new Vector2(300, 400);
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = ZeroEngine.EditorUI.EditorUiStyles.SectionTitle;

            _valueStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            ZeroEngine.EditorUI.EditorUiGUILayout.Header(
                "ZGS Analytics",
                "Analytics SDK configuration and runtime health");
            DrawHeader();
            EditorGUILayout.Space(10);

            if (Application.isPlaying)
            {
                DrawRuntimeStatus();
            }
            else
            {
                DrawConfigStatus();
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("进入 Play 模式查看运行时状态", MessageType.Info);
            }

            EditorGUILayout.Space(10);
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Status", _headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔄", GUILayout.Width(30)))
            {
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void DrawConfigStatus()
        {
            EditorGUILayout.LabelField("配置状态", _headerStyle);

            var config = Resources.Load<ZGSAnalyticsConfig>("ZGSAnalyticsConfig");

            if (config != null)
            {
                EditorGUI.indentLevel++;
                DrawField("App ID", config.appId);
                DrawField("Server URL", string.IsNullOrEmpty(config.zgsServerUrl) ? "(未配置)" : config.zgsServerUrl);
                DrawField("Analytics", config.EnableAnalytics ? "✅ 启用" : "❌ 禁用");
                DrawField("Debug Mode", config.debugMode ? "✅ 开启" : "❌ 关闭");
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
                if (GUILayout.Button("选择配置文件"))
                {
                    Selection.activeObject = config;
                    EditorGUIUtility.PingObject(config);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("未找到 ZGSAnalyticsConfig\n请在 Resources 目录创建配置文件", MessageType.Warning);

                if (GUILayout.Button("创建配置文件"))
                {
                    CreateConfig();
                }
            }
        }

        private void DrawRuntimeStatus()
        {
            EditorGUILayout.LabelField("运行时状态", _headerStyle);

            EditorGUI.indentLevel++;

            // Session Info
            try
            {
                var sessionType = typeof(SessionInfo);
                var userIdProp = sessionType.GetProperty("UserId", BindingFlags.Public | BindingFlags.Static);
                var sessionIdProp = sessionType.GetProperty("SessionId", BindingFlags.Public | BindingFlags.Static);
                var sessionNumProp = sessionType.GetProperty("SessionNumber", BindingFlags.Public | BindingFlags.Static);

                if (userIdProp != null)
                {
                    string userId = userIdProp.GetValue(null) as string ?? "(未初始化)";
                    DrawField("User ID", userId.Length > 16 ? userId.Substring(0, 16) + "..." : userId);
                }
                if (sessionIdProp != null)
                {
                    string sessionId = sessionIdProp.GetValue(null) as string ?? "(未初始化)";
                    DrawField("Session ID", sessionId.Length > 16 ? sessionId.Substring(0, 16) + "..." : sessionId);
                }
                if (sessionNumProp != null)
                {
                    DrawField("Session #", sessionNumProp.GetValue(null)?.ToString() ?? "0");
                }
            }
            catch
            {
                DrawField("Session", "(无法读取)");
            }

            // Device Info
            try
            {
                var deviceType = typeof(DeviceInfo);
                var isEditorProp = deviceType.GetProperty("IsEditor", BindingFlags.Public | BindingFlags.Static);
                var platformProp = deviceType.GetProperty("Platform", BindingFlags.Public | BindingFlags.Static);
                var versionProp = deviceType.GetProperty("AppVersion", BindingFlags.Public | BindingFlags.Static);

                if (isEditorProp != null)
                {
                    bool isEditor = (bool)isEditorProp.GetValue(null);
                    DrawField("Is Editor", isEditor ? "✅ 是" : "❌ 否");
                }
                if (platformProp != null)
                {
                    DrawField("Platform", platformProp.GetValue(null)?.ToString() ?? "Unknown");
                }
                if (versionProp != null)
                {
                    DrawField("App Version", versionProp.GetValue(null)?.ToString() ?? "Unknown");
                }
            }
            catch
            {
                DrawField("Device", "(无法读取)");
            }

            EditorGUI.indentLevel--;
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("操作", _headerStyle);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("打开服务器 Dashboard"))
            {
                var config = Resources.Load<ZGSAnalyticsConfig>("ZGSAnalyticsConfig");
                if (config != null && !string.IsNullOrEmpty(config.zgsServerUrl))
                {
                    // 从 API URL 推断 Dashboard URL (5001 -> 8501)
                    string dashboardUrl = config.zgsServerUrl.Replace(":5001", ":8501");
                    Application.OpenURL(dashboardUrl);
                }
                else
                {
                    Debug.LogWarning("[ZGS.Analytics] 未配置服务器地址");
                }
            }

            if (GUILayout.Button("GitHub 仓库"))
            {
                Application.OpenURL("https://github.com/liuzqk/zgs-analytics-sdk");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            EditorGUILayout.LabelField(value, _valueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void CreateConfig()
        {
            // 确保 Resources 目录存在
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var config = CreateInstance<ZGSAnalyticsConfig>();
            AssetDatabase.CreateAsset(config, "Assets/Resources/ZGSAnalyticsConfig.asset");
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying)
            {
                Repaint();
            }
        }
    }
}
