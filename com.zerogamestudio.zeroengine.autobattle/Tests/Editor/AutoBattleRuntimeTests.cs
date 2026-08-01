using NUnit.Framework;
using UnityEngine;
using ZeroEngine.AutoBattle.AI;
using ZeroEngine.AutoBattle.Battle;
using ZeroEngine.AutoBattle.Grid;
using ZeroEngine.AutoBattle.Skill;

namespace ZeroEngine.AutoBattle.Tests.Editor
{
    public class AutoBattleRuntimeTests
    {
        [Test]
        public void BattleTick_HealingSkill_TargetsDamagedAlly()
        {
            var battle = new AutoBattleManager(5, 5);
            var healer = CreateUnit("healer", BattleTeam.Player, 100f, 10f, 0f);
            var ally = CreateUnit("ally", BattleTeam.Player, 100f, 10f, 0f);
            var enemy = CreateUnit("enemy", BattleTeam.Enemy, 100f, 10f, 0f);
            var heal = new RecordingHealSkill();
            healer.SkillSlots.EquipSkill(heal, 0);
            ally.TakeDamage(50f, enemy);

            battle.EnterPreparation();
            battle.AddPlayerUnit(healer, new Vector2Int(4, 2));
            battle.AddPlayerUnit(ally, new Vector2Int(3, 2));
            battle.AddEnemyUnit(enemy, new Vector2Int(0, 2));
            battle.StartBattle();

            battle.Tick(0.1f);

            Assert.AreSame(ally, heal.LastTarget);
            Assert.Greater(ally.CurrentHealth, 50f);
        }

        [Test]
        public void GetDistance_OpposingUnits_IsSymmetric()
        {
            var battle = new AutoBattleManager(5, 5);
            var player = CreateUnit("player", BattleTeam.Player, 100f, 10f, 0f);
            var enemy = CreateUnit("enemy", BattleTeam.Enemy, 100f, 10f, 0f);
            battle.EnterPreparation();
            battle.AddPlayerUnit(player, new Vector2Int(3, 1));
            battle.AddEnemyUnit(enemy, new Vector2Int(2, 4));

            Assert.AreEqual(battle.GetDistance(player, enemy), battle.GetDistance(enemy, player));
            Assert.AreEqual(7, battle.GetDistance(player, enemy));
        }

        [Test]
        public void BackRowPriority_IsRelativeToTargetTeamFacing()
        {
            var battle = new AutoBattleManager(5, 5);
            var player = CreateUnit("player", BattleTeam.Player, 100f, 10f, 0f);
            var enemy = CreateUnit("enemy", BattleTeam.Enemy, 100f, 10f, 0f);
            var playerBack = CreateUnit("player_back", BattleTeam.Player, 100f, 10f, 0f);
            var enemyBack = CreateUnit("enemy_back", BattleTeam.Enemy, 100f, 10f, 0f);
            player.AIConfig.TargetPriority = TargetPriority.BackRow;
            enemy.AIConfig.TargetPriority = TargetPriority.BackRow;

            battle.EnterPreparation();
            battle.AddPlayerUnit(player, new Vector2Int(4, 2));
            battle.AddPlayerUnit(playerBack, new Vector2Int(0, 1));
            battle.AddEnemyUnit(enemy, new Vector2Int(0, 2));
            battle.AddEnemyUnit(enemyBack, new Vector2Int(4, 1));

            Assert.AreSame(enemyBack, player.SelectTarget(battle));
            Assert.AreSame(playerBack, enemy.SelectTarget(battle));
        }

        [Test]
        public void Statistics_RecordsActualHealthChangesAndDeaths()
        {
            var battle = new AutoBattleManager(5, 5);
            var player = CreateUnit("player", BattleTeam.Player, 100f, 20f, 0f);
            var enemy = CreateUnit("enemy", BattleTeam.Enemy, 40f, 10f, 0f);
            int deathEvents = 0;
            battle.OnUnitDeath += _ => deathEvents++;

            battle.EnterPreparation();
            battle.AddPlayerUnit(player, new Vector2Int(4, 2));
            battle.AddEnemyUnit(enemy, new Vector2Int(0, 2));
            enemy.TakeDamage(50f, player);

            Assert.AreEqual(40f, battle.Statistics.PlayerDamageDealt);
            Assert.AreEqual(40f, battle.Statistics.EnemyDamageTaken);
            Assert.AreEqual(1, battle.Statistics.EnemyDeaths);
            Assert.AreEqual(1, deathEvents);
            Assert.AreEqual(0, battle.GetAliveEnemyUnits().Count);
        }

        [Test]
        public void PresentationEvents_ReportAttackAndSkillTargets()
        {
            var battle = new AutoBattleManager(5, 5);
            var player = CreateUnit("player", BattleTeam.Player, 100f, 20f, 0f);
            var enemy = CreateUnit("enemy", BattleTeam.Enemy, 100f, 5f, 0f);
            var skill = new RecordingDamageSkill();
            player.SkillSlots.EquipSkill(skill, 0);
            IBattleUnit attackTarget = null;
            IBattleUnit skillTarget = null;
            player.OnAttackPerformed += target => attackTarget = target;
            player.OnSkillUsed += (_, target) => skillTarget = target;

            battle.EnterPreparation();
            battle.AddPlayerUnit(player, new Vector2Int(4, 2));
            battle.AddEnemyUnit(enemy, new Vector2Int(0, 2));
            battle.StartBattle();
            battle.Tick(0.1f);

            Assert.AreSame(enemy, skillTarget);
            Assert.IsNull(attackTarget, "A ready skill should replace the normal attack.");

            battle.Tick(0.1f);

            Assert.AreSame(enemy, attackTarget);
        }

        private static TestUnit CreateUnit(
            string id,
            BattleTeam team,
            float health,
            float attack,
            float defense)
        {
            var unit = new TestUnit(id, team);
            unit.Initialize(health, attack, defense);
            return unit;
        }

        private sealed class TestUnit : BattleUnitBase
        {
            public TestUnit(string id, BattleTeam team) : base(id, team) { }

            public IBattleUnit SelectTarget(AutoBattleManager battleManager)
            {
                return FindTarget(battleManager);
            }
        }

        private sealed class RecordingHealSkill : SkillData
        {
            public IBattleUnit LastTarget { get; private set; }

            public RecordingHealSkill()
            {
                SkillId = "recording_heal";
                SkillName = "Recording Heal";
                Cooldown = 5f;
                Range = 5;
                Type = SkillType.Heal;
                TargetType = SkillTargetType.SingleAlly;
            }

            public override bool CanUse(IBattleUnit owner, IBattleUnit target)
            {
                return base.CanUse(owner, target)
                    && target is BattleUnitBase unit
                    && unit.CurrentHealth < unit.MaxHealth;
            }

            public override void Execute(
                IBattleUnit owner,
                IBattleUnit target,
                AutoBattleManager battleManager)
            {
                LastTarget = target;
                (target as BattleUnitBase)?.Heal(25f);
            }
        }

        private sealed class RecordingDamageSkill : SkillData
        {
            public RecordingDamageSkill()
            {
                SkillId = "recording_damage";
                SkillName = "Recording Damage";
                Cooldown = 5f;
                Range = 1;
                Type = SkillType.Damage;
                TargetType = SkillTargetType.SingleEnemy;
                BaseValue = 5f;
            }

            public override void Execute(
                IBattleUnit owner,
                IBattleUnit target,
                AutoBattleManager battleManager)
            {
                (target as BattleUnitBase)?.TakeDamage(BaseValue, owner);
            }
        }
    }
}
