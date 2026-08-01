using System;
using UnityEngine;
using ZeroEngine.AutoBattle.Grid;
using ZeroEngine.AutoBattle.Skill;
using ZeroEngine.AutoBattle.AI;

namespace ZeroEngine.AutoBattle.Battle
{
    /// <summary>
    /// 战斗单位基类，实现 IBattleUnit 接口
    /// </summary>
    public abstract class BattleUnitBase : IBattleUnit
    {
        /// <summary>
        /// 单位唯一ID
        /// </summary>
        public string UnitId { get; }

        /// <summary>
        /// 单位所属阵营
        /// </summary>
        public BattleTeam Team { get; }

        /// <summary>
        /// 当前所在格子
        /// </summary>
        public GridCell CurrentCell { get; private set; }

        /// <summary>
        /// 单位是否存活
        /// </summary>
        public bool IsAlive => CurrentHealth > 0;

        /// <summary>
        /// 当前生命值
        /// </summary>
        public float CurrentHealth { get; protected set; }

        /// <summary>
        /// 最大生命值
        /// </summary>
        public float MaxHealth { get; protected set; }

        /// <summary>
        /// 攻击力
        /// </summary>
        public float Attack { get; protected set; }

        /// <summary>
        /// 防御力
        /// </summary>
        public float Defense { get; protected set; }

        /// <summary>
        /// 攻击速度（每秒攻击次数）
        /// </summary>
        public float AttackSpeed { get; protected set; } = 1f;

        /// <summary>
        /// 攻击范围（格子数）
        /// </summary>
        public int AttackRange { get; protected set; } = 1;

        /// <summary>
        /// 技能槽位
        /// </summary>
        public SkillSlotManager SkillSlots { get; }

        /// <summary>
        /// AI 配置
        /// </summary>
        public UnitAIConfig AIConfig { get; set; }

        /// <summary>
        /// 攻击冷却计时器
        /// </summary>
        protected float _attackCooldown;

        /// <summary>
        /// 移动冷却计时器
        /// </summary>
        protected float _moveCooldown;

        /// <summary>
        /// 移动冷却时间（秒）
        /// </summary>
        protected float _moveCooldownTime = 0.8f;

        // 事件
        public event Action<float, float> OnHealthChanged;
        public event Action OnDeath;
        public event Action<IBattleUnit> OnAttackPerformed;
        public event Action<SkillData, IBattleUnit> OnSkillUsed;

        protected BattleUnitBase(string unitId, BattleTeam team, int skillSlotCount = 4)
        {
            UnitId = unitId;
            Team = team;
            SkillSlots = new SkillSlotManager(skillSlotCount);
            AIConfig = new UnitAIConfig();
        }

        /// <summary>
        /// 设置单位所在格子
        /// </summary>
        public void SetCell(GridCell cell)
        {
            CurrentCell = cell;
        }

        /// <summary>
        /// 初始化属性
        /// </summary>
        public virtual void Initialize(float maxHealth, float attack, float defense)
        {
            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
            Attack = attack;
            Defense = defense;
        }

        /// <summary>
        /// 受到伤害
        /// </summary>
        public virtual void TakeDamage(float damage, IBattleUnit attacker)
        {
            // 计算实际伤害（考虑防御）
            float actualDamage = Mathf.Max(1f, damage - Defense * 0.5f);
            float oldHealth = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - actualDamage);

            OnHealthChanged?.Invoke(oldHealth, CurrentHealth);

