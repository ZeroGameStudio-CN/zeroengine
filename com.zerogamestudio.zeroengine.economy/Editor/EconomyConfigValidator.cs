using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Crafting;
using ZeroEngine.Inventory;
using ZeroEngine.Loot;
using ZeroEngine.Shop;

namespace ZeroEngine.Economy.Editor
{
    public enum EconomyValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public readonly struct EconomyValidationIssue
    {
        public readonly ScriptableObject Asset;
        public readonly EconomyValidationSeverity Severity;
        public readonly string FieldPath;
        public readonly string Message;

        public EconomyValidationIssue(
            ScriptableObject asset,
            EconomyValidationSeverity severity,
            string fieldPath,
            string message)
        {
            Asset = asset;
            Severity = severity;
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public static class EconomyConfigValidator
    {
        public static IReadOnlyList<T> LoadAssets<T>() where T : ScriptableObject
        {
            var result = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    result.Add(asset);
                }
            }

            return result;
        }

        public static IReadOnlyList<EconomyValidationIssue> Validate(
            IEnumerable<InventoryItemSO> items = null,
            IEnumerable<CraftingRecipeSO> recipes = null,
            IEnumerable<RecipeBookSO> recipeBooks = null,
            IEnumerable<LootTableSO> lootTables = null,
            IEnumerable<ShopItemSO> shopItems = null,
            IEnumerable<ShopSO> shops = null)
        {
            var issues = new List<EconomyValidationIssue>();
            var itemList = Materialize(items);
            var recipeList = Materialize(recipes);
            var recipeBookList = Materialize(recipeBooks);
            var lootTableList = Materialize(lootTables);
            var shopItemList = Materialize(shopItems);
            var shopList = Materialize(shops);

            foreach (var item in itemList)
            {
                ValidateItem(item, issues);
            }

            foreach (var recipe in recipeList)
            {
                ValidateRecipe(recipe, issues);
            }

            foreach (var recipeBook in recipeBookList)
            {
                ValidateRecipeBook(recipeBook, issues);
            }

            foreach (var lootTable in lootTableList)
            {
                ValidateLootTable(lootTable, issues);
            }

            foreach (var shopItem in shopItemList)
            {
                ValidateShopItem(shopItem, issues);
            }

            foreach (var shop in shopList)
            {
                ValidateShop(shop, issues);
            }

            AddDuplicateIdIssues(itemList, item => item.Id, nameof(InventoryItemSO.Id), "Inventory item ID", issues);
            AddDuplicateIdIssues(recipeList, recipe => recipe.RecipeId, nameof(CraftingRecipeSO.RecipeId), "Recipe ID", issues);
            AddDuplicateIdIssues(recipeBookList, book => book.BookId, nameof(RecipeBookSO.BookId), "Recipe book ID", issues);
            AddDuplicateIdIssues(lootTableList, table => table.TableId, nameof(LootTableSO.TableId), "Loot table ID", issues);
            AddDuplicateIdIssues(shopItemList, item => item.ItemId, nameof(ShopItemSO.ItemId), "Shop item ID", issues);
            AddDuplicateIdIssues(shopList, shop => shop.ShopId, nameof(ShopSO.ShopId), "Shop ID", issues);

            return issues;
        }

        private static T[] Materialize<T>(IEnumerable<T> assets) where T : ScriptableObject
        {
            return (assets ?? Array.Empty<T>())
                .Where(asset => asset != null)
                .ToArray();
        }

        private static void ValidateItem(InventoryItemSO item, ICollection<EconomyValidationIssue> issues)
        {
            RequireId(item, issues, nameof(InventoryItemSO.Id), item.Id, "Inventory item ID");
            RequireDisplayName(item, issues, nameof(InventoryItemSO.ItemName), item.ItemName, "Inventory item name");
            RequirePositive(item, issues, nameof(InventoryItemSO.MaxStack), item.MaxStack, "MaxStack");
            RequireNonNegative(item, issues, nameof(InventoryItemSO.BuyPrice), item.BuyPrice, "BuyPrice");
            RequireNonNegative(item, issues, nameof(InventoryItemSO.SellPrice), item.SellPrice, "SellPrice");

            if (item.BuyPrice > 0 && item.SellPrice > item.BuyPrice)
            {
                issues.Add(new EconomyValidationIssue(item, EconomyValidationSeverity.Warning, nameof(InventoryItemSO.SellPrice), "SellPrice is higher than BuyPrice."));
            }
        }

