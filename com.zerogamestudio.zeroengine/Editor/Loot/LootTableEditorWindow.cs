using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Loot;

namespace ZeroEngine.Editor.Loot
{
    /// <summary>
    /// 掉落表编辑器窗口
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class LootTableEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private Vector2 _entryScrollPosition;
        private string _searchFilter = "";

        private List<LootTableSO> _allTables = new List<LootTableSO>();
        private LootTableSO _selectedTable;

        // 模拟抽取
        private int _simulateTimes = 100;
        private Dictionary<string, int> _simulateResults = new Dictionary<string, int>();
        private bool _showSimulateResults;

        [MenuItem("ZeroEngine/Loot/Loot Table Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<LootTableEditorWindow>("Loot Table Editor");
            window.minSize = new Vector2(800, 500);
        }

        private void OnEnable()
        {
            RefreshTableList();
        }

        private void OnFocus()
        {
            RefreshTableList();
        }

        private void RefreshTableList()
        {
            _allTables.Clear();
            var guids = AssetDatabase.FindAssets("t:LootTableSO");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var table = AssetDatabase.LoadAssetAtPath<LootTableSO>(path);
                if (table != null)
                {
                    _allTables.Add(table);
                }
            }
            _allTables.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName));
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Loot Table Editor", "Build and inspect weighted loot tables");
            EditorGUILayout.BeginHorizontal();

            // 左侧列表
            DrawTableList();

            // 右侧详情
            DrawTableDetails();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTableList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(250));

            // 搜索框
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton")))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // 工具栏
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建", GUILayout.Height(24)))
            {
                CreateNewTable();
            }
            if (GUILayout.Button("刷新", GUILayout.Height(24)))
            {
                RefreshTableList();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 列表
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var table in _allTables)
            {
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    if (!table.DisplayName.ToLower().Contains(_searchFilter.ToLower()) &&
                        !table.TableId.ToLower().Contains(_searchFilter.ToLower()))
                    {
                        continue;
                    }
                }

                bool isSelected = _selectedTable == table;
                GUI.backgroundColor = isSelected ? new Color(0.3f, 0.6f, 1f) : Color.white;

                if (GUILayout.Button(new GUIContent(table.DisplayName, $"ID: {table.TableId}\n{table.Entries.Count} 条目"),
                    GUILayout.Height(28)))
                {
                    _selectedTable = table;
                    Selection.activeObject = table;
                    _showSimulateResults = false;
                }

                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawTableDetails()
        {
            EditorGUILayout.BeginVertical();

            if (_selectedTable == null)
            {
                EditorGUILayout.HelpBox("选择一个掉落表查看详情", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // 标题
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(_selectedTable.DisplayName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("定位", EditorStyles.toolbarButton))
            {
                EditorGUIUtility.PingObject(_selectedTable);
                Selection.activeObject = _selectedTable;
            }
            EditorGUILayout.EndHorizontal();

            _entryScrollPosition = EditorGUILayout.BeginScrollView(_entryScrollPosition);

            // 基本信息
            EditorGUILayout.LabelField("基本信息", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("ID", _selectedTable.TableId);
            EditorGUILayout.LabelField("模式", _selectedTable.DropMode.ToString());
            EditorGUILayout.LabelField("掉落数量", _selectedTable.DropCount.ToString());
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // 条目列表
            EditorGUILayout.LabelField($"条目列表 ({_selectedTable.Entries.Count})", EditorStyles.boldLabel);

            if (_selectedTable.Entries.Count > 0)
            {
                DrawEntryTable();
            }

            // 权重可视化
            EditorGUILayout.Space(10);
            DrawWeightVisualization();

            // 模拟抽取
            EditorGUILayout.Space(10);
            DrawSimulateSection();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEntryTable()
        {
            // 表头
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("类型", GUILayout.Width(60));
            GUILayout.Label("名称", GUILayout.Width(150));
            GUILayout.Label("数量", GUILayout.Width(80));
            GUILayout.Label("权重", GUILayout.Width(60));
            GUILayout.Label("概率", GUILayout.Width(60));
            GUILayout.Label("条件", GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            float totalWeight = 0;
            foreach (var entry in _selectedTable.Entries)
            {
                totalWeight += entry.Weight;
            }

            // 条目行
            for (int i = 0; i < _selectedTable.Entries.Count; i++)
            {
                var entry = _selectedTable.Entries[i];
                float probability = totalWeight > 0 ? entry.Weight / totalWeight : 0;

                EditorGUILayout.BeginHorizontal();

                // 类型图标
                var typeColor = entry.Type switch
                {
                    LootEntryType.Item => new Color(0.5f, 0.8f, 0.5f),
                    LootEntryType.Currency => new Color(1f, 0.8f, 0.3f),
                    LootEntryType.Table => new Color(0.5f, 0.7f, 1f),
                    _ => Color.gray
                };
                GUI.backgroundColor = typeColor;
                GUILayout.Box(entry.Type.ToString().Substring(0, 1), GUILayout.Width(60), GUILayout.Height(20));
                GUI.backgroundColor = Color.white;

                // 名称
                GUILayout.Label(entry.GetDisplayName(), GUILayout.Width(150));

                // 数量
                string amountStr = entry.AmountMin == entry.AmountMax
                    ? entry.AmountMin.ToString()
                    : $"{entry.AmountMin}-{entry.AmountMax}";
                GUILayout.Label(amountStr, GUILayout.Width(80));

                // 权重
                GUILayout.Label(entry.Weight.ToString("F1"), GUILayout.Width(60));

                // 概率
                GUILayout.Label($"{probability * 100:F1}%", GUILayout.Width(60));

                // 条件数量
                int condCount = entry.Conditions?.Count ?? 0;
                if (condCount > 0)
                {
                    GUI.color = Color.yellow;
                }
                GUILayout.Label(condCount.ToString(), GUILayout.Width(60));
                GUI.color = Color.white;

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawWeightVisualization()
        {
            EditorGUILayout.LabelField("权重分布", EditorStyles.boldLabel);

            if (_selectedTable.Entries.Count == 0) return;

            float totalWeight = 0;
            foreach (var entry in _selectedTable.Entries)
            {
                totalWeight += entry.Weight;
            }

            if (totalWeight <= 0) return;

            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(30));
            rect.x += EditorGUI.indentLevel * 15;
            rect.width -= EditorGUI.indentLevel * 15;

            float x = rect.x;
            Color[] colors = new Color[]
            {
                new Color(0.2f, 0.6f, 0.9f),
                new Color(0.9f, 0.4f, 0.4f),
                new Color(0.4f, 0.8f, 0.4f),
                new Color(0.9f, 0.7f, 0.2f),
                new Color(0.7f, 0.4f, 0.9f),
                new Color(0.4f, 0.9f, 0.9f)
            };

            for (int i = 0; i < _selectedTable.Entries.Count; i++)
            {
                var entry = _selectedTable.Entries[i];
                float width = (entry.Weight / totalWeight) * rect.width;

                if (width > 2)
                {
                    EditorGUI.DrawRect(new Rect(x, rect.y, width - 1, rect.height), colors[i % colors.Length]);

                    if (width > 30)
                    {
                        GUI.Label(new Rect(x + 2, rect.y + 5, width - 4, 20),
                            $"{entry.Weight / totalWeight * 100:F0}%",
                            EditorStyles.miniLabel);
                    }
                }

                x += width;
            }
        }

        private void DrawSimulateSection()
        {
            EditorGUILayout.LabelField("模拟抽取", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _simulateTimes = EditorGUILayout.IntField("抽取次数", _simulateTimes);
            _simulateTimes = Mathf.Clamp(_simulateTimes, 1, 100000);

            if (GUILayout.Button("模拟", GUILayout.Width(60)))
            {
                RunSimulation();
            }
            EditorGUILayout.EndHorizontal();

            if (_showSimulateResults && _simulateResults.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("模拟结果:", EditorStyles.boldLabel);

                foreach (var kvp in _simulateResults)
                {
                    float percent = (float)kvp.Value / _simulateTimes * 100;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(200));
                    EditorGUILayout.LabelField($"{kvp.Value} 次 ({percent:F2}%)");

                    // 进度条
                    Rect barRect = GUILayoutUtility.GetRect(100, 16);
                    EditorGUI.ProgressBar(barRect, percent / 100f, "");

                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void RunSimulation()
        {
            _simulateResults.Clear();

            if (_selectedTable == null || _selectedTable.Entries.Count == 0)
            {
                _showSimulateResults = false;
                return;
            }

            float totalWeight = 0;
            foreach (var entry in _selectedTable.Entries)
            {
                totalWeight += entry.Weight;
            }

            if (totalWeight <= 0)
            {
                _showSimulateResults = false;
                return;
            }

            // 模拟抽取
            for (int t = 0; t < _simulateTimes; t++)
            {
                float roll = Random.Range(0f, totalWeight);
                float cumulative = 0;

                foreach (var entry in _selectedTable.Entries)
                {
                    cumulative += entry.Weight;
                    if (roll <= cumulative)
                    {
                        string name = entry.GetDisplayName();
                        if (!_simulateResults.ContainsKey(name))
                        {
                            _simulateResults[name] = 0;
                        }
                        _simulateResults[name]++;
                        break;
                    }
                }
            }

            _showSimulateResults = true;
        }

        private void CreateNewTable()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建掉落表",
                "New LootTable",
                "asset",
                "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var table = ScriptableObject.CreateInstance<LootTableSO>();
            table.TableId = System.IO.Path.GetFileNameWithoutExtension(path);
            table.DisplayName = table.TableId;

            AssetDatabase.CreateAsset(table, path);
            AssetDatabase.SaveAssets();

            RefreshTableList();
            _selectedTable = table;
            Selection.activeObject = table;
        }
    }
}
