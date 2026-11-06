using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.Save;
using Object = UnityEngine.Object;

namespace ZeroEngine.Economy.Tests
{
    [TestFixture, Category("Unit")]
    public sealed class InventoryExchangeParticipantTests
    {
        private readonly List<Object> _owned = new();
        private InventoryManager _inventory;
        private InventoryItemSO _item;
        private int _notifications, _saves, _savedPeer, _savedCount, _saveRoots, _slotRoots;
        private Peer _peer;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            _saveRoots = Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _slotRoots = Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var root = new GameObject("OwnedInventoryParticipant"); _owned.Add(root);
            _inventory = root.AddComponent<InventoryManager>();
            _inventory.ConfigurePersistence(persist: () => { _saves++; _savedPeer = _peer.Value; _savedCount = _inventory.GetItemCount(_item); });
            _item = ScriptableObject.CreateInstance<InventoryItemSO>(); _owned.Add(_item);
            _item.Id = "test.gift"; _item.ItemName = "test.gift"; _item.MaxStack = 10;
            _inventory.RegisterItem(_item);
            _inventory.ImportSaveData(new List<InventorySlot> { new InventorySlot(_item, 1) });
            _inventory.OnItemRemoved += (_, _) => _notifications++;
            _peer = new Peer(); _notifications = 0; _saves = 0; _savedPeer = -1; _savedCount = -1;
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear(); _inventory = null; _item = null;
            Assert.That(Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(_saveRoots));
            Assert.That(Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(_slotRoots));
        }

        [Test]
        public void Exchange_InsufficientInventory_DoesNotPrepareOrCommitPeer()
        {
            Assert.That(_inventory.TryExchangeItems(new[] { new InventoryItemAmount(_item, 2) }, null, _peer, false), Is.False);
            Assert.That(_peer.Prepares, Is.Zero); Assert.That(_peer.Commits, Is.Zero);
            AssertUnchanged();
        }

        [Test]
        public void Exchange_StalePeer_LeavesInventoryAndNotificationsUntouched()
        {
            _peer.Allow = false;
            Assert.That(_inventory.TryExchangeItems(Cost(), null, _peer, false), Is.False);
            Assert.That(_peer.Prepares, Is.EqualTo(1)); Assert.That(_peer.Commits, Is.Zero);
            AssertUnchanged();
        }

        [Test]
        public void Exchange_Success_ExposesBothCommittedStatesToSavingAndObservers()
        {
            _peer.BeforeCommit = () => Assert.That(_inventory.GetItemCount(_item), Is.Zero);
            var observed = false;
            _inventory.OnItemRemoved += (_, _) => { observed = true; Assert.That(_peer.Value, Is.EqualTo(42)); };
            Assert.That(_inventory.TryExchangeItems(Cost(), null, _peer), Is.True);
            Assert.That(_peer.Commits, Is.EqualTo(1)); Assert.That(_peer.Rollbacks, Is.Zero);
            Assert.That(observed, Is.True); Assert.That(_notifications, Is.EqualTo(1));
            Assert.That(_saves, Is.EqualTo(1)); Assert.That(_savedPeer, Is.EqualTo(42)); Assert.That(_savedCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Exchange_CommitThrows_RestoresInventoryAndPeerBeforeAnyNotification(bool afterWrite)
        {
            _peer.ThrowCommit = true; _peer.WriteBeforeThrow = afterWrite;
            Assert.Throws<InvalidOperationException>(() => _inventory.TryExchangeItems(Cost(), null, _peer, false));
            Assert.That(_peer.Rollbacks, Is.EqualTo(1));
            AssertUnchanged();
        }

        [Test]
        public void Exchange_RollbackThrows_PreservesBothErrorsAndRestoresInventory()
        {
            _peer.ThrowCommit = true; _peer.WriteBeforeThrow = true; _peer.ThrowRollback = true;
            var error = Assert.Throws<AggregateException>(() => _inventory.TryExchangeItems(Cost(), null, _peer, false));
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(error.InnerExceptions[0].Message, Is.EqualTo("peer commit"));
            Assert.That(error.InnerExceptions[1].Message, Is.EqualTo("peer rollback"));
            AssertUnchanged();
        }

        [Test]
        public void Exchange_MissingParticipant_RefusesWithoutChangingInventory()
        {
            Assert.Throws<ArgumentNullException>(() => _inventory.TryExchangeItems(Cost(), null, (IInventoryExchangeParticipant)null, false));
            AssertUnchanged();
        }

        private InventoryItemAmount[] Cost() => new[] { new InventoryItemAmount(_item, 1) };
        private void AssertUnchanged()
        {
            Assert.That(_inventory.GetItemCount(_item), Is.EqualTo(1)); Assert.That(_peer.Value, Is.Zero);
            Assert.That(_notifications, Is.Zero); Assert.That(_saves, Is.Zero);
        }

        private sealed class Peer : IInventoryExchangeParticipant
        {
            public bool Allow = true, ThrowCommit, WriteBeforeThrow, ThrowRollback;
            public int Value, Prepares, Commits, Rollbacks;
            public Action BeforeCommit;
            public bool TryPrepare() { Prepares++; return Allow; }
            public void Commit()
            {
                Commits++; BeforeCommit?.Invoke();
                if (WriteBeforeThrow) Value = 42;
                if (ThrowCommit) throw new InvalidOperationException("peer commit");
                Value = 42;
            }
            public void Rollback()
            {
                Rollbacks++; Value = 0;
                if (ThrowRollback) throw new InvalidOperationException("peer rollback");
            }
        }
    }
}
