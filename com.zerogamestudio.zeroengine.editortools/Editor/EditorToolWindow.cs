using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorTools
{
    public sealed class EditorToolWindow : EditorWindow
    {
        private static readonly GUIContent ProjectLabel = new("项目", "选择当前项目注册的编辑器工具集合。");
        private static readonly GUIContent EmptyProfileMessage = new("没有已注册的编辑器工具项目。", "请确认项目侧已提供 EditorToolProjectProvider。");
        private static readonly GUIContent GeneratorsHeader = new("生成器", "创建或更新项目资产、Prefab、场景等编辑器资源。");
        private static readonly GUIContent ValidationHeader = new("校验", "检查项目配置、资产和工具链状态。");
        private static readonly GUIContent CommandsHeader = new("命令", "打开工具窗口或执行轻量编辑器命令。");
        private static readonly GUIContent TestRunnerHeader = new("测试运行器", "按项目预设运行 Unity Test Runner 任务。");

        private int _selectedProfileIndex;
        private Vector2 _scrollPosition;
        private string _lastResult;

        [MenuItem("ZGS/Editor Tools")]
        public static void ShowWindow()
        {
            EditorToolProjectRegistry.RefreshFromProviders();
            var window = GetWindow<EditorToolWindow>("ZGS Editor Tools");
            window.minSize = new Vector2(460, 560);
        }

        public static void Open()
        {
            ShowWindow();
        }

        private void OnGUI()
        {
            var profiles = EditorToolProjectRegistry.GetProfiles();
            if (profiles.Count == 0)
            {
                EditorGUILayout.HelpBox(EmptyProfileMessage.text, MessageType.Info);
                return;
            }

            _selectedProfileIndex = Mathf.Clamp(_selectedProfileIndex, 0, profiles.Count - 1);
            _selectedProfileIndex = EditorGUILayout.Popup(
                ProjectLabel,
                _selectedProfileIndex,
                profiles.Select(profile => profile.Title).ToArray());

            var profile = profiles[_selectedProfileIndex];
            EditorGUILayout.HelpBox(profile.Description, MessageType.None);
            EditorGUILayout.Space(6);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawPanels(profile.Panels);
            DrawCommands(GeneratorsHeader, profile.GenerationTasks.Cast<IEditorToolCommand>());
            DrawCommands(ValidationHeader, profile.ValidationTasks.Cast<IEditorToolCommand>());
            DrawCommands(CommandsHeader, profile.Commands);
            DrawTestRunnerTasks(profile.TestRunnerTasks);
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_lastResult, MessageType.None);
            }
        }

        private void DrawPanels(IReadOnlyList<IEditorToolPanel> panels)
        {
            foreach (var group in panels.GroupBy(panel => panel.Group))
            {
                DrawHeader(new GUIContent(group.First().GroupDisplayName, group.Key));
                foreach (var panel in group)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(new GUIContent(panel.DisplayName, panel.Tooltip), EditorStyles.boldLabel);
                    panel.Draw();
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void DrawCommands(GUIContent title, IEnumerable<IEditorToolCommand> commands)
        {
            var groupedCommands = commands.GroupBy(command => command.Group).ToArray();
            if (groupedCommands.Length == 0)
            {
                return;
            }

            DrawHeader(title);
            foreach (var group in groupedCommands)
            {
                EditorGUILayout.LabelField(new GUIContent(group.First().GroupDisplayName, group.Key), EditorStyles.miniBoldLabel);
                foreach (var command in group)
                {
                    if (GUILayout.Button(new GUIContent(command.DisplayName, command.Tooltip), GUILayout.Height(26)))
                    {
                        SetLastResult(command.Execute());
                    }
                }
            }
        }

        private void DrawTestRunnerTasks(IReadOnlyList<ITestRunnerTask> tasks)
        {
            if (tasks.Count == 0)
            {
                return;
            }

            DrawHeader(TestRunnerHeader);
            foreach (var group in tasks.GroupBy(task => task.Group))
            {
                EditorGUILayout.LabelField(new GUIContent(group.First().GroupDisplayName, group.Key), EditorStyles.miniBoldLabel);
                foreach (var task in group)
                {
                    if (GUILayout.Button(new GUIContent(task.DisplayName, task.Tooltip), GUILayout.Height(26)))
                    {
                        SetLastResult(EditorToolTestRunner.Execute(task));
                    }
                }
            }
        }

        private static void DrawHeader(GUIContent title)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private void SetLastResult(EditorToolExecutionResult result)
        {
            if (result == null)
            {
                _lastResult = string.Empty;
                return;
            }

            _lastResult = result.Details.Count == 0
                ? result.Message
                : $"{result.Message}\n{string.Join("\n", result.Details)}";
        }
    }
}
