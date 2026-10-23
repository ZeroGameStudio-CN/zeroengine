using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Equipment;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Tests.Character
{
    [TestFixture]
    [Category("Unit")]
    public sealed class EquipmentLoadoutTests
    {
        private const string Weapon = "weapon";
        private const string Armor = "armor";

        [Test]
        public void TryEquip_TwoCharactersSharingContent_KeepIndependentStatsAndSlots()
        {
            var firstStats = CreateStats();
            var secondStats = CreateStats();
            using var first = CreateLoadout(firstStats);
            using var second = CreateLoadout(secondStats);
            var sword = Item(Weapon, "attack", 10f);

            Assert.That(first.TryEquip(sword, out _), Is.True);
            Assert.That(second.TryEquip(sword, out _), Is.True);
            Assert.That(first.Unequip(Weapon), Is.SameAs(sword));

            Assert.That(firstStats["attack"].Value, Is.EqualTo(100f));
            Assert.That(secondStats["attack"].Value, Is.EqualTo(110f));
            Assert.That(first.Equipped, Is.Empty);
            Assert.That(second.GetEquipped(Weapon), Is.SameAs(sword));
        }

        [Test]
        public void TryEquip_ReplacementAndRepeatedEquip_DoNotStackAndPreserveOtherSources()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var unrelated = new StatModifier(3f, StatModType.Flat);
            stats["attack"].AddModifier(unrelated);
            var old = Item(Weapon, "attack", 10f);
            var next = Item(Weapon, "attack", 25f);
            loadout.TryEquip(old, out _);

            Assert.That(loadout.TryEquip(next, out var replaced), Is.True);
            Assert.That(replaced, Is.SameAs(old));
            Assert.That(loadout.TryEquip(next, out _), Is.True);
            Assert.That(stats["attack"].Value, Is.EqualTo(128f));
            Assert.That(stats["attack"].ModifierCount, Is.EqualTo(2));
            Assert.That(loadout.Equipped.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryEquip_UnavailableSlotOrRejectedEligibility_PreserveExistingEquipment()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var old = Item(Weapon, "attack", 10f);
            loadout.TryEquip(old, out _);
            var rejected = Item(Weapon, "attack", 25f);
            rejected.Eligible = false;

            Assert.That(loadout.TryEquip(rejected, out var replaced), Is.False);
            Assert.That(replaced, Is.Null);
            Assert.That(loadout.TryEquip(Item("invalid", "attack", 30f), out _), Is.False);
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(old));
            Assert.That(stats["attack"].Value, Is.EqualTo(110f));
        }

        [Test]
        public void TryEquip_UnresolvedOrInvalidModifier_RejectsBeforeChangingOldStats()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var old = Item(Weapon, "attack", 10f);
            loadout.TryEquip(old, out _);
            var next = Item(Weapon, "attack", 25f);
            next.Modifiers.Add(new EquipmentStatModifier("missing", new StatModifier(5f, StatModType.Flat)));

            Assert.That(loadout.TryEquip(next, out _), Is.False);
            next.Modifiers[1] = new EquipmentStatModifier("defense", new StatModifier(float.NaN, StatModType.Flat));
            Assert.That(loadout.TryEquip(next, out _), Is.False);
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(old));
            Assert.That(stats["attack"].Value, Is.EqualTo(110f));
            Assert.That(stats["attack"].ModifierCount, Is.EqualTo(1));
        }

        [Test]
        public void TryEquip_StatThrowsAfterPartialApplication_RollsBackSlotsStatsAndResources()
        {
            var stats = CreateStats();
            var failing = new ThrowOnceStat(50f);
            stats["defense"] = failing;
            using var loadout = CreateLoadout(stats);
            var hp = (CurrentStat)stats["hp"];
            var old = Item(Weapon, "hp", 100f);
            loadout.TryEquip(old, out _);
            var next = Item(Weapon, "hp", 200f);
            next.Modifiers.Add(new EquipmentStatModifier("defense", new StatModifier(5f, StatModType.Flat)));
            failing.ThrowOnNextAdd = true;

            Assert.That(loadout.TryEquip(next, out var replaced), Is.False);
            Assert.That(replaced, Is.Null);
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(old));
            Assert.That(hp.MaxValue, Is.EqualTo(200f));
            Assert.That(hp.CurrentValue, Is.EqualTo(100f));
            Assert.That(hp.ModifierCount, Is.EqualTo(1));
            Assert.That(failing.ModifierCount, Is.Zero);
        }

        [Test]
        public void TryEquip_ExternalCommitRejects_RestoresOldSlotStatsAndCurrentResourceValue()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var hp = (CurrentStat)stats["hp"];
            var old = Item(Weapon, "hp", 100f);
            var next = Item(Weapon, "hp", 300f);
            loadout.TryEquip(old, out _);
            var called = 0;
            var stagedCorrectly = false;

            Assert.That(loadout.TryEquip(next, out var replaced, () =>
            {
                called++;
                stagedCorrectly = ReferenceEquals(loadout.GetEquipped(Weapon), next) &&
                    hp.MaxValue == 400f && hp.CurrentValue == 200f;
                return false;
            }), Is.False);

            Assert.That(stagedCorrectly, Is.True);
            Assert.That(called, Is.EqualTo(1));
            Assert.That(replaced, Is.Null);
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(old));
            Assert.That(hp.MaxValue, Is.EqualTo(200f));
            Assert.That(hp.CurrentValue, Is.EqualTo(100f));
            Assert.That(hp.ModifierCount, Is.EqualTo(1));
        }

        [Test]
        public void TryEquip_ExternalCommitRejectsEmptySlot_RemovesStagedState()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);

            Assert.That(loadout.TryEquip(Item(Weapon, "attack", 10f), out _, () => false), Is.False);

            Assert.That(loadout.Equipped, Is.Empty);
            Assert.That(stats["attack"].Value, Is.EqualTo(100f));
            Assert.That(stats["attack"].ModifierCount, Is.Zero);
        }

        [Test]
        public void Unequip_ExternalCommitRejects_RestoresOldSlotAndStats()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var item = Item(Weapon, "attack", 10f);
            loadout.TryEquip(item, out _);
            var called = 0;
            var stagedCorrectly = false;

            Assert.That(loadout.Unequip(Weapon, () =>
            {
                called++;
                stagedCorrectly = loadout.GetEquipped(Weapon) == null && stats["attack"].Value == 100f;
                return false;
            }), Is.Null);

            Assert.That(stagedCorrectly, Is.True);
            Assert.That(called, Is.EqualTo(1));
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(item));
            Assert.That(stats["attack"].Value, Is.EqualTo(110f));
            Assert.That(stats["attack"].ModifierCount, Is.EqualTo(1));
        }

        [Test]
        public void TryEquip_ExternalCommitSucceeds_RejectsReentrantWritesAndCommitsOnce()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var item = Item(Weapon, "attack", 10f);
            var called = 0;
            var rejectedReentry = false;

            Assert.That(loadout.TryEquip(item, out _, () =>
            {
                called++;
                rejectedReentry = !loadout.TryEquip(Item(Armor, "defense", 10f), out _) &&
                    loadout.Unequip(Weapon) == null && !loadout.TryRestore(Array.Empty<TestEquipment>()) &&
                    !loadout.RefreshStats();
                return true;
            }), Is.True);

            Assert.That(rejectedReentry, Is.True);
            Assert.That(called, Is.EqualTo(1));
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(item));
            Assert.That(loadout.GetEquipped(Armor), Is.Null);
            Assert.That(loadout.Unequip(Weapon, () => true), Is.SameAs(item));
            Assert.That(stats["attack"].ModifierCount, Is.Zero);
        }

        [Test]
        public void TryEquip_RejectedBeforeStaging_DoesNotCallExternalCommit()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var item = Item(Weapon, "missing", 10f);
            var called = false;

            Assert.That(loadout.TryEquip(item, out _, () => { called = true; return true; }), Is.False);

            Assert.That(called, Is.False);
            Assert.That(loadout.Equipped, Is.Empty);
        }

        [Test]
        public void TryEquip_ExternalCommitThrowsBeforeMutation_RestoresPreviousState()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var old = Item(Weapon, "attack", 10f);
            loadout.TryEquip(old, out _);

            Assert.That(loadout.TryEquip(Item(Weapon, "attack", 20f), out _,
                () => throw new InvalidOperationException("No external state was changed.")), Is.False);

            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(old));
            Assert.That(stats["attack"].Value, Is.EqualTo(110f));
            Assert.That(stats["attack"].ModifierCount, Is.EqualTo(1));
        }

        [Test]
        public void Clear_TwoSlotsWithSharedSource_RemovesOnlyOwnedModifiersAndPreservesResourcePercent()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var sharedSource = new object();
            var unrelated = new StatModifier(5f, StatModType.Flat, 100, sharedSource);
            stats["attack"].AddModifier(unrelated);
            var weapon = Item(Weapon, "hp", 100f);
            weapon.Modifiers[0].Modifier.Source = sharedSource;
            var armor = Item(Armor, "attack", 20f);
            armor.Modifiers[0].Modifier.Source = sharedSource;
            loadout.TryEquip(weapon, out _);
            loadout.TryEquip(armor, out _);
            var hp = (CurrentStat)stats["hp"];

            Assert.That(hp.MaxValue, Is.EqualTo(200f));
            Assert.That(hp.CurrentValue, Is.EqualTo(100f));
            loadout.Clear();
            loadout.Clear();

            Assert.That(loadout.Equipped, Is.Empty);
            Assert.That(hp.MaxValue, Is.EqualTo(100f));
            Assert.That(hp.CurrentValue, Is.EqualTo(50f));
            Assert.That(stats["attack"].Value, Is.EqualTo(105f));
            Assert.That(stats["attack"].GetModifiers()[0], Is.SameAs(unrelated));
        }

        [Test]
        public void TryRestore_DuplicateSlotOrUnresolvedItem_DoesNotClearCurrentLoadout()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var old = Item(Weapon, "attack", 10f);
            loadout.TryEquip(old, out _);

            Assert.That(loadout.TryRestore(new[] { Item(Armor, "defense", 5f), Item(Armor, "defense", 20f) }), Is.False);
            Assert.That(loadout.TryRestore(new[] { Item(Armor, "missing", 5f) }), Is.False);
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(old));
            Assert.That(stats["attack"].Value, Is.EqualTo(110f));
            Assert.That(stats["defense"].Value, Is.EqualTo(50f));
        }

        [Test]
        public void TryRestore_ValidSnapshot_ReplacesWholeLoadoutAndSupportsClearThenRestore()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            loadout.TryEquip(Item(Weapon, "attack", 10f), out _);
            var restored = Item(Armor, "defense", 20f);

            Assert.That(loadout.TryRestore(new[] { restored }), Is.True);
            Assert.That(loadout.GetEquipped(Weapon), Is.Null);
            Assert.That(loadout.GetEquipped(Armor), Is.SameAs(restored));
            Assert.That(stats["attack"].Value, Is.EqualTo(100f));
            Assert.That(stats["defense"].Value, Is.EqualTo(70f));
            loadout.Clear();
            Assert.That(loadout.TryRestore(new[] { restored }), Is.True);
            Assert.That(stats["defense"].Value, Is.EqualTo(70f));
            Assert.That(stats["defense"].ModifierCount, Is.EqualTo(1));
        }

        [Test]
        public void RefreshStats_RebuiltStatObjects_RebindsOnceAndRetainsEquippedItems()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var item = Item(Weapon, "attack", 10f);
            loadout.TryEquip(item, out _);
            var previous = stats["attack"];
            stats["attack"] = new Stat(200f);
            item.Eligible = false;

            Assert.That(loadout.RefreshStats(), Is.True);
            Assert.That(loadout.RefreshStats(), Is.True);
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(item));
            Assert.That(previous.Value, Is.EqualTo(100f));
            Assert.That(stats["attack"].Value, Is.EqualTo(210f));
            Assert.That(stats["attack"].ModifierCount, Is.EqualTo(1));
        }

        [Test]
        public void RefreshStats_FailedResolution_PreservesPreviouslyAppliedStats()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var item = Item(Weapon, "attack", 10f);
            loadout.TryEquip(item, out _);
            item.Modifiers.Add(new EquipmentStatModifier("missing", new StatModifier(5f, StatModType.Flat)));

            Assert.That(loadout.RefreshStats(), Is.False);
            Assert.That(stats["attack"].Value, Is.EqualTo(110f));
            Assert.That(loadout.GetEquipped(Weapon), Is.SameAs(item));
        }

        [Test]
        public void TryEquip_ContentModifierMutatedLater_DoesNotChangeAppliedSnapshot()
        {
            var stats = CreateStats();
            using var loadout = CreateLoadout(stats);
            var item = Item(Weapon, "attack", 10f);
            loadout.TryEquip(item, out _);
            item.Modifiers[0].Modifier.Value = 1000f;

            Assert.That(stats["attack"].Value, Is.EqualTo(110f));
            Assert.That(loadout.RefreshStats(), Is.True);
            Assert.That(stats["attack"].Value, Is.EqualTo(1100f));
            Assert.That(stats["attack"].ModifierCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_EquippedItems_ClearsOwnedStatsAndRejectsFurtherWrites()
        {
            var stats = CreateStats();
            var loadout = CreateLoadout(stats);
            loadout.TryEquip(Item(Weapon, "attack", 10f), out _);

            loadout.Dispose();
            loadout.Dispose();

            Assert.That(loadout.Equipped, Is.Empty);
            Assert.That(stats["attack"].Value, Is.EqualTo(100f));
            Assert.That(loadout.TryEquip(Item(Weapon, "attack", 5f), out _), Is.False);
            Assert.That(loadout.RefreshStats(), Is.False);
        }

        private static EquipmentLoadout<string, TestEquipment> CreateLoadout(Dictionary<StatId, Stat> stats)
        {
            return new EquipmentLoadout<string, TestEquipment>(new[] { Weapon, Armor },
                item => item.Slot, item => item.Modifiers,
                id => stats.TryGetValue(id, out var stat) ? stat : null,
                item => item.Eligible);
        }

        private static Dictionary<StatId, Stat> CreateStats()
        {
            return new Dictionary<StatId, Stat>
            {
                ["attack"] = new Stat(100f),
                ["defense"] = new Stat(50f),
                ["hp"] = new CurrentStat(100f, 50f)
            };
        }

        private static TestEquipment Item(string slot, StatId stat, float value)
        {
            var item = new TestEquipment { Slot = slot };
            item.Modifiers.Add(new EquipmentStatModifier(stat, new StatModifier(value, StatModType.Flat)));
            return item;
        }

        private sealed class TestEquipment
        {
            public string Slot;
            public bool Eligible = true;
            public readonly List<EquipmentStatModifier> Modifiers = new List<EquipmentStatModifier>();
        }

        private sealed class ThrowOnceStat : Stat
        {
            public bool ThrowOnNextAdd;

            public ThrowOnceStat(float value) : base(value) { }

            public override void AddModifier(StatModifier modifier)
            {
                base.AddModifier(modifier);
                if (!ThrowOnNextAdd) return;
                ThrowOnNextAdd = false;
                throw new InvalidOperationException("Synthetic failure after modifying the stat.");
            }
        }
    }
}
