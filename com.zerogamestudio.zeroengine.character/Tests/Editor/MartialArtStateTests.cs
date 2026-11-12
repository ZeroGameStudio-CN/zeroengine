using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Character.MartialArts;
using Object = UnityEngine.Object;

namespace ZeroEngine.Tests.Character
{
    [TestFixture]
    [Category("Unit")]
    public sealed class MartialArtStateTests
    {
        private readonly Dictionary<string, MartialArtDataSO> _definitions = new();
        private readonly List<MartialArtState> _owners = new();
        private MartialArtDataSO _basic;

        [SetUp]
        public void SetUp() => _basic = Definition("art.basic", MartialArtType.Sword);

        [TearDown]
        public void TearDown()
        {
            foreach (var owner in _owners) owner.Dispose();
            foreach (var data in _definitions.Values) Object.DestroyImmediate(data);
            _owners.Clear(); _definitions.Clear();
        }

        [Test]
        public void FourActors_SharingDefinitions_KeepProgressAndSlotsIndependent()
        {
            var actors = new[] { Owner("hero.a"), Owner("hero.b"), Owner("hero.c"), Owner("hero.d") };
            foreach (var actor in actors)
            {
                Assert.That(actor.LearnMartialArt(_basic), Is.True);
                Assert.That(actor.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter), Is.True);
                Assert.That(actor.TrySetPreparedSkills(new[] { "skill.basic" }, out _), Is.True);
            }
            actors[0].TrySetPreparedSkills(Array.Empty<string>(), out _);
            Assert.That(actors[0].PreparedSkills, Is.Empty);
            actors[0].AddExpToEquippedArts(25);
            Assert.That(actors[0].ActiveOuterArt.CurrentLevel, Is.EqualTo(2));
            Assert.That(actors[0].GetTotalEquippedStatBonus().attack, Is.EqualTo(6));
            for (var i = 1; i < actors.Length; i++)
            {
                Assert.That(actors[i].PreparedSkills, Is.EqualTo(new[] { "skill.basic" }));
                Assert.That(actors[i].ActiveOuterArt.CurrentLevel, Is.EqualTo(1));
                Assert.That(actors[i].ActiveOuterArt.CurrentExp, Is.Zero);
                Assert.That(actors[i].GetSaveData().characterId, Is.EqualTo("hero." + (char)('a' + i)));
                Assert.That(actors[i].GetTotalEquippedStatBonus().attack, Is.EqualTo(3));
            }
            Assert.That(actors[1].UnequipMartialArt(MartialArtSlotType.PrimaryOuter), Is.True);
            Assert.That(actors[0].ActiveOuterArt, Is.Not.Null);
            Assert.That(actors[2].ActiveOuterArt, Is.Not.Null);
        }

