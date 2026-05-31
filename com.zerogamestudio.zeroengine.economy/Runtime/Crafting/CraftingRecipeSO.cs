using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Economy;

namespace ZeroEngine.Crafting
{
    /// <summary>
    /// 合成配方 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "New Recipe", menuName = "ZeroEngine/Crafting/Recipe")]
    public class CraftingRecipeSO : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("配方唯一ID")]
        public string RecipeId;

        [Tooltip("显示名称")]
        public string DisplayName;

        [Tooltip("配方描述")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("配方图标")]
        public Sprite Icon;

        [Tooltip("配方分类")]
        public RecipeCategory Category = RecipeCategory.Other;

        [Header("材料")]
        [Tooltip("所需材料")]
        public List<RecipeIngredient> Ingredients = new List<RecipeIngredient>();

        [Header("产出")]
        [Tooltip("产出物品")]
        public List<RecipeOutput> Outputs = new List<RecipeOutput>();

        [Header("合成设置")]
        [Tooltip("合成时间（秒，0=即时）")]
        public float CraftTime = 0f;

        [Tooltip("成功率（1.0 = 100%）")]
        [Range(0f, 1f)]
        public float SuccessRate = 1f;

        [Tooltip("大成功率（1.0 = 100%）")]
        [Range(0f, 1f)]
        public float GreatSuccessRate = 0f;

        [Tooltip("失败时保留材料")]
        public bool KeepMaterialsOnFail = false;

        [Tooltip("需要的工作台ID（空=无需工作台）")]
        public string RequiredWorkbench;

        [Header("解锁条件")]
        [Tooltip("解锁方式")]
        public RecipeUnlockType UnlockType = RecipeUnlockType.Default;

        [Tooltip("解锁等级（UnlockType=Level时使用）")]
        public int UnlockLevel;

        [Tooltip("解锁任务ID（UnlockType=Quest时使用）")]
        public string UnlockQuestId;

        [Tooltip("解锁成就ID（UnlockType=Achievement时使用）")]
        public string UnlockAchievementId;

        [Tooltip("解锁物品（UnlockType=Item时使用，学习后消耗）")]
        public Inventory.InventoryItemSO UnlockItem;

        [Tooltip("解锁好感度NPC ID")]
        public string UnlockRelationshipNpcId;

        [Tooltip("解锁好感度等级")]
        public int UnlockRelationshipLevel;

        [Tooltip("自定义解锁条件ID")]
        public string CustomUnlockId;

        [Header("技能经验")]
        [Tooltip("关联技能ID")]
        public string SkillId;

        [Tooltip("获得经验")]
        public int ExpReward = 10;

        [Tooltip("需要技能等级")]
        public int RequiredSkillLevel = 0;

        [Header("高级设置")]
        [Tooltip("排序优先级")]
        public int SortOrder;

        [Tooltip("标签")]
        public List<string> Tags = new List<string>();

        /// <summary>
        /// 检查材料是否充足
        /// </summary>
        public bool CheckIngredients(IInventoryProvider inventory, int batchCount = 1)
        {
            if (inventory == null) return false;

            for (int i = 0; i < Ingredients.Count; i++)
            {
                var ingredient = Ingredients[i];
                if (ingredient.Item == null) continue;

                int required = ingredient.Amount * batchCount;
                if (inventory.GetItemCount(ingredient.Item) < required)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 消耗材料
        /// </summary>
        public void ConsumeIngredients(IInventoryProvider inventory, int batchCount = 1)
        {
            if (inventory == null) return;

            for (int i = 0; i < Ingredients.Count; i++)
            {
                var ingredient = Ingredients[i];
                if (ingredient.Item == null || !ingredient.IsConsumed) continue;

                int amount = ingredient.Amount * batchCount;
                inventory.RemoveItem(ingredient.Item, amount);
            }
        }

        /// <summary>
        /// 获取主产出物品
        /// </summary>
        public Inventory.InventoryItemSO GetMainOutput()
        {
            if (Outputs == null || Outputs.Count == 0) return null;
            return Outputs[0].Item;
        }

        /// <summary>
        /// 计算最大可制作数量
        /// </summary>
        public int CalculateMaxCraftCount(IInventoryProvider inventory)
        {
            if (inventory == null) return 0;

            int maxCount = int.MaxValue;

            for (int i = 0; i < Ingredients.Count; i++)
            {
                var ingredient = Ingredients[i];
                if (ingredient.Item == null || !ingredient.IsConsumed) continue;

                int have = inventory.GetItemCount(ingredient.Item);
                int canMake = have / ingredient.Amount;
                maxCount = Mathf.Min(maxCount, canMake);
            }

            return maxCount == int.MaxValue ? 0 : maxCount;
        }

        /// <summary>
        /// 获取材料描述列表
        /// </summary>
        public void GetIngredientDescriptions(List<string> results)
        {
            results.Clear();
            for (int i = 0; i < Ingredients.Count; i++)
            {
                var ingredient = Ingredients[i];
                if (ingredient.Item != null)
                {
                    string consumed = ingredient.IsConsumed ? "" : " (不消耗)";
                    results.Add($"{ingredient.Item.ItemName} x{ingredient.Amount}{consumed}");
                }
            }
        }

        /// <summary>
        /// 获取产出描述列表
        /// </summary>
        public void GetOutputDescriptions(List<string> results)
        {
            results.Clear();
            for (int i = 0; i < Outputs.Count; i++)
            {
                var output = Outputs[i];
                if (output.Item != null)
                {
                    string probStr = output.Probability < 1f ? $" ({output.Probability * 100:F0}%)" : "";
                    string bonusStr = output.BonusAmount > 0 ? $" (+{output.BonusAmount})" : "";
                    results.Add($"{output.Item.ItemName} x{output.BaseAmount}{bonusStr}{probStr}");
                }
            }
        }

        /// <summary>
        /// 是否有指定标签
        /// </summary>
        public bool HasTag(string tag)
        {
            if (Tags == null || string.IsNullOrEmpty(tag)) return false;
            for (int i = 0; i < Tags.Count; i++)
            {
                if (Tags[i] == tag) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(RecipeId))
            {
                RecipeId = name;
            }
            if (string.IsNullOrEmpty(DisplayName))
            {
                DisplayName = name;
            }
        }
#endif
    }

    /// <summary>
    /// 配方书（配方集合）
    /// </summary>
    [CreateAssetMenu(fileName = "New Recipe Book", menuName = "ZeroEngine/Crafting/Recipe Book")]
    public class RecipeBookSO : ScriptableObject
    {
        [Tooltip("配方书ID")]
        public string BookId;

        [Tooltip("显示名称")]
        public string DisplayName;

        [Tooltip("描述")]
        [TextArea(2, 3)]
        public string Description;

        [Tooltip("图标")]
        public Sprite Icon;

        [Tooltip("包含的配方")]
        public List<CraftingRecipeSO> Recipes = new List<CraftingRecipeSO>();

        [Tooltip("关联工作台ID")]
        public string WorkbenchId;

        [Tooltip("排序优先级")]
        public int SortOrder;
    }
}