            if (CurrentHealth <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// 恢复生命
        /// </summary>
        public virtual void Heal(float amount)
        {
            float oldHealth = CurrentHealth;
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
            OnHealthChanged?.Invoke(oldHealth, CurrentHealth);
        }

        /// <summary>
        /// 死亡
        /// </summary>
        protected virtual void Die()
        {
            OnDeath?.Invoke();
        }

        /// <summary>
        /// 战斗更新
        /// </summary>
        public virtual void BattleTick(float deltaTime, AutoBattleManager battleManager)
        {
            if (!IsAlive) return;

            if (_attackCooldown > 0) _attackCooldown -= deltaTime;
            if (_moveCooldown > 0) _moveCooldown -= deltaTime;

            SkillSlots.UpdateCooldowns(deltaTime);
            ProcessAI(deltaTime, battleManager);
        }

        /// <summary>
        /// 处理 AI 决策
        /// </summary>
        protected virtual void ProcessAI(float deltaTime, AutoBattleManager battleManager)
        {
            var target = FindTarget(battleManager);
            if (target == null) return;

            // 尝试使用技能
            if (SkillSlots.TryGetAvailableSkill(
                this, battleManager, target, out var availableSkill, out var skillTarget))
            {
                UseSkill(availableSkill, skillTarget, battleManager);
                return;
            }

            // 在攻击范围内 → 攻击
            if (IsInRange(target, battleManager))
            {
                if (_attackCooldown <= 0)
                {
                    PerformAttack(target);
                    _attackCooldown = 1f / AttackSpeed;
                }
                return;
            }

            // 不在范围内 → 尝试移动靠近
            if (_moveCooldown <= 0 && AIConfig.MovementTendency != MovementTendency.Hold)
            {
                TryMoveTowards(target, battleManager);
                _moveCooldown = _moveCooldownTime;
            }
        }

        /// <summary>
        /// 寻找目标
        /// </summary>
        protected virtual IBattleUnit FindTarget(AutoBattleManager battleManager)
        {
            var enemies = Team == BattleTeam.Player
                ? battleManager.GetAliveEnemyUnits()
                : battleManager.GetAlivePlayerUnits();

            if (enemies.Count == 0) return null;

            // 根据 AI 配置选择目标
            return AIConfig.TargetPriority switch
            {
                TargetPriority.Nearest => FindNearestTarget(enemies, battleManager),
                TargetPriority.Farthest => FindFarthestTarget(enemies, battleManager),
                TargetPriority.LowestHealth => FindLowestHealthTarget(enemies),
                TargetPriority.HighestHealth => FindHighestHealthTarget(enemies),
                TargetPriority.BackRow => FindBackRowTarget(enemies, battleManager),
                TargetPriority.FrontRow => FindFrontRowTarget(enemies, battleManager),
                _ => enemies[0]
            };
        }

        private IBattleUnit FindNearestTarget(
            System.Collections.Generic.IReadOnlyList<IBattleUnit> enemies,
            AutoBattleManager battleManager)
        {
            IBattleUnit nearest = null;
            int minDistance = int.MaxValue;

            foreach (var enemy in enemies)
            {
                int distance = battleManager.GetDistance(this, enemy);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = enemy;
                }
            }

            return nearest ?? enemies[0];
        }

        private IBattleUnit FindFarthestTarget(
            System.Collections.Generic.IReadOnlyList<IBattleUnit> enemies,
            AutoBattleManager battleManager)
        {
            IBattleUnit farthest = null;
            int maxDistance = -1;

            foreach (var enemy in enemies)
            {
                int distance = battleManager.GetDistance(this, enemy);
                if (distance > maxDistance && distance < int.MaxValue)
                {
                    maxDistance = distance;
                    farthest = enemy;
                }
            }

            return farthest ?? enemies[0];
        }

        private IBattleUnit FindLowestHealthTarget(System.Collections.Generic.IReadOnlyList<IBattleUnit> enemies)
        {
            IBattleUnit lowest = null;
            float lowestHealth = float.MaxValue;

            foreach (var enemy in enemies)
            {
                if (enemy is BattleUnitBase unit && unit.CurrentHealth < lowestHealth)
                {
                    lowestHealth = unit.CurrentHealth;
                    lowest = enemy;
                }
            }

            return lowest ?? enemies[0];
        }

