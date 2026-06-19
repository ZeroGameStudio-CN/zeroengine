using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Inventory;

namespace ZeroEngine.Economy.Editor.Tests
{
    public sealed class InventorySlotTests
    {
        [Test]
        public void AddAmountClampsToItemMaxStack()
        {
            var item = CreateItem("potion", maxStack: 10);
            try
            {
                var slot = new InventorySlot(item, 7);

                slot.AddAmount(8);

                Assert.AreEqual(10, slot.Amount);
                Assert.IsTrue(slot.IsFull);
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void TryMergeIntoMovesOnlyAvailableStackSpace()
        {
            var item = CreateItem("potion", maxStack: 10);
            try
            {
                var source = new InventorySlot(item, 7);
                var target = new InventorySlot(item, 6);

                var remaining = source.TryMergeInto(target);

                Assert.AreEqual(10, target.Amount);
                Assert.AreEqual(3, source.Amount);
                Assert.AreEqual(3, remaining);
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void TryMergeIntoEmptyTargetMovesAllAndClearsSource()
        {
            var item = CreateItem("gem", maxStack: 99);
            try
            {
                var source = new InventorySlot(item, 4);
                var target = new InventorySlot();

                var remaining = source.TryMergeInto(target);

                Assert.AreEqual(0, remaining);
                Assert.IsTrue(source.IsEmpty);
                Assert.AreEqual("gem", target.ItemId);
                Assert.AreEqual(4, target.Amount);
                Assert.AreSame(item, target.ItemData);
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void CloneCopiesSlotDataWithoutSharingSlotInstance()
        {
            var item = CreateItem("ore", maxStack: 20);
            try
            {
                var slot = new InventorySlot(item, 6)
                {
                    SlotIndex = 2
                };

                var clone = slot.Clone();
                clone.RemoveAmount(2);

                Assert.AreNotSame(slot, clone);
                Assert.AreEqual(6, slot.Amount);
                Assert.AreEqual(4, clone.Amount);
                Assert.AreEqual(2, clone.SlotIndex);
                Assert.AreSame(item, clone.ItemData);
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        private static InventoryItemSO CreateItem(string id, int maxStack)
        {
            var item = ScriptableObject.CreateInstance<InventoryItemSO>();
            item.Id = id;
            item.MaxStack = maxStack;
            return item;
        }
    }
}