        [Test]
        public void EquipSameArtInAnotherSlot_MovesItWithoutMultiplyingProgressOrBonus()
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.SecondaryOuter);
            owner.AddExpToEquippedArts(10);
            Assert.That(owner.ActiveOuterArt, Is.Null);
            Assert.That(owner.GetMartialArt(_basic.artId).CurrentExp, Is.EqualTo(10));
            Assert.That(owner.GetTotalEquippedStatBonus().attack, Is.EqualTo(3));
        }

        [Test]
        public void ForgetEquippedArt_RemovesItsSlotAndDetachesOldInstanceEvents()
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            var removed = owner.GetMartialArt(_basic.artId);
            var levels = 0; owner.OnMartialArtLevelUp += _ => levels++;
            Assert.That(owner.ForgetMartialArt(_basic.artId), Is.True);
            Assert.That(owner.GetSaveData().equippedSlots, Is.Empty);
            Assert.That(owner.LearnMartialArt(_basic), Is.True);
            removed.AddExp(25);
            Assert.That(levels, Is.Zero);
            owner.AddMartialArtExp(_basic.artId, 25);
            Assert.That(levels, Is.EqualTo(1));
        }

        [Test]
        public void Restore_UsesIndependentSnapshotDataAndPreservesProgress()
        {
            var first = Owner(); first.LearnMartialArt(_basic);
            first.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            first.AddMartialArtExp(_basic.artId, 25);
            var saved = first.GetSaveData();
            var restored = Owner();
            Assert.That(restored.TryRestore(saved, out _), Is.True);
            saved.learnedArts[0].unlockedSkills[0] = "tampered";
            saved.equippedSlots[0].artId = "tampered";
            Assert.That(restored.ActiveOuterArt.ArtId, Is.EqualTo(_basic.artId));
            Assert.That(restored.ActiveOuterArt.CurrentLevel, Is.EqualTo(2));
            Assert.That(restored.ActiveOuterArt.CurrentExp, Is.EqualTo(5));
            Assert.That(restored.ActiveOuterArt.UnlockedSkills, Does.Contain("skill.basic"));
            restored.AddMartialArtExp(_basic.artId, 1);
            Assert.That(first.ActiveOuterArt.CurrentExp, Is.EqualTo(5));
        }

        [TestCase("owner")]
        [TestCase("missing-arts")]
        [TestCase("duplicate-art")]
        [TestCase("duplicate-slot")]
        [TestCase("duplicate-equipped-art")]
        [TestCase("incompatible-slot")]
        [TestCase("unknown-art")]
        [TestCase("negative-exp")]
        [TestCase("future-skill")]
        [TestCase("missing-skills")]
        public void Restore_InvalidSnapshot_LeavesCurrentInstancesAndLoadoutUntouched(string defect)
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            owner.AddMartialArtExp(_basic.artId, 5);
            var live = owner.ActiveOuterArt;
            var saved = owner.GetSaveData();
            switch (defect)
            {
                case "owner": saved.characterId = "another-owner"; break;
                case "missing-arts": saved.learnedArts = null; break;
                case "duplicate-art": saved.learnedArts = new[] { saved.learnedArts[0], saved.learnedArts[0] }; break;
                case "duplicate-slot": saved.equippedSlots = new[] { saved.equippedSlots[0], saved.equippedSlots[0] }; break;
                case "duplicate-equipped-art": saved.equippedSlots = new[] { saved.equippedSlots[0], new EquippedSlotSaveData { slot = MartialArtSlotType.SecondaryOuter, artId = _basic.artId } }; break;
                case "incompatible-slot": saved.equippedSlots[0].slot = MartialArtSlotType.PrimaryInner; break;
                case "unknown-art": saved.learnedArts[0].artId = "missing"; break;
                case "negative-exp": saved.learnedArts[0].exp = -1; break;
                case "future-skill": saved.learnedArts[0].unlockedSkills = new[] { "skill.basic", "skill.advanced" }; break;
                case "missing-skills": saved.learnedArts[0].unlockedSkills = null; break;
            }
            Assert.That(owner.TryRestore(saved, out var reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(owner.ActiveOuterArt, Is.SameAs(live));
            Assert.That(live.CurrentExp, Is.EqualTo(5));
            Assert.That(owner.LearnedArtCount, Is.EqualTo(1));
        }

        [TestCase(LearnRequirementType.Realm)]
        [TestCase(LearnRequirementType.Stat)]
        [TestCase(LearnRequirementType.Sect)]
        [TestCase(LearnRequirementType.Item)]
        [TestCase(LearnRequirementType.Special)]
        [TestCase((LearnRequirementType)512)]
        [TestCase(LearnRequirementType.Realm | LearnRequirementType.Sect)]
        public void LearningRequirement_WithoutSupportedActorQuery_RefusesTheArt(LearnRequirementType type)
        {
            _basic.requirements.Add(new LearnRequirement { type = type, valueRequired = 1 });
            var owner = Owner();
            Assert.That(owner.LearnMartialArt(_basic), Is.False);
            Assert.That(owner.LearnedArts, Is.Empty);
        }

        [Test]
        public void PrerequisiteRequirement_UsesOnlyThisActorsLearnedArts()
        {
            var next = Definition("art.next", MartialArtType.Sword);
            next.requirements.Add(new LearnRequirement { type = LearnRequirementType.PrerequisiteArt, stringRequired = _basic.artId });
            var first = Owner("first"); var second = Owner("second");
            first.LearnMartialArt(_basic);
            Assert.That(first.LearnMartialArt(next), Is.True);
            Assert.That(second.LearnMartialArt(next), Is.False);
        }

        [Test]
        public void ExternalRequirement_IsQueriedAgainstTheConfiguredActor()
        {
            _basic.requirements.Add(new LearnRequirement { type = LearnRequirementType.Realm, valueRequired = 2 });
            var eligible = false;
            var owner = Owner(query: requirement => eligible && requirement.valueRequired == 2);
            Assert.That(owner.CanLearnMartialArt(_basic.artId, out _), Is.False);
            eligible = true;
            Assert.That(owner.LearnMartialArt(_basic), Is.True);
        }

        [Test]
        public void EquippedExperience_ObserverChangingSlots_DoesNotInvalidateIteration()
        {
            var inner = Definition("art.inner", MartialArtType.InnerArt);
            var owner = Owner(); owner.LearnMartialArt(_basic); owner.LearnMartialArt(inner);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            owner.EquipMartialArt(inner.artId, MartialArtSlotType.PrimaryInner);
            owner.OnMartialArtLevelUp += _ => owner.UnequipMartialArt(MartialArtSlotType.PrimaryInner);
            Assert.DoesNotThrow(() => owner.AddExpToEquippedArts(25));
            Assert.That(owner.GetMartialArt(inner.artId).CurrentLevel, Is.EqualTo(2));
        }

        [Test]
        public void Dispose_DetachesProgressEventsFromExternallyRetainedArt()
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            var art = owner.GetMartialArt(_basic.artId);
            var callbacks = 0; owner.OnMartialArtLevelUp += _ => callbacks++;
            owner.Dispose(); art.AddExp(25);
            Assert.That(callbacks, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => owner.LearnMartialArt(_basic));
        }

        [Test]
        public void Restore_PreservesAlreadyLearnedArtWhenItsLearningEligibilityChanges()
        {
            var source = Owner(query: _ => true); source.LearnMartialArt(_basic);
            var saved = source.GetSaveData();
            _basic.requirements.Add(new LearnRequirement { type = LearnRequirementType.Realm, valueRequired = 99 });
            var target = Owner();
            Assert.That(target.TryRestore(saved, out _), Is.True);
            Assert.That(target.HasLearnedMartialArt(_basic.artId), Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void SetLevel_LowerLevel_RecomputesUnlocksAndKeepsSnapshotRestorable(int level)
        {
            _basic.skills.Add(new MartialSkillEntry { skillId = "skill.master", unlockLevel = 3 });
            var source = Owner(); source.LearnMartialArt(_basic);
            var art = source.GetMartialArt(_basic.artId);
            art.SetLevel(3);
            Assert.That(art.UnlockedSkills, Does.Contain("skill.master"));
            art.SetLevel(level);
            Assert.That(art.UnlockedSkills, Does.Not.Contain("skill.master"));
            Assert.That(art.UnlockedSkills.Contains("skill.advanced"), Is.EqualTo(level >= 2));
            var target = Owner();
            Assert.That(target.TryRestore(source.GetSaveData(), out _), Is.True);
            Assert.That(target.GetMartialArt(_basic.artId).CurrentLevel, Is.EqualTo(level));
        }

        [Test]
        public void PreparedSkills_StartEmptyWithoutFallingBackToAllUnlockedSkills()
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            Assert.That(owner.PreparedSkills, Is.Empty);
            Assert.That(owner.GetEffectiveSkillIds(), Is.Empty);
        }

        [Test]
        public void PreparedSkills_RequireEquippedUnlockedOrExplicitIntrinsicOwnership()
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            Assert.That(owner.TrySetPreparedSkills(new[] { "skill.basic" }, out _), Is.False);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            Assert.That(owner.TrySetPreparedSkills(new[] { "skill.basic" }, out _), Is.True);
            Assert.That(owner.TrySetPreparedSkills(new[] { "missing" }, out _), Is.False);
            Assert.That(owner.PreparedSkills, Is.EqualTo(new[] { "skill.basic" }));
            var intrinsic = Owner("intrinsic", intrinsic: id => id == "skill.intrinsic");
            Assert.That(intrinsic.TrySetPreparedSkills(new[] { "skill.intrinsic" }, out _), Is.True);
        }

        [Test]
        public void PreparedSkills_ExplicitEmpty_RestoresWithoutRepopulatingLearnedSkills()
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            owner.TrySetPreparedSkills(new[] { "skill.basic" }, out _);
            Assert.That(owner.TrySetPreparedSkills(Array.Empty<string>(), out _), Is.True);
            var snapshot = owner.GetSaveData();
            Assert.That(snapshot.preparedSkills, Is.Not.Null.And.Empty);
            var target = Owner();
            Assert.That(target.TryRestore(snapshot, out _), Is.True);
            Assert.That(target.GetEffectiveSkillIds(), Is.Empty);
            Assert.That(target.LearnedArtCount, Is.EqualTo(1));
        }

        [TestCase("duplicate")]
        [TestCase("capacity")]
        public void PreparedSkills_InvalidSelection_LeavesPreviousOrderUntouched(string defect)
        {
            var owner = Owner(maxSkills: defect == "capacity" ? 1 : 2); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            owner.GetMartialArt(_basic.artId).SetLevel(2);
            owner.TrySetPreparedSkills(new[] { "skill.basic" }, out _);
            var invalid = defect == "duplicate" ? new[] { "skill.basic", "skill.basic" } : new[] { "skill.basic", "skill.advanced" };
            Assert.That(owner.TrySetPreparedSkills(invalid, out _), Is.False);
            Assert.That(owner.PreparedSkills, Is.EqualTo(new[] { "skill.basic" }));
        }

        [Test]
        public void PreparedSkills_RemovingOneSource_PreservesAnotherAndPrunesTheLast()
        {
            var inner = Definition("art.inner", MartialArtType.InnerArt);
            var owner = Owner(); owner.LearnMartialArt(_basic); owner.LearnMartialArt(inner);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            owner.EquipMartialArt(inner.artId, MartialArtSlotType.PrimaryInner);
            owner.TrySetPreparedSkills(new[] { "skill.basic" }, out _);
            owner.UnequipMartialArt(MartialArtSlotType.PrimaryOuter);
            Assert.That(owner.PreparedSkills, Is.EqualTo(new[] { "skill.basic" }));
            owner.ForgetMartialArt(inner.artId);
            Assert.That(owner.PreparedSkills, Is.Empty);
        }

        [Test]
        public void PreparedSkills_LowerLevel_PrunesAdvancedChoiceBeforeNotifyingObservers()
        {
            var owner = Owner(); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            var art = owner.GetMartialArt(_basic.artId); art.SetLevel(2);
            owner.TrySetPreparedSkills(new[] { "skill.advanced" }, out _);
            var observed = false;
            owner.OnMartialArtLevelUp += _ => { observed = true; Assert.That(owner.PreparedSkills, Is.Empty); };
            art.SetLevel(1);
            Assert.That(observed, Is.True);
        }

        [Test]
        public void PreparedSkills_RevokedIntrinsicGrant_IsUnavailableBeforeExplicitRefresh()
        {
            var granted = true;
            var owner = Owner(intrinsic: id => granted && id == "skill.intrinsic");
            owner.TrySetPreparedSkills(new[] { "skill.intrinsic" }, out _);
            granted = false;
            Assert.That(owner.GetEffectiveSkillIds(), Is.Empty);
            Assert.That(owner.RefreshPreparedSkills(), Is.True);
            Assert.That(owner.PreparedSkills, Is.Empty);
        }

        [TestCase("missing")]
        [TestCase("unknown")]
        [TestCase("duplicate")]
        [TestCase("unequipped")]
        [TestCase("capacity")]
        public void Restore_InvalidPreparedSkills_PreservesExistingActorState(string defect)
        {
            var owner = Owner(maxSkills: defect == "capacity" ? 1 : 0); owner.LearnMartialArt(_basic);
            owner.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            var live = owner.GetMartialArt(_basic.artId); live.SetLevel(2);
            owner.TrySetPreparedSkills(new[] { "skill.basic" }, out _);
            var snapshot = owner.GetSaveData();
            switch (defect)
            {
                case "missing": snapshot.preparedSkills = null; break;
                case "unknown": snapshot.preparedSkills = new[] { "missing" }; break;
                case "duplicate": snapshot.preparedSkills = new[] { "skill.basic", "skill.basic" }; break;
                case "unequipped": snapshot.equippedSlots = Array.Empty<EquippedSlotSaveData>(); break;
                case "capacity": snapshot.preparedSkills = new[] { "skill.basic", "skill.advanced" }; break;
            }
            Assert.That(owner.TryRestore(snapshot, out _), Is.False);
            Assert.That(owner.ActiveOuterArt, Is.SameAs(live));
            Assert.That(owner.PreparedSkills, Is.EqualTo(new[] { "skill.basic" }));
        }

        [Test]
        public void Restore_PreparedSkillOrder_IsCopiedFromStagedOwnership()
        {
            var source = Owner(); source.LearnMartialArt(_basic);
            source.EquipMartialArt(_basic.artId, MartialArtSlotType.PrimaryOuter);
            source.GetMartialArt(_basic.artId).SetLevel(2);
            source.TrySetPreparedSkills(new[] { "skill.advanced", "skill.basic" }, out _);
            var snapshot = source.GetSaveData();
            var target = Owner();
            Assert.That(target.TryRestore(snapshot, out _), Is.True);
            snapshot.preparedSkills[0] = "tampered";
            Assert.That(target.PreparedSkills, Is.EqualTo(new[] { "skill.advanced", "skill.basic" }));
        }

        private MartialArtState Owner(string id = "hero", Func<LearnRequirement, bool> query = null,
            int maxSkills = 0, Func<string, bool> intrinsic = null)
        {
            var state = new MartialArtState(id, artId => _definitions.TryGetValue(artId, out var data) ? data : null,
                requirementQuery: query, maxPreparedSkills: maxSkills, intrinsicSkillQuery: intrinsic);
            _owners.Add(state); return state;
        }

        private MartialArtDataSO Definition(string id, MartialArtType type)
        {
            var data = ScriptableObject.CreateInstance<MartialArtDataSO>();
            data.artId = id; data.artType = type; data.maxLevel = 3; data.expPerLevel = new[] { 10, 20, 40 };
            data.statBonusPerLevel = new MartialArtStatBonus { attack = 3 };
            data.masteryBonus = new MartialArtStatBonus();
            data.skills.Add(new MartialSkillEntry { skillId = "skill.basic", unlockLevel = 1 });
            data.skills.Add(new MartialSkillEntry { skillId = "skill.advanced", unlockLevel = 2 });
            _definitions.Add(id, data); return data;
        }
    }
}
