using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.AutoBattle.Simulation;

namespace ZeroEngine.AutoBattle.Tests.Editor.Simulation
{
    public class AutoBattleSimulationTests
    {
        [Test]
        public void BattleTargeting_SelectsLowestHealthAlly_ForHealer()
        {
            var healer = Unit("healer", SimulationTeam.Player, SimulationUnitRole.Healer, 100f, 100f, 0, 0);
            var wounded = Unit("wounded", SimulationTeam.Player, SimulationUnitRole.Damage, 30f, 100f, 0, 1);
            var healthy = Unit("healthy", SimulationTeam.Player, SimulationUnitRole.Damage, 95f, 100f, 0, 2);
            var enemy = Unit("enemy", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 4, 4);

            var target = BattleSimulationTargeting.FindTarget(healer, new[] { healer, wounded, healthy, enemy });

            Assert.AreSame(wounded, target);
        }

        [Test]
        public void BattleTargeting_UsesThreat_ForTank()
        {
            var tank = Unit("tank", SimulationTeam.Player, SimulationUnitRole.Tank, 100f, 100f, 0, 0);
            var lowThreatEnemy = Unit("enemy_low", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 1, 0);
            var highThreatEnemy = Unit("enemy_high", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 3, 0);
            tank.SetThreat(lowThreatEnemy.UnitId, 10f);
            tank.SetThreat(highThreatEnemy.UnitId, 50f);

            var target = BattleSimulationTargeting.FindTarget(tank, new[] { tank, lowThreatEnemy, highThreatEnemy });

            Assert.AreSame(highThreatEnemy, target);
        }

        [Test]
        public void GridBattleSimulationLayout_MovesTowardTarget_WithoutOverlap()
        {
            var layout = new GridBattleSimulationLayout(5, 5);
            var mover = Unit("mover", SimulationTeam.Player, SimulationUnitRole.Damage, 100f, 100f, 0, 0);
            var blocker = Unit("blocker", SimulationTeam.Player, SimulationUnitRole.Tank, 100f, 100f, 1, 1);
            var target = Unit("target", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 3, 0);

            bool moved = layout.MoveTowards(mover, target, new[] { mover, blocker, target });

            Assert.IsTrue(moved);
            Assert.AreEqual(1, mover.Row);
            Assert.AreEqual(0, mover.Col);
            Assert.IsFalse(mover.Row == blocker.Row && mover.Col == blocker.Col);
        }

        [Test]
        public void SlotBattleSimulationLayout_MeleeOnlyHitsFrontTarget()
        {
            var layout = new SlotBattleSimulationLayout();
            var attacker = Unit("attacker", SimulationTeam.Player, SimulationUnitRole.Damage, 100f, 100f, 0, 0, attackRange: 1);
            var front = Unit("front", SimulationTeam.Enemy, SimulationUnitRole.Tank, 100f, 100f, 0, 0);
            var back = Unit("back", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 0, 0);
            front.SlotIndex = 0;
            back.SlotIndex = 1;

            var units = new[] { attacker, front, back };

            Assert.IsTrue(layout.CanAttack(attacker, front, units));
            Assert.IsFalse(layout.CanAttack(attacker, back, units));
        }

        [Test]
        public void BattleSimulation_EndsWithPlayerWin_WhenEnemiesAreDead()
        {
            var simulation = new BattleSimulation();
            simulation.AddUnit(Unit("player", SimulationTeam.Player, SimulationUnitRole.Damage, 100f, 100f, 0, 0));
            simulation.AddUnit(Unit("enemy", SimulationTeam.Enemy, SimulationUnitRole.Damage, 0f, 100f, 1, 0));

            Assert.AreEqual(SimulationBattleResult.PlayerWin, simulation.CheckResult());
        }

        [Test]
        public void BattleSimulation_EndsWithEnemyWin_WhenPlayersAreDead()
        {
            var simulation = new BattleSimulation();
            simulation.AddUnit(Unit("player", SimulationTeam.Player, SimulationUnitRole.Damage, 0f, 100f, 0, 0));
            simulation.AddUnit(Unit("enemy", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 1, 0));

            Assert.AreEqual(SimulationBattleResult.EnemyWin, simulation.CheckResult());
        }

