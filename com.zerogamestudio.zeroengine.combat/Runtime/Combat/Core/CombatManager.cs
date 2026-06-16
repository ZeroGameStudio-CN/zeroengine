using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Combat
{
    /// <summary>
    /// 战斗管理器 - 管理战斗生命周期和战斗单位
    /// </summary>
    public class CombatManager : MonoSingleton<CombatManager>
    {
        [Header("配置")]
        [SerializeField] private bool _autoRegisterOnAwake = true;
        [SerializeField] private float _combatExitDelay = 3f;

        /// <summary>所有注册的战斗单位</summary>
        private readonly Dictionary<string, ICombatant> _combatants = new();

        /// <summary>所有战斗队伍</summary>
        private readonly Dictionary<int, CombatTeam> _teams = new();

        /// <summary>队伍关系管理器</summary>
        private readonly TeamRelationManager _relationManager = new();

        /// <summary>当前活跃的战斗上下文</summary>
        private readonly List<CombatContext> _activeContexts = new();

        /// <summary>伤害计算器</summary>
        public DamageCalculator DamageCalculator { get; private set; }

        /// <summary>队伍关系管理器</summary>
        public TeamRelationManager RelationManager => _relationManager;

        /// <summary>是否处于战斗中</summary>
        public bool IsInCombat => _activeContexts.Count > 0;

        /// <summary>战斗单位是否应在 Awake 阶段自动注册</summary>
        public bool AutoRegisterOnAwake => _autoRegisterOnAwake;

        /// <summary>默认战斗退出延迟</summary>
        public float CombatExitDelay => _combatExitDelay;

        /// <summary>战斗开始事件</summary>
        public event Action<CombatContext> OnCombatStarted;

        /// <summary>战斗结束事件</summary>
        public event Action<CombatContext> OnCombatEnded;

        /// <summary>战斗单位注册事件</summary>
        public event Action<ICombatant> OnCombatantRegistered;

        /// <summary>战斗单位注销事件</summary>
        public event Action<ICombatant> OnCombatantUnregistered;

        /// <summary>伤害事件</summary>
        public event Action<DamageEventArgs> OnDamageDealt;

        /// <summary>治疗事件</summary>
        public event Action<HealEventArgs> OnHealApplied;

        /// <summary>击杀事件</summary>
        public event Action<KillEventArgs> OnKillOccurred;

        protected override void Awake()
        {
            base.Awake();
            DamageCalculator = new DamageCalculator();
        }

        #region 战斗单位管理

        /// <summary>
        /// 注册战斗单位
        /// </summary>
        public void RegisterCombatant(ICombatant combatant)
        {
            if (combatant == null) return;

            if (_combatants.TryAdd(combatant.CombatantId, combatant))
            {
                // 自动加入队伍
                var team = GetOrCreateTeam(combatant.TeamId);
                team.AddMember(combatant);

                OnCombatantRegistered?.Invoke(combatant);
            }
        }

        /// <summary>
        /// 注销战斗单位
        /// </summary>
        public void UnregisterCombatant(ICombatant combatant)
        {
            if (combatant == null) return;

            if (_combatants.Remove(combatant.CombatantId))
            {
                // 从队伍移除
                if (_teams.TryGetValue(combatant.TeamId, out var team))
                {
                    team.RemoveMember(combatant);
                }

                OnCombatantUnregistered?.Invoke(combatant);
            }
        }

        /// <summary>
        /// 获取战斗单位
        /// </summary>
        public ICombatant GetCombatant(string combatantId)
        {
            return _combatants.GetValueOrDefault(combatantId);
        }

        /// <summary>
        /// 获取所有战斗单位
        /// </summary>
        public IEnumerable<ICombatant> GetAllCombatants()
        {
            return _combatants.Values;
        }

        /// <summary>
        /// 获取指定队伍的所有战斗单位
        /// </summary>
        public IEnumerable<ICombatant> GetTeamCombatants(int teamId)
        {
            if (_teams.TryGetValue(teamId, out var team))
            {
                return team.Members;
            }
            return Array.Empty<ICombatant>();
        }

        #endregion

        #region 队伍管理

        /// <summary>
        /// 获取或创建队伍
        /// </summary>
        public CombatTeam GetOrCreateTeam(int teamId, string teamName = null)
        {
            if (!_teams.TryGetValue(teamId, out var team))
            {
                team = new CombatTeam(teamId, teamName);
                _teams[teamId] = team;
            }
            return team;
        }

        /// <summary>
        /// 获取队伍
        /// </summary>
        public CombatTeam GetTeam(int teamId)
        {
            return _teams.GetValueOrDefault(teamId);
        }

        /// <summary>
        /// 设置队伍关系
        /// </summary>
        public void SetTeamRelation(int teamA, int teamB, TeamRelation relation)
        {
            _relationManager.SetRelation(teamA, teamB, relation);
        }

        /// <summary>
        /// 检查两个单位是否敌对
        /// </summary>
        public bool AreHostile(ICombatant a, ICombatant b)
        {
            return _relationManager.IsHostile(a, b);
        }

        /// <summary>
        /// 检查两个单位是否友好
        /// </summary>
        public bool AreFriendly(ICombatant a, ICombatant b)
        {
            return _relationManager.IsFriendly(a, b);
        }

        #endregion

        #region 战斗管理

        /// <summary>
        /// 开始战斗
        /// </summary>
        public CombatContext StartCombat(string contextId = null)
        {
            var context = new CombatContext(contextId);
            _activeContexts.Add(context);
            context.Start();

            OnCombatStarted?.Invoke(context);
            return context;
        }

        /// <summary>
        /// 结束战斗
        /// </summary>
        public void EndCombat(CombatContext context)
        {
            if (context == null) return;

            if (_activeContexts.Remove(context))
            {
                context.End();
                OnCombatEnded?.Invoke(context);
            }
        }

        /// <summary>
        /// 结束所有战斗
        /// </summary>
        public void EndAllCombat()
        {
            foreach (var context in _activeContexts.ToArray())
            {
                EndCombat(context);
            }
        }

        /// <summary>
        /// 获取当前活跃的战斗上下文
        /// </summary>
        public IReadOnlyList<CombatContext> GetActiveContexts()
        {
            return _activeContexts;
        }

        #endregion

        #region 伤害处理

        /// <summary>
        /// 对目标造成伤害
        /// </summary>
        /// <param name="damage">伤害数据</param>
        /// <param name="target">目标</param>
        /// <param name="attackerStatGetter">攻击者属性获取器</param>
        /// <param name="defenderStatGetter">防御者属性获取器</param>
        /// <returns>伤害结果</returns>
        public DamageResult DealDamage(
            DamageData damage,
            ICombatant target,
            Func<string, float> attackerStatGetter = null,
            Func<string, float> defenderStatGetter = null)
        {
            if (target == null || !target.IsAlive)
            {
                return new DamageResult(damage, 0f);
            }

            // 计算伤害
            var result = DamageCalculator.Calculate(damage, target, attackerStatGetter, defenderStatGetter);

            // 应用伤害
            var finalResult = target.TakeDamage(damage.WithBaseDamage(result.FinalDamage));

            // 触发事件
            var eventArgs = new DamageEventArgs(damage.Source, target, finalResult);
            OnDamageDealt?.Invoke(eventArgs);

            // 检查击杀
            if (finalResult.IsKill && damage.Source != null)
            {
                var killArgs = new KillEventArgs(damage.Source, target, finalResult);
                OnKillOccurred?.Invoke(killArgs);
            }

            return finalResult;
        }

        /// <summary>
        /// 对目标治疗
        /// </summary>
        public float Heal(ICombatant target, float amount, ICombatant source = null)
        {
            if (target == null || !target.IsAlive || amount <= 0)
            {
                return 0f;
            }

            float actualHeal = target.ReceiveHeal(amount, source);

            var eventArgs = new HealEventArgs(source, target, amount, actualHeal);
            OnHealApplied?.Invoke(eventArgs);

            return actualHeal;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取指定范围内的敌对目标
        /// </summary>
        public IEnumerable<ICombatant> GetHostileTargetsInRange(ICombatant source, float range)
        {
            if (source == null) yield break;

            var sourcePos = source.GetCombatPosition();

            foreach (var combatant in _combatants.Values)
            {
                if (combatant == source) continue;
                if (!combatant.IsAlive || !combatant.IsTargetable) continue;
                if (!AreHostile(source, combatant)) continue;

                float distance = Vector3.Distance(sourcePos, combatant.GetCombatPosition());
                if (distance <= range)
                {
                    yield return combatant;
                }
            }
        }

        /// <summary>
        /// 获取指定范围内的友方目标
        /// </summary>
        public IEnumerable<ICombatant> GetFriendlyTargetsInRange(ICombatant source, float range)
        {
            if (source == null) yield break;

            var sourcePos = source.GetCombatPosition();

            foreach (var combatant in _combatants.Values)
            {
                if (combatant == source) continue;
                if (!combatant.IsAlive || !combatant.IsTargetable) continue;
                if (!AreFriendly(source, combatant)) continue;

                float distance = Vector3.Distance(sourcePos, combatant.GetCombatPosition());
                if (distance <= range)
                {
                    yield return combatant;
                }
            }
        }

        /// <summary>
        /// 清理所有数据
        /// </summary>
        public void Clear()
        {
            EndAllCombat();
            _combatants.Clear();
            _teams.Clear();
            _relationManager.Clear();
        }

        #endregion
    }
}
