using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Crafting;
using ZeroEngine.Inventory;
using ZeroEngine.Save;
using Object = UnityEngine.Object;

namespace ZeroEngine.Economy.Tests
{
    // Permanent lifecycle regression: configured owners survive Start, timed Update and destruction without legacy saves.
    public sealed class CraftingLifecycleTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private readonly HashSet<ISaveable> _registered = new HashSet<ISaveable>();
        private int _saveOwners;
        private int _slotOwners;

        [UnityTest, Category("Boundary")]
        public IEnumerator ConfiguredOwners_StartCompleteAndUnregisterTogether()
        {
            _saveOwners = Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _slotOwners = Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.That(Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                Is.Empty, "This isolated package fixture must not borrow a running consumer inventory.");
            Assert.That(Object.FindObjectsByType<CraftingManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                Is.Empty, "This isolated package fixture must not borrow a running craft owner.");
            var root = Own(new GameObject("CraftingLifecycleRegression"));
            root.SetActive(false);
            var inventory = root.AddComponent<InventoryManager>();
            inventory.ConfigurePersistence(save => _registered.Add(save), save => _registered.Remove(save));
            var craft = root.AddComponent<CraftingManager>();
            craft.ConfigureDependencies(inventory, save => _registered.Add(save), save => _registered.Remove(save));
            var herb = Own(ScriptableObject.CreateInstance<InventoryItemSO>());
            herb.Id = "lifecycle.herb";
            var potion = Own(ScriptableObject.CreateInstance<InventoryItemSO>());
            potion.Id = "lifecycle.potion";
            inventory.InitializeDatabase(new List<InventoryItemSO> { herb, potion });
            inventory.ImportSaveData(new List<InventorySlot> { new InventorySlot(herb, 4) });
            var recipe = Own(ScriptableObject.CreateInstance<CraftingRecipeSO>());
            recipe.RecipeId = "lifecycle.recipe";
            recipe.CraftTime = 0.01f;
            recipe.Ingredients.Add(new RecipeIngredient { Item = herb, Amount = 1 });
            recipe.Outputs.Add(new RecipeOutput { Item = potion, BaseAmount = 1 });
            craft.ForceUnlock(recipe);
            root.SetActive(true);
            yield return null;
            Assert.That(_registered.Count, Is.EqualTo(2));
            Assert.That(craft.StartCraft(recipe, null, 2, "lifecycle-request"), Is.EqualTo(CraftingResult.Success));
            Assert.That(inventory.GetItemCount(herb), Is.EqualTo(2));
            Assert.That(craft.ActiveCraftingCount, Is.EqualTo(1));
            var deadline = Time.realtimeSinceStartup + 5;
            while (craft.ActiveCraftingCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(craft.ActiveCraftingCount, Is.Zero);
            Assert.That(inventory.GetItemCount(potion), Is.EqualTo(2));
            Assert.That(craft.TryGetReceipt("lifecycle-request", out var receipt), Is.True);
            Assert.That(receipt.Pending, Is.False);
            Object.Destroy(root);
            yield return null;
            Assert.That(_registered, Is.Empty);
            Assert.That(Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(_saveOwners));
            Assert.That(Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(_slotOwners));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Components unregister before their recipe/item assets are released, including assertion failures.
            foreach (var item in _owned)
                if (item is GameObject go && go != null) Object.Destroy(go);
            yield return null;
            foreach (var item in _owned)
                if (item != null) Object.Destroy(item);
            _owned.Clear();
            yield return null;
            Assert.That(_registered, Is.Empty);
        }

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }
    }
}