        private static void ValidateRecipe(CraftingRecipeSO recipe, ICollection<EconomyValidationIssue> issues)
        {
            RequireId(recipe, issues, nameof(CraftingRecipeSO.RecipeId), recipe.RecipeId, "Recipe ID");
            RequireDisplayName(recipe, issues, nameof(CraftingRecipeSO.DisplayName), recipe.DisplayName, "Recipe display name");
            RequireNonNegative(recipe, issues, nameof(CraftingRecipeSO.CraftTime), recipe.CraftTime, "CraftTime");
            RequireNormalized(recipe, issues, nameof(CraftingRecipeSO.SuccessRate), recipe.SuccessRate, "SuccessRate");
            RequireNormalized(recipe, issues, nameof(CraftingRecipeSO.GreatSuccessRate), recipe.GreatSuccessRate, "GreatSuccessRate");
            RequireNonNegative(recipe, issues, nameof(CraftingRecipeSO.ExpReward), recipe.ExpReward, "ExpReward");
            RequireNonNegative(recipe, issues, nameof(CraftingRecipeSO.RequiredSkillLevel), recipe.RequiredSkillLevel, "RequiredSkillLevel");

            if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
            {
                issues.Add(new EconomyValidationIssue(recipe, EconomyValidationSeverity.Error, nameof(CraftingRecipeSO.Ingredients), "Recipe has no ingredients."));
            }
            else
            {
                for (var i = 0; i < recipe.Ingredients.Count; i++)
                {
                    ValidateIngredient(recipe, recipe.Ingredients[i], $"{nameof(CraftingRecipeSO.Ingredients)}[{i}]", issues);
                }
            }

            if (recipe.Outputs == null || recipe.Outputs.Count == 0)
            {
                issues.Add(new EconomyValidationIssue(recipe, EconomyValidationSeverity.Error, nameof(CraftingRecipeSO.Outputs), "Recipe has no outputs."));
            }
            else
            {
                for (var i = 0; i < recipe.Outputs.Count; i++)
                {
                    ValidateOutput(recipe, recipe.Outputs[i], $"{nameof(CraftingRecipeSO.Outputs)}[{i}]", issues);
                }
            }

            ValidateRecipeUnlock(recipe, issues);
        }

        private static void ValidateIngredient(
            CraftingRecipeSO recipe,
            RecipeIngredient ingredient,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (ingredient == null)
            {
                issues.Add(new EconomyValidationIssue(recipe, EconomyValidationSeverity.Error, fieldPath, "Recipe ingredient is empty."));
                return;
            }

            if (ingredient.Item == null)
            {
                issues.Add(new EconomyValidationIssue(recipe, EconomyValidationSeverity.Error, fieldPath, "Recipe ingredient item is missing."));
            }

            RequirePositive(recipe, issues, fieldPath, ingredient.Amount, "Recipe ingredient amount");
        }

        private static void ValidateOutput(
            CraftingRecipeSO recipe,
            RecipeOutput output,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (output == null)
            {
                issues.Add(new EconomyValidationIssue(recipe, EconomyValidationSeverity.Error, fieldPath, "Recipe output is empty."));
                return;
            }

            if (output.Item == null)
            {
                issues.Add(new EconomyValidationIssue(recipe, EconomyValidationSeverity.Error, fieldPath, "Recipe output item is missing."));
            }

            RequirePositive(recipe, issues, fieldPath, output.BaseAmount, "Recipe output base amount");
            RequireNonNegative(recipe, issues, fieldPath, output.BonusAmount, "Recipe output bonus amount");
            RequireNormalized(recipe, issues, fieldPath, output.Probability, "Recipe output probability");
        }

        private static void ValidateRecipeUnlock(CraftingRecipeSO recipe, ICollection<EconomyValidationIssue> issues)
        {
            switch (recipe.UnlockType)
            {
                case RecipeUnlockType.Level:
                    RequirePositive(recipe, issues, nameof(CraftingRecipeSO.UnlockLevel), recipe.UnlockLevel, "UnlockLevel");
                    break;
                case RecipeUnlockType.Quest:
                    RequireId(recipe, issues, nameof(CraftingRecipeSO.UnlockQuestId), recipe.UnlockQuestId, "UnlockQuestId");
                    break;
                case RecipeUnlockType.Achievement:
                    RequireId(recipe, issues, nameof(CraftingRecipeSO.UnlockAchievementId), recipe.UnlockAchievementId, "UnlockAchievementId");
                    break;
                case RecipeUnlockType.Item:
                    if (recipe.UnlockItem == null)
                    {
                        issues.Add(new EconomyValidationIssue(recipe, EconomyValidationSeverity.Error, nameof(CraftingRecipeSO.UnlockItem), "UnlockItem is missing."));
                    }
                    break;
                case RecipeUnlockType.Relationship:
                    RequireId(recipe, issues, nameof(CraftingRecipeSO.UnlockRelationshipNpcId), recipe.UnlockRelationshipNpcId, "UnlockRelationshipNpcId");
                    RequirePositive(recipe, issues, nameof(CraftingRecipeSO.UnlockRelationshipLevel), recipe.UnlockRelationshipLevel, "UnlockRelationshipLevel");
                    break;
                case RecipeUnlockType.Custom:
                    RequireId(recipe, issues, nameof(CraftingRecipeSO.CustomUnlockId), recipe.CustomUnlockId, "CustomUnlockId");
                    break;
            }
        }

