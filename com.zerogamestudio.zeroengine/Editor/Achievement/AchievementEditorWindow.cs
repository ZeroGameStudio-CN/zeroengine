using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Achievement;

namespace ZeroEngine.Editor.Achievement
{
    /// <summary>
    /// 成就编辑器窗口
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class AchievementEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private Vector2 _detailScrollPosition;
        private string _searchFilter = "";
        private AchievementCategory? _categoryFilter = null;

        private List<AchievementSO> _allAchievements = new List<AchievementSO>();
        private List<AchievementGroupSO> _allGroups = new List<AchievementGroupSO>();
        private AchievementSO _selectedAchievement;

        private int _tabIndex = 0;
        private readonly string[] _tabNames = { "成就列表", "成就组", "统计" };

        public static void ShowWindow()
        {
            var window = GetWindow<AchievementEditorWindow>("Achievement Editor");
            window.minSize = new Vector2(900, 600);
        }

        private void OnEnable()
        {
            RefreshLists();
        }

        private void OnFocus()
        {
            RefreshLists();
        }

        private void RefreshLists()
        {
            _allAchievements.Clear();
            _allGroups.Clear();

            // 加载所有成就
            var achievementGuids = AssetDatabase.FindAssets("t:AchievementSO");
            foreach (var guid in achievementGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var achievement = AssetDatabase.LoadAssetAtPath<AchievementSO>(path);
                if (achievement != null)
                {
                    _allAchievements.Add(achievement);
                }
            }
            _allAchievements.Sort((a, b) =>
            {
                int catCompare = a.Category.CompareTo(b.Category);
                if (catCompare != 0) return catCompare;
                int orderCompare = a.SortOrder.CompareTo(b.SortOrder);
                if (orderCompare != 0) return orderCompare;
                return string.Compare(a.DisplayName, b.DisplayName);
            });

            // 加载所有成就组
            var groupGuids = AssetDatabase.FindAssets("t:AchievementGroupSO");
            foreach (var guid in groupGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var group = AssetDatabase.LoadAssetAtPath<AchievementGroupSO>(path);
                if (group != null)
                {
                    _allGroups.Add(group);
                }
            }
            _allGroups.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Achievement Editor", "Manage achievements and groups");
            // 标签页
            _tabIndex = GUILayout.Toolbar(_tabIndex, _tabNames, GUILayout.Height(30));
            EditorGUILayout.Space(5);

            switch (_tabIndex)
            {
                case 0:
                    DrawAchievementTab();
                    break;
                case 1:
                    DrawGroupTab();
                    break;
                case 2:
                    DrawStatisticsTab();
                    break;
            }
        }

        private void DrawAchievementTab()
        {
            EditorGUILayout.BeginHorizontal();

            // 左侧列表
            DrawAchievementList();

            // 右侧详情
            DrawAchievementDetails();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAchievementList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(280));

