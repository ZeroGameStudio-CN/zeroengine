using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Character.MartialArts;
using Object = UnityEngine.Object;

namespace ZeroEngine.Tests.Character.PlayMode
{
    public sealed class MartialArtRuntimePlayModeTests
    {
        private MartialArtDataSO _data;
        private readonly List<MartialArtState> _states = new();
        [UnityTest]
        public IEnumerator PackageGrowthFlow_FourActors_PreparationProgressSnapshotAndRetirement()
        {
            Assert.That(Application.isPlaying, Is.True);
            _data = ScriptableObject.CreateInstance<MartialArtDataSO>(); _data.artId = "runtime.sword";
            _data.artType = MartialArtType.Sword; _data.maxLevel = 2; _data.expPerLevel = new[] { 10, 20 };
            _data.statBonusPerLevel = new MartialArtStatBonus(); _data.masteryBonus = new MartialArtStatBonus();
            _data.skills.Add(new MartialSkillEntry { skillId = "runtime.strike", unlockLevel = 1 });
            for (var i = 0; i < 4; i++)
            {
                var state = new MartialArtState("runtime.hero." + i, id => id == _data.artId ? _data : null); _states.Add(state);
                Assert.That(state.LearnMartialArt(_data), Is.True); state.EquipMartialArt(_data.artId, MartialArtSlotType.PrimaryOuter);
                Assert.That(state.TrySetPreparedSkills(new[] { "runtime.strike" }, out _), Is.True);
            }
            var frame = Time.frameCount; yield return null; Assert.That(Time.frameCount, Is.GreaterThan(frame));
            _states[0].AddExpToEquippedArts(20); _states[0].TrySetPreparedSkills(Array.Empty<string>(), out _);
            Assert.That(_states[0].ActiveOuterArt.CurrentLevel, Is.EqualTo(2));
            for (var i = 1; i < 4; i++) { Assert.That(_states[i].ActiveOuterArt.CurrentLevel, Is.EqualTo(1)); Assert.That(_states[i].GetEffectiveSkillIds(), Is.EqualTo(new[] { "runtime.strike" })); }
            var snapshot = _states[0].GetSaveData(); var restored = new MartialArtState("runtime.hero.0", id => _data); _states.Add(restored);
            Assert.That(restored.TryRestore(snapshot, out _), Is.True); Assert.That(restored.GetEffectiveSkillIds(), Is.Empty);
            var retained = _states[0].ActiveOuterArt; var events = 0; _states[0].OnMartialArtLevelUp += _ => events++;
            _states[0].Dispose(); retained.SetLevel(1); Assert.That(events, Is.Zero);
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var state in _states) state.Dispose(); _states.Clear();
            if (_data != null) Object.Destroy(_data); _data = null; yield return null;
        }
    }
}
