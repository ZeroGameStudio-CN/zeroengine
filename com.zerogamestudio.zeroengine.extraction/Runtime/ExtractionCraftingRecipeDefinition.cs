using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionCraftingIngredient
    {
        public string ItemDefinitionId;
        public int Quantity;

        public bool IsValid => !string.IsNullOrEmpty(ItemDefinitionId) && Quantity > 0;

        public ExtractionCraftingIngredient(string itemDefinitionId, int quantity)
        {
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionCraftingRecipeDefinition
    {
        public string RecipeId;
        public List<ExtractionCraftingIngredient> Ingredients = new();
        public string OutputItemDefinitionId;
        public int OutputQuantity;
        public int RequiredCraftingLevel;
        public string RequiredSharedUnlockId;
        public int RequiredSharedUnlockLevel;
        public string RequiredSharedTalentId;

        // M2 SB2.2 新增（additive）：配方蓝图门。空/未设置=不需要蓝图，老配置零改动兼容；
        // dangling 引用交给 validator 拒绝（IsValid 这里没有 config 可查蓝图表是否真存在）。
        public string RequiredBlueprintId;

        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(RecipeId)) return false;
                if (string.IsNullOrEmpty(OutputItemDefinitionId) || OutputQuantity <= 0) return false;
                if (RequiredCraftingLevel <= 0) return false;
                if (Ingredients == null || Ingredients.Count == 0) return false;

                foreach (var ingredient in Ingredients)
                {
                    if (ingredient == null || !ingredient.IsValid) return false;
                }

                return true;
            }
        }

        public ExtractionCraftingRecipeDefinition(
            string recipeId,
            string outputItemDefinitionId,
            int outputQuantity,
            int requiredCraftingLevel)
        {
            RecipeId = recipeId;
            OutputItemDefinitionId = outputItemDefinitionId;
            OutputQuantity = outputQuantity;
            RequiredCraftingLevel = requiredCraftingLevel;
        }
    }
}
