using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionCraftingRecipeService
    {
        public static bool TryGetAvailableRecipes(
            ExtractionProfileSaveData profile,
            List<ExtractionCraftingRecipeDefinition> recipes,
            IExtractionItemCatalog itemCatalog,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionCraftingRecipeDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || recipes == null || itemCatalog == null) return false;
            profile.EnsureInitialized();

            int craftingLevel = profile.Facilities.GetLevel(ExtractionFacilityType.Crafting);
            foreach (var recipe in recipes)
            {
                if (recipe == null || !recipe.IsValid) continue;
                if (craftingLevel < recipe.RequiredCraftingLevel) continue;
                if (!HasCatalogItems(recipe, itemCatalog)) continue;
                if (!MeetsMetaRequirements(recipe, metaProgress)) continue;
                if (!HasRequiredBlueprint(profile, recipe)) continue;

                results.Add(recipe);
            }

            return results.Count > 0;
        }

        private static bool HasCatalogItems(
            ExtractionCraftingRecipeDefinition recipe,
            IExtractionItemCatalog itemCatalog)
        {
            if (!itemCatalog.TryGetItemDefinition(recipe.OutputItemDefinitionId, out _)) return false;

            foreach (var ingredient in recipe.Ingredients)
            {
                if (!itemCatalog.TryGetItemDefinition(ingredient.ItemDefinitionId, out _)) return false;
            }

            return true;
        }

        private static bool MeetsMetaRequirements(
            ExtractionCraftingRecipeDefinition recipe,
            IExtractionMetaProgressProvider metaProgress)
        {
            metaProgress ??= new ExtractionNoOpMetaProgressProvider();

            if (!string.IsNullOrEmpty(recipe.RequiredSharedUnlockId))
            {
                if (recipe.RequiredSharedUnlockLevel <= 0) return false;
                if (metaProgress.GetSharedUnlockLevel(recipe.RequiredSharedUnlockId) < recipe.RequiredSharedUnlockLevel)
                    return false;
            }

            if (!string.IsNullOrEmpty(recipe.RequiredSharedTalentId))
            {
                if (!metaProgress.HasSharedTalent(recipe.RequiredSharedTalentId)) return false;
            }

            return true;
        }

        // M2 SB2.2：配方蓝图门，照 RequiredSharedUnlockId/RequiredSharedTalentId 同款"空=不限制"惯例。
        private static bool HasRequiredBlueprint(
            ExtractionProfileSaveData profile,
            ExtractionCraftingRecipeDefinition recipe)
        {
            if (string.IsNullOrEmpty(recipe.RequiredBlueprintId)) return true;
            return ExtractionBlueprintService.IsBlueprintUnlocked(profile, recipe.RequiredBlueprintId);
        }
    }
}
