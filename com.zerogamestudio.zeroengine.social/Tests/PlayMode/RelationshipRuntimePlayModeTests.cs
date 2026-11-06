using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Inventory;
using ZeroEngine.Relationship;
using ZeroEngine.Save;
using Object = UnityEngine.Object;

namespace ZeroEngine.Social.Tests.PlayMode
{
    public sealed class RelationshipRuntimePlayModeTests
    {
        private readonly List<Object> _owned = new();
        private int _saveRoots, _slotRoots;
        private T Own<T>(T obj) where T : Object { _owned.Add(obj); return obj; }

        [UnityTest]
        public IEnumerator PackageGrowthFlow_ExplicitHost_GiftCalendarSaveRestoreAndRetirement()
        {
            Assert.That(Application.isPlaying, Is.True);
            Assert.That(Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            _saveRoots = Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            _slotRoots = Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var day = new DateTime(2026, 1, 1);
            var item = Own(ScriptableObject.CreateInstance<InventoryItemSO>()); item.Id = "runtime.herb"; item.MaxStack = 10;
            var npc = Own(ScriptableObject.CreateInstance<RelationshipDataSO>()); npc.NpcId = "runtime.teacher";
            npc.Thresholds = new List<RelationshipThreshold> { new RelationshipThreshold { Level = RelationshipLevel.Stranger, RequiredPoints = 0 } };
            npc.DefaultGiftPoints = 5; npc.DailyDecay = 1;
            var inventoryRoot = Own(new GameObject("OwnedRuntimeInventory"));
            var inventory = inventoryRoot.AddComponent<InventoryManager>(); inventory.ConfigurePersistence(); inventory.RegisterItem(item);
            inventory.ImportSaveData(new List<InventorySlot> { new InventorySlot(item, 2) });
            var hostRoot = Own(new GameObject("OwnedRelationshipHost")); var host = hostRoot.AddComponent<RelationshipManager>();
            host.Configure("runtime.hero", new[] { npc }, () => day);
            var state = host.State; var frame = Time.frameCount;
            yield return null;
            Assert.That(Time.frameCount, Is.GreaterThan(frame));
            var observedBoth = false;
            inventory.OnItemRemoved += (_, _) => { observedBoth = state.GetPoints(npc.NpcId) == 5 && state.GetSnapshot().LastReceipt != null; };
            Assert.That(state.TryGiveGift(state.CaptureContext("runtime.gift"), npc.NpcId, item, inventory, out _, out _), Is.True);
            Assert.That(observedBoth, Is.True); Assert.That(inventory.GetItemCount(item), Is.EqualTo(1));
            var participant = host.CreateSaveParticipant("runtime.relationship"); var snapshot = participant.Capture();
            var stale = state.CaptureContext("before-restore");
            state.TryChangePoints(state.CaptureContext("runtime.points"), npc.NpcId, 7, out _, out _);
            participant.PrepareRestoreAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult(); participant.Restore(snapshot);
            Assert.That(state.GetPoints(npc.NpcId), Is.EqualTo(5));
            Assert.That(state.TryTalk(stale, npc.NpcId, out _, out _), Is.False);
            day = day.AddDays(1); Assert.That(state.AdvanceGameDate(), Is.True);
            Assert.That(state.GetPoints(npc.NpcId), Is.EqualTo(4)); Assert.That(state.GetProgress(npc.NpcId).GiftCountToday, Is.Zero);
            Object.Destroy(hostRoot);
            yield return null;
            Assert.Throws<ObjectDisposedException>(() => state.CaptureContext("retired"));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            for (var i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.Destroy(_owned[i]);
            _owned.Clear(); yield return null;
            Assert.That(Object.FindObjectsByType<SaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(_saveRoots));
            Assert.That(Object.FindObjectsByType<SaveSlotManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(_slotRoots));
        }
    }
}
