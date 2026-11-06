using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Character.MartialArts
{
    /// <summary>Unity host for one configured actor. Parties own independent MartialArtState instances.</summary>
    public class MartialArtManager : MonoSingleton<MartialArtManager>
    {
        [SerializeField] private MartialArtDatabaseSO _database;
        [SerializeField] private int _maxLearnedArts;
        [SerializeField] private string _characterId = "player";
        private MartialArtState _state;
        private Func<LearnRequirement, bool> _requirementQuery;

        public MartialArtDatabaseSO Database => _database;
        public string CharacterId => State.CharacterId;
        public int LearnedArtCount => State.LearnedArtCount;
        public IReadOnlyCollection<MartialArtInstance> LearnedArts => State.LearnedArts;
        public MartialArtInstance ActiveInnerArt => State.ActiveInnerArt;
        public MartialArtInstance ActiveOuterArt => State.ActiveOuterArt;
        public MartialArtInstance ActiveLightness => State.ActiveLightness;

        public event Action<MartialArtLearnedEventArgs> OnMartialArtLearned;
        public event Action<MartialArtLevelUpEventArgs> OnMartialArtLevelUp;
        public event Action<SkillUnlockedEventArgs> OnSkillUnlocked;
        public event Action<MartialArtEquippedEventArgs> OnMartialArtEquipped;
        public event Action<CultivationExpGainedEventArgs> OnCultivationExpGained;

        private MartialArtState State
        {
            get
            {
                if (_state != null) return _state;
                _state = new MartialArtState(_characterId, id => _database?.GetMartialArt(id),
                    _maxLearnedArts, requirement => _requirementQuery?.Invoke(requirement) ?? false);
                _state.OnMartialArtLearned += Learned;
                _state.OnMartialArtLevelUp += Leveled;
                _state.OnSkillUnlocked += Unlocked;
                _state.OnMartialArtEquipped += Equipped;
                _state.OnCultivationExpGained += GainedExperience;
                _state.OnMartialArtMastered += Mastered;
                _state.OnMartialArtTranscended += Transcended;
                return _state;
            }
        }

        public void SetRequirementQuery(Func<LearnRequirement, bool> query) => _requirementQuery = query;
        public bool LearnMartialArt(string artId) => State.LearnMartialArt(artId);
        public bool LearnMartialArt(MartialArtDataSO data) => State.LearnMartialArt(data);
        public bool ForgetMartialArt(string artId) => State.ForgetMartialArt(artId);
        public bool HasLearnedMartialArt(string artId) => State.HasLearnedMartialArt(artId);
        public MartialArtInstance GetMartialArt(string artId) => State.GetMartialArt(artId);
        public List<MartialArtInstance> GetMartialArtsByType(MartialArtType type) => State.GetMartialArtsByType(type);
        public List<MartialArtInstance> GetMartialArtsByGrade(MartialArtGrade grade) => State.GetMartialArtsByGrade(grade);
        public bool EquipMartialArt(string artId, MartialArtSlotType slot) => State.EquipMartialArt(artId, slot);
        public MartialArtInstance GetEquippedArt(MartialArtSlotType slot) => State.GetEquippedArt(slot);
        public void AddMartialArtExp(string artId, int amount, CultivationExpSource source = CultivationExpSource.Other) =>
            State.AddMartialArtExp(artId, amount, source);
        public void AddExpToEquippedArts(int amount, CultivationExpSource source = CultivationExpSource.Combat) =>
            State.AddExpToEquippedArts(amount, source);
        public MartialArtStatBonus GetTotalEquippedStatBonus() => State.GetTotalEquippedStatBonus();
        public MartialArtManagerSaveData GetSaveData() => State.GetSaveData();

        public bool UnequipMartialArt(MartialArtSlotType slot)
        {
            var artId = State.GetEquippedArt(slot)?.ArtId;
            if (!State.UnequipMartialArt(slot)) return false;
            EventManager.Trigger(MartialArtEvents.OnMartialArtUnequipped, new { CharacterId, ArtId = artId, Slot = slot });
            return true;
        }

        public void LoadSaveData(MartialArtManagerSaveData saved)
        {
            if (saved != null && !State.TryRestore(saved, out var reason))
                throw new ArgumentException(reason, nameof(saved));
        }

        protected override void OnDestroy()
        {
            _state?.Dispose();
            _state = null;
            OnMartialArtLearned = null; OnMartialArtLevelUp = null; OnSkillUnlocked = null;
            OnMartialArtEquipped = null; OnCultivationExpGained = null;
            base.OnDestroy();
        }

        // Existing scene consumers still use these notifications; the reusable state has no global bus.
        private void Learned(MartialArtLearnedEventArgs args)
        {
            OnMartialArtLearned?.Invoke(args);
            EventManager.Trigger(MartialArtEvents.OnMartialArtLearned, args);
        }
        private void Leveled(MartialArtLevelUpEventArgs args)
        {
            OnMartialArtLevelUp?.Invoke(args);
            EventManager.Trigger(MartialArtEvents.OnMartialArtLevelUp, args);
        }
        private void Unlocked(SkillUnlockedEventArgs args)
        {
            OnSkillUnlocked?.Invoke(args);
            EventManager.Trigger(MartialArtEvents.OnSkillUnlocked, args);
        }
        private void Equipped(MartialArtEquippedEventArgs args)
        {
            OnMartialArtEquipped?.Invoke(args);
            EventManager.Trigger(MartialArtEvents.OnMartialArtEquipped, args);
        }
        private void GainedExperience(CultivationExpGainedEventArgs args)
        {
            OnCultivationExpGained?.Invoke(args);
            EventManager.Trigger(MartialArtEvents.OnCultivationExpGained, args);
        }
        private void Mastered(MartialArtInstance art) => EventManager.Trigger(MartialArtEvents.OnMartialArtMastered,
            new { CharacterId, ArtId = art.ArtId, ArtName = art.Data.artName });
        private void Transcended(MartialArtInstance art) => EventManager.Trigger(MartialArtEvents.OnMartialArtTranscended,
            new { CharacterId, ArtId = art.ArtId, ArtName = art.Data.artName });
    }
}
