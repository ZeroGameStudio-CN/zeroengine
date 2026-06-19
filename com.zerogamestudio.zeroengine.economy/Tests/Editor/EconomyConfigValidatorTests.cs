using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Crafting;
using ZeroEngine.Economy.Editor;
using ZeroEngine.Inventory;
using ZeroEngine.Loot;
using ZeroEngine.Shop;
using Object = UnityEngine.Object;

namespace ZeroEngine.Economy.Editor.Tests
{
    public sealed class EconomyConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsDesignerBlockingEconomyConfigIssues()
        {
            var itemA = ScriptableObject.CreateInstance<InventoryItemSO>();
            var itemB = ScriptableObject.CreateInstance<InventoryItemSO>();
            var recipe = ScriptableObject.CreateInstance<CraftingRecipeSO>();
            var lootTable = ScriptableObject.CreateInstance<LootTableSO>();
            var shopItem = ScriptableObject.CreateInstance<ShopItemSO>();
            var shop = ScriptableObject.CreateInstance<ShopSO>();

            try
            {
                itemA.name = "ItemA";
                itemA.Id = " item_a ";
                itemA.MaxStack = 0;
                itemA.BuyPrice = 10;
                itemA.SellPrice = 20;

                itemB.name = "ItemB";
                itemB.Id = "item_a";
                itemB.ItemName = "Item B";

                recipe.name = "Recipe";
                recipe.RecipeId = "recipe_a";
                recipe.Ingredients.Add(new RecipeIngredient
                {
                    Amount = 0
                });
                recipe.Outputs.Add(new RecipeOutput
                {
                    BaseAmount = 0,
                    Probability = 1.5f
                });
                recipe.UnlockType = RecipeUnlockType.Quest;

                lootTable.name = "Loot";
                lootTable.TableId = "loot_a";
                lootTable.DropCount = 2;
                lootTable.MaxDropCount = 1;
                lootTable.Entries.Add(new LootEntry
                {
                    Type = LootEntryType.Item,
                    AmountMin = 2,
                    AmountMax = 1,
                    Weight = 0f
                });

                shopItem.name = "ShopItem";
                shopItem.ItemId = "shop_item_a";
                shopItem.Quantity = 0;
                shopItem.BuyPrice.Amount = -1;
                shopItem.Item = null;

                shop.name = "Shop";
                shop.ShopId = "shop_a";
                shop.SellPriceMultiplier = 1.5f;
                shop.Schedule.AlwaysOpen = false;
                shop.Schedule.OpenHour = 20;
                shop.Schedule.CloseHour = 8;
                shop.Items.Add(shopItem);
                shop.Items.Add(shopItem);

                var issues = EconomyConfigValidator.Validate(
                    new[] { itemA, itemB },
                    new[] { recipe },
                    lootTables: new[] { lootTable },
                    shopItems: new[] { shopItem },
                    shops: new[] { shop });

                AssertIssue(issues, itemA, EconomyValidationSeverity.Warning, "Inventory item ID has leading/trailing whitespace.");
                AssertIssue(issues, itemA, EconomyValidationSeverity.Warning, "Inventory item name is empty.");
                AssertIssue(issues, itemA, EconomyValidationSeverity.Error, "MaxStack must be greater than 0.");
                AssertIssue(issues, itemA, EconomyValidationSeverity.Warning, "SellPrice is higher than BuyPrice.");
                Assert.That(issues.Count(issue => issue.Message.Contains("Inventory item ID") && issue.Message.Contains("duplicated")), Is.EqualTo(2));

                AssertIssue(issues, recipe, EconomyValidationSeverity.Error, "Recipe ingredient item is missing.");
                AssertIssue(issues, recipe, EconomyValidationSeverity.Error, "Recipe ingredient amount must be greater than 0.");
                AssertIssue(issues, recipe, EconomyValidationSeverity.Error, "Recipe output item is missing.");
                AssertIssue(issues, recipe, EconomyValidationSeverity.Error, "Recipe output probability must be between 0 and 1.");
                AssertIssue(issues, recipe, EconomyValidationSeverity.Error, "UnlockQuestId is empty.");

                AssertIssue(issues, lootTable, EconomyValidationSeverity.Error, "MaxDropCount is lower than DropCount.");
                AssertIssue(issues, lootTable, EconomyValidationSeverity.Error, "Loot AmountMax is lower than AmountMin.");
                AssertIssue(issues, lootTable, EconomyValidationSeverity.Error, "Loot item entry is missing Item.");

                AssertIssue(issues, shopItem, EconomyValidationSeverity.Error, "Shop item is missing InventoryItemSO reference.");
                AssertIssue(issues, shopItem, EconomyValidationSeverity.Error, "Shop item quantity must be greater than 0.");
                AssertIssue(issues, shopItem, EconomyValidationSeverity.Error, "Shop price amount must not be negative.");

                AssertIssue(issues, shop, EconomyValidationSeverity.Error, "SellPriceMultiplier must be between 0 and 1.");
                AssertIssue(issues, shop, EconomyValidationSeverity.Error, "CloseHour must be later than OpenHour.");
                AssertIssue(issues, shop, EconomyValidationSeverity.Error, "Shop contains duplicate item 'shop_item_a'.");
            }
            finally
            {
                Object.DestroyImmediate(itemA);
                Object.DestroyImmediate(itemB);
                Object.DestroyImmediate(recipe);
                Object.DestroyImmediate(lootTable);
                Object.DestroyImmediate(shopItem);
                Object.DestroyImmediate(shop);
            }
        }

        private static void AssertIssue(
            System.Collections.Generic.IEnumerable<EconomyValidationIssue> issues,
            ScriptableObject asset,
            EconomyValidationSeverity severity,
            string message)
        {
            Assert.That(
                issues.Any(issue =>
                    issue.Asset == asset &&
                    issue.Severity == severity &&
                    issue.Message == message),
                Is.True,
                message);
        }
    }
}