        private IBattleUnit FindHighestHealthTarget(System.Collections.Generic.IReadOnlyList<IBattleUnit> enemies)
        {
            IBattleUnit highest = null;
            float highestHealth = 0f;

            foreach (var enemy in enemies)
            {
                if (enemy is BattleUnitBase unit && unit.CurrentHealth > highestHealth)
                {
                    highestHealth = unit.CurrentHealth;
                    highest = enemy;
                }
            }

            return highest ?? enemies[0];
        }

        private IBattleUnit FindBackRowTarget(
            System.Collections.Generic.IReadOnlyList<IBattleUnit> enemies,
            AutoBattleManager battleManager)
        {
            IBattleUnit backRow = null;
            int maxDepth = -1;

            foreach (var enemy in enemies)
            {
                int depth = battleManager.GetDepthFromFront(enemy);
                if (depth > maxDepth && depth < int.MaxValue)
                {
                    maxDepth = depth;
                    backRow = enemy;
                }
            }

            return backRow ?? enemies[0];
        }

        private IBattleUnit FindFrontRowTarget(
            System.Collections.Generic.IReadOnlyList<IBattleUnit> enemies,
            AutoBattleManager battleManager)
        {
            IBattleUnit frontRow = null;
            int minDepth = int.MaxValue;

            foreach (var enemy in enemies)
            {
                int depth = battleManager.GetDepthFromFront(enemy);
                if (depth < minDepth)
                {
                    minDepth = depth;
                    frontRow = enemy;
                }
            }

            return frontRow ?? enemies[0];
        }

        /// <summary>
        /// 检查目标是否在攻击范围内（支持跨棋盘）
        /// </summary>
        protected bool IsInRange(IBattleUnit target, AutoBattleManager battleManager = null)
        {
            if (CurrentCell == null || target.CurrentCell == null)
                return false;

            if (battleManager == null)
            {
                return GridBoard.GetManhattanDistance(CurrentCell, target.CurrentCell) <= AttackRange;
            }

            return battleManager.GetDistance(this, target) <= AttackRange;
        }

        /// <summary>
        /// 尝试向目标方向移动一格
        /// </summary>
        protected virtual void TryMoveTowards(IBattleUnit target, AutoBattleManager battleManager)
        {
            if (CurrentCell == null || target.CurrentCell == null) return;

            var myBoard = Team == BattleTeam.Player
                ? battleManager.PlayerBoard
                : battleManager.EnemyBoard;

            // 玩家向 X+ 靠近敌方，敌方向 X- 靠近玩家
            int dx = Team == BattleTeam.Player ? 1 : -1;

            // Y 轴对齐目标
            int dy = 0;
            int yDiff = target.CurrentCell.Y - CurrentCell.Y;
            if (yDiff != 0) dy = yDiff > 0 ? 1 : -1;

            // 优先 X 方向移动
            if (dx != 0)
            {
                int newX = CurrentCell.X + dx;
                if (myBoard.IsValidPosition(newX, CurrentCell.Y)
                    && myBoard.MoveUnit(this, newX, CurrentCell.Y))
                    return;
            }

            // X 走不了，尝试 Y 方向
            if (dy != 0)
            {
                int newY = CurrentCell.Y + dy;
                if (myBoard.IsValidPosition(CurrentCell.X, newY))
                    myBoard.MoveUnit(this, CurrentCell.X, newY);
            }
        }

        /// <summary>
        /// 执行普通攻击
        /// </summary>
        protected virtual void PerformAttack(IBattleUnit target)
        {
            if (target is BattleUnitBase targetUnit)
            {
                OnAttackPerformed?.Invoke(target);
                targetUnit.TakeDamage(Attack, this);
            }
        }

        /// <summary>
        /// 使用技能
        /// </summary>
        protected virtual void UseSkill(SkillData skill, IBattleUnit target, AutoBattleManager battleManager)
        {
            OnSkillUsed?.Invoke(skill, target);
            skill.Execute(this, target, battleManager);
            SkillSlots.StartCooldown(skill);
        }
    }
}