            // 搜索和筛选
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton")))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // 分类筛选
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全部", _categoryFilter == null ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = null;
            }
            if (GUILayout.Button("战斗", _categoryFilter == AchievementCategory.Combat ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = AchievementCategory.Combat;
            }
            if (GUILayout.Button("收集", _categoryFilter == AchievementCategory.Collection ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = AchievementCategory.Collection;
            }
            if (GUILayout.Button("探索", _categoryFilter == AchievementCategory.Exploration ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = AchievementCategory.Exploration;
            }
            EditorGUILayout.EndHorizontal();

            // 工具栏
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建", GUILayout.Height(24)))
            {
                CreateNewAchievement();
            }
            if (GUILayout.Button("刷新", GUILayout.Height(24)))
            {
                RefreshLists();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 列表
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            AchievementCategory? lastCategory = null;
            foreach (var achievement in _allAchievements)
            {
                // 筛选
                if (_categoryFilter.HasValue && achievement.Category != _categoryFilter.Value)
                    continue;

                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    if (!achievement.DisplayName.ToLower().Contains(_searchFilter.ToLower()) &&
                        !achievement.AchievementId.ToLower().Contains(_searchFilter.ToLower()))
                    {
                        continue;
                    }
                }

                // 分类标题
                if (!_categoryFilter.HasValue && lastCategory != achievement.Category)
                {
                    lastCategory = achievement.Category;
                    EditorGUILayout.LabelField(achievement.Category.ToString(), EditorStyles.boldLabel);
                }

                // 成就项
                bool isSelected = _selectedAchievement == achievement;
                var bgColor = isSelected ? new Color(0.3f, 0.6f, 1f) : Color.white;
                if (achievement.IsHidden)
                {
                    bgColor = isSelected ? new Color(0.5f, 0.5f, 0.7f) : new Color(0.8f, 0.8f, 0.9f);
                }
                GUI.backgroundColor = bgColor;

                EditorGUILayout.BeginHorizontal("box");

                // 图标
                if (achievement.Icon != null)
                {
                    GUILayout.Box(achievement.Icon.texture, GUILayout.Width(32), GUILayout.Height(32));
                }
                else
                {
                    GUILayout.Box("?", GUILayout.Width(32), GUILayout.Height(32));
                }

                // 信息
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(achievement.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{achievement.Points}点 | {achievement.Conditions?.Count ?? 0}条件 | {achievement.Rewards?.Count ?? 0}奖励",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();

                // 点击选择
                if (Event.current.type == EventType.MouseDown &&
                    GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    _selectedAchievement = achievement;
                    Selection.activeObject = achievement;
                    Event.current.Use();
                }

                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawAchievementDetails()
        {
            EditorGUILayout.BeginVertical();

            if (_selectedAchievement == null)
            {
                EditorGUILayout.HelpBox("选择一个成就查看详情", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // 标题栏
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(_selectedAchievement.DisplayName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("定位", EditorStyles.toolbarButton))
            {
                EditorGUIUtility.PingObject(_selectedAchievement);
                Selection.activeObject = _selectedAchievement;
            }
            if (GUILayout.Button("编辑", EditorStyles.toolbarButton))
            {
                Selection.activeObject = _selectedAchievement;
            }
            EditorGUILayout.EndHorizontal();

            _detailScrollPosition = EditorGUILayout.BeginScrollView(_detailScrollPosition);

            // 基本信息
            EditorGUILayout.LabelField("基本信息", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("ID", _selectedAchievement.AchievementId);
            EditorGUILayout.LabelField("分类", _selectedAchievement.Category.ToString());
            EditorGUILayout.LabelField("点数", _selectedAchievement.Points.ToString());
            EditorGUILayout.LabelField("隐藏", _selectedAchievement.IsHidden ? "是" : "否");
            EditorGUILayout.LabelField("可重复", _selectedAchievement.Repeatable ? "是" : "否");
            if (!string.IsNullOrEmpty(_selectedAchievement.Description))
            {
                EditorGUILayout.LabelField("描述", _selectedAchievement.Description, EditorStyles.wordWrappedLabel);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // 条件
            EditorGUILayout.LabelField($"完成条件 ({_selectedAchievement.Conditions?.Count ?? 0})", EditorStyles.boldLabel);
            if (_selectedAchievement.Conditions != null && _selectedAchievement.Conditions.Count > 0)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < _selectedAchievement.Conditions.Count; i++)
                {
                    var condition = _selectedAchievement.Conditions[i];
                    if (condition != null)
                    {
                        string typeName = condition.GetType().Name.Replace("Condition", "");
                        EditorGUILayout.LabelField($"{i + 1}. [{typeName}] {condition.Description}");
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // 奖励
            EditorGUILayout.LabelField($"奖励 ({_selectedAchievement.Rewards?.Count ?? 0})", EditorStyles.boldLabel);
            if (_selectedAchievement.Rewards != null && _selectedAchievement.Rewards.Count > 0)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < _selectedAchievement.Rewards.Count; i++)
                {
                    var reward = _selectedAchievement.Rewards[i];
                    if (reward != null)
                    {
                        string typeName = reward.GetType().Name.Replace("Reward", "");
                        EditorGUILayout.LabelField($"{i + 1}. [{typeName}] {reward.Description}");
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // 前置条件
            if (_selectedAchievement.Prerequisites != null && _selectedAchievement.Prerequisites.Count > 0)
            {
                EditorGUILayout.LabelField($"前置成就 ({_selectedAchievement.Prerequisites.Count})", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                foreach (var prereq in _selectedAchievement.Prerequisites)
                {
                    if (prereq != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(prereq.DisplayName);
                        if (GUILayout.Button("→", GUILayout.Width(24)))
                        {
                            _selectedAchievement = prereq;
                            Selection.activeObject = prereq;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUI.indentLevel--;
            }

            if (_selectedAchievement.RequiredLevel > 0)
            {
                EditorGUILayout.LabelField("需要等级", _selectedAchievement.RequiredLevel.ToString());
            }

            // 标签
            if (_selectedAchievement.Tags != null && _selectedAchievement.Tags.Count > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("标签", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                foreach (var tag in _selectedAchievement.Tags)
                {
                    GUILayout.Label(tag, "AssetLabel");
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGroupTab()
        {
            EditorGUILayout.BeginHorizontal();

            // 组列表
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            EditorGUILayout.LabelField("成就组", EditorStyles.boldLabel);

            if (GUILayout.Button("新建成就组", GUILayout.Height(24)))
            {
                CreateNewGroup();
            }

            EditorGUILayout.Space(4);

            foreach (var group in _allGroups)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(group.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{group.Achievements?.Count ?? 0} 个成就 | {group.GetTotalPoints()} 点",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                if (GUILayout.Button("编辑", GUILayout.Width(40)))
                {
                    Selection.activeObject = group;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            // 组详情
            EditorGUILayout.BeginVertical();
            EditorGUILayout.HelpBox("选择成就组进行编辑", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatisticsTab()
        {
            EditorGUILayout.LabelField("成就统计", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // 总览
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("总览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"成就总数: {_allAchievements.Count}");

            int totalPoints = 0;
            foreach (var ach in _allAchievements)
            {
                totalPoints += ach.Points;
            }
            EditorGUILayout.LabelField($"总点数: {totalPoints}");
            EditorGUILayout.LabelField($"成就组数: {_allGroups.Count}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 分类统计
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("分类统计", EditorStyles.boldLabel);

            var categoryCount = new Dictionary<AchievementCategory, int>();
            var categoryPoints = new Dictionary<AchievementCategory, int>();

            foreach (var ach in _allAchievements)
            {
                if (!categoryCount.ContainsKey(ach.Category))
                {
                    categoryCount[ach.Category] = 0;
                    categoryPoints[ach.Category] = 0;
                }
                categoryCount[ach.Category]++;
                categoryPoints[ach.Category] += ach.Points;
            }

            foreach (var kvp in categoryCount)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(kvp.Key.ToString(), GUILayout.Width(100));
                EditorGUILayout.LabelField($"{kvp.Value} 个", GUILayout.Width(60));
                EditorGUILayout.LabelField($"{categoryPoints[kvp.Key]} 点");

                // 进度条
                float ratio = (float)kvp.Value / _allAchievements.Count;
                Rect barRect = GUILayoutUtility.GetRect(100, 16);
                EditorGUI.ProgressBar(barRect, ratio, $"{ratio * 100:F1}%");

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 条件/奖励统计
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("条件/奖励统计", EditorStyles.boldLabel);

            int totalConditions = 0;
            int totalRewards = 0;
            int hiddenCount = 0;
            int repeatableCount = 0;

            foreach (var ach in _allAchievements)
            {
                totalConditions += ach.Conditions?.Count ?? 0;
                totalRewards += ach.Rewards?.Count ?? 0;
                if (ach.IsHidden) hiddenCount++;
                if (ach.Repeatable) repeatableCount++;
            }

            EditorGUILayout.LabelField($"条件总数: {totalConditions}");
            EditorGUILayout.LabelField($"奖励总数: {totalRewards}");
            EditorGUILayout.LabelField($"隐藏成就: {hiddenCount}");
            EditorGUILayout.LabelField($"可重复成就: {repeatableCount}");
            EditorGUILayout.EndVertical();
        }

        private void CreateNewAchievement()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建成就",
                "New Achievement",
                "asset",
                "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var achievement = ScriptableObject.CreateInstance<AchievementSO>();
            achievement.AchievementId = System.IO.Path.GetFileNameWithoutExtension(path);
            achievement.DisplayName = achievement.AchievementId;

            AssetDatabase.CreateAsset(achievement, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            _selectedAchievement = achievement;
            Selection.activeObject = achievement;
        }

        private void CreateNewGroup()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建成就组",
                "New Achievement Group",
                "asset",
                "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var group = ScriptableObject.CreateInstance<AchievementGroupSO>();
            group.GroupId = System.IO.Path.GetFileNameWithoutExtension(path);
            group.DisplayName = group.GroupId;

            AssetDatabase.CreateAsset(group, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            Selection.activeObject = group;
        }
    }
}