        [Test]
        public void BattleSimulation_EndsWithTimeout_WhenDurationExpires()
        {
            var simulation = new BattleSimulation { MaxDuration = 1f };
            simulation.AddUnit(Unit("player", SimulationTeam.Player, SimulationUnitRole.Damage, 100f, 100f, 0, 0));
            simulation.AddUnit(Unit("enemy", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 1, 0));

            Assert.AreEqual(SimulationBattleResult.Timeout, simulation.Advance(1.1f));
        }

        [Test]
        public void BattleSimulation_AdvanceTick_MovesUnitTowardTarget()
        {
            var layout = new GridBattleSimulationLayout(5, 5);
            var resolver = new TestActionResolver();
            var simulation = new BattleSimulation(layout, resolver);
            var player = Unit("player", SimulationTeam.Player, SimulationUnitRole.Damage, 100f, 100f, 0, 0);
            var enemy = Unit("enemy", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 3, 0);
            simulation.AddUnit(player);
            simulation.AddUnit(enemy);

            var result = simulation.AdvanceTick();

            Assert.AreEqual(SimulationBattleResult.InProgress, result);
            Assert.AreEqual(1, player.Row);
            Assert.AreEqual(0, player.Col);
            Assert.AreEqual(0, resolver.BasicAttackCount);
        }

        [Test]
        public void BattleSimulation_AdvanceTick_UsesResolverWhenInRange()
        {
            var layout = new GridBattleSimulationLayout(5, 5);
            var resolver = new TestActionResolver();
            var simulation = new BattleSimulation(layout, resolver);
            var player = Unit("player", SimulationTeam.Player, SimulationUnitRole.Damage, 100f, 100f, 0, 0);
            var enemy = Unit("enemy", SimulationTeam.Enemy, SimulationUnitRole.Damage, 100f, 100f, 1, 0);
            simulation.AddUnit(player);
            simulation.AddUnit(enemy);

            var result = simulation.AdvanceTick();

            Assert.AreEqual(SimulationBattleResult.PlayerWin, result);
            Assert.AreEqual(1, resolver.BasicAttackCount);
            Assert.AreEqual("player", resolver.LastAttackerId);
            Assert.AreEqual("enemy", resolver.LastTargetId);
        }

        private static TestSimulationUnit Unit(
            string id,
            SimulationTeam team,
            SimulationUnitRole role,
            float currentHealth,
            float maxHealth,
            int row,
            int col,
            int attackRange = 1)
        {
            return new TestSimulationUnit(id, team, role, currentHealth, maxHealth, row, col, attackRange);
        }

        private sealed class TestActionResolver : IBattleSimulationActionResolver
        {
            public int BasicAttackCount { get; private set; }
            public string LastAttackerId { get; private set; }
            public string LastTargetId { get; private set; }

            public void ResolveAction(ISimulationUnit actor, ISimulationUnit target, BattleSimulationContext context)
            {
                BasicAttackCount++;
                LastAttackerId = actor.UnitId;
                LastTargetId = target.UnitId;
                target.ApplyDamage(target.CurrentHealth, actor);
            }
        }

        private sealed class TestSimulationUnit : ISimulationUnit
        {
            private readonly Dictionary<string, float> _threats = new Dictionary<string, float>();

            public TestSimulationUnit(
                string unitId,
                SimulationTeam team,
                SimulationUnitRole role,
                float currentHealth,
                float maxHealth,
                int row,
                int col,
                int attackRange)
            {
                UnitId = unitId;
                Team = team;
                Role = role;
                CurrentHealth = currentHealth;
                MaxHealth = maxHealth;
                Row = row;
                Col = col;
                AttackRange = attackRange;
            }

            public string UnitId { get; }
            public SimulationTeam Team { get; }
            public SimulationUnitRole Role { get; }
            public bool IsAlive => CurrentHealth > 0f;
            public int Row { get; set; }
            public int Col { get; set; }
            public int SlotIndex { get; set; }
            public int AttackRange { get; }
            public float CurrentHealth { get; private set; }
            public float MaxHealth { get; }
            public IReadOnlyDictionary<string, float> Threats => _threats;

            public void SetThreat(string unitId, float value)
            {
                _threats[unitId] = value;
            }

            public void ApplyDamage(float amount, ISimulationUnit attacker)
            {
                CurrentHealth -= amount;
            }

            public void ApplyHeal(float amount, ISimulationUnit healer)
            {
                CurrentHealth += amount;
                if (CurrentHealth > MaxHealth)
                {
                    CurrentHealth = MaxHealth;
                }
            }
        }
    }
}
