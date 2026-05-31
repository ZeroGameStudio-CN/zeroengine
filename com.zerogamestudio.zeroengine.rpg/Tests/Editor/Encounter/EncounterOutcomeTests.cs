using NUnit.Framework;
using ZeroEngine.RPG.Encounter;
using ZeroEngine.RPG.TurnBased;

namespace ZeroEngine.RPG.Tests.Encounter
{
    [TestFixture]
    public sealed class EncounterOutcomeTests
    {
        [TestCase(EncounterOutcomeType.Started)]
        [TestCase(EncounterOutcomeType.Victory)]
        [TestCase(EncounterOutcomeType.Defeat)]
        [TestCase(EncounterOutcomeType.Escape)]
        [TestCase(EncounterOutcomeType.Cancelled)]
        [TestCase(EncounterOutcomeType.Failed)]
        public void Create_CapturesNeutralEncounterResult(EncounterOutcomeType type)
        {
            var outcome = EncounterOutcome.Create(
                "encounter.dark_early_demo",
                BattleMode.Classic,
                type,
                new[] { "enemy.bandit" },
                new[] { "Herb x2" },
                new[] { "item.herb" },
                "P5Victory");

            Assert.AreEqual("encounter.dark_early_demo", outcome.EncounterId);
            Assert.AreEqual(BattleMode.Classic, outcome.BattleMode);
            Assert.AreEqual(type, outcome.OutcomeType);
            Assert.That(outcome.EnemyIds, Has.Member("enemy.bandit"));
            Assert.That(outcome.RewardSummaryLines, Has.Member("Herb x2"));
            Assert.That(outcome.AppliedRewardIds, Has.Member("item.herb"));
            Assert.AreEqual("P5Victory", outcome.ProjectResultName);
        }
    }
}