        private static void ValidateRecipeBook(RecipeBookSO recipeBook, ICollection<EconomyValidationIssue> issues)
        {
            RequireId(recipeBook, issues, nameof(RecipeBookSO.BookId), recipeBook.BookId, "Recipe book ID");
            RequireDisplayName(recipeBook, issues, nameof(RecipeBookSO.DisplayName), recipeBook.DisplayName, "Recipe book display name");

            if (recipeBook.Recipes == null || recipeBook.Recipes.Count == 0)
            {
                issues.Add(new EconomyValidationIssue(recipeBook, EconomyValidationSeverity.Warning, nameof(RecipeBookSO.Recipes), "Recipe book has no recipes."));
                return;
            }

            for (var i = 0; i < recipeBook.Recipes.Count; i++)
            {
                if (recipeBook.Recipes[i] == null)
                {
                    issues.Add(new EconomyValidationIssue(recipeBook, EconomyValidationSeverity.Error, $"{nameof(RecipeBookSO.Recipes)}[{i}]", "Recipe book contains an empty recipe reference."));
                }
            }
        }

        private static void ValidateLootTable(LootTableSO lootTable, ICollection<EconomyValidationIssue> issues)
        {
            RequireId(lootTable, issues, nameof(LootTableSO.TableId), lootTable.TableId, "Loot table ID");
            RequireDisplayName(lootTable, issues, nameof(LootTableSO.DisplayName), lootTable.DisplayName, "Loot table display name");
            RequirePositive(lootTable, issues, nameof(LootTableSO.DropCount), lootTable.DropCount, "DropCount");
            RequireNonNegative(lootTable, issues, nameof(LootTableSO.MaxDropCount), lootTable.MaxDropCount, "MaxDropCount");

            if (lootTable.MaxDropCount > 0 && lootTable.MaxDropCount < lootTable.DropCount)
            {
                issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, nameof(LootTableSO.MaxDropCount), "MaxDropCount is lower than DropCount."));
            }

