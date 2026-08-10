using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Equipment;

namespace ZeroEngine.Editor.Equipment
{
    /// <summary>
    /// 装备配置编辑器窗口
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class EquipmentEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private int _selectedTab;
        private readonly string[] _tabNames = { "Equipment", "Slot Types", "Sets" };

        // 缓存
        private List<EquipmentDataSO> _equipmentList = new();
        private List<EquipmentSlotType> _slotTypes = new();
        private List<EquipmentSetSO> _sets = new();

        // 筛选
        private string _searchFilter = "";
        private EquipmentSlotType _slotFilter;

        [MenuItem("ZeroEngine/Equipment/Equipment Editor")]
        public static void Open()
        {
            var window = GetWindow<EquipmentEditorWindow>();
            window.titleContent = new GUIContent("Equipment Editor", EditorGUIUtility.IconContent("d_Preset.Context@2x").image);
            window.minSize = new Vector2(600, 400);
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
            _equipmentList.Clear();
            _slotTypes.Clear();
            _sets.Clear();

            // 查找所有装备资产
            var guids = AssetDatabase.FindAssets("t:EquipmentDataSO");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<EquipmentDataSO>(path);
                if (asset != null)
                {
                    _equipmentList.Add(asset);
                }
            }

            // 查找所有槽位类型
            guids = AssetDatabase.FindAssets("t:EquipmentSlotType");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<EquipmentSlotType>(path);
                if (asset != null)
                {
                    _slotTypes.Add(asset);
                }
            }

            // 查找所有套装
            guids = AssetDatabase.FindAssets("t:EquipmentSetSO");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<EquipmentSetSO>(path);
                if (asset != null)
                {
                    _sets.Add(asset);
                }
            }
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Equipment Editor", "Manage equipment, slots, and sets");
            // 标签页
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTab)
            {
                case 0:
                    DrawEquipmentTab();
                    break;
                case 1:
                    DrawSlotTypesTab();
                    break;
                case 2:
                    DrawSetsTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEquipmentTab()
        {
            // 工具栏
            EditorGUILayout.BeginHorizontal();

            // 搜索
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, GUILayout.Width(200));

            // 槽位筛选
            EditorGUILayout.LabelField("Slot:", GUILayout.Width(30));
            _slotFilter = (EquipmentSlotType)EditorGUILayout.ObjectField(_slotFilter, typeof(EquipmentSlotType), false, GUILayout.Width(150));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Create Equipment", GUILayout.Width(130)))
            {
                CreateEquipment();
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshLists();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 装备列表
            EditorGUILayout.LabelField($"Equipment ({_equipmentList.Count})", EditorStyles.boldLabel);

            foreach (var equipment in _equipmentList)
            {
                if (equipment == null) continue;

                // 应用筛选
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !equipment.ItemName.ToLower().Contains(_searchFilter.ToLower()) &&
                    !equipment.Id.ToLower().Contains(_searchFilter.ToLower()))
                {
                    continue;
                }

                if (_slotFilter != null && equipment.SlotType != _slotFilter)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal("box");

                // 图标
                if (equipment.Icon != null)
                {
                    GUILayout.Label(equipment.Icon.texture, GUILayout.Width(32), GUILayout.Height(32));
                }
                else
                {
                    GUILayout.Label("", GUILayout.Width(32), GUILayout.Height(32));
                }

                // 信息
                EditorGUILayout.BeginVertical();

                EditorGUILayout.LabelField(equipment.ItemName, EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Slot: {(equipment.SlotType != null ? equipment.SlotType.DisplayName : "None")}", GUILayout.Width(150));
                EditorGUILayout.LabelField($"Rarity: {equipment.Rarity}", GUILayout.Width(120));
                EditorGUILayout.LabelField($"Stats: {equipment.BaseStats.Count}", GUILayout.Width(80));
                if (equipment.BelongsToSet != null)
                {
                    EditorGUILayout.LabelField($"Set: {equipment.BelongsToSet.SetName}", GUILayout.Width(150));
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();

                // 操作按钮
                if (GUILayout.Button("Edit", GUILayout.Width(50)))
                {
                    Selection.activeObject = equipment;
                    EditorGUIUtility.PingObject(equipment);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSlotTypesTab()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Create Slot Type", GUILayout.Width(130)))
            {
                CreateSlotType();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Slot Types ({_slotTypes.Count})", EditorStyles.boldLabel);

            foreach (var slotType in _slotTypes)
            {
                if (slotType == null) continue;

                EditorGUILayout.BeginHorizontal("box");

                if (slotType.Icon != null)
                {
                    GUILayout.Label(slotType.Icon.texture, GUILayout.Width(32), GUILayout.Height(32));
                }
                else
                {
                    GUILayout.Label("", GUILayout.Width(32), GUILayout.Height(32));
                }

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(slotType.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"ID: {slotType.SlotId}");
                EditorGUILayout.EndVertical();

                if (GUILayout.Button("Edit", GUILayout.Width(50)))
                {
                    Selection.activeObject = slotType;
                    EditorGUIUtility.PingObject(slotType);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSetsTab()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Create Set", GUILayout.Width(100)))
            {
                CreateSet();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Equipment Sets ({_sets.Count})", EditorStyles.boldLabel);

            foreach (var set in _sets)
            {
                if (set == null) continue;

                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();

                if (set.Icon != null)
                {
                    GUILayout.Label(set.Icon.texture, GUILayout.Width(48), GUILayout.Height(48));
                }

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(set.SetName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Pieces: {set.Pieces.Count} | Effects: {set.Effects.Count}");

                // 显示效果阈值
                if (set.Effects.Count > 0)
                {
                    var thresholds = new List<string>();
                    foreach (var effect in set.Effects)
                    {
                        thresholds.Add($"{effect.RequiredPieces}pc");
                    }
                    EditorGUILayout.LabelField($"Thresholds: {string.Join(", ", thresholds)}");
                }

                EditorGUILayout.EndVertical();

                if (GUILayout.Button("Edit", GUILayout.Width(50)))
                {
                    Selection.activeObject = set;
                    EditorGUIUtility.PingObject(set);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }
        }

        private void CreateEquipment()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Equipment",
                "NewEquipment",
                "asset",
                "Select location for new equipment");

            if (string.IsNullOrEmpty(path)) return;

            var equipment = CreateInstance<EquipmentDataSO>();
            equipment.Id = System.Guid.NewGuid().ToString();
            equipment.ItemName = System.IO.Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(equipment, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            Selection.activeObject = equipment;
        }

        private void CreateSlotType()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Slot Type",
                "NewSlotType",
                "asset",
                "Select location for new slot type");

            if (string.IsNullOrEmpty(path)) return;

            var slotType = CreateInstance<EquipmentSlotType>();
            slotType.SlotId = System.IO.Path.GetFileNameWithoutExtension(path).ToLower();
            slotType.DisplayName = System.IO.Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(slotType, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            Selection.activeObject = slotType;
        }

        private void CreateSet()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Equipment Set",
                "NewEquipmentSet",
                "asset",
                "Select location for new equipment set");

            if (string.IsNullOrEmpty(path)) return;

            var set = CreateInstance<EquipmentSetSO>();
            set.SetId = System.IO.Path.GetFileNameWithoutExtension(path).ToLower();
            set.SetName = System.IO.Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(set, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            Selection.activeObject = set;
        }
    }
}
