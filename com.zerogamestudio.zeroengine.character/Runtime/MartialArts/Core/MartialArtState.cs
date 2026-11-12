using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZeroEngine.Character.MartialArts
{
    /// <summary>A single actor's progression and loadout; definitions remain owned by the caller.</summary>
    public sealed class MartialArtState : IMartialArtist, IMartialArtRequirementQuery, IDisposable
    {
        private readonly Func<string, MartialArtDataSO> _resolve;
        private readonly Func<LearnRequirement, bool> _requirementQuery;
        private readonly int _maxLearnedArts;
        private readonly int _maxPreparedSkills;
        private readonly Func<string, bool> _intrinsicSkillQuery;
        private readonly List<string> _preparedSkills = new();
        private readonly ReadOnlyCollection<string> _preparedView;
        private readonly Dictionary<string, MartialArtInstance> _arts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArtBinding> _bindings = new(StringComparer.Ordinal);
        private readonly Dictionary<MartialArtSlotType, string> _slots = new();
        private readonly List<MartialArtInstance> _orderedArts = new();
        private readonly ReadOnlyCollection<MartialArtInstance> _learnedView;
        private bool _disposed;

        public MartialArtState(
            string characterId,
            Func<string, MartialArtDataSO> resolve,
            int maxLearnedArts = 0,
            Func<LearnRequirement, bool> requirementQuery = null,
            int maxPreparedSkills = 0,
            Func<string, bool> intrinsicSkillQuery = null)
        {
            if (string.IsNullOrWhiteSpace(characterId)) throw new ArgumentException("A stable character id is required.", nameof(characterId));
            if (maxLearnedArts < 0) throw new ArgumentOutOfRangeException(nameof(maxLearnedArts));
            if (maxPreparedSkills < 0) throw new ArgumentOutOfRangeException(nameof(maxPreparedSkills));
            CharacterId = characterId;
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            _maxLearnedArts = maxLearnedArts;
            _maxPreparedSkills = maxPreparedSkills;
            _intrinsicSkillQuery = intrinsicSkillQuery;
            _preparedView = _preparedSkills.AsReadOnly();
            _requirementQuery = requirementQuery;
            _learnedView = _orderedArts.AsReadOnly();
        }

        public string CharacterId { get; }
        public int LearnedArtCount => _arts.Count;
        public IReadOnlyList<MartialArtInstance> LearnedArts => _learnedView;
        public IReadOnlyList<string> PreparedSkills => _preparedView;
        public MartialArtInstance ActiveInnerArt => GetEquippedArt(MartialArtSlotType.PrimaryInner);
        public MartialArtInstance ActiveOuterArt => GetEquippedArt(MartialArtSlotType.PrimaryOuter);
        public MartialArtInstance ActiveLightness => GetEquippedArt(MartialArtSlotType.Lightness);

        public event Action<MartialArtLearnedEventArgs> OnMartialArtLearned;
        public event Action<MartialArtLevelUpEventArgs> OnMartialArtLevelUp;
        public event Action<SkillUnlockedEventArgs> OnSkillUnlocked;
        public event Action<MartialArtEquippedEventArgs> OnMartialArtEquipped;
        public event Action<CultivationExpGainedEventArgs> OnCultivationExpGained;
        public event Action<MartialArtInstance> OnMartialArtMastered;
        public event Action<MartialArtInstance> OnMartialArtTranscended;

        public bool CanLearnMartialArt(string artId, out string reason) =>
            TryResolveLearnable(artId, out _, out reason);

        private bool TryResolveLearnable(string artId, out MartialArtDataSO data, out string reason)
        {
            ThrowIfDisposed();
            data = null;
            if (string.IsNullOrWhiteSpace(artId)) { reason = "martial.invalid-id"; return false; }
            if (_arts.ContainsKey(artId)) { reason = "martial.already-learned"; return false; }
            if (_maxLearnedArts > 0 && _arts.Count >= _maxLearnedArts) { reason = "martial.capacity"; return false; }
            data = _resolve(artId);
            if (!IsValidDefinition(data, artId)) { reason = "martial.invalid-definition"; return false; }
            if (!data.CheckRequirements(this)) { reason = "martial.requirement"; return false; }
            reason = string.Empty;
            return true;
        }

        public bool LearnMartialArt(string artId)
        {
            if (!TryResolveLearnable(artId, out var data, out _)) return false;
            var instance = new MartialArtInstance(data);
            _arts.Add(artId, instance);
            _orderedArts.Add(instance);
            Bind(instance);
            OnMartialArtLearned?.Invoke(new MartialArtLearnedEventArgs
            {
                CharacterId = CharacterId, ArtId = artId, ArtName = data.artName,
                ArtType = data.artType, Grade = data.grade
            });
            return true;
        }

        public bool LearnMartialArt(MartialArtDataSO data) => data != null && LearnMartialArt(data.artId);
        public bool HasLearnedMartialArt(string artId) => !string.IsNullOrWhiteSpace(artId) && _arts.ContainsKey(artId);
        public MartialArtInstance GetMartialArt(string artId) =>
            !string.IsNullOrWhiteSpace(artId) && _arts.TryGetValue(artId, out var art) ? art : null;

        public bool IsRequirementMet(LearnRequirement requirement)
        {
            ThrowIfDisposed();
            return _requirementQuery?.Invoke(requirement) ?? false;
        }

        public bool ForgetMartialArt(string artId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(artId) || !_arts.TryGetValue(artId, out var art)) return false;
            RemoveArtSlot(artId);
            _bindings[artId].Dispose();
            _bindings.Remove(artId);
            _arts.Remove(artId);
            _orderedArts.Remove(art);
            RefreshPreparedSkills();
            return true;
        }

        public bool EquipMartialArt(string artId, MartialArtSlotType slot)
        {
            ThrowIfDisposed();
            var art = GetMartialArt(artId);
            if (art == null || !IsSlotCompatible(art.Data.artType, slot)) return false;
            _slots.TryGetValue(slot, out var previous);
            if (string.Equals(previous, artId, StringComparison.Ordinal)) return true;
            // An art occupies one slot: moving it must not multiply experience or bonuses.
            RemoveArtSlot(artId);
            _slots[slot] = artId;
            RefreshPreparedSkills();
            OnMartialArtEquipped?.Invoke(new MartialArtEquippedEventArgs
            {
                CharacterId = CharacterId, ArtId = artId, Slot = slot, PreviousArtId = previous
            });
            return true;
        }

        public bool UnequipMartialArt(MartialArtSlotType slot)
        {
            ThrowIfDisposed();
            if (!_slots.Remove(slot)) return false;
            RefreshPreparedSkills();
            return true;
        }

        public MartialArtInstance GetEquippedArt(MartialArtSlotType slot) =>
            _slots.TryGetValue(slot, out var artId) ? GetMartialArt(artId) : null;

        /// <summary>Changes the whole ordered selection or leaves it untouched. An empty selection is explicit.</summary>
        public bool TrySetPreparedSkills(IReadOnlyList<string> skills, out string reason)
        {
            ThrowIfDisposed();
            reason = "martial.invalid-skill-loadout";
            if (skills == null || (_maxPreparedSkills > 0 && skills.Count > _maxPreparedSkills)) return false;
            var staged = new List<string>(skills.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in skills)
            {
                if (!unique.Add(id) || !IsSkillAvailable(id, _arts, _slots)) return false;
                staged.Add(id);
            }
            _preparedSkills.Clear();
            _preparedSkills.AddRange(staged);
            reason = string.Empty;
            return true;
        }

        /// <summary>Permanent intrinsic-grant changes must refresh the selection before saving it.</summary>
        public bool RefreshPreparedSkills()
        {
            ThrowIfDisposed();
            var count = _preparedSkills.Count;
            for (var i = count - 1; i >= 0; i--)
                if (!IsSkillAvailable(_preparedSkills[i], _arts, _slots)) _preparedSkills.RemoveAt(i);
            return count != _preparedSkills.Count;
        }

        /// <summary>A revoked source cannot become usable merely because a stale UI still has it selected.</summary>
        public IReadOnlyList<string> GetEffectiveSkillIds()
        {
            ThrowIfDisposed();
            List<string> filtered = null;
            for (var i = 0; i < _preparedSkills.Count; i++)
            {
                var id = _preparedSkills[i];
                bool available = IsSkillAvailable(id, _arts, _slots);
                if (!available && filtered == null) filtered = _preparedSkills.GetRange(0, i);
                else if (available && filtered != null) filtered.Add(id);
            }
            return filtered == null ? _preparedView : filtered.AsReadOnly();
        }

        public List<MartialArtInstance> GetMartialArtsByType(MartialArtType type) =>
            _orderedArts.FindAll(art => art.Data.artType == type);
        public List<MartialArtInstance> GetMartialArtsByGrade(MartialArtGrade grade) =>
            _orderedArts.FindAll(art => art.Data.grade == grade);

        public void AddMartialArtExp(string artId, int amount) => AddMartialArtExp(artId, amount, CultivationExpSource.Other);

        public void AddMartialArtExp(string artId, int amount, CultivationExpSource source)
        {
            ThrowIfDisposed();
            var art = GetMartialArt(artId);
            if (art == null || amount <= 0) return;
            art.AddExp(amount);
            OnCultivationExpGained?.Invoke(new CultivationExpGainedEventArgs
            {
                CharacterId = CharacterId, ArtId = artId, ExpGained = amount,
                CurrentExp = art.CurrentExp, CurrentLevel = art.CurrentLevel, Source = source
            });
        }

        public void AddExpToEquippedArts(int amount, CultivationExpSource source = CultivationExpSource.Combat)
        {
            ThrowIfDisposed();
            // Observers may change the loadout after a level-up. Iterate this command's starting set.
            var artIds = new List<string>(_slots.Values);
            foreach (var artId in artIds) AddMartialArtExp(artId, amount, source);
        }

        public MartialArtStatBonus GetTotalEquippedStatBonus()
        {
            ThrowIfDisposed();
            var total = new MartialArtStatBonus();
            foreach (var artId in _slots.Values)
            {
                var bonus = _arts[artId].GetCurrentStatBonus();
                total.health += bonus.health; total.energy += bonus.energy;
                total.attack += bonus.attack; total.defense += bonus.defense; total.speed += bonus.speed;
                total.innerDamage += bonus.innerDamage; total.outerDamage += bonus.outerDamage;
            }
            return total;
        }

        public MartialArtManagerSaveData GetSaveData()
        {
            ThrowIfDisposed();
            var arts = new List<MartialArtSaveData>(_arts.Count);
            foreach (var art in _orderedArts) arts.Add(art.ToSaveData());
            arts.Sort((left, right) => string.CompareOrdinal(left.artId, right.artId));
            var slots = new List<EquippedSlotSaveData>(_slots.Count);
            foreach (var pair in _slots) slots.Add(new EquippedSlotSaveData { slot = pair.Key, artId = pair.Value });
            slots.Sort((left, right) => left.slot.CompareTo(right.slot));
            return new MartialArtManagerSaveData
            {
                characterId = CharacterId, learnedArts = arts.ToArray(), equippedSlots = slots.ToArray(),
                preparedSkills = _preparedSkills.ToArray()
            };
        }

        public bool TryRestore(MartialArtManagerSaveData saved, out string reason)
        {
            ThrowIfDisposed();
            reason = "martial.invalid-snapshot";
            if (saved == null || !string.Equals(saved.characterId, CharacterId, StringComparison.Ordinal)
                || saved.learnedArts == null || saved.equippedSlots == null
                || (_maxLearnedArts > 0 && saved.learnedArts.Length > _maxLearnedArts)) return false;
            if (saved.preparedSkills == null || (_maxPreparedSkills > 0 && saved.preparedSkills.Length > _maxPreparedSkills))
            {
                reason = "martial.missing-or-oversized-skill-loadout";
                return false;
            }
            var arts = new Dictionary<string, MartialArtInstance>(StringComparer.Ordinal);
            var ordered = new List<MartialArtInstance>(saved.learnedArts.Length);
            var slots = new Dictionary<MartialArtSlotType, string>();
            var slottedArts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in saved.learnedArts)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.artId) || arts.ContainsKey(record.artId)) return false;
                var data = _resolve(record.artId);
                if (!IsValidDefinition(data, record.artId) || !IsValidProgress(record, data)) return false;
                var art = new MartialArtInstance(record, data);
                arts.Add(record.artId, art);
                ordered.Add(art);
            }
            foreach (var record in saved.equippedSlots)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.artId) || slots.ContainsKey(record.slot)
                    || !arts.TryGetValue(record.artId, out var art)
                    || !IsSlotCompatible(art.Data.artType, record.slot) || !slottedArts.Add(record.artId)) return false;
                slots.Add(record.slot, record.artId);
            }
            var prepared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in saved.preparedSkills)
                if (!prepared.Add(id) || !IsSkillAvailable(id, arts, slots)) return false;
            // Resolve and validate everything before replacing any live state or subscriptions.
            ClearArts();
            foreach (var art in ordered)
            {
                _arts.Add(art.ArtId, art); _orderedArts.Add(art); Bind(art);
            }
            foreach (var pair in slots) _slots.Add(pair.Key, pair.Value);
            _preparedSkills.AddRange(saved.preparedSkills);
            reason = string.Empty;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            ClearArts();
            OnMartialArtLearned = null; OnMartialArtLevelUp = null; OnSkillUnlocked = null;
            OnMartialArtEquipped = null; OnCultivationExpGained = null;
            OnMartialArtMastered = null; OnMartialArtTranscended = null;
            _disposed = true;
        }

        internal static bool IsSlotCompatible(MartialArtType type, MartialArtSlotType slot)
        {
            if (type == MartialArtType.None || !Enum.IsDefined(typeof(MartialArtType), type)) return false;
            return slot switch
            {
                MartialArtSlotType.PrimaryInner or MartialArtSlotType.SecondaryInner => type == MartialArtType.InnerArt,
                MartialArtSlotType.PrimaryOuter or MartialArtSlotType.SecondaryOuter => type != MartialArtType.InnerArt && type != MartialArtType.Lightness,
                MartialArtSlotType.Lightness => type == MartialArtType.Lightness,
                MartialArtSlotType.Ultimate => type == MartialArtType.Ultimate || type == MartialArtType.Forbidden,
                _ => false
            };
        }

        private static bool IsValidDefinition(MartialArtDataSO data, string artId)
        {
            if (data == null || !string.Equals(data.artId, artId, StringComparison.Ordinal)
                || data.maxLevel < 1 || data.maxLevel > 20 || data.skills == null || data.requirements == null
                || data.expPerLevel == null || data.expPerLevel.Length == 0
                || data.statBonusPerLevel == null || data.masteryBonus == null
                || float.IsNaN(data.cultivationSpeedMultiplier) || float.IsInfinity(data.cultivationSpeedMultiplier)
                || data.cultivationSpeedMultiplier <= 0
                || data.artType == MartialArtType.None || !Enum.IsDefined(typeof(MartialArtType), data.artType)) return false;
            for (var level = 2; level <= data.maxLevel; level++) if (data.GetExpForLevel(level) <= 0) return false;
            var skills = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skill in data.skills)
                if (skill == null || string.IsNullOrWhiteSpace(skill.skillId) || skill.unlockLevel < 1
                    || skill.unlockLevel > data.maxLevel || !skills.Add(skill.skillId)) return false;
            foreach (var requirement in data.requirements) if (requirement == null) return false;
            return true;
        }

        private static bool IsValidProgress(MartialArtSaveData record, MartialArtDataSO data)
        {
            if (record.level < 1 || record.level > data.maxLevel || record.exp < 0
                || float.IsNaN(record.totalTime) || float.IsInfinity(record.totalTime) || record.totalTime < 0
                || (record.isTranscendent && record.level != data.maxLevel) || record.unlockedSkills == null
                || (record.level < data.maxLevel && record.exp >= data.GetExpForLevel(record.level + 1))
                || (record.level == data.maxLevel && !record.isTranscendent && record.exp != 0)) return false;
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skill in data.skills) if (skill.unlockLevel <= record.level) expected.Add(skill.skillId);
            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skill in record.unlockedSkills)
                if (string.IsNullOrWhiteSpace(skill) || !actual.Add(skill)) return false;
            return actual.SetEquals(expected);
        }

        private bool IsSkillAvailable(string id, Dictionary<string, MartialArtInstance> arts,
            Dictionary<MartialArtSlotType, string> slots)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (_intrinsicSkillQuery?.Invoke(id) ?? false) return true;
            foreach (var artId in slots.Values)
                if (arts.TryGetValue(artId, out var art) && art.IsSkillUnlocked(id)) return true;
            return false;
        }

        private void RemoveArtSlot(string artId)
        {
            MartialArtSlotType? occupied = null;
            foreach (var pair in _slots)
                if (string.Equals(pair.Value, artId, StringComparison.Ordinal)) { occupied = pair.Key; break; }
            if (occupied.HasValue) _slots.Remove(occupied.Value);
        }

        private void Bind(MartialArtInstance art) => _bindings.Add(art.ArtId, new ArtBinding(this, art));
        private void ClearArts()
        {
            foreach (var binding in _bindings.Values) binding.Dispose();
            _bindings.Clear(); _slots.Clear(); _arts.Clear(); _orderedArts.Clear(); _preparedSkills.Clear();
        }
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MartialArtState));
        }

        private sealed class ArtBinding : IDisposable
        {
            private readonly MartialArtState _owner;
            private readonly MartialArtInstance _art;
            public ArtBinding(MartialArtState owner, MartialArtInstance art)
            {
                _owner = owner; _art = art;
                art.OnLevelUp += LevelUp; art.OnSkillUnlocked += SkillUnlocked;
                art.OnMastered += Mastered; art.OnTranscended += Transcended;
            }
            public void Dispose()
            {
                _art.OnLevelUp -= LevelUp; _art.OnSkillUnlocked -= SkillUnlocked;
                _art.OnMastered -= Mastered; _art.OnTranscended -= Transcended;
            }
            private void LevelUp(int oldLevel, int newLevel)
            {
                _owner.RefreshPreparedSkills();
                _owner.OnMartialArtLevelUp?.Invoke(new MartialArtLevelUpEventArgs
                {
                    CharacterId = _owner.CharacterId, ArtId = _art.ArtId, ArtName = _art.Data.artName,
                    OldLevel = oldLevel, NewLevel = newLevel, IsMastered = _art.IsMastered
                });
            }
            private void SkillUnlocked(string id)
            {
                var skill = _art.Data.skills.Find(entry => entry.skillId == id);
                _owner.OnSkillUnlocked?.Invoke(new SkillUnlockedEventArgs
                {
                    CharacterId = _owner.CharacterId, ArtId = _art.ArtId, SkillId = id,
                    SkillName = skill?.skillName ?? string.Empty, IsUltimate = skill?.isUltimate ?? false
                });
            }
            private void Mastered() => _owner.OnMartialArtMastered?.Invoke(_art);
            private void Transcended() => _owner.OnMartialArtTranscended?.Invoke(_art);
        }
    }
}
