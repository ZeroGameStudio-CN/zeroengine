using System.IO;
using NUnit.Framework;

namespace ZeroEngine.Economy.Tests
{
    [TestFixture]
    public class EconomySourceGuardTests
    {
        private static readonly string RuntimeRoot =
            Path.GetFullPath("../ZeroEngine/Packages/com.zerogamestudio.zeroengine.economy/Runtime");

        [Test]
        public void EconomySourceGuards_NoCurrencyStubsAndNoDirectInventorySingletonInShopCraftingLoot()
        {
            string shop = File.ReadAllText(Path.Combine(RuntimeRoot, "Shop/ShopManager.cs"));
            string crafting = File.ReadAllText(Path.Combine(RuntimeRoot, "Crafting/CraftingManager.cs"));
            string lootManager = File.ReadAllText(Path.Combine(RuntimeRoot, "Loot/LootTableManager.cs"));
            string lootCondition = File.ReadAllText(Path.Combine(RuntimeRoot, "Loot/LootCondition.cs"));

            Assert.IsFalse(shop.Contains("TODO: 接入货币系统"));
            Assert.IsFalse(shop.Contains("InventoryManager.Instance"));
            Assert.IsFalse(crafting.Contains("Inventory.InventoryManager.Instance"));
            Assert.IsFalse(lootManager.Contains("Inventory.InventoryManager.Instance"));
            Assert.IsFalse(lootCondition.Contains("Inventory.InventoryManager.Instance"));
        }

        [Test]
        public void EconomyProviderResolver_DoesNotUseAutoCreatingSingletonInstance()
        {
            string resolver = File.ReadAllText(Path.Combine(RuntimeRoot, "Providers/EconomyProviderResolver.cs"));

            Assert.IsFalse(resolver.Contains(".Instance"));
            Assert.IsTrue(resolver.Contains("FindFirstObjectByType"));
            Assert.IsTrue(resolver.Contains("FindObjectsInactive.Include"));
        }
    }
}
