using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Equipment
{
    /// <summary>
    /// Per-character equipment state, independent of scenes, save services and game content.
    /// Host selectors must be side-effect free. Resolve stats before equipping; an unresolved
    /// stat rejects the entire change. Operations are synchronous and reject reentrant writes.
    /// </summary>
    public sealed class EquipmentLoadout<TSlot, TEquipment> : IDisposable where TEquipment : class
    {
        private readonly HashSet<TSlot> _availableSlots;
        private readonly Func<TEquipment, TSlot> _getSlot;
        private readonly Func<TEquipment, IEnumerable<EquipmentStatModifier>> _getModifiers;
        private readonly Func<StatId, Stat> _resolveStat;
        private readonly Predicate<TEquipment> _canEquip;
        private readonly Dictionary<TSlot, TEquipment> _equipped = new Dictionary<TSlot, TEquipment>();
        private readonly Dictionary<TSlot, List<AppliedModifier>> _applied =
            new Dictionary<TSlot, List<AppliedModifier>>();
        private bool _changing;
        private bool _disposed;

        public IReadOnlyDictionary<TSlot, TEquipment> Equipped { get; }

        public EquipmentLoadout(
            IEnumerable<TSlot> availableSlots,
            Func<TEquipment, TSlot> getSlot,
            Func<TEquipment, IEnumerable<EquipmentStatModifier>> getModifiers,
            Func<StatId, Stat> resolveStat,
            Predicate<TEquipment> canEquip = null)
        {
            _availableSlots = new HashSet<TSlot>(availableSlots ?? throw new ArgumentNullException(nameof(availableSlots)));
            _getSlot = getSlot ?? throw new ArgumentNullException(nameof(getSlot));
            _getModifiers = getModifiers ?? throw new ArgumentNullException(nameof(getModifiers));
            _resolveStat = resolveStat ?? throw new ArgumentNullException(nameof(resolveStat));
            _canEquip = canEquip;
            Equipped = new ReadOnlyDictionary<TSlot, TEquipment>(_equipped);
        }

        public TEquipment GetEquipped(TSlot slot)
        {
            return !ReferenceEquals(slot, null) && _equipped.TryGetValue(slot, out var item) ? item : null;
        }

        /// <summary>
        /// Stages the new loadout before a synchronous external commit (for example an inventory
        /// exchange). A false result must leave external state unchanged; it restores this loadout.
        /// </summary>
        public bool TryEquip(TEquipment equipment, out TEquipment replaced, Func<bool> commitExternal = null)
        {
            replaced = null;
            if (_disposed || _changing) return false;
            _changing = true;
            try
            {
                if (!TryPrepare(equipment, true, out var slot, out var modifiers)) return false;
                foreach (var pair in _equipped)
                {
                    if (!EqualityComparer<TSlot>.Default.Equals(pair.Key, slot) && ReferenceEquals(pair.Value, equipment))
                        return false;
                }

                _applied.TryGetValue(slot, out var previous);
                _equipped.TryGetValue(slot, out var previousItem);
                if (!TryChangeModifiers(previous, modifiers,
                    () =>
                    {
                        _equipped[slot] = equipment;
                        _applied[slot] = modifiers;
                        return commitExternal == null || commitExternal();
                    },
                    () =>
                    {
                        if (previousItem == null)
                        {
                            _equipped.Remove(slot);
                            _applied.Remove(slot);
                        }
                        else
                        {
                            _equipped[slot] = previousItem;
                            _applied[slot] = previous;
                        }
                    })) return false;
                replaced = previousItem;
                return true;
            }
            finally { _changing = false; }
        }

        /// <summary>Stages removal before an optional external commit with the same failure contract.</summary>
        public TEquipment Unequip(TSlot slot, Func<bool> commitExternal = null)
        {
            if (_disposed || _changing || ReferenceEquals(slot, null) || !_equipped.TryGetValue(slot, out var item))
                return null;
            _changing = true;
            try
            {
                var previous = _applied[slot];
                if (!TryChangeModifiers(previous, null,
                    () =>
                    {
                        _equipped.Remove(slot);
                        _applied.Remove(slot);
                        return commitExternal == null || commitExternal();
                    },
                    () =>
                    {
                        _equipped[slot] = item;
                        _applied[slot] = previous;
                    })) return null;
                return item;
            }
            finally { _changing = false; }
        }

        /// <summary>Atomically replaces a loadout, for example after resolving saved content IDs.</summary>
        public bool TryRestore(IEnumerable<TEquipment> equipment)
        {
            if (_disposed || _changing || equipment == null) return false;
            _changing = true;
            try
            {
                var restored = new Dictionary<TSlot, TEquipment>();
                var applied = new Dictionary<TSlot, List<AppliedModifier>>();
                foreach (var item in equipment)
                {
                    if (!TryPrepare(item, true, out var slot, out var modifiers) || restored.ContainsKey(slot))
                        return false;
                    restored.Add(slot, item);
                    applied.Add(slot, modifiers);
                }

                if (!TryChangeModifiers(Flatten(_applied), Flatten(applied))) return false;
                _equipped.Clear();
                _applied.Clear();
                foreach (var pair in restored) _equipped.Add(pair.Key, pair.Value);
                foreach (var pair in applied) _applied.Add(pair.Key, pair.Value);
                return true;
            }
            finally { _changing = false; }
        }

        /// <summary>
        /// Rebinds all current items after enhancement or rebuilding the host's Stat instances.
        /// Existing equipment is retained even if the host's equip eligibility has since changed.
        /// </summary>
        public bool RefreshStats()
        {
            if (_disposed || _changing) return false;
            _changing = true;
            try
            {
                var refreshed = new Dictionary<TSlot, List<AppliedModifier>>();
                foreach (var pair in _equipped)
                {
                    if (!TryPrepare(pair.Value, false, out var slot, out var modifiers) ||
                        !EqualityComparer<TSlot>.Default.Equals(pair.Key, slot)) return false;
                    refreshed.Add(slot, modifiers);
                }
                if (!TryChangeModifiers(Flatten(_applied), Flatten(refreshed))) return false;
                _applied.Clear();
                foreach (var pair in refreshed) _applied.Add(pair.Key, pair.Value);
                return true;
            }
            finally { _changing = false; }
        }

        public void Clear()
        {
            if (_disposed) return;
            if (!TryRestore(Array.Empty<TEquipment>()))
                throw new InvalidOperationException("Equipment loadout could not be cleared.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            _disposed = true;
        }

        private bool TryPrepare(TEquipment equipment, bool checkEligibility, out TSlot slot,
            out List<AppliedModifier> modifiers)
        {
            slot = default;
            modifiers = null;
            if (equipment == null || (checkEligibility && _canEquip != null && !_canEquip(equipment))) return false;
            slot = _getSlot(equipment);
            if (ReferenceEquals(slot, null) || !_availableSlots.Contains(slot)) return false;
            var data = _getModifiers(equipment);
            if (data == null) return false;
            modifiers = new List<AppliedModifier>();
            foreach (var entry in data)
            {
                var modifier = entry.Modifier;
                if (entry.StatId.IsEmpty || modifier == null || float.IsNaN(modifier.Value) ||
                    float.IsInfinity(modifier.Value) || !Enum.IsDefined(typeof(StatModType), modifier.ModType)) return false;
                var stat = _resolveStat(entry.StatId);
                if (stat == null) return false;
                modifiers.Add(new AppliedModifier(stat, new StatModifier(modifier.Value, modifier.ModType,
                    modifier.Order, modifier.Source ?? equipment) { Formula = modifier.Formula }));
            }
            return true;
        }

        private static List<AppliedModifier> Flatten(Dictionary<TSlot, List<AppliedModifier>> entries)
        {
            var result = new List<AppliedModifier>();
            foreach (var modifiers in entries.Values) result.AddRange(modifiers);
            return result;
        }

        private static bool TryChangeModifiers(List<AppliedModifier> previous, List<AppliedModifier> next,
            Func<bool> commit = null, Action rollback = null)
        {
            var resources = new Dictionary<CurrentStat, (float Value, float Percent)>();
            CaptureResources(previous, resources);
            CaptureResources(next, resources);
            try
            {
                Remove(previous);
                Add(next);
                foreach (var pair in resources) pair.Key.SetCurrent(pair.Key.MaxValue * pair.Value.Percent);
                if (commit == null || commit()) return true;
            }
            catch (Exception)
            {
                // Stat targets and synchronous commits can fail after a partial local mutation.
            }
            // Track exact copied modifiers rather than sources, so a shared content source
            // cannot remove another slot's or another system's contribution during rollback.
            rollback?.Invoke();
            Remove(next);
            Add(previous);
            foreach (var pair in resources) pair.Key.SetCurrent(pair.Value.Value);
            return false;
        }

        private static void CaptureResources(List<AppliedModifier> modifiers,
            Dictionary<CurrentStat, (float Value, float Percent)> resources)
        {
            if (modifiers == null) return;
            foreach (var entry in modifiers)
            {
                if (entry.Stat is CurrentStat resource && !resources.ContainsKey(resource))
                    resources.Add(resource, (resource.CurrentValue, resource.Percent));
            }
        }

        private static void Add(List<AppliedModifier> modifiers)
        {
            if (modifiers == null) return;
            foreach (var entry in modifiers)
            {
                if (!Contains(entry)) entry.Stat.AddModifier(entry.Modifier);
                if (!Contains(entry)) throw new InvalidOperationException("Stat target did not add the equipment modifier.");
            }
        }

        private static void Remove(List<AppliedModifier> modifiers)
        {
            if (modifiers == null) return;
            foreach (var entry in modifiers)
            {
                if (Contains(entry)) entry.Stat.RemoveModifier(entry.Modifier);
                if (Contains(entry)) throw new InvalidOperationException("Stat target did not remove the equipment modifier.");
            }
        }

        private static bool Contains(AppliedModifier entry)
        {
            foreach (var modifier in entry.Stat.GetModifiers())
                if (ReferenceEquals(modifier, entry.Modifier)) return true;
            return false;
        }

        private readonly struct AppliedModifier
        {
            public Stat Stat { get; }
            public StatModifier Modifier { get; }

            public AppliedModifier(Stat stat, StatModifier modifier)
            {
                Stat = stat;
                Modifier = modifier;
            }
        }
    }
}
