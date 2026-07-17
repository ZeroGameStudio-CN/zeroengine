using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionCraftingTransactionService
    {
        public static bool TryCraftRecipe(
            ExtractionProfileSaveData profile,
            ExtractionCraftingRecipeDefinition recipe,
            IExtractionItemCatalog itemCatalog,
            List<ExtractionInventoryContainerType> ingredientSourceContainers,
            ExtractionInventoryContainerType targetContainer,
            string outputItemInstanceId,
            int x,
            int y,
            bool rotated)
        {
            if (!CanCraftRecipe(profile, recipe, itemCatalog, outputItemInstanceId, out var outputDefinition))
                return false;
            if (!TryConvertIngredients(recipe.Ingredients, out var costs)) return false;
            if (!TryGetGrid(profile, targetContainer, out var targetGrid)) return false;

            var outputItem = new ExtractionItemInstance(
                outputItemInstanceId,
                recipe.OutputItemDefinitionId,
                recipe.OutputQuantity);
            if (!CanPlace(targetGrid, outputItem, outputDefinition, x, y, rotated)) return false;
            if (!ExtractionItemCostService.HasEnoughItems(profile, costs, ingredientSourceContainers)) return false;
            if (!ExtractionItemCostService.TryConsumeCosts(profile, costs, ingredientSourceContainers, out var costReceipt)) return false;

            if (!profile.Items.Register(outputItem))
            {
                ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
                return false;
            }
            if (!targetGrid.TryPlace(outputItem, outputDefinition, x, y, rotated))
            {
                profile.Items.TryRemove(outputItemInstanceId);
                ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
                return false;
            }

            if (profile.Ownership.Register(outputItemInstanceId, targetContainer)) return true;

            targetGrid.TryRemove(outputItemInstanceId);
            profile.Items.TryRemove(outputItemInstanceId);
            ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
            return false;
        }

        private static bool CanCraftRecipe(
            ExtractionProfileSaveData profile,
            ExtractionCraftingRecipeDefinition recipe,
            IExtractionItemCatalog itemCatalog,
            string outputItemInstanceId,
            out ExtractionItemDefinition outputDefinition)
        {
            outputDefinition = null;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || recipe == null || !recipe.IsValid || itemCatalog == null) return false;
            if (string.IsNullOrEmpty(outputItemInstanceId)) return false;
            profile.EnsureInitialized();
            if (profile.Facilities.GetLevel(ExtractionFacilityType.Crafting) < recipe.RequiredCraftingLevel)
                return false;
            // M2 SB2.2：照 RequiredCraftingLevel 同款纵深防御——不信任调用方只会拿"可用列表"里的
            // recipeId 来调用，蓝图门在执行层再兜底一次(查询层 TryGetAvailableRecipes 已经过滤过)。
            if (!string.IsNullOrEmpty(recipe.RequiredBlueprintId)
                && !ExtractionBlueprintService.IsBlueprintUnlocked(profile, recipe.RequiredBlueprintId))
            {
                return false;
            }
            if (profile.Items.TryGet(outputItemInstanceId, out _)) return false;
            if (profile.Ownership.TryGetContainer(outputItemInstanceId, out _)) return false;
            if (!itemCatalog.TryGetItemDefinition(recipe.OutputItemDefinitionId, out outputDefinition)) return false;

            foreach (var ingredient in recipe.Ingredients)
            {
                if (!itemCatalog.TryGetItemDefinition(ingredient.ItemDefinitionId, out _)) return false;
            }

            return true;
        }

        private static bool TryConvertIngredients(
            List<ExtractionCraftingIngredient> ingredients,
            out List<ExtractionItemCost> costs)
        {
            costs = new List<ExtractionItemCost>();
            if (ingredients == null || ingredients.Count == 0) return false;

            foreach (var ingredient in ingredients)
            {
                if (ingredient == null || !ingredient.IsValid) return false;
                costs.Add(new ExtractionItemCost(ingredient.ItemDefinitionId, ingredient.Quantity));
            }

            return true;
        }

        private static bool CanPlace(
            ExtractionItemGrid grid,
            ExtractionItemInstance item,
            ExtractionItemDefinition definition,
            int x,
            int y,
            bool rotated)
        {
            if (!grid.TryPlace(item, definition, x, y, rotated)) return false;
            grid.TryRemove(item.InstanceId);
            return true;
        }

        private static bool TryGetGrid(
            ExtractionProfileSaveData profile,
            ExtractionInventoryContainerType container,
            out ExtractionItemGrid grid)
        {
            grid = container switch
            {
                ExtractionInventoryContainerType.Stash => profile.Stash,
                ExtractionInventoryContainerType.Loadout => profile.Loadout,
                ExtractionInventoryContainerType.SecureContainer => profile.SecureContainer,
                ExtractionInventoryContainerType.Holding => profile.RecoveryHolding,
                _ => null
            };

            return grid != null;
        }
    }
}
