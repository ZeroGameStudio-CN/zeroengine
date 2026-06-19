using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Combat;
using ZeroEngine.RPG.TurnBased;
using ZeroEngine.RPG.TurnBased.Variants;

namespace ZeroEngine.RPG.Editor.Tests
{
    public sealed class TurnBasedContractTests
    {
        [Test]
        public void BattleActionFactoriesPopulateExpectedFields()
        {
            var actor = new TestCombatant("actor", speed: 10);
            var target = new TestCombatant("target", speed: 5);

            var attack = BattleAction.Attack(actor, target, boostLevel: 2);
            var defend = BattleAction.Defend(actor);
            var item = BattleAction.Item(actor, "potion", target);
            var skill = BattleAction.Skill(actor, "fire", new System.Collections.Generic.List<ITurnBasedCombatant> { target });

            Assert.AreSame(actor, attack.Actor);
            Assert.AreEqual(BattleActionType.Attack, attack.ActionType);
            Assert.AreSame(target, attack.Targets.Single());
            Assert.AreEqual(2, attack.BoostLevel);
            Assert.AreEqual(BattleActionType.Defend, defend.ActionType);
            Assert.AreSame(actor, defend.Targets.Single());
            Assert.AreEqual("potion", item.ItemId);
            Assert.AreSame(target, item.Targets.Single());
            Assert.AreEqual("fire", skill.SkillId);
        }

        [Test]
        public void SpeedBasedOrderIgnoresDeadCombatantsAndSortsDescending()
        {
            var slow = new TestCombatant("slow", speed: 5);
            var fast = new TestCombatant("fast", speed: 20);
            var dead = new TestCombatant("dead", speed: 100, isAlive: false);
            var calculator = new SpeedBasedTurnOrder
            {
                RandomizeOnTie = false
            };

            var order = calculator.CalculateOrder(new ITurnBasedCombatant[] { slow, dead, fast }, null).ToArray();

            Assert.AreEqual(new[] { fast, slow }, order);
        }

        [Test]
        public void SpeedBasedFutureOrderRepeatsEachPreviewTurn()
        {
            var slow = new TestCombatant("slow", speed: 5);
            var fast = new TestCombatant("fast", speed: 20);
            var calculator = new SpeedBasedTurnOrder();

            var order = calculator.GetFutureOrder(new ITurnBasedCombatant[] { slow, fast }, previewTurns: 2).ToArray();

            Assert.AreEqual(new[] { fast, slow, fast, slow }, order);
        }

        private sealed class TestCombatant : ITurnBasedCombatant
        {
            public TestCombatant(string id, int speed, bool isAlive = true)
            {
                CombatantId = id;
                DisplayName = id;
                Speed = speed;
                IsAlive = isAlive;
            }

            public string CombatantId { get; }
            public string DisplayName { get; }
            public int TeamId => 0;
            public bool IsAlive { get; }
            public bool IsTargetable => IsAlive;
            public GameObject GameObject => null;
            public Transform Transform => null;
            public int Speed { get; }
            public bool CanAct => IsAlive;
            public bool HasActed { get; set; }
            public bool IsPlayerControlled => true;
            public Vector3 GetCombatPosition() => Vector3.zero;
            public DamageResult TakeDamage(DamageData damage) => default;
            public float ReceiveHeal(float amount, ICombatant source = null) => amount;
            public void OnEnterCombat() { }
            public void OnExitCombat() { }
            public void OnTurnStart() { }
            public void OnTurnEnd() { }
            public void ResetTurnState() => HasActed = false;
        }
    }
}