            if (lootTable.DropMode == LootDropMode.Layered)
            {
                if (lootTable.Layers == null || lootTable.Layers.Count == 0)
                {
                    issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, nameof(LootTableSO.Layers), "Layered loot table has no layers."));
                }
                else
                {
                    for (var i = 0; i < lootTable.Layers.Count; i++)
                    {
                        ValidateLootLayer(lootTable, lootTable.Layers[i], $"{nameof(LootTableSO.Layers)}[{i}]", issues);
                    }
                }
            }
            else if (lootTable.Entries == null || lootTable.Entries.Count == 0)
            {
                issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, nameof(LootTableSO.Entries), "Loot table has no entries."));
            }

            ValidateLootEntries(lootTable, lootTable.Entries, nameof(LootTableSO.Entries), issues);
            ValidateLootEntries(lootTable, lootTable.GuaranteedDrops, nameof(LootTableSO.GuaranteedDrops), issues);
            ValidateLootConditions(lootTable, lootTable.GlobalConditions, nameof(LootTableSO.GlobalConditions), issues);
        }

        private static void ValidateLootLayer(
            LootTableSO lootTable,
            LootLayer layer,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (layer == null)
            {
                issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, fieldPath, "Loot layer is empty."));
                return;
            }

            RequireDisplayName(lootTable, issues, fieldPath, layer.LayerName, "Loot layer name");
            RequirePositive(lootTable, issues, fieldPath, layer.Weight, "Loot layer weight");
            if (layer.Entries == null || layer.Entries.Count == 0)
            {
                issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, fieldPath, "Loot layer has no entries."));
                return;
            }

            ValidateLootEntries(lootTable, layer.Entries, $"{fieldPath}.Entries", issues);
        }

        private static void ValidateLootEntries(
            LootTableSO lootTable,
            IReadOnlyList<LootEntry> entries,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (entries == null) return;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var entryPath = $"{fieldPath}[{i}]";
                if (entry == null)
                {
                    issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, entryPath, "Loot entry is empty."));
                    continue;
                }

                RequirePositive(lootTable, issues, entryPath, entry.AmountMin, "Loot amount min");
                RequirePositive(lootTable, issues, entryPath, entry.AmountMax, "Loot amount max");
                if (entry.AmountMax < entry.AmountMin)
                {
                    issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, entryPath, "Loot AmountMax is lower than AmountMin."));
                }

                if (entry.Type != LootEntryType.Nothing)
                {
                    RequirePositive(lootTable, issues, entryPath, entry.Weight, "Loot entry weight");
                }

                switch (entry.Type)
                {
                    case LootEntryType.Item:
                        if (entry.Item == null)
                            issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, entryPath, "Loot item entry is missing Item."));
                        break;
                    case LootEntryType.Table:
                        if (entry.NestedTable == null)
                            issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, entryPath, "Loot table entry is missing NestedTable."));
                        else if (entry.NestedTable == lootTable)
                            issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, entryPath, "Loot table entry references itself."));
                        break;
                }

                ValidatePity(lootTable, entry.Pity, entryPath, issues);
                ValidateLootConditions(lootTable, entry.Conditions, $"{entryPath}.Conditions", issues);
            }
        }

        private static void ValidatePity(
            LootTableSO lootTable,
            PityConfig pity,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (pity == null) return;

            RequirePositive(lootTable, issues, fieldPath, pity.MaxAttempts, "Pity MaxAttempts");
            RequireNormalized(lootTable, issues, fieldPath, pity.IncrementPerFail, "Pity IncrementPerFail");
        }

        private static void ValidateLootConditions(
            LootTableSO lootTable,
            IReadOnlyList<LootCondition> conditions,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (conditions == null) return;

            for (var i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] == null)
                {
                    issues.Add(new EconomyValidationIssue(lootTable, EconomyValidationSeverity.Error, $"{fieldPath}[{i}]", "Loot condition is empty."));
                }
            }
        }

        private static void ValidateShopItem(ShopItemSO shopItem, ICollection<EconomyValidationIssue> issues)
        {
            RequireId(shopItem, issues, nameof(ShopItemSO.ItemId), shopItem.ItemId, "Shop item ID");
            if (shopItem.Item == null)
            {
                issues.Add(new EconomyValidationIssue(shopItem, EconomyValidationSeverity.Error, nameof(ShopItemSO.Item), "Shop item is missing InventoryItemSO reference."));
            }

            RequirePositive(shopItem, issues, nameof(ShopItemSO.Quantity), shopItem.Quantity, "Shop item quantity");
            ValidatePrice(shopItem, shopItem.BuyPrice, nameof(ShopItemSO.BuyPrice), issues);
            ValidatePrice(shopItem, shopItem.SellPrice, nameof(ShopItemSO.SellPrice), issues);
            ValidateDiscount(shopItem, shopItem.Discount, nameof(ShopItemSO.Discount), issues);
            ValidateLimit(shopItem, shopItem.Limit, nameof(ShopItemSO.Limit), issues);
            ValidateStock(shopItem, shopItem.Stock, nameof(ShopItemSO.Stock), issues);
            RequireNonNegative(shopItem, issues, nameof(ShopItemSO.RequiredLevel), shopItem.RequiredLevel, "RequiredLevel");
            RequireNonNegative(shopItem, issues, nameof(ShopItemSO.RequiredReputation), shopItem.RequiredReputation, "RequiredReputation");
        }

        private static void ValidateShop(ShopSO shop, ICollection<EconomyValidationIssue> issues)
        {
            RequireId(shop, issues, nameof(ShopSO.ShopId), shop.ShopId, "Shop ID");
            RequireDisplayName(shop, issues, nameof(ShopSO.DisplayName), shop.DisplayName, "Shop display name");
            RequireNormalized(shop, issues, nameof(ShopSO.SellPriceMultiplier), shop.SellPriceMultiplier, "SellPriceMultiplier");
            RequireNonNegative(shop, issues, nameof(ShopSO.RequiredReputation), shop.RequiredReputation, "RequiredReputation");
            ValidateSchedule(shop, shop.Schedule, nameof(ShopSO.Schedule), issues);

            if (shop.Items == null || shop.Items.Count == 0)
            {
                issues.Add(new EconomyValidationIssue(shop, EconomyValidationSeverity.Warning, nameof(ShopSO.Items), "Shop has no items."));
                return;
            }

            var seenItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < shop.Items.Count; i++)
            {
                var shopItem = shop.Items[i];
                var fieldPath = $"{nameof(ShopSO.Items)}[{i}]";
                if (shopItem == null)
                {
                    issues.Add(new EconomyValidationIssue(shop, EconomyValidationSeverity.Error, fieldPath, "Shop contains an empty item reference."));
                    continue;
                }

                var id = shopItem.ItemId?.Trim();
                if (!string.IsNullOrEmpty(id) && !seenItemIds.Add(id))
                {
                    issues.Add(new EconomyValidationIssue(shop, EconomyValidationSeverity.Error, fieldPath, $"Shop contains duplicate item '{id}'."));
                }
            }
        }

        private static void ValidatePrice(
            ScriptableObject asset,
            ShopPrice price,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (price == null)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, "Shop price is missing."));
                return;
            }

            RequireNonNegative(asset, issues, fieldPath, price.Amount, "Shop price amount");
            if (price.CurrencyType == ShopCurrencyType.Custom)
            {
                RequireId(asset, issues, fieldPath, price.CustomCurrencyId, "Custom currency ID");
            }
        }

        private static void ValidateDiscount(
            ScriptableObject asset,
            DiscountConfig discount,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (discount == null) return;

            if (discount.Type == DiscountType.Percentage)
            {
                RequireNormalized(asset, issues, fieldPath, discount.Value, "Discount percentage");
            }
            else if (discount.Type == DiscountType.FlatAmount)
            {
                RequireNonNegative(asset, issues, fieldPath, discount.Value, "Discount flat amount");
            }
        }

        private static void ValidateLimit(
            ScriptableObject asset,
            PurchaseLimit limit,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (limit == null) return;

            RequireNonNegative(asset, issues, fieldPath, limit.MaxCount, "Purchase limit max count");
        }

        private static void ValidateStock(
            ScriptableObject asset,
            StockConfig stock,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (stock == null) return;

            if (stock.InitialStock < -1)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, "InitialStock must be -1 or greater."));
            }

            RequireNonNegative(asset, issues, fieldPath, stock.RestockAmount, "RestockAmount");
            if (stock.RestockType != RestockType.Never && stock.RestockAmount <= 0)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Warning, fieldPath, "RestockAmount should be greater than 0 when restocking is enabled."));
            }
        }

        private static void ValidateSchedule(
            ScriptableObject asset,
            ShopSchedule schedule,
            string fieldPath,
            ICollection<EconomyValidationIssue> issues)
        {
            if (schedule == null) return;

            if (schedule.OpenHour < 0 || schedule.OpenHour > 23)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, "OpenHour must be between 0 and 23."));
            }

            if (schedule.CloseHour < 1 || schedule.CloseHour > 24)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, "CloseHour must be between 1 and 24."));
            }

            if (!schedule.AlwaysOpen && schedule.CloseHour <= schedule.OpenHour)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, "CloseHour must be later than OpenHour."));
            }

            if (schedule.OpenDays == null || schedule.OpenDays.Length != 7)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, "OpenDays must contain 7 entries."));
            }
        }

        private static void AddDuplicateIdIssues<T>(
            IEnumerable<T> assets,
            Func<T, string> getId,
            string fieldPath,
            string label,
            ICollection<EconomyValidationIssue> issues)
            where T : ScriptableObject
        {
            foreach (var duplicateGroup in assets
                         .Where(asset => !string.IsNullOrWhiteSpace(getId(asset)))
                         .GroupBy(asset => getId(asset).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var duplicates = duplicateGroup.ToArray();
                if (duplicates.Length <= 1) continue;

                foreach (var duplicate in duplicates)
                {
                    issues.Add(new EconomyValidationIssue(
                        duplicate,
                        EconomyValidationSeverity.Error,
                        fieldPath,
                        $"{label} '{duplicateGroup.Key}' is duplicated in {duplicates.Length} assets."));
                }
            }
        }

        private static void RequireId(
            ScriptableObject asset,
            ICollection<EconomyValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, $"{label} is empty."));
            }
            else if (value != value.Trim())
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Warning, fieldPath, $"{label} has leading/trailing whitespace."));
            }
        }

        private static void RequireDisplayName(
            ScriptableObject asset,
            ICollection<EconomyValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Warning, fieldPath, $"{label} is empty."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<EconomyValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value <= 0)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<EconomyValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value <= 0f)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<EconomyValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value < 0)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<EconomyValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNormalized(
            ScriptableObject asset,
            ICollection<EconomyValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f || value > 1f)
            {
                issues.Add(new EconomyValidationIssue(asset, EconomyValidationSeverity.Error, fieldPath, $"{label} must be between 0 and 1."));
            }
        }
    }
}
