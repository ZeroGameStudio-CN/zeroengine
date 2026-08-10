using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Crafting;

namespace ZeroEngine.Editor.Crafting
{
    /// <summary>
    /// 合成配方编辑器窗口
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class CraftingEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private Vector2 _detailScrollPosition;
        private string _searchFilter = "";
        private RecipeCategory? _categoryFilter = null;

        private List<CraftingRecipeSO> _allRecipes = new List<CraftingRecipeSO>();
        private List<RecipeBookSO> _allBooks = new List<RecipeBookSO>();
        private CraftingRecipeSO _selectedRecipe;

        private int _tabIndex = 0;
        private readonly string[] _tabNames = { "配方列表", "配方书", "统计" };

        [MenuItem("ZeroEngine/Crafting/Recipe Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<CraftingEditorWindow>("Recipe Editor");
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
            _allRecipes.Clear();
            _allBooks.Clear();

            // 加载所有配方
            var recipeGuids = AssetDatabase.FindAssets("t:CraftingRecipeSO");
            foreach (var guid in recipeGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipeSO>(path);
                if (recipe != null)
                {
                    _allRecipes.Add(recipe);
                }
            }
            _allRecipes.Sort((a, b) =>
            {
                int catCompare = a.Category.CompareTo(b.Category);
                if (catCompare != 0) return catCompare;
                int orderCompare = a.SortOrder.CompareTo(b.SortOrder);
                if (orderCompare != 0) return orderCompare;
                return string.Compare(a.DisplayName, b.DisplayName);
            });

            // 加载所有配方书
            var bookGuids = AssetDatabase.FindAssets("t:RecipeBookSO");
            foreach (var guid in bookGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var book = AssetDatabase.LoadAssetAtPath<RecipeBookSO>(path);
                if (book != null)
                {
                    _allBooks.Add(book);
                }
            }
            _allBooks.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Recipe Editor", "Manage crafting recipes and books");
            // 标签页
            _tabIndex = GUILayout.Toolbar(_tabIndex, _tabNames, GUILayout.Height(30));
            EditorGUILayout.Space(5);

            switch (_tabIndex)
            {
                case 0:
                    DrawRecipeTab();
                    break;
                case 1:
                    DrawBookTab();
                    break;
                case 2:
                    DrawStatisticsTab();
                    break;
            }
        }

        private void DrawRecipeTab()
        {
            EditorGUILayout.BeginHorizontal();

            // 左侧列表
            DrawRecipeList();

            // 右侧详情
            DrawRecipeDetails();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRecipeList()
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

            // 分类筛选
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全部", _categoryFilter == null ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = null;
            }
            if (GUILayout.Button("武器", _categoryFilter == RecipeCategory.Weapon ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = RecipeCategory.Weapon;
            }
            if (GUILayout.Button("防具", _categoryFilter == RecipeCategory.Armor ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = RecipeCategory.Armor;
            }
            if (GUILayout.Button("消耗", _categoryFilter == RecipeCategory.Consumable ? EditorStyles.toolbarButton : EditorStyles.miniButton))
            {
                _categoryFilter = RecipeCategory.Consumable;
            }
            EditorGUILayout.EndHorizontal();

            // 工具栏
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建", GUILayout.Height(24)))
            {
                CreateNewRecipe();
            }
            if (GUILayout.Button("刷新", GUILayout.Height(24)))
            {
                RefreshLists();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 列表
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            RecipeCategory? lastCategory = null;
            foreach (var recipe in _allRecipes)
            {
                // 筛选
                if (_categoryFilter.HasValue && recipe.Category != _categoryFilter.Value)
                    continue;

                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    if (!recipe.DisplayName.ToLower().Contains(_searchFilter.ToLower()) &&
                        !recipe.RecipeId.ToLower().Contains(_searchFilter.ToLower()))
                    {
                        continue;
                    }
                }

                // 分类标题
                if (!_categoryFilter.HasValue && lastCategory != recipe.Category)
                {
                    lastCategory = recipe.Category;
                    EditorGUILayout.LabelField(recipe.Category.ToString(), EditorStyles.boldLabel);
                }

                // 配方项
                bool isSelected = _selectedRecipe == recipe;
                GUI.backgroundColor = isSelected ? new Color(0.3f, 0.6f, 1f) : Color.white;

                EditorGUILayout.BeginHorizontal("box");

                // 图标
                var mainOutput = recipe.GetMainOutput();
                if (mainOutput != null && mainOutput.Icon != null)
                {
                    GUILayout.Box(mainOutput.Icon.texture, GUILayout.Width(32), GUILayout.Height(32));
                }
                else if (recipe.Icon != null)
                {
                    GUILayout.Box(recipe.Icon.texture, GUILayout.Width(32), GUILayout.Height(32));
                }
                else
                {
                    GUILayout.Box("?", GUILayout.Width(32), GUILayout.Height(32));
                }

                // 信息
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(recipe.DisplayName, EditorStyles.boldLabel);

                string timeStr = recipe.CraftTime > 0 ? $"{recipe.CraftTime}秒" : "即时";
                string successStr = recipe.SuccessRate < 1f ? $"{recipe.SuccessRate * 100:F0}%" : "100%";
                EditorGUILayout.LabelField($"{recipe.Ingredients.Count}材料 | {timeStr} | {successStr}",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();

                // 点击选择
                if (Event.current.type == EventType.MouseDown &&
                    GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    _selectedRecipe = recipe;
                    Selection.activeObject = recipe;
                    Event.current.Use();
                }

                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRecipeDetails()
        {
            EditorGUILayout.BeginVertical();

            if (_selectedRecipe == null)
            {
                EditorGUILayout.HelpBox("选择一个配方查看详情", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // 标题栏
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(_selectedRecipe.DisplayName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("定位", EditorStyles.toolbarButton))
            {
                EditorGUIUtility.PingObject(_selectedRecipe);
                Selection.activeObject = _selectedRecipe;
            }
            if (GUILayout.Button("编辑", EditorStyles.toolbarButton))
            {
                Selection.activeObject = _selectedRecipe;
            }
            EditorGUILayout.EndHorizontal();

            _detailScrollPosition = EditorGUILayout.BeginScrollView(_detailScrollPosition);

            // 基本信息
            EditorGUILayout.LabelField("基本信息", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("ID", _selectedRecipe.RecipeId);
            EditorGUILayout.LabelField("分类", _selectedRecipe.Category.ToString());
            EditorGUILayout.LabelField("合成时间", _selectedRecipe.CraftTime > 0 ? $"{_selectedRecipe.CraftTime} 秒" : "即时");
            EditorGUILayout.LabelField("成功率", $"{_selectedRecipe.SuccessRate * 100:F0}%");
            if (_selectedRecipe.GreatSuccessRate > 0)
            {
                EditorGUILayout.LabelField("大成功率", $"{_selectedRecipe.GreatSuccessRate * 100:F0}%");
            }
            if (!string.IsNullOrEmpty(_selectedRecipe.RequiredWorkbench))
            {
                EditorGUILayout.LabelField("需要工作台", _selectedRecipe.RequiredWorkbench);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // 材料
            EditorGUILayout.LabelField($"材料 ({_selectedRecipe.Ingredients.Count})", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var ingredient in _selectedRecipe.Ingredients)
            {
                if (ingredient.Item != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (ingredient.Item.Icon != null)
                    {
                        GUILayout.Box(ingredient.Item.Icon.texture, GUILayout.Width(20), GUILayout.Height(20));
                    }
                    string consumed = ingredient.IsConsumed ? "" : " (不消耗)";
                    EditorGUILayout.LabelField($"{ingredient.Item.ItemName} x{ingredient.Amount}{consumed}");
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // 产出
            EditorGUILayout.LabelField($"产出 ({_selectedRecipe.Outputs.Count})", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var output in _selectedRecipe.Outputs)
            {
                if (output.Item != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (output.Item.Icon != null)
                    {
                        GUILayout.Box(output.Item.Icon.texture, GUILayout.Width(20), GUILayout.Height(20));
                    }
                    string probStr = output.Probability < 1f ? $" ({output.Probability * 100:F0}%)" : "";
                    string bonusStr = output.BonusAmount > 0 ? $" (+{output.BonusAmount})" : "";
                    EditorGUILayout.LabelField($"{output.Item.ItemName} x{output.BaseAmount}{bonusStr}{probStr}");
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // 解锁条件
            EditorGUILayout.LabelField("解锁条件", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("解锁方式", _selectedRecipe.UnlockType.ToString());
            switch (_selectedRecipe.UnlockType)
            {
                case RecipeUnlockType.Level:
                    EditorGUILayout.LabelField("需要等级", _selectedRecipe.UnlockLevel.ToString());
                    break;
                case RecipeUnlockType.Quest:
                    EditorGUILayout.LabelField("需要任务", _selectedRecipe.UnlockQuestId);
                    break;
                case RecipeUnlockType.Achievement:
                    EditorGUILayout.LabelField("需要成就", _selectedRecipe.UnlockAchievementId);
                    break;
                case RecipeUnlockType.Item:
                    if (_selectedRecipe.UnlockItem != null)
                    {
                        EditorGUILayout.LabelField("学习物品", _selectedRecipe.UnlockItem.ItemName);
                    }
                    break;
                case RecipeUnlockType.Relationship:
                    EditorGUILayout.LabelField("NPC", _selectedRecipe.UnlockRelationshipNpcId);
                    EditorGUILayout.LabelField("好感等级", _selectedRecipe.UnlockRelationshipLevel.ToString());
                    break;
            }
            EditorGUI.indentLevel--;

            // 技能
            if (!string.IsNullOrEmpty(_selectedRecipe.SkillId))
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("技能经验", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("技能ID", _selectedRecipe.SkillId);
                EditorGUILayout.LabelField("经验奖励", _selectedRecipe.ExpReward.ToString());
                if (_selectedRecipe.RequiredSkillLevel > 0)
                {
                    EditorGUILayout.LabelField("需要等级", _selectedRecipe.RequiredSkillLevel.ToString());
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawBookTab()
        {
            EditorGUILayout.BeginHorizontal();

            // 配方书列表
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            EditorGUILayout.LabelField("配方书", EditorStyles.boldLabel);

            if (GUILayout.Button("新建配方书", GUILayout.Height(24)))
            {
                CreateNewBook();
            }

            EditorGUILayout.Space(4);

            foreach (var book in _allBooks)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(book.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{book.Recipes?.Count ?? 0} 个配方",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                if (GUILayout.Button("编辑", GUILayout.Width(40)))
                {
                    Selection.activeObject = book;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            // 配方书详情
            EditorGUILayout.BeginVertical();
            EditorGUILayout.HelpBox("选择配方书进行编辑", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatisticsTab()
        {
            EditorGUILayout.LabelField("配方统计", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // 总览
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("总览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"配方总数: {_allRecipes.Count}");
            EditorGUILayout.LabelField($"配方书数: {_allBooks.Count}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 分类统计
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("分类统计", EditorStyles.boldLabel);

            var categoryCount = new Dictionary<RecipeCategory, int>();
            foreach (var recipe in _allRecipes)
            {
                if (!categoryCount.ContainsKey(recipe.Category))
                {
                    categoryCount[recipe.Category] = 0;
                }
                categoryCount[recipe.Category]++;
            }

            foreach (var kvp in categoryCount)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(kvp.Key.ToString(), GUILayout.Width(100));
                EditorGUILayout.LabelField($"{kvp.Value} 个");

                float ratio = (float)kvp.Value / _allRecipes.Count;
                Rect barRect = GUILayoutUtility.GetRect(100, 16);
                EditorGUI.ProgressBar(barRect, ratio, $"{ratio * 100:F1}%");

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 解锁方式统计
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("解锁方式统计", EditorStyles.boldLabel);

            var unlockCount = new Dictionary<RecipeUnlockType, int>();
            int instantCount = 0;
            int timedCount = 0;

            foreach (var recipe in _allRecipes)
            {
                if (!unlockCount.ContainsKey(recipe.UnlockType))
                {
                    unlockCount[recipe.UnlockType] = 0;
                }
                unlockCount[recipe.UnlockType]++;

                if (recipe.CraftTime > 0)
                    timedCount++;
                else
                    instantCount++;
            }

            foreach (var kvp in unlockCount)
            {
                EditorGUILayout.LabelField($"{kvp.Key}: {kvp.Value}");
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"即时合成: {instantCount}");
            EditorGUILayout.LabelField($"延时合成: {timedCount}");

            EditorGUILayout.EndVertical();
        }

        private void CreateNewRecipe()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建配方",
                "New Recipe",
                "asset",
                "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var recipe = ScriptableObject.CreateInstance<CraftingRecipeSO>();
            recipe.RecipeId = System.IO.Path.GetFileNameWithoutExtension(path);
            recipe.DisplayName = recipe.RecipeId;

            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            _selectedRecipe = recipe;
            Selection.activeObject = recipe;
        }

        private void CreateNewBook()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建配方书",
                "New Recipe Book",
                "asset",
                "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var book = ScriptableObject.CreateInstance<RecipeBookSO>();
            book.BookId = System.IO.Path.GetFileNameWithoutExtension(path);
            book.DisplayName = book.BookId;

            AssetDatabase.CreateAsset(book, path);
            AssetDatabase.SaveAssets();

            RefreshLists();
            Selection.activeObject = book;
        }
    }
}
