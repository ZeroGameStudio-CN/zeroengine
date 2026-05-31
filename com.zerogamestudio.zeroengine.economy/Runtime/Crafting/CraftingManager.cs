using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Economy;
using ZeroEngine.Save;

namespace ZeroEngine.Crafting
{
    /// <summary>
    /// 合成系统管理器
    /// 负责配方解锁、合成逻辑和技能经验
    /// </summary>
    public class CraftingManager : MonoSingleton<CraftingManager>, ISaveable
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
        private readonly List<RecipeOutput> _tempOutputList = new List<RecipeOutput>(8);
        private readonly List<CraftingRecipeSO> _tempRecipeList = new List<CraftingRecipeSO>(32);

        private IInventoryProvider _inventoryProvider;
        private IExternalSystemProvider _externalSystemProvider;
        private ICurrencyProvider _currencyProvider;
        private float _nextCompletionCheckTime;
        private const float CompletionCheckInterval = 0.5f;

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
            SaveSlotManager.Instance?.Register(this);
        }

        public void Unregister()
        {
            SaveSlotManager.Instance?.Unregister(this);
        }

        public object ExportSaveData()
        {
            return new CraftingSaveData
            {
                UnlockedRecipes = new List<string>(_unlockedRecipes),
                ActiveProgress = new List<CraftingProgress>(_activeProgress),
                SkillData = new Dictionary<string, CraftingSkillData>(_skillData)
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is not CraftingSaveData saveData) return;

            _unlockedRecipes.Clear();
            if (saveData.UnlockedRecipes != null)
            {
                foreach (var id in saveData.UnlockedRecipes)
                {
                    _unlockedRecipes.Add(id);
                }
            }

            _activeProgress.Clear();
            if (saveData.ActiveProgress != null)
            {
                _activeProgress.AddRange(saveData.ActiveProgress);
            }

            _skillData.Clear();
            if (saveData.SkillData != null)
            {
                foreach (var kvp in saveData.SkillData)
                {
                    _skillData[kvp.Key] = kvp.Value;
                }
            }
        }

        public void ResetToDefault()
        {
            _unlockedRecipes.Clear();
            _activeProgress.Clear();
            _skillData.Clear();

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
            ResolveDefaultProviders();
            Register();

            // 初始化默认解锁
            if (_unlockedRecipes.Count == 0)
            {
                ResetToDefault();
            }
        }

        private void Update()
        {
            if (_activeProgress.Count == 0 || Time.time < _nextCompletionCheckTime)
            {
                return;
            }

            _nextCompletionCheckTime = Time.time + CompletionCheckInterval;
            CheckCompletedCrafting();
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Public API

        public void SetInventoryProvider(IInventoryProvider provider)
        {
            _inventoryProvider = provider;
        }

        public void SetExternalSystemProvider(IExternalSystemProvider provider)
        {
            _externalSystemProvider = provider;
        }

        public void SetCurrencyProvider(ICurrencyProvider provider)
        {
            _currencyProvider = provider;
        }

        /// <summary>
        /// 注册配方（运行时添加）
        /// </summary>
        public void RegisterRecipe(CraftingRecipeSO recipe)
        {
            if (recipe == null) return;
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
            if (recipe == null) return false;
            if (_unlockedRecipes.Contains(recipe.RecipeId)) return false;

            // 检查解锁条件
            if (!CheckUnlockCondition(recipe))
            {
                Log($"配方解锁条件未满足: {recipe.DisplayName}");
                return false;
            }

            _unlockedRecipes.Add(recipe.RecipeId);
            RaiseCraftingEvent(CraftingEventArgs.RecipeUnlocked(recipe));
            Log($"配方解锁: {recipe.DisplayName}");

            return true;
        }

        /// <summary>
        /// 通过物品学习配方
        /// </summary>
        public bool LearnRecipeFromItem(Inventory.InventoryItemSO learnItem)
        {
            if (learnItem == null) return false;

            var inventory = GetInventoryProvider();
            if (inventory == null || inventory.GetItemCount(learnItem) <= 0)
            {
                return false;
            }

            // 查找可学习的配方
            foreach (var recipe in _allRecipes)
            {
                if (recipe.UnlockType == RecipeUnlockType.Item &&
                    recipe.UnlockItem == learnItem &&
                    !IsUnlocked(recipe))
                {
                    // 消耗学习物品
                    inventory.RemoveItem(learnItem, 1);

                    // 解锁配方
                    _unlockedRecipes.Add(recipe.RecipeId);
                    RaiseCraftingEvent(CraftingEventArgs.RecipeUnlocked(recipe));
                    Log($"学习配方: {recipe.DisplayName}");

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查是否可以合成
        /// </summary>
        public CraftingResult CanCraft(CraftingRecipeSO recipe, string currentWorkbench = null, int batchCount = 1)
        {
            if (recipe == null)
                return CraftingResult.RecipeLocked;

            // 检查解锁
            if (!IsUnlocked(recipe))
                return CraftingResult.RecipeLocked;

            // 检查工作台
            if (!string.IsNullOrEmpty(recipe.RequiredWorkbench) &&
                recipe.RequiredWorkbench != currentWorkbench)
            {
                return CraftingResult.WrongWorkbench;
            }

            // 检查技能等级
            if (recipe.RequiredSkillLevel > 0 && !string.IsNullOrEmpty(recipe.SkillId))
            {
                int level = GetSkillLevel(recipe.SkillId);
                if (level < recipe.RequiredSkillLevel)
                {
                    return CraftingResult.RecipeLocked;
                }
            }

            // 检查材料
            var inventory = GetInventoryProvider();
            if (!recipe.CheckIngredients(inventory, batchCount))
            {
                return CraftingResult.InsufficientMaterials;
            }

            return CraftingResult.Success;
        }

        /// <summary>
        /// 开始合成
        /// </summary>
        public CraftingResult StartCraft(CraftingRecipeSO recipe, string currentWorkbench = null, int batchCount = 1)
        {
            // 检查是否可以合成
            var canCraft = CanCraft(recipe, currentWorkbench, batchCount);
            if (canCraft != CraftingResult.Success)
            {
                return canCraft;
            }

            var inventory = GetInventoryProvider();

            // 消耗材料
            recipe.ConsumeIngredients(inventory, batchCount);

            // 即时合成
            if (recipe.CraftTime <= 0)
            {
                return CompleteCraft(recipe, batchCount);
            }

            // 延时合成
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var progress = new CraftingProgress
            {
                RecipeId = recipe.RecipeId,
                StartTime = now,
                EndTime = now + (long)(recipe.CraftTime * batchCount),
                BatchCount = batchCount
            };

            _activeProgress.Add(progress);
            RaiseCraftingEvent(CraftingEventArgs.Started(recipe, batchCount));
            Log($"开始合成: {recipe.DisplayName} x{batchCount}");

            return CraftingResult.Success;
        }

        /// <summary>
        /// 取消正在进行的合成
        /// </summary>
        public bool CancelCraft(int progressIndex)
        {
            if (progressIndex < 0 || progressIndex >= _activeProgress.Count)
                return false;

            var progress = _activeProgress[progressIndex];
            var recipe = GetRecipe(progress.RecipeId);

            // 返还材料（如果配置了）
            if (recipe != null && recipe.KeepMaterialsOnFail)
            {
                var inventory = GetInventoryProvider();
                if (inventory != null)
                {
                    for (int i = 0; i < recipe.Ingredients.Count; i++)
                    {
                        var ingredient = recipe.Ingredients[i];
                        if (ingredient.Item != null && ingredient.IsConsumed)
                        {
                            inventory.AddItem(ingredient.Item, ingredient.Amount * progress.BatchCount);
                        }
                    }
                }
            }

            _activeProgress.RemoveAt(progressIndex);
            Log($"取消合成: {recipe?.DisplayName}");

            return true;
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
        public IReadOnlyList<CraftingProgress> GetActiveProgress() => _activeProgress;

        /// <summary>
        /// 获取配方书列表
        /// </summary>
        public IReadOnlyList<RecipeBookSO> GetRecipeBooks() => _recipeBooks;

        /// <summary>
        /// 强制解锁配方（调试用）
        /// </summary>
        public void ForceUnlock(CraftingRecipeSO recipe)
        {
            if (recipe == null) return;
            _unlockedRecipes.Add(recipe.RecipeId);
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
            switch (recipe.UnlockType)
            {
                case RecipeUnlockType.Default:
                    return true;

                case RecipeUnlockType.Level:
                    return (GetExternalSystemProvider()?.GetPlayerLevel() ?? 1) >= recipe.UnlockLevel;

                case RecipeUnlockType.Quest:
                    return GetExternalSystemProvider()?.IsQuestCompleted(recipe.UnlockQuestId) ?? false;

                case RecipeUnlockType.Achievement:
#if ZEROENGINE_NARRATIVE
                    var achievementMgr = Achievement.AchievementManager.Instance;
                    if (achievementMgr != null && !string.IsNullOrEmpty(recipe.UnlockAchievementId))
                    {
                        return achievementMgr.IsUnlocked(recipe.UnlockAchievementId);
                    }
#endif
                    return true;

                case RecipeUnlockType.Item:
                    // 物品解锁需要通过 LearnRecipeFromItem 方法
                    return false;

                case RecipeUnlockType.Relationship:
                    return (GetExternalSystemProvider()?.GetRelationshipLevel(recipe.UnlockRelationshipNpcId) ?? 0)
                        >= recipe.UnlockRelationshipLevel;

                case RecipeUnlockType.Custom:
                    if (_customUnlockChecks.TryGetValue(recipe.CustomUnlockId, out var check))
                    {
                        return check();
                    }
                    return true;

                default:
                    return true;
            }
        }

        private CraftingResult CompleteCraft(CraftingRecipeSO recipe, int batchCount)
        {
            _tempOutputList.Clear();

            // 计算成功/失败
            bool success = UnityEngine.Random.value <= recipe.SuccessRate;
            bool greatSuccess = success && UnityEngine.Random.value <= recipe.GreatSuccessRate;

            CraftingResult result;

            if (!success)
            {
                result = recipe.KeepMaterialsOnFail
                    ? CraftingResult.FailedKeepMaterials
                    : CraftingResult.FailedLoseMaterials;

                // 返还材料
                if (recipe.KeepMaterialsOnFail)
                {
                    var inventory = GetInventoryProvider();
                    if (inventory != null)
                    {
                        for (int i = 0; i < recipe.Ingredients.Count; i++)
                        {
                            var ingredient = recipe.Ingredients[i];
                            if (ingredient.Item != null && ingredient.IsConsumed)
                            {
                                inventory.AddItem(ingredient.Item, ingredient.Amount * batchCount);
                            }
                        }
                    }
                }

                RaiseCraftingEvent(CraftingEventArgs.Failed(recipe, result));
                Log($"合成失败: {recipe.DisplayName}");
                return result;
            }

            result = greatSuccess ? CraftingResult.GreatSuccess : CraftingResult.Success;

            // 发放产出
            var inventoryMgr = GetInventoryProvider();
            if (inventoryMgr != null)
            {
                for (int i = 0; i < recipe.Outputs.Count; i++)
                {
                    var output = recipe.Outputs[i];
                    if (output.Item == null) continue;

                    // 概率产出
                    if (output.Probability < 1f && UnityEngine.Random.value > output.Probability)
                    {
                        continue;
                    }

                    int amount = output.BaseAmount * batchCount;
                    if (greatSuccess)
                    {
                        amount += output.BonusAmount * batchCount;
                    }

                    inventoryMgr.AddItem(output.Item, amount);
                    _tempOutputList.Add(output);
                }
            }

            // 添加技能经验
            if (!string.IsNullOrEmpty(recipe.SkillId) && recipe.ExpReward > 0)
            {
                var skill = GetSkillData(recipe.SkillId);
                int totalExp = recipe.ExpReward * batchCount;
                if (skill.AddExperience(totalExp, out int levelUps))
                {
                    Log($"技能升级: {recipe.SkillId} -> Lv.{skill.Level}");
                }
            }

            // 触发成就事件
#if ZEROENGINE_NARRATIVE
            var achievementMgr = Achievement.AchievementManager.Instance;
            achievementMgr?.TriggerEvent("Craft", recipe.RecipeId);
            achievementMgr?.TriggerEvent("CraftCategory", recipe.Category.ToString());
#endif

            RaiseCraftingEvent(CraftingEventArgs.Completed(recipe, result, _tempOutputList));
            Log($"合成完成: {recipe.DisplayName} x{batchCount} ({result})");

            return result;
        }

        private void CheckCompletedCrafting()
        {
            for (int i = _activeProgress.Count - 1; i >= 0; i--)
            {
                var progress = _activeProgress[i];
                if (progress.IsComplete)
                {
                    var recipe = GetRecipe(progress.RecipeId);
                    if (recipe != null)
                    {
                        CompleteCraft(recipe, progress.BatchCount);
                    }
                    _activeProgress.RemoveAt(i);
                }
            }
        }

        private void ResolveDefaultProviders()
        {
            _inventoryProvider ??= EconomyProviderResolver.FindInventoryProvider();
            _currencyProvider ??= EconomyProviderResolver.FindCurrencyProvider();
            _externalSystemProvider ??= EconomyProviderResolver.DefaultExternalSystemProvider();
        }

        private IInventoryProvider GetInventoryProvider()
        {
            _inventoryProvider ??= EconomyProviderResolver.FindInventoryProvider();
            return _inventoryProvider;
        }

        private IExternalSystemProvider GetExternalSystemProvider()
        {
            _externalSystemProvider ??= EconomyProviderResolver.DefaultExternalSystemProvider();
            return _externalSystemProvider;
        }

        private ICurrencyProvider GetCurrencyProvider()
        {
            _currencyProvider ??= EconomyProviderResolver.FindCurrencyProvider();
            return _currencyProvider;
        }

        private void RaiseCraftingEvent(CraftingEventArgs args)
        {
            OnCraftingEvent?.Invoke(args);
            switch (args.EventType)
            {
                case CraftingEventType.Completed:
                    EventManager.Trigger(EconomyEvents.CraftingCompleted, args);
                    break;
                case CraftingEventType.RecipeUnlocked:
                    EventManager.Trigger(EconomyEvents.RecipeUnlocked, args);
                    break;
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
    }

    #endregion
}
