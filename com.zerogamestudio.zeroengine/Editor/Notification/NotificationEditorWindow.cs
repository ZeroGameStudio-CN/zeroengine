using UnityEngine;
using UnityEditor;
using ZeroEngine.Notification;

namespace ZeroEngine.Editor.Notification
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class NotificationEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private int _selectedTab;
        private readonly string[] _tabNames = { "测试发送", "配置", "预览" };

        // 测试发送
        private string _testTitle = "测试通知";
        private string _testMessage = "这是一条测试通知消息";
        private NotificationType _testType = NotificationType.Info;
        private NotificationPriority _testPriority = NotificationPriority.Normal;
        private float _testDuration = 3f;

        // 配置
        private int _maxVisibleCount = 3;
        private float _defaultDuration = 3f;
        private NotificationPriority _minPriority = NotificationPriority.Low;
        private bool _playSound = true;
        private NotificationPosition _position = NotificationPosition.TopRight;
        private NotificationAnimation _animation = NotificationAnimation.SlideIn;

        public static void ShowWindow()
        {
            var window = GetWindow<NotificationEditorWindow>("Notification Editor");
            window.minSize = new Vector2(500, 400);
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Notification Editor", "Send, configure, and preview notifications");
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            EditorGUILayout.Space(10);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTab)
            {
                case 0: DrawTestSend(); break;
                case 1: DrawConfig(); break;
                case 2: DrawPreview(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTestSend()
        {
            EditorGUILayout.LabelField("发送测试通知", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            _testTitle = EditorGUILayout.TextField("标题", _testTitle);
            EditorGUILayout.LabelField("消息");
            _testMessage = EditorGUILayout.TextArea(_testMessage, GUILayout.Height(60));

            EditorGUILayout.Space(5);

            _testType = (NotificationType)EditorGUILayout.EnumPopup("类型", _testType);
            _testPriority = (NotificationPriority)EditorGUILayout.EnumPopup("优先级", _testPriority);
            _testDuration = EditorGUILayout.Slider("持续时间", _testDuration, 0, 10);

            EditorGUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("发送通知", GUILayout.Height(30)))
            {
                SendTestNotification();
            }

            if (GUILayout.Button("清除所有", GUILayout.Height(30)))
            {
                NotificationManager.Instance?.HideAll();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("请在运行时测试通知功能", MessageType.Info);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("快捷发送", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("成功"))
                NotificationManager.Instance?.ShowSuccess("操作成功", "您的操作已完成");

            if (GUILayout.Button("警告"))
                NotificationManager.Instance?.ShowWarning("警告", "请注意检查");

            if (GUILayout.Button("错误"))
                NotificationManager.Instance?.ShowError("错误", "发生了一个错误");

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("成就"))
                NotificationManager.Instance?.ShowAchievement("解锁成就", "首次测试通知系统!");

            if (GUILayout.Button("奖励"))
                NotificationManager.Instance?.ShowReward("获得奖励", "金币 x100");

            if (GUILayout.Button("信息"))
                NotificationManager.Instance?.Show("提示", "这是一条普通信息");

            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            // 运行时状态
            if (Application.isPlaying && NotificationManager.Instance != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("运行时状态", EditorStyles.boldLabel);

                var manager = NotificationManager.Instance;
                EditorGUILayout.LabelField($"当前显示: {manager.ActiveCount}");
                EditorGUILayout.LabelField($"队列等待: {manager.PendingCount}");
            }
        }

        private void DrawConfig()
        {
            EditorGUILayout.LabelField("通知配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("显示设置", EditorStyles.miniBoldLabel);
            _maxVisibleCount = EditorGUILayout.IntSlider("最大显示数量", _maxVisibleCount, 1, 10);
            _defaultDuration = EditorGUILayout.Slider("默认持续时间", _defaultDuration, 1, 10);
            _position = (NotificationPosition)EditorGUILayout.EnumPopup("显示位置", _position);
            _animation = (NotificationAnimation)EditorGUILayout.EnumPopup("动画效果", _animation);

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("过滤设置", EditorStyles.miniBoldLabel);
            _minPriority = (NotificationPriority)EditorGUILayout.EnumPopup("最低优先级", _minPriority);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("类型开关:", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            DrawTypeToggle(NotificationType.Info, "信息");
            DrawTypeToggle(NotificationType.Success, "成功");
            DrawTypeToggle(NotificationType.Warning, "警告");
            DrawTypeToggle(NotificationType.Error, "错误");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawTypeToggle(NotificationType.Achievement, "成就");
            DrawTypeToggle(NotificationType.Reward, "奖励");
            DrawTypeToggle(NotificationType.System, "系统");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("音效设置", EditorStyles.miniBoldLabel);
            _playSound = EditorGUILayout.Toggle("播放音效", _playSound);

            EditorGUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            if (GUILayout.Button("应用配置到运行时"))
            {
                ApplyConfig();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("通知预览", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 模拟通知样式预览
            var previewRect = EditorGUILayout.GetControlRect(GUILayout.Height(200));

            // 背景
            EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f));

            // 计算通知位置
            var notificationRect = new Rect(
                previewRect.x + previewRect.width - 260,
                previewRect.y + 10,
                250,
                70
            );

            // 绘制示例通知
            DrawNotificationPreview(notificationRect, _testType, _testTitle, _testMessage);

            // 第二条通知
            if (_maxVisibleCount > 1)
            {
                var secondRect = new Rect(notificationRect.x, notificationRect.yMax + 5, 250, 70);
                DrawNotificationPreview(secondRect, NotificationType.Success, "操作成功", "您的更改已保存");
            }

            // 第三条通知
            if (_maxVisibleCount > 2)
            {
                var thirdRect = new Rect(notificationRect.x, notificationRect.y + 155, 250, 70);
                DrawNotificationPreview(thirdRect, NotificationType.Warning, "警告", "存储空间不足");
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("类型颜色参考", EditorStyles.boldLabel);

            DrawColorReference(NotificationType.Info, "信息", new Color(0.3f, 0.5f, 0.8f));
            DrawColorReference(NotificationType.Success, "成功", new Color(0.3f, 0.7f, 0.3f));
            DrawColorReference(NotificationType.Warning, "警告", new Color(0.9f, 0.7f, 0.2f));
            DrawColorReference(NotificationType.Error, "错误", new Color(0.8f, 0.3f, 0.3f));
            DrawColorReference(NotificationType.Achievement, "成就", new Color(0.8f, 0.6f, 0.2f));
            DrawColorReference(NotificationType.Reward, "奖励", new Color(0.6f, 0.4f, 0.8f));
        }

        private void DrawNotificationPreview(Rect rect, NotificationType type, string title, string message)
        {
            var color = GetTypeColor(type);

            // 背景
            EditorGUI.DrawRect(rect, new Color(0.25f, 0.25f, 0.25f));

            // 左侧颜色条
            var colorBar = new Rect(rect.x, rect.y, 4, rect.height);
            EditorGUI.DrawRect(colorBar, color);

            // 图标区域
            var iconRect = new Rect(rect.x + 10, rect.y + 10, 24, 24);
            GUI.Label(iconRect, GetTypeIcon(type));

            // 标题
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
            var titleRect = new Rect(rect.x + 40, rect.y + 8, rect.width - 50, 20);
            GUI.Label(titleRect, title, titleStyle);

            // 消息
            var msgStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }, wordWrap = true };
            var msgRect = new Rect(rect.x + 40, rect.y + 28, rect.width - 50, 40);
            GUI.Label(msgRect, message, msgStyle);

            // 关闭按钮
            var closeRect = new Rect(rect.xMax - 20, rect.y + 5, 15, 15);
            GUI.Label(closeRect, "×");
        }

        private void DrawColorReference(NotificationType type, string name, Color color)
        {
            EditorGUILayout.BeginHorizontal();
            var rect = EditorGUILayout.GetControlRect(GUILayout.Width(20), GUILayout.Height(16));
            EditorGUI.DrawRect(rect, color);
            EditorGUILayout.LabelField($"{name} ({type})", GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeToggle(NotificationType type, string label)
        {
            bool enabled = true; // 默认启用
            bool newEnabled = EditorGUILayout.ToggleLeft(label, enabled, GUILayout.Width(60));
            if (newEnabled != enabled && Application.isPlaying)
            {
                if (newEnabled)
                    NotificationManager.Instance?.EnableType(type);
                else
                    NotificationManager.Instance?.DisableType(type);
            }
        }

        private void SendTestNotification()
        {
            if (NotificationManager.Instance == null) return;

            var notification = new NotificationData(_testTitle, _testMessage, _testType)
            {
                Priority = _testPriority,
                Duration = _testDuration
            };

            NotificationManager.Instance.Show(notification);
        }

        private void ApplyConfig()
        {
            if (NotificationManager.Instance == null) return;

            NotificationManager.Instance.SetMaxVisibleCount(_maxVisibleCount);
            NotificationManager.Instance.SetDefaultDuration(_defaultDuration);
            NotificationManager.Instance.SetMinPriority(_minPriority);
        }

        private Color GetTypeColor(NotificationType type)
        {
            return type switch
            {
                NotificationType.Info => new Color(0.3f, 0.5f, 0.8f),
                NotificationType.Success => new Color(0.3f, 0.7f, 0.3f),
                NotificationType.Warning => new Color(0.9f, 0.7f, 0.2f),
                NotificationType.Error => new Color(0.8f, 0.3f, 0.3f),
                NotificationType.Achievement => new Color(0.8f, 0.6f, 0.2f),
                NotificationType.Reward => new Color(0.6f, 0.4f, 0.8f),
                NotificationType.System => new Color(0.5f, 0.5f, 0.5f),
                _ => Color.gray
            };
        }

        private GUIContent GetTypeIcon(NotificationType type)
        {
            return type switch
            {
                NotificationType.Info => EditorGUIUtility.IconContent("d_console.infoicon.sml"),
                NotificationType.Success => EditorGUIUtility.IconContent("d_FilterSelectedOnly"),
                NotificationType.Warning => EditorGUIUtility.IconContent("d_console.warnicon.sml"),
                NotificationType.Error => EditorGUIUtility.IconContent("d_console.erroricon.sml"),
                NotificationType.Achievement => EditorGUIUtility.IconContent("d_Favorite"),
                NotificationType.Reward => EditorGUIUtility.IconContent("d_Profiler.Custom"),
                _ => EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow")
            };
        }
    }
}
