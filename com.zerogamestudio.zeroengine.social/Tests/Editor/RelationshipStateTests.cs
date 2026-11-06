using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.Relationship;
using ZeroEngine.Save;
using Object = UnityEngine.Object;

namespace ZeroEngine.Social.Tests
{
    [TestFixture, Category("Unit")]
    public sealed class RelationshipStateTests
    {
        private readonly List<Object> _owned = new();
        private readonly List<RelationshipState> _states = new();
        private RelationshipDataSO _npc;
        private InventoryManager _inventory;
        private InventoryItemSO _gift;
        private DateTime _day;
        private int _saveRoots, _slotRoots, _notifications;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            _saveRoots = Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _slotRoots = Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _day = new DateTime(2026, 1, 1);
            var root = new GameObject("OwnedRelationshipInventory"); _owned.Add(root);
            _inventory = root.AddComponent<InventoryManager>(); _inventory.ConfigurePersistence();
            _gift = ScriptableObject.CreateInstance<InventoryItemSO>(); _owned.Add(_gift);
            _gift.Id = "item.herb"; _gift.ItemName = "item.herb"; _gift.MaxStack = 10; _inventory.RegisterItem(_gift);
            _inventory.ImportSaveData(new List<InventorySlot> { new InventorySlot(_gift, 2) });
            _inventory.OnItemRemoved += (_, _) => _notifications++;
            _npc = ScriptableObject.CreateInstance<RelationshipDataSO>(); _owned.Add(_npc);
            _npc.NpcId = "npc.teacher"; _npc.DisplayName = "teacher"; _npc.MaxGiftsPerDay = 2;
            _npc.MaxTalksPerDay = 3; _npc.TalkPoints = 2; _npc.DailyDecay = 1;
            // CreateInstance invokes the editor Reset defaults; this fixture owns a complete synthetic table.
            _npc.Thresholds = new List<RelationshipThreshold>();
            _npc.Thresholds.Add(new RelationshipThreshold { Level = RelationshipLevel.Stranger, RequiredPoints = 0 });
            _npc.Thresholds.Add(new RelationshipThreshold { Level = RelationshipLevel.Acquaintance, RequiredPoints = 5 });
            _npc.Thresholds.Add(new RelationshipThreshold { Level = RelationshipLevel.Friend, RequiredPoints = 10 });
            _npc.LikedGifts.Add(new GiftData { Item = _gift, Preference = GiftPreference.Loved, PointsChange = 15 });
            _npc.Events.Add(new RelationshipEvent { EventId = "event.friend", OneTime = true, RequiredPoints = 10, RequiredLevel = RelationshipLevel.Friend });
            _notifications = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var state in _states) state.Dispose(); _states.Clear();
            for (var i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear(); _inventory = null; _gift = null; _npc = null;
            Assert.That(Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(_saveRoots));
            Assert.That(Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(_slotRoots));
        }

        [Test]
        public void TwoOwners_SharingNpcDefinitions_KeepIndependentProgressAndCounters()
        {
            var first = State("first"); var second = State("second");
            Assert.That(first.TryTalk(first.CaptureContext("talk"), _npc.NpcId, out _, out _), Is.True);
            Assert.That(first.GetPoints(_npc.NpcId), Is.EqualTo(2));
            Assert.That(second.GetPoints(_npc.NpcId), Is.Zero);
            Assert.That(second.GetProgress(_npc.NpcId).TalkCountToday, Is.Zero);
            Assert.That(second.GetSnapshot().ProgressData, Is.Empty);
        }

        [Test]
        public void Gift_MissingExplicitInventory_DoesNotBorrowGlobalInventory()
        {
            var state = State();
            Assert.That(state.TryGiveGift(state.CaptureContext("gift"), _npc.NpcId, _gift, null, out _, out _), Is.False);
            Assert.That(_inventory.GetItemCount(_gift), Is.EqualTo(2));
            Assert.That(state.GetSnapshot().ProgressData, Is.Empty);
        }

        [Test]
        public void Gift_InsufficientCost_LeavesRelationshipCountersAndReceiptUntouched()
        {
            var state = State(); _inventory.ImportSaveData(new List<InventorySlot>());
            Assert.That(state.TryGiveGift(state.CaptureContext("gift"), _npc.NpcId, _gift, _inventory, out _, out _), Is.False);
            Assert.That(state.Revision, Is.Zero); Assert.That(state.GetSnapshot().LastReceipt, Is.Null);
            Assert.That(state.GetProgress(_npc.NpcId).GiftCountToday, Is.Zero); Assert.That(_notifications, Is.Zero);
        }

