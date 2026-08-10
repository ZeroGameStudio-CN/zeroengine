using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Relationship;

namespace ZeroEngine.Editor.Relationship
{
    /// <summary>
    /// 好感度编辑器窗口
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class RelationshipEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private Vector2 _detailScrollPosition;
        private string _searchFilter = "";
        private NpcType? _typeFilter = null;

        private List<RelationshipDataSO> _allNpcs = new List<RelationshipDataSO>();
        private List<RelationshipGroupSO> _allGroups = new List<RelationshipGroupSO>();
        private RelationshipDataSO _selectedNpc;

        private int _tabIndex = 0;
        private readonly string[] _tabNames = { "NPC列表", "NPC组", "统计" };

        [MenuItem("ZeroEngine/Relationship/Relationship Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<RelationshipEditorWindow>("Relationship Editor");
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
            _allNpcs.Clear();
            _allGroups.Clear();

            // 加载所有NPC
            var npcGuids = AssetDatabase.FindAssets("t:RelationshipDataSO");
            foreach (var guid in npcGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var npc = AssetDatabase.LoadAssetAtPath<RelationshipDataSO>(path);
                if (npc != null)
                {
                    _allNpcs.Add(npc);
                }
            }
            _allNpcs.Sort((a, b) =>
            {
                int typeCompare = a.NpcType.CompareTo(b.NpcType);
                if (typeCompare != 0) return typeCompare;
                int orderCompare = a.SortOrder.CompareTo(b.SortOrder);
                if (orderCompare != 0) return orderCompare;
                return string.Compare(a.DisplayName, b.DisplayName);
            });

            // 加载所有NPC组
            var groupGuids = AssetDatabase.FindAssets("t:RelationshipGroupSO");
            foreach (var guid in groupGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var group = AssetDatabase.LoadAssetAtPath<RelationshipGroupSO>(path);
                if (group != null)
                {
                    _allGroups.Add(group);
                }
            }
            _allGroups.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Relationship Editor", "Manage NPC relationships and groups");
            // 标签页
            _tabIndex = GUILayout.Toolbar(_tabIndex, _tabNames, GUILayout.Height(30));
            EditorGUILayout.Space(5);

            switch (_tabIndex)
            {
                case 0:
                    DrawNpcTab();
                    break;
                case 1:
                    DrawGroupTab();
                    break;
                case 2:
                    DrawStatisticsTab();
                    break;
            }
        }

        private void DrawNpcTab()
        {
            EditorGUILayout.BeginHorizontal();

            // 左侧列表
            DrawNpcList();

            // 右侧详情
            DrawNpcDetails();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNpcList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(280));

            // 搜索
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton")))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // 类型筛选
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全部", _typeFilter == null ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _typeFilter = null;
            }
            if (GUILayout.Button("普通", _typeFilter == NpcType.Normal ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _typeFilter = NpcType.Normal;
            }
            if (GUILayout.Button("可攻略", _typeFilter == NpcType.Romanceable ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _typeFilter = NpcType.Romanceable;
            }
            if (GUILayout.Button("商人", _typeFilter == NpcType.Merchant ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _typeFilter = NpcType.Merchant;
            }
            EditorGUILayout.EndHorizontal();

            // 工具栏
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建", GUILayout.Height(24)))
            {
                CreateNewNpc();
            }
            if (GUILayout.Button("刷新", GUILayout.Height(24)))
            {
                RefreshLists();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 列表
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            NpcType? lastType = null;
            foreach (var npc in _allNpcs)
            {
                // 筛选
                if (_typeFilter.HasValue && npc.NpcType != _typeFilter.Value)
                    continue;

                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    if (!npc.DisplayName.ToLower().Contains(_searchFilter.ToLower()) &&
                        !npc.NpcId.ToLower().Contains(_searchFilter.ToLower()))
                    {
                        continue;
                    }
                }

                // 类型标题
                if (!_typeFilter.HasValue && lastType != npc.NpcType)
                {
                    lastType = npc.NpcType;
                    EditorGUILayout.LabelField(npc.NpcType.ToString(), EditorStyles.boldLabel);
                }

                // NPC项
                bool isSelected = _selectedNpc == npc;
                var bgColor = isSelected ? new Color(0.3f, 0.6f, 1f) : Color.white;
                if (npc.NpcType == NpcType.Romanceable)
                {
                    bgColor = isSelected ? new Color(0.8f, 0.4f, 0.6f) : new Color(1f, 0.9f, 0.95f);
                }
                GUI.backgroundColor = bgColor;

                EditorGUILayout.BeginHorizontal("box");

                // 头像
                if (npc.Portrait != null)
                {
                    GUILayout.Box(npc.Portrait.texture, GUILayout.Width(40), GUILayout.Height(40));
                }
                else
                {
                    GUILayout.Box("?", GUILayout.Width(40), GUILayout.Height(40));
                }

                // 信息
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(npc.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{npc.Thresholds?.Count ?? 0}等级 | {npc.LikedGifts?.Count ?? 0}喜好 | {npc.Events?.Count ?? 0}事件",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();

                // 点击选择
                if (Event.current.type == EventType.MouseDown &&
                    GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    _selectedNpc = npc;
                    Selection.activeObject = npc;
                    Event.current.Use();
                }

                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawNpcDetails()
        {
            EditorGUILayout.BeginVertical();

            if (_selectedNpc == null)
            {
                EditorGUILayout.HelpBox("选择一个NPC查看详情", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // 标题栏
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(_selectedNpc.DisplayName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("定位", EditorStyles.toolbarButton))
            {
                EditorGUIUtility.PingObject(_selectedNpc);
                Selection.activeObject = _selectedNpc;
            }
            if (GUILayout.Button("编辑", EditorStyles.toolbarButton))
            {
                Selection.activeObject = _selectedNpc;
            }
            EditorGUILayout.EndHorizontal();

            _detailScrollPosition = EditorGUILayout.BeginScrollView(_detailScrollPosition);

            // 基本信息
            EditorGUILayout.LabelField("基本信息", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("ID", _selectedNpc.NpcId);
            EditorGUILayout.LabelField("类型", _selectedNpc.NpcType.ToString());
            EditorGUILayout.LabelField("初始好感度", _selectedNpc.InitialPoints.ToString());
            if (_selectedNpc.DailyDecay > 0)
            {
                EditorGUILayout.LabelField("每日衰减", _selectedNpc.DailyDecay.ToString());
            }
            EditorGUILayout.LabelField("每日送礼上限", _selectedNpc.MaxGiftsPerDay.ToString());
            EditorGUILayout.LabelField("每日对话上限", _selectedNpc.MaxTalksPerDay.ToString());
            EditorGUILayout.LabelField("对话好感度", _selectedNpc.TalkPoints.ToString());
            if (!string.IsNullOrEmpty(_selectedNpc.Description))
            {
                EditorGUILayout.LabelField("描述", _selectedNpc.Description, EditorStyles.wordWrappedLabel);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // 等级阈值
            EditorGUILayout.LabelField($"等级阈值 ({_selectedNpc.Thresholds?.Count ?? 0})", EditorStyles.boldLabel);
            if (_selectedNpc.Thresholds != null && _selectedNpc.Thresholds.Count > 0)
            {
                EditorGUI.indentLevel++;
                foreach (var threshold in _selectedNpc.Thresholds)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(threshold.Level.ToString(), GUILayout.Width(100));
                    EditorGUILayout.LabelField($">= {threshold.RequiredPoints} 点");
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // 礼物偏好
            EditorGUILayout.LabelField($"礼物偏好 (喜欢: {_selectedNpc.LikedGifts?.Count ?? 0}, 讨厌: {_selectedNpc.DislikedGifts?.Count ?? 0})", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            if (_selectedNpc.LikedGifts != null && _selectedNpc.LikedGifts.Count > 0)
            {
                EditorGUILayout.LabelField("喜欢:", EditorStyles.miniBoldLabel);
                foreach (var gift in _selectedNpc.LikedGifts)
                {
                    if (gift.Item != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (gift.Item.Icon != null)
                        {
                            GUILayout.Box(gift.Item.Icon.texture, GUILayout.Width(20), GUILayout.Height(20));
                        }
                        EditorGUILayout.LabelField($"{gift.Item.ItemName} ({gift.Preference}) +{gift.PointsChange}");
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            if (_selectedNpc.DislikedGifts != null && _selectedNpc.DislikedGifts.Count > 0)
            {
                EditorGUILayout.LabelField("讨厌:", EditorStyles.miniBoldLabel);
                foreach (var gift in _selectedNpc.DislikedGifts)
                {
                    if (gift.Item != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (gift.Item.Icon != null)
                        {
                            GUILayout.Box(gift.Item.Icon.texture, GUILayout.Width(20), GUILayout.Height(20));
                        }
                        EditorGUILayout.LabelField($"{gift.Item.ItemName} ({gift.Preference}) {gift.PointsChange}");
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            EditorGUILayout.LabelField($"默认礼物: +{_selectedNpc.DefaultGiftPoints}");
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // 事件
            EditorGUILayout.LabelField($"好感度事件 ({_selectedNpc.Events?.Count ?? 0})", EditorStyles.boldLabel);
            if (_selectedNpc.Events != null && _selectedNpc.Events.Count > 0)
            {
                EditorGUI.indentLevel++;
                foreach (var evt in _selectedNpc.Events)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"[{evt.RequiredLevel}] {evt.DisplayName}");
                    if (evt.OneTime)
                    {
                        GUILayout.Label("一次性", EditorStyles.miniLabel, GUILayout.Width(40));
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGroupTab()
        {
            EditorGUILayout.BeginHorizontal();

            // NPC组列表
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            EditorGUILayout.LabelField("NPC组", EditorStyles.boldLabel);

            if (GUILayout.Button("新建NPC组", GUILayout.Height(24)))
            {
                CreateNewGroup();
            }

            EditorGUILayout.Space(4);

            foreach (var group in _allGroups)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(group.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{group.Members?.Count ?? 0} 个NPC",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                if (GUILayout.Button("编辑", GUILayout.Width(40)))
                {
                    Selection.activeObject = group;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            // NPC组详情
            EditorGUILayout.BeginVertical();
            EditorGUILayout.HelpBox("选择NPC组进行编辑", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatisticsTab()
        {
            EditorGUILayout.LabelField("好感度统计", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // 总览
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("总览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"NPC总数: {_allNpcs.Count}");
            EditorGUILayout.LabelField($"NPC组数: {_allGroups.Count}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 类型统计
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("类型统计", EditorStyles.boldLabel);

            var typeCount = new Dictionary<NpcType, int>();
            int totalGifts = 0;
            int totalEvents = 0;

            foreach (var npc in _allNpcs)
            {
                if (!typeCount.ContainsKey(npc.NpcType))
                {
                    typeCount[npc.NpcType] = 0;
                }
                typeCount[npc.NpcType]++;

                totalGifts += (npc.LikedGifts?.Count ?? 0) + (npc.DislikedGifts?.Count ?? 0);
                totalEvents += npc.Events?.Count ?? 0;
            }

            foreach (var kvp in typeCount)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(kvp.Key.ToString(), GUILayout.Width(100));
                EditorGUILayout.LabelField($"{kvp.Value} 个");

                float ratio = (float)kvp.Value / _allNpcs.Count;
                Rect barRect = GUILayoutUtility.GetRect(100, 16);
                EditorGUI.ProgressBar(barRect, ratio, $"{ratio * 100:F1}%");

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 内容统计
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("内容统计", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"礼物偏好总数: {totalGifts}");
            EditorGUILayout.LabelField($"好感度事件总数: {totalEvents}");
            EditorGUILayout.EndVertical();
        }

        private void CreateNewNpc()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建NPC",
                "New NPC Relationship",
                "asset",
                "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var npc = ScriptableObject.CreateInstance<RelationshipDataSO>();
            npc.NpcId = System.IO.Path.GetFileNameWithoutExtension(path);
            npc.DisplayName = npc.NpcId;

            AssetDatabase.CreateAsset(npc, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            _selectedNpc = npc;
            Selection.activeObject = npc;
        }

        private void CreateNewGroup()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建NPC组",
                "New NPC Group",
                "asset",
                "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var group = ScriptableObject.CreateInstance<RelationshipGroupSO>();
            group.GroupId = System.IO.Path.GetFileNameWithoutExtension(path);
            group.DisplayName = group.GroupId;

            AssetDatabase.CreateAsset(group, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            Selection.activeObject = group;
        }
    }
}
