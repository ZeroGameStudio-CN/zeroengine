using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Crafting;
using ZeroEngine.Inventory;
using ZeroEngine.Save;
using Object = UnityEngine.Object;

namespace ZeroEngine.Economy.Tests
{
    [TestFixture, Category("Unit")]
    public sealed class InventoryInstanceTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private InventoryManager _inventory;
        private InventoryItemSO _weapon;
        private InventoryItemSO _material;
        private int _notifications;
        private int _saves;
        private int _saveRoots;
        private int _slotRoots;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                Is.Empty, "This fixture must not borrow a consumer inventory.");
            _saveRoots = Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _slotRoots = Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _inventory = Own(new GameObject("InventoryInstanceRegression")).AddComponent<InventoryManager>();
            _inventory.ConfigurePersistence(persist: () => _saves++);
            _weapon = Item("test.weapon", 1);
            _material = Item("test.material", 99);
            _inventory.OnSlotChanged += _ => _notifications++;
            _inventory.OnItemAdded += (_, _) => _notifications++;
            _inventory.OnItemRemoved += (_, _) => _notifications++;
            EventManager.Subscribe(GameEvents.InventoryUpdated, OnInventoryUpdated);
        }

        [TearDown]
        public void TearDown()
        {
            EventManager.Unsubscribe(GameEvents.InventoryUpdated, OnInventoryUpdated);
            for (var i = _owned.Count - 1; i >= 0; i--)
                if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            Assert.That(Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(_saveRoots));
            Assert.That(Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(_slotRoots));
        }

        [Test]
        public void Exchange_TwoInstancesOfSameDefinitionKeepDistinctState()
        {
            var first = State("first", "{\"strengthening\":3}");
            var second = State("second", "{\"strengthening\":7}");
            Assert.That(_inventory.TryExchangeItems(null, new[]
            {
                new InventoryItemAmount(_weapon, first), new InventoryItemAmount(_weapon, second)
            }, false), Is.True);
            Assert.That(_inventory.Slots.Count, Is.EqualTo(2));
            AssertState(_inventory.GetSlot(0), "first", first.PayloadJson);
            AssertState(_inventory.GetSlot(1), "second", second.PayloadJson);
            Assert.That(_inventory.GetSlot(0).InstanceData, Is.Not.SameAs(first));
            Assert.That(_inventory.GetItemCount(_weapon), Is.Zero);
            Assert.That(_inventory.HasItem(_weapon.Id), Is.False);
        }

        [Test]
        public void DefinitionAddRemoveAndUse_NeverConsumeOrStackWithInstances()
        {
            Seed(new InventorySlot(_material, State("owned")), new InventorySlot(_material, 2));
            Assert.That(_inventory.AddItem(_material, 3), Is.True);
            Assert.That(_inventory.GetSlot(0).Amount, Is.EqualTo(1));
            Assert.That(_inventory.GetSlot(1).Amount, Is.EqualTo(5));
            Assert.That(_inventory.GetItemCount(_material), Is.EqualTo(5));
            Assert.That(_inventory.GetOwnedItemCount(_material.Id), Is.EqualTo(6));
            Assert.That(_inventory.FindSlot(_material.Id), Is.SameAs(_inventory.GetSlot(1)));
            _inventory.RemoveItem(_material, 99);
            Assert.That(_inventory.GetItemCount(_material), Is.Zero);
            Assert.That(_inventory.GetOwnedItemCount(_material.Id), Is.EqualTo(1));
            AssertState(_inventory.GetSlot(0), "owned", "{}");
            var before = Snapshot();
            ResetObservations();
            Assert.That(_inventory.UseItem(0), Is.False);
            Assert.That(_inventory.CanExchangeItems(new[] { new InventoryItemAmount(_material, 1) }, null), Is.False);
            AssertUnchanged(before);
        }

        [Test]
        public void FullInventory_OneInstanceReplacesAnotherAfterConsumption()
        {
            Fill(new InventorySlot(_weapon, State("old")));
            var changedSlots = new List<int>();
            _inventory.OnSlotChanged += args => changedSlots.Add(args.SlotIndex);
            Assert.That(_inventory.CanExchangeItems(new[] { new InventoryItemAmount(_weapon, State("old")) },
                new[] { new InventoryItemAmount(_weapon, State("new")) }), Is.True);
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(_weapon, State("old")) },
                new[] { new InventoryItemAmount(_weapon, State("new")) }, false), Is.True);
            Assert.That(_inventory.IsFull, Is.True);
            Assert.That(changedSlots, Is.EqualTo(new[] { 0 }), "A same-definition identity change must notify the slot.");
            AssertState(_inventory.GetSlot(0), "new", "{}");
        }

        [Test]
        public void FullInventory_ExtraOutputRejectsWithoutConsumptionEventsOrSave()
        {
            Fill(new InventorySlot(_weapon, State("old")));
            var before = Snapshot();
            ResetObservations();
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(_weapon, State("old")) },
                new[] { new InventoryItemAmount(_weapon, State("new")),
                    new InventoryItemAmount(_weapon, State("extra")) }), Is.False);
            AssertUnchanged(before);
        }

        [Test]
        public void DefinitionExchange_CanReuseConsumedSlotButCannotSpendInstance()
        {
            Fill(new InventorySlot(_weapon, State("owned")));
            var before = Snapshot();
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(_weapon, 30) }, null, false), Is.False);
            Assert.That(Snapshot(), Is.EqualTo(before));
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(_weapon, 1) },
                new[] { new InventoryItemAmount(_weapon, State("new")) }, false), Is.True);
            Assert.That(_inventory.GetItemCount(_weapon), Is.EqualTo(28));
            AssertState(_inventory.GetSlot(0), "owned", "{}");
            AssertState(_inventory.GetSlot(1), "new", "{}");
        }

        [Test]
        public void RecipeIngredients_DoNotCountOwnedInstancesAsDefinitionMaterials()
        {
            Seed(new InventorySlot(_material, State("owned")));
            var recipe = Own(ScriptableObject.CreateInstance<CraftingRecipeSO>());
            recipe.Ingredients.Add(new RecipeIngredient { Item = _material, Amount = 1 });
            recipe.Outputs.Add(new RecipeOutput { Item = _weapon, BaseAmount = 1 });
            Assert.That(recipe.CheckIngredients(_inventory), Is.False);
            Assert.That(recipe.CalculateMaxCraftCount(_inventory), Is.Zero);
            AssertState(_inventory.GetSlot(0), "owned", "{}");
        }

        [TestCase("definition")]
        [TestCase("instance")]
        [TestCase("type")]
        [TestCase("version")]
        [TestCase("payload")]
        public void InstanceConsumption_RequiresExactDefinitionIdentityAndPayload(string mismatch)
        {
            Seed(new InventorySlot(_weapon, State("owned")));
            var before = Snapshot();
            var item = mismatch == "definition" ? _material : _weapon;
            var state = new InventoryInstanceData(mismatch == "instance" ? "other" : "owned",
                mismatch == "type" ? "test.other" : "test.equipment", mismatch == "version" ? 2 : 1,
                mismatch == "payload" ? "{\"changed\":true}" : "{}");
            ResetObservations();
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(item, state) }, null), Is.False);
            AssertUnchanged(before);
        }

        [Test]
        public void DuplicateInstanceIds_RejectBothDuplicateRequestRowsAndExistingIdentity()
        {
            Seed(new InventorySlot(_weapon, State("owned")));
            var before = Snapshot();
            ResetObservations();
            Assert.That(_inventory.TryExchangeItems(null, new[]
            {
                new InventoryItemAmount(_weapon, State("duplicate")),
                new InventoryItemAmount(_material, State("duplicate"))
            }), Is.False);
            Assert.That(_inventory.TryExchangeItems(null,
                new[] { new InventoryItemAmount(_material, State("owned")) }), Is.False);
            Assert.That(_inventory.TryExchangeItems(new[]
            {
                new InventoryItemAmount(_weapon, State("owned")),
                new InventoryItemAmount(_weapon, State("owned"))
            }, null), Is.False);
            AssertUnchanged(before);
        }

        [Test]
        public void InstanceExchange_CannotReuseConsumedIdentityForAnotherDefinition()
        {
            Seed(new InventorySlot(_weapon, State("owned")));
            var before = Snapshot();
            ResetObservations();
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(_weapon, State("owned")) },
                new[] { new InventoryItemAmount(_material, State("owned")) }), Is.False);
            AssertUnchanged(before);
        }

        [Test]
        public void InvalidInstancePayload_RejectsInsteadOfProducingDefinitionItem()
        {
            Seed(new InventorySlot(_weapon, 1));
            var before = Snapshot();
            ResetObservations();
            foreach (var state in new[] { null, new InventoryInstanceData("", "test.equipment", 1, "{}"),
                new InventoryInstanceData("id", "", 1, "{}"), new InventoryInstanceData("id", "type", 0, "{}"),
                new InventoryInstanceData("id", "type", 1, " ") })
                Assert.That(_inventory.TryExchangeItems(null, new[] { new InventoryItemAmount(_weapon, state) }), Is.False);
            AssertUnchanged(before);
        }

        [Test]
        public void SlotCloneSwapAndMerge_PreserveIdentityAndClearRemovedPayload()
        {
            var original = new InventorySlot(_material, State("original"));
            var clone = original.Clone();
            Assert.That(clone.InstanceData, Is.Not.SameAs(original.InstanceData));
            clone.InstanceData = State("clone");
            AssertState(original, "original", "{}");
            var definition = new InventorySlot(_material, 3);
            original.SwapWith(definition);
            Assert.That(original.HasInstance, Is.False);
            Assert.That(original.Amount, Is.EqualTo(3));
            AssertState(definition, "original", "{}");
            Assert.That(original.TryMergeInto(definition), Is.EqualTo(3));
            Assert.That(definition.TryMergeInto(clone), Is.EqualTo(1));
            var empty = new InventorySlot();
            Assert.That(definition.TryMergeInto(empty), Is.Zero);
            Assert.That(definition.HasInstance, Is.False);
            AssertState(empty, "original", "{}");
            empty.Set(_material, 2);
            Assert.That(empty.HasInstance, Is.False);
            clone.Clear();
            Assert.That(clone.HasInstance, Is.False);
        }

        [Test]
        public void SwapAndSort_KeepBothInstancesAndTheirPayloads()
        {
            Seed(new InventorySlot(_weapon, State("a", "{\"value\":1}")),
                new InventorySlot(_weapon, State("b", "{\"value\":2}")));
            _inventory.SwapSlots(0, 1);
            AssertState(_inventory.GetSlot(0), "b", "{\"value\":2}");
            _inventory.Sort((left, right) => string.Compare(left.InstanceData.InstanceId, right.InstanceData.InstanceId,
                StringComparison.Ordinal));
            AssertState(_inventory.GetSlot(0), "a", "{\"value\":1}");
            AssertState(_inventory.GetSlot(1), "b", "{\"value\":2}");
            Assert.That(_inventory.GetSlot(0).SlotIndex, Is.Zero);
            Assert.That(_inventory.GetSlot(1).SlotIndex, Is.EqualTo(1));
        }

        [Test]
        public void ImportAndExport_CloneListsSlotsAndPayloadsIndependently()
        {
            var source = new List<InventorySlot> { new InventorySlot(_weapon, State("owned")) };
            Assert.That(_inventory.CanImportSaveData(source), Is.True);
            _inventory.ImportSaveData(source);
            Assert.That(_inventory.GetSlot(0), Is.Not.SameAs(source[0]));
            Assert.That(_inventory.GetSlot(0).InstanceData, Is.Not.SameAs(source[0].InstanceData));
            source[0].Clear();
            source.Clear();
            AssertState(_inventory.GetSlot(0), "owned", "{}");
            var exported = (List<InventorySlot>)_inventory.ExportSaveData();
            Assert.That(exported[0].InstanceData, Is.Not.SameAs(_inventory.GetSlot(0).InstanceData));
            exported[0].Clear();
            exported.Clear();
            AssertState(_inventory.GetSlot(0), "owned", "{}");
        }

        [Test]
        public void ExplicitResolverPrecheck_ValidatesWithoutRegisteringDefinitionsOrChangingLiveState()
        {
            Seed(new InventorySlot(_weapon, State("live")));
            var targetItem = Own(ScriptableObject.CreateInstance<InventoryItemSO>());
            targetItem.Id = "test.prepared";
            targetItem.MaxStack = 1;
            var target = new List<InventorySlot> { new InventorySlot(targetItem, State("target")) };
            var before = Snapshot();
            ResetObservations();
            Assert.That(_inventory.CanImportSaveData(target), Is.False);
            Assert.That(_inventory.CanImportSaveData(target, id => id == targetItem.Id ? targetItem : null), Is.True);
            Assert.That(_inventory.CanImportSaveData(target, _ => null), Is.False);
            Assert.That(_inventory.CanImportSaveData(target, null), Is.False);
            Assert.That(_inventory.GetItemData(targetItem.Id), Is.Null);
            Assert.Throws<ArgumentException>(() => _inventory.ImportSaveData(target));
            AssertUnchanged(before);
        }

        [TestCase("null-list")]
        [TestCase("null-slot")]
        [TestCase("unknown-id")]
        [TestCase("negative")]
        [TestCase("overflow")]
        [TestCase("empty-with-state")]
        [TestCase("instance-quantity")]
        [TestCase("invalid-payload")]
        [TestCase("duplicate-instance")]
        [TestCase("capacity")]
        public void InvalidImport_LeavesLiveInventoryAndEventsUnchanged(string shape)
        {
            Seed(new InventorySlot(_weapon, State("live")));
            var before = Snapshot();
            var target = new List<InventorySlot> { new InventorySlot(_material, 2) };
            switch (shape)
            {
                case "null-list": target = null; break;
                case "null-slot": target[0] = null; break;
                case "unknown-id": target[0].ItemId = "unknown"; break;
                case "negative": target[0].Amount = -1; break;
                case "overflow": target[0].Amount = 100; break;
                case "empty-with-state": target[0] = new InventorySlot { InstanceData = State("invalid") }; break;
                case "instance-quantity": target[0] = new InventorySlot(_material, State("invalid")) { Amount = 2 }; break;
                case "invalid-payload": target[0].InstanceData = new InventoryInstanceData("id", "", 1, "{}"); break;
                case "duplicate-instance": target = new List<InventorySlot>
                    { new InventorySlot(_weapon, State("same")), new InventorySlot(_material, State("same")) }; break;
                case "capacity": while (target.Count <= InventoryManager.MaxSlots) target.Add(new InventorySlot()); break;
            }
            ResetObservations();
            Assert.That(_inventory.CanImportSaveData(target), Is.False);
            Assert.Throws<ArgumentException>(() => _inventory.ImportSaveData(target));
            AssertUnchanged(before);
        }

        [Test]
        public void InvalidImportObjectType_DoesNotResetInventory()
        {
            Seed(new InventorySlot(_weapon, State("live")));
            var before = Snapshot();
            ResetObservations();
            Assert.Throws<ArgumentException>(() => _inventory.ImportSaveData(new object()));
            AssertUnchanged(before);
        }

        [Test]
        public void SerializedSlotList_RoundTripsStablePayloadAndRelinksRegisteredDefinition()
        {
            var wrapper = new SavedInventory { Slots = new List<InventorySlot>
                { new InventorySlot(_weapon, State("persisted", "{\"gems\":[\"ruby\"]}")) } };
            var serialized = JsonUtility.ToJson(wrapper);
            var loaded = JsonUtility.FromJson<SavedInventory>(serialized);
            Assert.That(loaded.Slots[0].ItemData, Is.Null);
            AssertState(loaded.Slots[0], "persisted", "{\"gems\":[\"ruby\"]}");
            Assert.That(loaded.Slots[0].InstanceData.PayloadType, Is.EqualTo("test.equipment"));
            Assert.That(loaded.Slots[0].InstanceData.PayloadVersion, Is.EqualTo(1));
            _inventory.ImportSaveData(loaded.Slots);
            Assert.That(_inventory.GetSlot(0).ItemData, Is.SameAs(_weapon));
        }

        private void OnInventoryUpdated() => _notifications++;
        private void ResetObservations() { _notifications = 0; _saves = 0; }
        private void AssertUnchanged(string before)
        {
            Assert.That(Snapshot(), Is.EqualTo(before));
            Assert.That(_notifications, Is.Zero);
            Assert.That(_saves, Is.Zero);
        }
        private static void AssertState(InventorySlot slot, string id, string json)
        {
            Assert.That(slot.Amount, Is.EqualTo(1));
            Assert.That(slot.HasInstance, Is.True);
            Assert.That(slot.InstanceData.InstanceId, Is.EqualTo(id));
            Assert.That(slot.InstanceData.PayloadJson, Is.EqualTo(json));
        }
        private static InventoryInstanceData State(string id, string json = "{}")
            => new InventoryInstanceData(id, "test.equipment", 1, json);
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }
        private InventoryItemSO Item(string id, int maxStack)
        {
            var item = Own(ScriptableObject.CreateInstance<InventoryItemSO>());
            item.Id = id;
            item.ItemName = id;
            item.MaxStack = maxStack;
            _inventory.RegisterItem(item);
            return item;
        }
        private void Seed(params InventorySlot[] slots) => _inventory.ImportSaveData(new List<InventorySlot>(slots));
        private void Fill(InventorySlot first)
        {
            var slots = new List<InventorySlot> { first };
            while (slots.Count < InventoryManager.MaxSlots) slots.Add(new InventorySlot(_weapon, 1));
            _inventory.ImportSaveData(slots);
        }
        private string Snapshot() => JsonUtility.ToJson(new SavedInventory
            { Slots = (List<InventorySlot>)_inventory.ExportSaveData() });

        [Serializable]
        private sealed class SavedInventory
        {
            public List<InventorySlot> Slots;
        }
    }
}