        [Test]
        public void Gift_InventoryObservers_SeeRelationshipAndReceiptAlreadyCommitted()
        {
            var state = State(); var context = state.CaptureContext("gift"); var observed = false;
            _inventory.OnItemRemoved += (_, _) =>
            {
                observed = true;
                Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(15));
                Assert.That(state.GetProgress(_npc.NpcId).GiftCountToday, Is.EqualTo(1));
                Assert.That(state.GetSnapshot().LastReceipt.RequestId, Is.EqualTo("gift"));
            };
            Assert.That(state.TryGiveGift(context, _npc.NpcId, _gift, _inventory, out var receipt, out _), Is.True);
            Assert.That(observed, Is.True); Assert.That(receipt.Replayed, Is.False);
            Assert.That(_inventory.GetItemCount(_gift), Is.EqualTo(1));
        }

        [Test]
        public void Gift_RepeatedAndReentrantRequest_DeductsAndGrantsOnlyOnce()
        {
            var state = State(); var context = state.CaptureContext("gift"); var reentered = false;
            _inventory.OnItemRemoved += (_, _) =>
            {
                reentered = true;
                Assert.That(state.TryGiveGift(context, _npc.NpcId, _gift, _inventory, out var replay, out _), Is.True);
                Assert.That(replay.Replayed, Is.True);
            };
            Assert.That(state.TryGiveGift(context, _npc.NpcId, _gift, _inventory, out _, out _), Is.True);
            Assert.That(state.TryGiveGift(context, _npc.NpcId, _gift, _inventory, out var second, out _), Is.True);
            Assert.That(second.Replayed, Is.True); Assert.That(reentered, Is.True);
            Assert.That(_notifications, Is.EqualTo(1)); Assert.That(_inventory.GetItemCount(_gift), Is.EqualTo(1));
            Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(15));
        }

        [Test]
        public void Command_OldRevisionOrDifferentOwnerEpoch_IsRejected()
        {
            var first = State(); var second = State(); var old = first.CaptureContext("old");
            Assert.That(first.TryTalk(first.CaptureContext("new"), _npc.NpcId, out _, out _), Is.True);
            Assert.That(first.TryTalk(old, _npc.NpcId, out _, out _), Is.False);
            Assert.That(second.TryTalk(first.CaptureContext("wrong-owner"), _npc.NpcId, out _, out _), Is.False);
            Assert.That(first.GetPoints(_npc.NpcId), Is.EqualTo(2)); Assert.That(second.GetPoints(_npc.NpcId), Is.Zero);
        }

        [Test]
        public void Command_ConflictingRequestIdPayload_IsRejectedWithoutMutation()
        {
            var state = State(); var context = state.CaptureContext("same");
            state.TryChangePoints(context, _npc.NpcId, 5, out _, out _);
            Assert.That(state.TryChangePoints(context, _npc.NpcId, 6, out _, out _), Is.False);
            Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(5));
        }

        [Test]
        public void Calendar_GameDateAdvance_ResetsCountersAndAppliesEachElapsedDaysDecay()
        {
            var state = State();
            state.TryGiveGift(state.CaptureContext("gift"), _npc.NpcId, _gift, _inventory, out _, out _);
            state.TryTalk(state.CaptureContext("talk"), _npc.NpcId, out _, out _);
            _day = _day.AddDays(3);
            Assert.That(state.TryTalk(state.CaptureContext("not-advanced"), _npc.NpcId, out _, out _), Is.False);
            Assert.That(state.AdvanceGameDate(), Is.True);
            var row = state.GetProgress(_npc.NpcId);
            Assert.That(row.Points, Is.EqualTo(14)); Assert.That(row.GiftCountToday, Is.Zero); Assert.That(row.TalkCountToday, Is.Zero);
            _day = _day.AddDays(-1);
            Assert.That(state.AdvanceGameDate(), Is.False);
            Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(14));
        }

        [Test]
        public void QueriesAndSnapshots_DeepCopyMutableProgressAndReceipt()
        {
            var state = State(); state.TryGiveGift(state.CaptureContext("gift"), _npc.NpcId, _gift, _inventory, out _, out _);
            var row = state.GetProgress(_npc.NpcId); row.Points = 999; row.TriggeredEvents.Add("external"); row.CustomData["external"] = "value";
            var saved = state.GetSnapshot(); saved.ProgressData[_npc.NpcId].Points = 888; saved.LastReceipt.RequestId = "changed";
            Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(15));
            Assert.That(state.GetProgress(_npc.NpcId).TriggeredEvents, Is.Empty);
            Assert.That(state.GetProgress(_npc.NpcId).CustomData, Is.Empty);
            Assert.That(state.GetSnapshot().LastReceipt.RequestId, Is.EqualTo("gift"));
        }

        [TestCase("owner")]
        [TestCase("unknown-npc")]
        [TestCase("negative-points")]
        [TestCase("negative-count")]
        [TestCase("missing-events")]
        [TestCase("missing-custom")]
        [TestCase("invalid-date")]
        [TestCase("duplicate-events")]
        [TestCase("missing-receipt-npc")]
        public void Restore_InvalidSnapshot_PreservesLiveProgressAndRevision(string defect)
        {
            var state = State(); state.TryGiveGift(state.CaptureContext("gift"), _npc.NpcId, _gift, _inventory, out _, out _);
            var revision = state.Revision; var saved = state.GetSnapshot(); var row = saved.ProgressData[_npc.NpcId];
            switch (defect)
            {
                case "owner": saved.OwnerId = "other"; break;
                case "unknown-npc": saved.ProgressData["unknown"] = row; break;
                case "negative-points": row.Points = -1; break;
                case "negative-count": row.GiftCountToday = -1; break;
                case "missing-events": row.TriggeredEvents = null; break;
                case "missing-custom": row.CustomData = null; break;
                case "invalid-date": saved.Date = "not-a-day"; break;
                case "duplicate-events": row.TriggeredEvents.Add("event.friend"); row.TriggeredEvents.Add("event.friend"); break;
                case "missing-receipt-npc": saved.LastReceipt.NpcId = null; break;
            }
            Assert.That(state.TryRestore(saved, out _), Is.False);
            Assert.That(state.Revision, Is.EqualTo(revision)); Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(15));
        }

        [Test]
        public void Restore_DeepCopiesSavedStateAndExpiresPreLoadCommands()
        {
            var state = State(); state.TryTalk(state.CaptureContext("talk"), _npc.NpcId, out _, out _);
            var old = state.CaptureContext("before-load"); var saved = state.GetSnapshot();
            Assert.That(state.TryRestore(saved, out _), Is.True);
            saved.ProgressData[_npc.NpcId].Points = 999;
            Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(2));
            Assert.That(state.TryTalk(old, _npc.NpcId, out _, out _), Is.False);
        }

        [Test]
        public void SaveParticipant_PreparesWithoutMutationAndUsesInjectedOwnerKey()
        {
            var state = State(); state.TryTalk(state.CaptureContext("talk"), _npc.NpcId, out _, out _);
            var participant = new RelationshipSaveParticipant("relationship.hero", state);
            var snapshot = (RelationshipStateSnapshot)participant.Capture(); var before = state.CaptureContext("before");
            participant.PrepareRestoreAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(state.Revision, Is.EqualTo(before.Revision)); Assert.That(participant.Key, Is.EqualTo("relationship.hero"));
            participant.Restore(snapshot);
            Assert.That(state.TryTalk(before, _npc.NpcId, out _, out _), Is.False);
            Assert.That(state.GetPoints(_npc.NpcId), Is.EqualTo(2));
        }

        [Test]
        public void DefinitionGiftPreference_UsesStableItemIdentityAcrossReloadedObjects()
        {
            var clone = ScriptableObject.CreateInstance<InventoryItemSO>(); _owned.Add(clone); clone.Id = _gift.Id;
            Assert.That(_npc.CalculateGiftPoints(clone, out var preference), Is.EqualTo(15));
            Assert.That(preference, Is.EqualTo(GiftPreference.Loved));
        }

        private RelationshipState State(string ownerId = "hero")
        {
            var state = new RelationshipState(ownerId, id => id == _npc.NpcId ? _npc : null, () => _day);
            _states.Add(state);
            Assert.That(state.GetProgress(_npc.NpcId), Is.Not.Null, "The synthetic NPC configuration must be valid before exercising behavior.");
            return state;
        }
    }
}
