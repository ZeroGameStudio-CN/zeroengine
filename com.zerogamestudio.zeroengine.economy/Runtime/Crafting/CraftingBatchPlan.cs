using System;
using System.Collections.Generic;
using ZeroEngine.Inventory;

namespace ZeroEngine.Crafting
{
    internal sealed class CraftingBatchPlan
    {
        internal List<InventoryItemAmount> Required;
        internal List<InventoryItemAmount> Consumed;
        internal List<InventoryItemAmount> MaximumOutputs;
        internal int Experience;
        internal long Duration;

        internal static bool TryIngredients(CraftingRecipeSO recipe, int batchCount,
            out List<InventoryItemAmount> required, out List<InventoryItemAmount> consumed)
        {
            required = new List<InventoryItemAmount>();
            consumed = new List<InventoryItemAmount>();
            if (recipe == null || batchCount <= 0 || recipe.Ingredients == null) return false;
            var requiredTotals = new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            var consumedTotals = new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient == null || ingredient.Amount <= 0) return false;
                // A tool is reused across a sequential batch, while consumed rows multiply.
                var count = (long)ingredient.Amount * (ingredient.IsConsumed ? batchCount : 1);
                if (!Add(requiredTotals, ingredient.Item, count)) return false;
                if (ingredient.IsConsumed && !Add(consumedTotals, ingredient.Item, count)) return false;
            }
            required.AddRange(requiredTotals.Values);
            consumed.AddRange(consumedTotals.Values);
            return true;
        }

        internal static bool TryCreate(CraftingRecipeSO recipe, int batchCount, out CraftingBatchPlan plan)
        {
            plan = null;
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.RecipeId)
                || !TryIngredients(recipe, batchCount, out var required, out var consumed)
                || !Probability(recipe.SuccessRate) || !Probability(recipe.GreatSuccessRate)
                || float.IsNaN(recipe.CraftTime) || float.IsInfinity(recipe.CraftTime) || recipe.CraftTime < 0
                || recipe.Outputs == null || recipe.Outputs.Count == 0 || recipe.ExpReward < 0)
                return false;
            var duration = Math.Ceiling((double)recipe.CraftTime * batchCount);
            var experience = (long)recipe.ExpReward * batchCount;
            if (duration > long.MaxValue - DateTimeOffset.UtcNow.ToUnixTimeSeconds() || experience > int.MaxValue)
                return false;
            var outputs = new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            foreach (var output in recipe.Outputs)
            {
                if (output == null || output.BaseAmount <= 0 || output.BonusAmount < 0 || !Probability(output.Probability))
                    return false;
                if (!Add(definitions, output.Item, ((long)output.BaseAmount + output.BonusAmount) * batchCount))
                    return false;
                if (recipe.SuccessRate > 0 && output.Probability > 0
                    && !Add(outputs, output.Item,
                        ((long)output.BaseAmount + (recipe.GreatSuccessRate > 0 ? output.BonusAmount : 0)) * batchCount))
                    return false;
            }
            foreach (var input in required)
            {
                if (definitions.TryGetValue(input.Item.Id, out var output) && input.Item != output.Item)
                    return false;
            }
            plan = new CraftingBatchPlan
            {
                Required = required,
                Consumed = consumed,
                MaximumOutputs = new List<InventoryItemAmount>(outputs.Values),
                Experience = (int)experience,
                Duration = (long)duration
            };
            return true;
        }

        internal CraftingProgress Prepare(CraftingRecipeSO recipe, int batchCount, string requestId)
        {
            var success = Roll(recipe.SuccessRate);
            var great = success && Roll(recipe.GreatSuccessRate);
            var result = success ? (great ? CraftingResult.GreatSuccess : CraftingResult.Success)
                : (recipe.KeepMaterialsOnFail ? CraftingResult.FailedKeepMaterials : CraftingResult.FailedLoseMaterials);
            var outputs = new Dictionary<string, InventoryItemAmount>(StringComparer.Ordinal);
            if (success)
            {
                foreach (var output in recipe.Outputs)
                {
                    if (Roll(output.Probability))
                        Add(outputs, output.Item, ((long)output.BaseAmount + (great ? output.BonusAmount : 0)) * batchCount);
                }
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new CraftingProgress
            {
                ProgressId = requestId,
                RecipeId = recipe.RecipeId,
                BatchCount = batchCount,
                StartTime = now,
                EndTime = now + Duration,
                OutcomePrepared = true,
                Result = result,
                LastResult = CraftingResult.Success,
                ConsumedItems = ToSaved(Consumed),
                OutputItems = ToSaved(new List<InventoryItemAmount>(outputs.Values)),
                SkillId = recipe.SkillId,
                ExperienceReward = success ? Experience : 0
            };
        }

        internal static List<CraftingItemAmount> ToSaved(IReadOnlyList<InventoryItemAmount> items)
        {
            var saved = new List<CraftingItemAmount>(items.Count);
            foreach (var item in items)
                saved.Add(new CraftingItemAmount { ItemId = item.Item.Id, Amount = item.Amount });
            return saved;
        }

        private static bool Add(Dictionary<string, InventoryItemAmount> totals, InventoryItemSO item, long amount)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.MaxStack <= 0 || amount <= 0) return false;
            if (totals.TryGetValue(item.Id, out var old))
            {
                if (old.Item != item) return false;
                amount += old.Amount;
            }
            if (amount > int.MaxValue) return false;
            totals[item.Id] = new InventoryItemAmount(item, (int)amount);
            return true;
        }

        private static bool Probability(float value) => !float.IsNaN(value) && value >= 0 && value <= 1;
        private static bool Roll(float rate) => rate >= 1 || (rate > 0 && UnityEngine.Random.value < rate);
    }
}
