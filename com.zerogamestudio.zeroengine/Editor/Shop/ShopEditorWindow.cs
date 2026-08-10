using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using ZeroEngine.Shop;

namespace ZeroEngine.Editor.Shop
{
    /// <summary>
    /// 商店系统编辑器窗口
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class ShopEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private int _selectedTab;
        private readonly string[] _tabNames = { "商店列表", "商品列表", "统计" };

        private List<ShopSO> _shops = new List<ShopSO>();
        private List<ShopItemSO> _items = new List<ShopItemSO>();
        private ShopSO _selectedShop;
        private string _searchFilter = "";
        private ShopType _typeFilter = (ShopType)(-1);

        [MenuItem("ZeroEngine/Shop/Shop Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<ShopEditorWindow>("Shop Editor");
            window.minSize = new Vector2(600, 400);
        }

        private void OnEnable()
        {
            RefreshAssets();
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Shop Editor", "Manage shops, products, and statistics");
            DrawToolbar();

            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            EditorGUILayout.Space(5);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTab)
            {
                case 0:
                    DrawShopList();
                    break;
                case 1:
                    DrawItemList();
                    break;
                case 2:
                    DrawStatistics();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                RefreshAssets();
            }

            GUILayout.FlexibleSpace();

            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));

            if (GUILayout.Button("+商店", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                CreateNewShop();
            }

            if (GUILayout.Button("+商品", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                CreateNewItem();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawShopList()
        {
            // 类型筛选
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("类型筛选:", GUILayout.Width(60));
            _typeFilter = (ShopType)EditorGUILayout.EnumFlagsField(_typeFilter, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            foreach (var shop in _shops)
            {
                if (shop == null) continue;
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !shop.DisplayName.ToLower().Contains(_searchFilter.ToLower()) &&
                    !shop.ShopId.ToLower().Contains(_searchFilter.ToLower()))
                    continue;

                if ((int)_typeFilter != -1 && (_typeFilter & shop.ShopType) == 0)
                    continue;

                DrawShopEntry(shop);
            }
        }

        private void DrawShopEntry(ShopSO shop)
        {
            bool isSelected = _selectedShop == shop;
            Color bgColor = isSelected ? new Color(0.3f, 0.5f, 0.8f, 0.3f) : Color.clear;

            EditorGUILayout.BeginHorizontal(GetBoxStyle(bgColor));

            // 图标
            if (shop.Icon != null)
            {
                GUILayout.Label(AssetPreview.GetAssetPreview(shop.Icon), GUILayout.Width(40), GUILayout.Height(40));
            }
            else
            {
                GUILayout.Label(EditorGUIUtility.IconContent("d_GridLayoutGroup Icon"), GUILayout.Width(40), GUILayout.Height(40));
            }

            // 信息
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(shop.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"[{shop.ShopType}] {shop.Items.Count} 商品", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // 按钮
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                _selectedShop = shop;
                Selection.activeObject = shop;
            }

            if (GUILayout.Button("编辑", GUILayout.Width(50)))
            {
                Selection.activeObject = shop;
                EditorGUIUtility.PingObject(shop);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawItemList()
        {
            if (_selectedShop != null)
            {
                EditorGUILayout.LabelField($"商店: {_selectedShop.DisplayName}", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                foreach (var item in _selectedShop.Items)
                {
                    if (item == null) continue;
                    DrawItemEntry(item);
                }
            }
            else
            {
                EditorGUILayout.LabelField("所有商品", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                foreach (var item in _items)
                {
                    if (item == null) continue;
                    if (!string.IsNullOrEmpty(_searchFilter) &&
                        !item.DisplayName.ToLower().Contains(_searchFilter.ToLower()) &&
                        !item.ItemId.ToLower().Contains(_searchFilter.ToLower()))
                        continue;

                    DrawItemEntry(item);
                }
            }
        }

        private void DrawItemEntry(ShopItemSO item)
        {
            EditorGUILayout.BeginHorizontal("box");

            // 图标
            if (item.Icon != null)
            {
                GUILayout.Label(AssetPreview.GetAssetPreview(item.Icon), GUILayout.Width(32), GUILayout.Height(32));
            }
            else
            {
                GUILayout.Label(EditorGUIUtility.IconContent("d_Prefab Icon"), GUILayout.Width(32), GUILayout.Height(32));
            }

            // 信息
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(item.DisplayName);

            string priceText = item.HasDiscount
                ? $"<color=red><s>{item.BuyPrice.Amount}</s></color> {item.GetFinalBuyPrice()} {item.BuyPrice.CurrencyType}"
                : $"{item.BuyPrice.Amount} {item.BuyPrice.CurrencyType}";

            EditorGUILayout.LabelField(priceText, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // 标签
            if (item.IsFeatured)
            {
                GUILayout.Label("★", GUILayout.Width(20));
            }

            if (item.Stock.InitialStock >= 0)
            {
                GUILayout.Label($"库存:{item.Stock.InitialStock}", EditorStyles.miniLabel, GUILayout.Width(60));
            }

            if (item.Limit.MaxCount > 0)
            {
                GUILayout.Label($"限购:{item.Limit.MaxCount}", EditorStyles.miniLabel, GUILayout.Width(60));
            }

            if (GUILayout.Button("编辑", GUILayout.Width(50)))
            {
                Selection.activeObject = item;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatistics()
        {
            EditorGUILayout.LabelField("商店统计", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField($"商店总数: {_shops.Count}");
            EditorGUILayout.LabelField($"商品总数: {_items.Count}");

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("按类型统计", EditorStyles.boldLabel);

            var typeCounts = new Dictionary<ShopType, int>();
            foreach (var shop in _shops)
            {
                if (shop == null) continue;
                if (!typeCounts.ContainsKey(shop.ShopType))
                    typeCounts[shop.ShopType] = 0;
                typeCounts[shop.ShopType]++;
            }

            foreach (var kvp in typeCounts)
            {
                EditorGUILayout.LabelField($"  {kvp.Key}: {kvp.Value}");
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("折扣商品", EditorStyles.boldLabel);

            int discountCount = 0;
            foreach (var item in _items)
            {
                if (item != null && item.HasDiscount)
                    discountCount++;
            }
            EditorGUILayout.LabelField($"  折扣商品数: {discountCount}");

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("限购商品", EditorStyles.boldLabel);

            int limitedCount = 0;
            foreach (var item in _items)
            {
                if (item != null && item.Limit.MaxCount > 0)
                    limitedCount++;
            }
            EditorGUILayout.LabelField($"  限购商品数: {limitedCount}");
        }

        private void RefreshAssets()
        {
            _shops.Clear();
            _items.Clear();

            string[] shopGuids = AssetDatabase.FindAssets("t:ShopSO");
            foreach (string guid in shopGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shop = AssetDatabase.LoadAssetAtPath<ShopSO>(path);
                if (shop != null)
                {
                    _shops.Add(shop);
                }
            }

            string[] itemGuids = AssetDatabase.FindAssets("t:ShopItemSO");
            foreach (string guid in itemGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ShopItemSO>(path);
                if (item != null)
                {
                    _items.Add(item);
                }
            }
        }

        private void CreateNewShop()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建商店", "New Shop", "asset", "选择保存位置");

            if (!string.IsNullOrEmpty(path))
            {
                var shop = CreateInstance<ShopSO>();
                AssetDatabase.CreateAsset(shop, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = shop;
                RefreshAssets();
            }
        }

        private void CreateNewItem()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建商品", "New Shop Item", "asset", "选择保存位置");

            if (!string.IsNullOrEmpty(path))
            {
                var item = CreateInstance<ShopItemSO>();
                AssetDatabase.CreateAsset(item, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = item;
                RefreshAssets();
            }
        }

        private GUIStyle GetBoxStyle(Color bgColor)
        {
            var style = new GUIStyle("box");
            if (bgColor != Color.clear)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, bgColor);
                tex.Apply();
                style.normal.background = tex;
            }
            return style;
        }
    }
}
