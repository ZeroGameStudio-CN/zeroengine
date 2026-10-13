using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Crafting;
using ZeroEngine.Inventory;
using ZeroEngine.Save;
using Object = UnityEngine.Object;

namespace ZeroEngine.Economy.Tests
{
    // Permanent regression: one request owns inventory, escrow, result and save state together.
    [TestFixture, Category("Unit")]
    public sealed class CraftingTransactionTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private InventoryManager _inventory;
        private CraftingManager _craft;
        private InventoryItemSO _herb;
        private InventoryItemSO _potion;
        private InventoryItemSO _tool;
        private CraftingRecipeSO _recipe;
        private int _saveRoots;
        private int _slotRoots;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                Is.Empty, "This isolated package fixture must not borrow a running consumer inventory.");
            Assert.That(Object.FindObjectsByType<CraftingManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                Is.Empty, "This isolated package fixture must not borrow a running craft owner.");
            _saveRoots = Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _slotRoots = Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var inventoryRoot = Own(new GameObject("CraftingRegression.Inventory"));
            _inventory = inventoryRoot.AddComponent<InventoryManager>();
            _inventory.ConfigurePersistence();
            _herb = Item("test.herb", 99);
            _potion = Item("test.potion", 99);
            _tool = Item("test.tool", 1);
            _recipe = Own(ScriptableObject.CreateInstance<CraftingRecipeSO>());
            _recipe.RecipeId = "test.recipe";
            _recipe.Ingredients.Add(new RecipeIngredient { Item = _herb, Amount = 2 });
            _recipe.Outputs.Add(new RecipeOutput { Item = _potion, BaseAmount = 3 });
            _recipe.SkillId = "test.alchemy";
            _recipe.ExpReward = 10;
            CreateCraft();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _owned.Count - 1; i >= 0; i--)
                if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            Assert.That(Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(_saveRoots), "Configured crafting must not create the default save owner.");
            Assert.That(Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(_slotRoots), "Configured crafting must not create a legacy slot owner.");
        }

        [Test]
        public void InstantBatch_CommitsActualAmountsAndExperienceOnce()
        {
            Seed(new InventorySlot(_herb, 8));
            CraftingEventArgs completed = default;
            _craft.OnCraftingEvent += args => completed = args;
            Assert.That(_craft.StartCraft(_recipe, null, 2, "request-a"), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(4));
            Assert.That(_inventory.GetItemCount(_potion), Is.EqualTo(6));
            Assert.That(_craft.GetSkillData(_recipe.SkillId).Experience, Is.EqualTo(20));
            Assert.That(completed.BatchCount, Is.EqualTo(2));
            Assert.That(completed.Outputs[0].BaseAmount, Is.EqualTo(6));
            Assert.That(completed.ProgressId, Is.EqualTo("request-a"));
            Assert.That(_craft.StartCraft(_recipe, null, 2, "request-a"), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_potion), Is.EqualTo(6));
            Assert.That(_craft.GetSkillData(_recipe.SkillId).Experience, Is.EqualTo(20));
            Assert.That(_craft.StartCraft(_recipe, null, 1, "request-a"), Is.EqualTo(CraftingResult.InvalidRequest));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidBatch_ChangesNothing(int amount)
        {
            Seed(new InventorySlot(_herb, 8));
            Assert.That(_craft.StartCraft(_recipe, null, amount, "invalid"), Is.EqualTo(CraftingResult.InvalidRequest));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
            Assert.That(_craft.TryGetReceipt("invalid", out _), Is.False);
        }

        [Test]
        public void OverflowingBatch_RejectsBeforeConsumption()
        {
            Seed(new InventorySlot(_herb, 8));
            Assert.That(_craft.StartCraft(_recipe, null, int.MaxValue), Is.EqualTo(CraftingResult.InvalidRecipe));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
        }

        [Test]
        public void DuplicateIngredients_AggregateBeforeEligibilityAndConsumption()
        {
            _recipe.Ingredients.Add(new RecipeIngredient { Item = _herb, Amount = 3 });
            Seed(new InventorySlot(_herb, 4));
            Assert.That(_recipe.CheckIngredients(_inventory), Is.False);
            Assert.That(_recipe.CalculateMaxCraftCount(_inventory), Is.Zero);
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.InsufficientMaterials));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(4));
            Seed(new InventorySlot(_herb, 10));
            Assert.That(_recipe.CalculateMaxCraftCount(_inventory), Is.EqualTo(2));
            Assert.That(_craft.StartCraft(_recipe, null, 2), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_herb), Is.Zero);
        }

        [Test]
        public void SequentialBatch_ReusesToolAndReservesToolSharedWithConsumedRows()
        {
            _recipe.Ingredients.Add(new RecipeIngredient { Item = _tool, Amount = 1, IsConsumed = false });
            Seed(new InventorySlot(_herb, 8), new InventorySlot(_tool, 1));
            Assert.That(_recipe.CalculateMaxCraftCount(_inventory), Is.EqualTo(4));
            Assert.That(_craft.StartCraft(_recipe, null, 3), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_tool), Is.EqualTo(1));
            _recipe.Ingredients.Add(new RecipeIngredient { Item = _herb, Amount = 1, IsConsumed = false });
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.InsufficientMaterials));
        }

        [Test]
        public void FullInventory_NoPartialOutputConsumptionReceiptOrEvents()
        {
            FillInventory(8);
            var before = Slots();
            var events = 0;
            _inventory.OnSlotChanged += _ => events++;
            _craft.OnCraftingEvent += _ => events++;
            Assert.That(_craft.StartCraft(_recipe, null, 1, "full"), Is.EqualTo(CraftingResult.InventoryFull));
            Assert.That(Slots(), Is.EqualTo(before));
            Assert.That(events, Is.Zero);
            Assert.That(_craft.TryGetReceipt("full", out _), Is.False);
        }

        [Test]
        public void FullInventory_ConsumedSlotCanHoldResult()
        {
            FillInventory(2);
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_herb), Is.Zero);
            Assert.That(_inventory.GetItemCount(_potion), Is.EqualTo(3));
        }

        [Test]
        public void InventoryObserver_SeesCommittedCraftAndRejectsReentrantRequest()
        {
            Seed(new InventorySlot(_herb, 8));
            var countAtEvent = -1;
            var experienceAtEvent = -1;
            var receiptAtEvent = false;
            var reentrant = CraftingResult.Success;
            _inventory.OnItemRemoved += (item, count) =>
            {
                countAtEvent = _inventory.GetItemCount(_potion);
                experienceAtEvent = _craft.GetSkillData(_recipe.SkillId).Experience;
                receiptAtEvent = _craft.TryGetReceipt("outer", out _);
                reentrant = _craft.StartCraft(_recipe, null, 1, "inner");
            };
            Assert.That(_craft.StartCraft(_recipe, null, 1, "outer"), Is.EqualTo(CraftingResult.Success));
            Assert.That(countAtEvent, Is.EqualTo(3));
            Assert.That(experienceAtEvent, Is.EqualTo(10));
            Assert.That(receiptAtEvent, Is.True);
            Assert.That(reentrant, Is.EqualTo(CraftingResult.Busy));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(6));
        }

        [Test]
        public void TimedCancel_RefundsEvenWhenFailureWouldLoseMaterialsAndIsIdempotent()
        {
            _recipe.CraftTime = 60;
            _recipe.KeepMaterialsOnFail = false;
            Seed(new InventorySlot(_herb, 8));
            Assert.That(_craft.StartCraft(_recipe, null, 2, "cancel"), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(4));
            Assert.That(_craft.TryCancelCraft("cancel", out var result), Is.True);
            Assert.That(result, Is.EqualTo(CraftingResult.Cancelled));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
            Assert.That(_craft.TryCancelCraft("cancel", out _), Is.True);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
            Assert.That(_craft.ActiveCraftingCount, Is.Zero);
        }

        [Test]
        public void TimedCancel_FullRefundDestinationRetainsEscrowUntilItFits()
        {
            _recipe.CraftTime = 60;
            Seed(new InventorySlot(_herb, 2));
            _craft.StartCraft(_recipe, null, 1, "cancel-full");
            FillInventory(0);
            var before = Slots();
            Assert.That(_craft.TryCancelCraft("cancel-full", out var result), Is.False);
            Assert.That(result, Is.EqualTo(CraftingResult.InventoryFull));
            Assert.That(Slots(), Is.EqualTo(before));
            Assert.That(_craft.ActiveCraftingCount, Is.EqualTo(1));
            _inventory.ClearSlot(0);
            Assert.That(_craft.TryCancelCraft("cancel-full", out _), Is.True);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(2));
        }

        [Test]
        public void TimedCompletion_FullDestinationRetainsFixedOutcomeAndAwardsOnceAfterRetry()
        {
            _recipe.CraftTime = 60;
            _recipe.GreatSuccessRate = 1;
            _recipe.Outputs[0].BonusAmount = 2;
            Seed(new InventorySlot(_herb, 2));
            _craft.StartCraft(_recipe, null, 1, "pending");
            FillInventory(0);
            var state = (CraftingSaveData)_craft.ExportSaveData();
            state.ActiveProgress[0].EndTime = state.ActiveProgress[0].StartTime;
            _craft.ImportSaveData(state);
            _recipe.Outputs[0].BaseAmount = 70;
            _recipe.ExpReward = 100;
            var blocked = 0;
            _craft.OnCraftingEvent += args => { if (args.EventType == CraftingEventType.Blocked) blocked++; };
            Tick();
            Tick();
            Assert.That(_craft.ActiveCraftingCount, Is.EqualTo(1));
            Assert.That(_craft.GetActiveProgress()[0].LastResult, Is.EqualTo(CraftingResult.InventoryFull));
            Assert.That(blocked, Is.EqualTo(1));
            Assert.That(_craft.GetSkillData(_recipe.SkillId).Experience, Is.Zero);
            _inventory.ClearSlot(0);
            Tick();
            Tick();
            Assert.That(_craft.ActiveCraftingCount, Is.Zero);
            Assert.That(_inventory.GetItemCount(_potion), Is.EqualTo(5));
            Assert.That(_craft.GetSkillData(_recipe.SkillId).Experience, Is.EqualTo(10));
            Assert.That(_craft.TryGetReceipt("pending", out var receipt), Is.True);
            Assert.That(receipt.Result, Is.EqualTo(CraftingResult.GreatSuccess));
            Assert.That(receipt.Pending, Is.False);
        }

        [Test]
        public void SavedPending_RecreatedOwnerCanCancelWithoutRepeatingConsumption()
        {
            _recipe.CraftTime = 60;
            Seed(new InventorySlot(_herb, 8));
            _craft.StartCraft(_recipe, null, 2, "restore");
            var save = (CraftingSaveData)_craft.ExportSaveData();
            Object.DestroyImmediate(_craft.gameObject);
            CreateCraft();
            _craft.ImportSaveData(save);
            Assert.That(_craft.StartCraft(_recipe, null, 2, "restore"), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(4));
            Assert.That(_craft.TryCancelCraft("restore", out _), Is.True);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
        }

        [Test]
        public void ExportAndProgressViews_DoNotAliasOwnedEscrowOrReceipts()
        {
            _recipe.CraftTime = 60;
            Seed(new InventorySlot(_herb, 8));
            _craft.StartCraft(_recipe, null, 1, "snapshot");
            var save = (CraftingSaveData)_craft.ExportSaveData();
            save.ActiveProgress[0].ConsumedItems[0].Amount = 999;
            save.Receipts[0].Result = CraftingResult.Cancelled;
            _craft.GetActiveProgress()[0].ConsumedItems[0].Amount = 777;
            Assert.That(_craft.GetActiveProgress()[0].ConsumedItems[0].Amount, Is.EqualTo(2));
            Assert.That(_craft.TryGetReceipt("snapshot", out var receipt), Is.True);
            Assert.That(receipt.Result, Is.EqualTo(CraftingResult.Success));
        }

        [Test]
        public void DuplicateSavedProgress_IsRejectedBeforeReplacingCurrentState()
        {
            _recipe.CraftTime = 60;
            Seed(new InventorySlot(_herb, 8));
            _craft.StartCraft(_recipe, null, 1, "snapshot");
            var save = (CraftingSaveData)_craft.ExportSaveData();
            save.ActiveProgress.Add(save.ActiveProgress[0]);
            Assert.Throws<ArgumentException>(() => _craft.ImportSaveData(save));
            Assert.That(_craft.ActiveCraftingCount, Is.EqualTo(1));
            save = (CraftingSaveData)_craft.ExportSaveData();
            save.ActiveProgress.Clear();
            Assert.Throws<ArgumentException>(() => _craft.ImportSaveData(save));
            Assert.That(_craft.ActiveCraftingCount, Is.EqualTo(1));
        }

        [TestCase(RecipeUnlockType.Level)]
        [TestCase(RecipeUnlockType.Quest)]
        [TestCase(RecipeUnlockType.Achievement)]
        [TestCase(RecipeUnlockType.Relationship)]
        [TestCase(RecipeUnlockType.Custom)]
        public void ExternalUnlock_MissingProviderFailsClosed(RecipeUnlockType unlockType)
        {
            _recipe.UnlockType = unlockType;
            _recipe.CustomUnlockId = "test.absent";
            _craft.ResetToDefault();
            Assert.That(_craft.TryUnlockRecipe(_recipe), Is.False);
            _craft.ConfigureDependencies(_inventory, unlockCondition: recipe => recipe == _recipe);
            Assert.That(_craft.TryUnlockRecipe(_recipe), Is.True);
        }

        [Test]
        public void ExplicitMissingInventory_DoesNotFallBackToGlobalInstance()
        {
            Seed(new InventorySlot(_herb, 8));
            _craft.ConfigureDependencies(null);
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.InventoryUnavailable));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
        }

        [TestCase(true, CraftingResult.FailedKeepMaterials, 8)]
        [TestCase(false, CraftingResult.FailedLoseMaterials, 6)]
        public void ZeroSuccessRate_HasDeterministicFailureWithoutExperience(bool keep, CraftingResult expected, int remaining)
        {
            _recipe.SuccessRate = 0;
            _recipe.KeepMaterialsOnFail = keep;
            Seed(new InventorySlot(_herb, 8));
            Assert.That(_craft.StartCraft(_recipe, null, 1, "failure"), Is.EqualTo(expected));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(remaining));
            Assert.That(_inventory.GetItemCount(_potion), Is.Zero);
            Assert.That(_craft.GetSkillData(_recipe.SkillId).Experience, Is.Zero);
            Assert.That(_craft.StartCraft(_recipe, null, 1, "failure"), Is.EqualTo(expected));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(remaining));
        }

        [Test]
        public void LegacyPendingWithoutEscrow_ReceivesStableIdentityAndCompletesOnce()
        {
            Seed();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _craft.ImportSaveData(new CraftingSaveData
            {
                UnlockedRecipes = new List<string> { _recipe.RecipeId },
                ActiveProgress = new List<CraftingProgress>
                {
                    new CraftingProgress { RecipeId = _recipe.RecipeId, BatchCount = 1, StartTime = now, EndTime = now }
                }
            });
            var id = _craft.GetActiveProgress()[0].ProgressId;
            Assert.That(id, Is.Not.Empty);
            Tick();
            Tick();
            Assert.That(_craft.ActiveCraftingCount, Is.Zero);
            Assert.That(_inventory.GetItemCount(_potion), Is.EqualTo(3));
            Assert.That(_craft.TryGetReceipt(id, out var receipt), Is.True);
            Assert.That(receipt.Pending, Is.False);
        }

        [Test]
        public void LegacyParallelPendingInSameSecond_HaveDistinctStableIdentitiesAndRefundOnce()
        {
            Seed(new InventorySlot(_herb, 4));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var legacy = new CraftingSaveData
            {
                UnlockedRecipes = new List<string> { _recipe.RecipeId },
                ActiveProgress = new List<CraftingProgress>
                {
                    new CraftingProgress { RecipeId = _recipe.RecipeId, BatchCount = 1, StartTime = now, EndTime = now + 30 },
                    new CraftingProgress { RecipeId = _recipe.RecipeId, BatchCount = 1, StartTime = now, EndTime = now + 30 }
                }
            };
            _craft.ImportSaveData(legacy);
            var saved = (CraftingSaveData)_craft.ExportSaveData();
            var first = saved.ActiveProgress[0].ProgressId;
            var second = saved.ActiveProgress[1].ProgressId;
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(saved.Receipts.Count, Is.EqualTo(2));
            _craft.ImportSaveData(saved);
            Assert.That(_craft.GetActiveProgress()[0].ProgressId, Is.EqualTo(first));
            Assert.That(_craft.GetActiveProgress()[1].ProgressId, Is.EqualTo(second));
            Assert.That(_craft.TryCancelCraft(first, out _), Is.True);
            Assert.That(_craft.TryCancelCraft(second, out _), Is.True);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
            Assert.That(_craft.TryCancelCraft(first, out _), Is.True);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
        }

        [Test]
        public void ConfiguredSaveRegistration_IsBalancedAndDoesNotCreateLegacyOwners()
        {
            var registrations = 0;
            var releases = 0;
            _craft.ConfigureDependencies(_inventory, _ => registrations++, _ => releases++);
            _craft.Register();
            _craft.Register();
            Assert.Throws<InvalidOperationException>(() => _craft.ConfigureDependencies(_inventory));
            _craft.Unregister();
            _craft.Unregister();
            Assert.That(registrations, Is.EqualTo(1));
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void AtomicExchange_PartialCapacityFailurePublishesNothing()
        {
            FillInventory(0);
            _inventory.ClearSlot(0);
            var before = Slots();
            var events = 0;
            _inventory.OnItemAdded += (_, _) => events++;
            Assert.That(_inventory.TryExchangeItems(null, new[] { new InventoryItemAmount(_tool, 2) }, false), Is.False);
            Assert.That(Slots(), Is.EqualTo(before));
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void AtomicExchange_DuplicateRowsCannotOverspendOrOverflow()
        {
            Seed(new InventorySlot(_herb, 3));
            Assert.That(_inventory.TryExchangeItems(new[]
            {
                new InventoryItemAmount(_herb, 2), new InventoryItemAmount(_herb, 2)
            }, null, false), Is.False);
            Assert.That(_inventory.TryExchangeItems(null, new[]
            {
                new InventoryItemAmount(_herb, int.MaxValue), new InventoryItemAmount(_herb, 1)
            }, false), Is.False);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(3));
        }

        [Test]
        public void InvalidRecipe_RejectsNonFiniteTimeAndZeroIngredients()
        {
            Seed(new InventorySlot(_herb, 8));
            _recipe.CraftTime = float.NaN;
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.InvalidRecipe));
            _recipe.CraftTime = 0;
            _recipe.Ingredients[0].Amount = 0;
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.InvalidRecipe));
            Assert.That(_recipe.CalculateMaxCraftCount(_inventory), Is.Zero);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
        }

        [Test]
        public void ZeroChanceBonus_DoesNotRequireUnavailableCapacity()
        {
            FillInventory(2);
            _recipe.Outputs[0].BonusAmount = 200;
            _recipe.GreatSuccessRate = 0;
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.Success));
            Assert.That(_inventory.GetItemCount(_potion), Is.EqualTo(3));
        }

        [Test]
        public void CountAcrossLargeStacks_DoesNotWrapNegative()
        {
            _herb.MaxStack = int.MaxValue;
            Seed(new InventorySlot(_herb, int.MaxValue), new InventorySlot(_herb, int.MaxValue));
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(int.MaxValue));
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(_herb, int.MaxValue) }, null, false), Is.True);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void PreparedSave_MissingReceiptOrFailureWithOutputsIsRejectedAtomically()
        {
            _recipe.CraftTime = 60;
            Seed(new InventorySlot(_herb, 8));
            _craft.StartCraft(_recipe, null, 1, "prepared");
            var state = (CraftingSaveData)_craft.ExportSaveData();
            state.Receipts.Clear();
            Assert.Throws<ArgumentException>(() => _craft.ImportSaveData(state));
            state = (CraftingSaveData)_craft.ExportSaveData();
            state.ActiveProgress[0].Result = CraftingResult.FailedLoseMaterials;
            Assert.Throws<ArgumentException>(() => _craft.ImportSaveData(state));
            Assert.That(_craft.ActiveCraftingCount, Is.EqualTo(1));
            Assert.That(_craft.TryCancelCraft("prepared", out _), Is.True);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
        }

        [Test]
        public void FailedKeepMaterials_DoesNotPublishAcquisitionOrConsumptionForUnchangedCounts()
        {
            Seed(new InventorySlot(_herb, 8));
            _recipe.SuccessRate = 0;
            _recipe.KeepMaterialsOnFail = true;
            var added = 0;
            var removed = 0;
            _inventory.OnItemAdded += (_, count) => added += count;
            _inventory.OnItemRemoved += (_, count) => removed += count;
            Assert.That(_craft.StartCraft(_recipe), Is.EqualTo(CraftingResult.FailedKeepMaterials));
            Assert.That(added, Is.Zero);
            Assert.That(removed, Is.Zero);
            Assert.That(_inventory.GetItemCount(_herb), Is.EqualTo(8));
        }

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        private InventoryItemSO Item(string id, int stack)
        {
            var item = Own(ScriptableObject.CreateInstance<InventoryItemSO>());
            item.Id = id;
            item.ItemName = id;
            item.MaxStack = stack;
            _inventory.RegisterItem(item);
            return item;
        }

        private void CreateCraft()
        {
            var go = Own(new GameObject("CraftingRegression.Craft"));
            _craft = go.AddComponent<CraftingManager>();
            _craft.enabled = false;
            _craft.ConfigureDependencies(_inventory);
            _craft.ForceUnlock(_recipe);
        }

        private void Seed(params InventorySlot[] slots)
        {
            _inventory.ImportSaveData(new List<InventorySlot>(slots));
        }

        private void FillInventory(int herbs)
        {
            var slots = new List<InventorySlot>();
            if (herbs > 0) slots.Add(new InventorySlot(_herb, herbs));
            var filler = Item("test.filler", 1);
            while (slots.Count < InventoryManager.MaxSlots) slots.Add(new InventorySlot(filler, 1));
            _inventory.ImportSaveData(slots);
        }

        private string Slots()
        {
            var rows = new List<string>();
            foreach (var slot in _inventory.Slots) rows.Add($"{slot.SlotIndex}:{slot.ItemId}:{slot.Amount}");
            return string.Join("|", rows);
        }

        private void Tick() => typeof(CraftingManager).GetMethod("CheckCompletedCrafting",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_craft, null);
    }
}
