using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI.Tests.Editor
{
    internal sealed class EditorUiGalleryWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _details;
        private bool _help;

        [MenuItem("ZeroEngine Tests/Editor UI Gallery")]
        private static void Open()
        {
            var window = GetWindow<EditorUiGalleryWindow>("Editor UI Gallery");
            window.minSize = new Vector2(420f, 480f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorUiGUILayout.CompactHeader(
                "ZeroEngine Editor UI",
                "共享层级、语义状态、响应式间距和主题色板。",
                "仅用于布局验收",
                () => EditorUiGUILayout.Chip(EditorUiGUILayout.ResponsiveMode(position.width).ToString()));

            using (EditorUiGUILayout.Section("自适应操作行", "覆盖长中文、长英文和 1–3 个动作；缩窄窗口检查按钮是否自动换行。"))
            {
                DrawActionRow(
                    "公式中心",
                    "在同一个窗口宿主中切换公式目录与公式工作台。",
                    120f,
                    () => EditorUiGUILayout.PrimaryButton(new GUIContent("打开", "打开公式中心。"), GUILayout.Width(112f)));
                DrawActionRow(
                    "POB 存档兼容性验证与启动恢复检查",
                    "完整说明进入 hover 或帮助抽屉；常驻区域只承担识别和状态。",
                    240f,
                    () =>
                    {
                        EditorUiGUILayout.PrimaryButton(new GUIContent("关键测试", "运行关键 EditMode 测试。"), GUILayout.Width(112f));
                        GUILayout.Button(new GUIContent("存档兼容", "运行存档兼容测试。"), GUILayout.Width(112f));
                    });
                DrawActionRow(
                    "Configuration pipeline diagnostics with an intentionally long English surface name",
                    "Actions remain reachable without overlapping the shrinkable text region.",
                    360f,
                    () =>
                    {
                        EditorUiGUILayout.PrimaryButton(new GUIContent("刷新", "刷新目录。"), GUILayout.Width(112f));
                        GUILayout.Button(new GUIContent("详情", "查看技术详情。"), GUILayout.Width(112f));
                        EditorUiGUILayout.DestructiveButton(new GUIContent("清理", "需要确认的破坏性操作。"), GUILayout.Width(112f));
                    });
                EditorUiGUILayout.Chip("POB");
                EditorUiGUILayout.Chip(new GUIContent("项目写入", "此标记不会因紧凑布局隐藏。"));
                _details = EditorUiGUILayout.Disclosure(_details, new GUIContent("技术详情", "展开稳定 ID、菜单路径和来源。"));
                if (_details)
                    EditorGUILayout.SelectableLabel("module/entry · ZeroEngine/Menu/Path", EditorStyles.miniLabel);
                _help = EditorUiGUILayout.Disclosure(_help, new GUIContent("帮助抽屉", "说明和使用方法只在需要时展开。"));
                if (_help)
                    EditorGUILayout.HelpBox("用途：验证 Dashboard 文案分层和布局。\n用法：拖动窗口到 420 / 760 / 960 / 1440 point，确认文本、chip 和动作不重叠。", MessageType.Info);
            }

            using (EditorUiGUILayout.Section("Actions", "Primary, secondary, destructive, and validation states"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorUiGUILayout.PrimaryButton("Primary");
                    GUILayout.Button("Secondary");
                    EditorUiGUILayout.DestructiveButton("Destructive");
                }

                EditorUiGUILayout.StatusLabel("Success · ready", EditorUiStatus.Success);
                EditorUiGUILayout.StatusLabel("Warning · review", EditorUiStatus.Warning);
                EditorUiGUILayout.StatusLabel("Error · blocked", EditorUiStatus.Error);
                EditorUiGUILayout.ValidationSummary("Validation summary", EditorUiStatus.Warning);
                EditorUiGUILayout.EmptyState("No items match the current filter.");
            }

            using (EditorUiGUILayout.Section("色板预览", "只定义语义色；内置控件继续跟随当前 Editor 主题。"))
            {
                if (EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact)
                {
                    DrawPalette("浅色", EditorUiPalette.ResolveForSkin(false));
                    DrawPalette("深色", EditorUiPalette.ResolveForSkin(true));
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawPalette("浅色", EditorUiPalette.ResolveForSkin(false));
                        DrawPalette("深色", EditorUiPalette.ResolveForSkin(true));
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawActionRow(string title, string description, float trailingWidth, System.Action drawTrailing)
        {
            float availableWidth = Mathf.Max(240f, position.width - 48f);
            EditorUiGUILayout.ActionRow(
                new GUIContent(title, description),
                new GUIContent(description, description),
                drawTrailing,
                EditorUiGUILayout.ResolveActionRowMode(availableWidth, trailingWidth));
        }

        private static void DrawPalette(string label, EditorUiPalette palette)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(250f)))
            {
                GUILayout.Label(label, EditorStyles.boldLabel);
                DrawSwatch("Surface", palette.Surface);
                DrawSwatch("Raised", palette.RaisedSurface);
                DrawSwatch("Accent", palette.Accent);
                DrawSwatch("Success", palette.Success);
                DrawSwatch("Warning", palette.Warning);
                DrawSwatch("Error", palette.Error);
            }
        }

        private static void DrawSwatch(string label, Color color)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect swatch = new Rect(rect.x, rect.y + 2f, 44f, rect.height - 4f);
            EditorGUI.DrawRect(swatch, color);
            EditorGUI.LabelField(new Rect(rect.x + 52f, rect.y, rect.width - 52f, rect.height), label);
        }
    }
}
