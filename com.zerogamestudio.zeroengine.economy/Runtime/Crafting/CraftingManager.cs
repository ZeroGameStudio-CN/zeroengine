using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.Crafting
{
    /// <summary>
    /// 合成系统管理器
    /// 负责配方解锁、合成逻辑和技能经验
    /// </summary>
    public partial class CraftingManager : MonoSingleton<CraftingManager>, ISaveable
    {
        [Header("配置")]
        [Tooltip("所有配方")]
        [SerializeField] private List<CraftingRecipeSO> _allRecipes = new List<CraftingRecipeSO>();

        [Tooltip("配方书")]
        [SerializeField] private List<RecipeBookSO> _recipeBooks = new List<RecipeBookSO>();

        [Header("调试")]
        [SerializeField] private bool _debugMode;

        // 已解锁配方
        private readonly HashSet<string> _unlockedRecipes = new HashSet<string>();

        // 进行中的合成
        private readonly List<CraftingProgress> _activeProgress = new List<CraftingProgress>();

        // 技能数据
        private readonly Dictionary<string, CraftingSkillData> _skillData =
            new Dictionary<string, CraftingSkillData>();

        // 缓存：ID -> Recipe
        private readonly Dictionary<string, CraftingRecipeSO> _recipeLookup =
            new Dictionary<string, CraftingRecipeSO>();

        // 缓存：分类 -> 配方列表
        private readonly Dictionary<RecipeCategory, List<CraftingRecipeSO>> _categoryCache =
            new Dictionary<RecipeCategory, List<CraftingRecipeSO>>();

        // 临时列表
        private readonly List<CraftingRecipeSO> _tempRecipeList = new List<CraftingRecipeSO>(32);

        #region Events

        /// <summary>合成事件</summary>
        public event Action<CraftingEventArgs> OnCraftingEvent;

        #endregion

        #region Properties

        /// <summary>已解锁配方数量</summary>
        public int UnlockedRecipeCount => _unlockedRecipes.Count;

        /// <summary>总配方数量</summary>
        public int TotalRecipeCount => _allRecipes.Count;

        /// <summary>进行中的合成数量</summary>
        public int ActiveCraftingCount => _activeProgress.Count;

        #endregion

        #region ISaveable

        public string SaveKey => "CraftingManager";

        public void Register()
        {
            RegisterWithSaveProvider();
        }

        public void Unregister()
        {
            ReleaseSaveRegistration();
        }

        public object ExportSaveData()
        {
            return CaptureState();
        }

        public void ImportSaveData(object data)
        {
            if (data is CraftingSaveData saveData) RestoreState(saveData);
        }

        public void ResetToDefault()
        {
            if (_isMutating) throw new InvalidOperationException("Cannot reset during a crafting transaction.");
            _unlockedRecipes.Clear();
            _activeProgress.Clear();
            _skillData.Clear();
            _receipts.Clear();

            // 解锁默认配方
            foreach (var recipe in _allRecipes)
            {
                if (recipe != null && recipe.UnlockType == RecipeUnlockType.Default)
                {
                    _unlockedRecipes.Add(recipe.RecipeId);
                }
            }
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            BuildCaches();
        }

        private void Start()
        {
            Register();

            // 初始化默认解锁
            if (!_stateImported && _unlockedRecipes.Count == 0 && _activeProgress.Count == 0 && _receipts.Count == 0)
            {
                ResetToDefault();
            }
        }

        private void Update()
        {
            // 检查完成的合成
            CheckCompletedCrafting();
        }

        protected override void OnDestroy()
        {
            try { Unregister(); }
            finally { base.OnDestroy(); }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 注册配方（运行时添加）
        /// </summary>
        public void RegisterRecipe(CraftingRecipeSO recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.RecipeId)) return;
            if (_recipeLookup.ContainsKey(recipe.RecipeId)) return;

            _allRecipes.Add(recipe);
            _recipeLookup[recipe.RecipeId] = recipe;

            // 更新分类缓存
            if (!_categoryCache.TryGetValue(recipe.Category, out var list))
            {
                list = new List<CraftingRecipeSO>();
                _categoryCache[recipe.Category] = list;
            }
            list.Add(recipe);
        }

        /// <summary>
        /// 检查配方是否已解锁
        /// </summary>
        public bool IsUnlocked(CraftingRecipeSO recipe)
        {
            if (recipe == null) return false;
            return _unlockedRecipes.Contains(recipe.RecipeId);
        }

        /// <summary>
        /// 检查配方是否已解锁（通过ID）
        /// </summary>
        public bool IsUnlocked(string recipeId)
        {
            return _unlockedRecipes.Contains(recipeId);
        }

        /// <summary>
        /// 尝试解锁配方
        /// </summary>
        public bool TryUnlockRecipe(CraftingRecipeSO recipe)
        {
            if (_isMutating || recipe == null || IsUnlocked(recipe) || !CheckUnlockCondition(recipe)) return false;
            RegisterRecipe(recipe);
            if (GetRecipe(recipe.RecipeId) != recipe) return false;
            _unlockedRecipes.Add(recipe.RecipeId);
            PublishCrafting(CraftingEventArgs.RecipeUnlocked(recipe));
            return true;
        }

        /// <summary>
        /// 通过物品学习配方
        /// </summary>
        public bool LearnRecipeFromItem(Inventory.InventoryItemSO learnItem)
        {
            if (_isMutating || learnItem == null) return false;
            var inventory = ResolveInventory();
            if (inventory == null || inventory.GetItemCount(learnItem) <= 0) return false;
            foreach (var recipe in _allRecipes)
            {
                if (recipe == null || recipe.UnlockType != RecipeUnlockType.Item || recipe.UnlockItem != learnItem
                    || IsUnlocked(recipe)) continue;
                _isMutating = true;
                try
                {
                    if (!inventory.TryExchangeItems(
                        new[] { new Inventory.InventoryItemAmount(learnItem, 1) }, null, !_dependenciesConfigured,
                        () => _unlockedRecipes.Add(recipe.RecipeId))) return false;
                    PublishCrafting(CraftingEventArgs.RecipeUnlocked(recipe));
                    return true;
                }
                finally { _isMutating = false; }
            }
            return false;
        }

        /// <summary>
        /// 检查是否可以合成
        /// </summary>
        public CraftingResult CanCraft(CraftingRecipeSO recipe, string currentWorkbench = null, int batchCount = 1)
        {
            if (batchCount <= 0) return CraftingResult.InvalidRequest;
            if (recipe == null || !IsUnlocked(recipe) || GetRecipe(recipe.RecipeId) != recipe)
                return CraftingResult.RecipeLocked;
            if (!CraftingBatchPlan.TryCreate(recipe, batchCount, out var plan)) return CraftingResult.InvalidRecipe;
            if (!string.IsNullOrEmpty(recipe.RequiredWorkbench) && recipe.RequiredWorkbench != currentWorkbench)
                return CraftingResult.WrongWorkbench;
            if (recipe.RequiredSkillLevel > 0 && !string.IsNullOrEmpty(recipe.SkillId)
                && GetSkillLevel(recipe.SkillId) < recipe.RequiredSkillLevel)
                return CraftingResult.RecipeLocked;
            if (!CanAwardExperience(recipe.SkillId, plan.Experience)) return CraftingResult.InvalidRequest;
            var inventory = ResolveInventory();
            if (inventory == null) return CraftingResult.InventoryUnavailable;
            foreach (var item in plan.Required)
            {
                if (inventory.GetItemData(item.Item.Id) != item.Item) return CraftingResult.InvalidRecipe;
                if (inventory.GetItemCount(item.Item) < item.Amount) return CraftingResult.InsufficientMaterials;
            }
            foreach (var item in plan.MaximumOutputs)
            {
                if (inventory.GetItemData(item.Item.Id) != item.Item) return CraftingResult.InvalidRecipe;
            }
            return inventory.CanExchangeItems(plan.Consumed, plan.MaximumOutputs)
                ? CraftingResult.Success : CraftingResult.InventoryFull;
        }

        /// <summary>
        /// 开始合成
        /// </summary>
        public CraftingResult StartCraft(CraftingRecipeSO recipe, string currentWorkbench = null, int batchCount = 1)
        {
            return StartCraft(recipe, currentWorkbench, batchCount, Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// 取消正在进行的合成
        /// </summary>
        public bool CancelCraft(int progressIndex)
        {
            return progressIndex >= 0 && progressIndex < _activeProgress.Count
                && TryCancelCraft(_activeProgress[progressIndex].ProgressId, out _);
        }

        /// <summary>
        /// 获取技能等级
        /// </summary>
        public int GetSkillLevel(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0;
            return _skillData.TryGetValue(skillId, out var data) ? data.Level : 1;
        }

        /// <summary>
        /// 获取技能数据
        /// </summary>
        public CraftingSkillData GetSkillData(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;

            if (!_skillData.TryGetValue(skillId, out var data))
            {
                data = new CraftingSkillData { SkillId = skillId, Level = 1 };
                _skillData[skillId] = data;
            }
            return data;
        }

        /// <summary>
        /// 获取配方
        /// </summary>
        public CraftingRecipeSO GetRecipe(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) return null;
            _recipeLookup.TryGetValue(recipeId, out var recipe);
            return recipe;
        }

        /// <summary>
        /// 获取所有配方
        /// </summary>
        public IReadOnlyList<CraftingRecipeSO> GetAllRecipes() => _allRecipes;

        /// <summary>
        /// 获取已解锁配方
        /// </summary>
        public void GetUnlockedRecipes(List<CraftingRecipeSO> results)
        {
            results.Clear();
            foreach (var recipe in _allRecipes)
            {
                if (recipe != null && IsUnlocked(recipe))
                {
                    results.Add(recipe);
                }
            }
        }

        /// <summary>
        /// 获取分类配方
        /// </summary>
        public void GetRecipesByCategory(RecipeCategory category, List<CraftingRecipeSO> results)
        {
            results.Clear();
            if (_categoryCache.TryGetValue(category, out var cached))
            {
                results.AddRange(cached);
            }
        }

        /// <summary>
        /// 获取进行中的合成列表
        /// </summary>
        public IReadOnlyList<CraftingProgress> GetActiveProgress() => _activeProgress.ConvertAll(CloneProgress);

        /// <summary>
        /// 获取配方书列表
        /// </summary>
        public IReadOnlyList<RecipeBookSO> GetRecipeBooks() => _recipeBooks;

        /// <summary>
        /// 强制解锁配方（调试用）
        /// </summary>
        public void ForceUnlock(CraftingRecipeSO recipe)
        {
            if (recipe == null || _isMutating) return;
            RegisterRecipe(recipe);
            if (GetRecipe(recipe.RecipeId) == recipe) _unlockedRecipes.Add(recipe.RecipeId);
        }

        #endregion

        #region Internal

        private void BuildCaches()
        {
            _recipeLookup.Clear();
            _categoryCache.Clear();

            foreach (var recipe in _allRecipes)
            {
                if (recipe == null) continue;

                _recipeLookup[recipe.RecipeId] = recipe;

                if (!_categoryCache.TryGetValue(recipe.Category, out var list))
                {
                    list = new List<CraftingRecipeSO>();
                    _categoryCache[recipe.Category] = list;
                }
                list.Add(recipe);
            }
        }

        private bool CheckUnlockCondition(CraftingRecipeSO recipe)
        {
            if (recipe.UnlockType == RecipeUnlockType.Default) return true;
            if (recipe.UnlockType == RecipeUnlockType.Item) return false;
            if (recipe.UnlockType == RecipeUnlockType.Custom && !string.IsNullOrEmpty(recipe.CustomUnlockId)
                && _customUnlockChecks.TryGetValue(recipe.CustomUnlockId, out var check))
                return check != null && check();
            // External conditions must be proven by the consumer; missing providers fail closed.
            return Enum.IsDefined(typeof(RecipeUnlockType), recipe.UnlockType) && _unlockCondition?.Invoke(recipe) == true;
        }

        private void CheckCompletedCrafting()
        {
            if (_isMutating) return;
            for (var i = _activeProgress.Count - 1; i >= 0; i--)
            {
                var progress = _activeProgress[i];
                if (!progress.IsComplete) continue;
                _isMutating = true;
                try { TryComplete(progress); }
                finally { _isMutating = false; }
            }
        }

        // 自定义解锁检查
        private static readonly Dictionary<string, Func<bool>> _customUnlockChecks =
            new Dictionary<string, Func<bool>>();

        public static void RegisterCustomUnlockCheck(string id, Func<bool> check)
        {
            _customUnlockChecks[id] = check;
        }

        public static void UnregisterCustomUnlockCheck(string id)
        {
            _customUnlockChecks.Remove(id);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[Crafting] {message}");
            }
        }

        #endregion
    }

    #region Save Data

    [Serializable]
    public class CraftingSaveData
    {
        public List<string> UnlockedRecipes;
        public List<CraftingProgress> ActiveProgress;
        public Dictionary<string, CraftingSkillData> SkillData;
        public List<CraftingReceipt> Receipts;
    }

    #endregion
}