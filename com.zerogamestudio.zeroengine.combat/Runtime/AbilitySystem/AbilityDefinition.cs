using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.AbilitySystem
{
    public enum AbilityTargetMode
    {
        SelectedTargets,
        Self,
        AllTargets
    }

    public enum AbilityExecutionResultType
    {
        Failed,
        ResourceInsufficient,
        CooldownBlocked,
        LevelBlocked,
        BoostInsufficient,
        NoValidTargets,
        ConditionFailed,
        ResourceConsumed,
        BoostConsumed,
        Damage,
        Heal,
        ShieldDamage,
        BuffApplied,
        BuffRemoved,
        CooldownStarted
    }

    public enum AbilityTargetRelationship
    {
        Any,
        Ally,
        Enemy
    }

    public enum AbilityValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct AbilityValidationIssue
    {
        public AbilityValidationIssue(string code, AbilityValidationSeverity severity, string message)
        {
            Code = code;
            Severity = severity;
            Message = message;
        }

        public string Code { get; }
        public AbilityValidationSeverity Severity { get; }
        public string Message { get; }
    }

    public static class AbilityDefinitionValidator
    {
        public static IEnumerable<AbilityValidationIssue> Validate(AbilityDefinition ability)
        {
            if (ability == null)
            {
                yield return Error("ABILITY_NULL", "Ability definition is null.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(ability.AbilityId))
            {
                yield return Error("ABILITY_ID_EMPTY", "Ability id is empty.");
            }

            if (ability.Effects == null || ability.Effects.Count == 0)
            {
                yield return Error("ABILITY_EFFECTS_EMPTY", "Ability has no effects.");
            }

            foreach (var issue in ValidateComponents(ability.Triggers, "Trigger"))
            {
                yield return issue;
            }

            foreach (var issue in ValidateComponents(ability.Conditions, "Condition"))
            {
                yield return issue;
            }

            foreach (var issue in ValidateComponents(ability.Effects, "Effect"))
            {
                yield return issue;
            }

            if (ability.Effects == null)
            {
                yield break;
            }

            foreach (var effect in ability.Effects)
            {
                if (effect is AbilityRemoveBuffEffect removeBuff
                    && removeBuff.BuffData == null
                    && !removeBuff.RemoveAllDispellable)
                {
                    yield return Error("REMOVE_BUFF_EMPTY", "Remove Buff effect needs a BuffData or RemoveAllDispellable.");
                }
            }
        }

        private static IEnumerable<AbilityValidationIssue> ValidateComponents<T>(IEnumerable<T> components, string label)
        {
            if (components == null)
            {
                yield break;
            }

            foreach (var component in components)
            {
                if (component == null)
                {
                    yield return Error("ABILITY_COMPONENT_NULL", $"{label} list contains a missing managed reference.");
                }
            }
        }

        private static AbilityValidationIssue Error(string code, string message)
        {
            return new AbilityValidationIssue(code, AbilityValidationSeverity.Error, message);
        }
    }

    public enum AbilityStatComparison
    {
        Less,
        LessOrEqual,
        Equal,
        GreaterOrEqual,
        Greater
    }

    [Serializable]
    public sealed class AbilityDefinition
    {
        [AbilityFieldDoc("技能 ID", "技能逻辑使用的稳定标识，建议与项目侧 skillId 保持一致。")]
        public string AbilityId;
        [AbilityFieldDoc("显示名称", "编辑器和可选 UI 使用的技能名称。")]
        public string DisplayName;
        [AbilityFieldDoc("技能描述", "策划和 UI 显示的技能说明。")]
        [TextArea]
        public string Description;
        [AbilityFieldDoc("图标", "技能在 UI 或编辑器列表中显示的图标。")]
        public Sprite Icon;

        [Header("Execution")]
        [AbilityFieldDoc("目标模式", "技能默认解析目标的方式，效果未覆盖目标时会使用此设置。")]
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        [AbilityFieldDoc("资源消耗", "释放技能需要消耗的资源数量，由项目 adapter 决定是 MP、内力或其他资源。")]
        public int ResourceCost;
        [AbilityFieldDoc("冷却回合", "技能成功释放后写入的冷却回合数。")]
        public int CooldownTurns;
        [AbilityFieldDoc("需求等级", "释放者等级低于该值时技能会被阻止。")]
        public int RequiredLevel = 1;
        [AbilityFieldDoc("允许 Boost", "开启后执行上下文里的 Boost 等级会提高效果威力并消耗 Boost。")]
        public bool CanBoost = true;
        [AbilityFieldDoc("Boost 威力加成", "每级 Boost 额外增加的威力倍率，例如 0.5 表示每级增加 50%。")]
        public float BoostPowerMultiplier = 0.5f;

        [AbilityFieldDoc("触发器", "定义技能由什么入口触发，主动技能通常使用手动释放。")]
        [SerializeReference]
        public List<AbilityTriggerDefinition> Triggers = new();
        [AbilityFieldDoc("条件", "释放前按顺序检查的条件，任一失败会中断技能。")]
        [SerializeReference]
        public List<AbilityConditionDefinition> Conditions = new();
        [AbilityFieldDoc("效果", "技能成功后按顺序执行的效果列表。")]
        [SerializeReference]
        public List<AbilityEffectDefinition> Effects = new();

        public float GetPowerMultiplier(int boostLevel)
        {
            if (!CanBoost || boostLevel <= 0)
            {
                return 1f;
            }

            return 1f + Mathf.Max(0f, BoostPowerMultiplier) * boostLevel;
        }
    }

    [Serializable]
    public abstract class AbilityTriggerDefinition
    {
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Trigger,
        "手动释放",
        "由外部战斗流程主动调用的技能触发器。",
        "适合玩家、AI 或战棋指令直接释放的主动技能。被动、事件响应类技能后续再增加专用 Trigger。")]
    public sealed class ManualAbilityTrigger : AbilityTriggerDefinition
    {
    }

    [Serializable]
    public abstract class AbilityConditionDefinition
    {
        public abstract bool CanExecute(AbilityExecutionContext context, out AbilityExecutionResult result);

        public static AbilityExecutionResult Failed(AbilityExecutionContext context, object target = null)
        {
            return AbilityExecutionResult.Simple(
                AbilityExecutionResultType.ConditionFailed,
                context.Actor,
                target,
                abilityId: context.Ability?.AbilityId);
        }

        protected static bool EvaluateTargets(
            AbilityExecutionContext context,
            AbilityTargetMode targetMode,
            bool requireAllTargets,
            Func<object, bool> predicate,
            out AbilityExecutionResult result)
        {
            var anyTarget = false;
            var anyMatch = false;
            foreach (var target in AbilityTargetResolver.ResolveTargets(context, targetMode))
            {
                anyTarget = true;
                var matches = predicate(target);
                anyMatch |= matches;
                if (requireAllTargets && !matches)
                {
                    result = Failed(context, target);
                    return false;
                }
            }

            if (!anyTarget || (!requireAllTargets && !anyMatch))
            {
                result = Failed(context);
                return false;
            }

            result = default;
            return true;
        }
    }

    [Serializable]
    public abstract class AbilityEffectDefinition
    {
        [AbilityFieldDoc("效果目标", "该效果实际作用的目标；选中目标会回落到技能默认目标模式。")]
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;

        public abstract void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results);
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Condition,
        "目标存活",
        "要求目标处于存活或死亡状态。",
        "主动攻击、治疗和控制技能通常要求目标存活；复活类技能可以把 RequireAlive 关闭后改为要求死亡目标。")]
    public sealed class AbilityTargetAliveCondition : AbilityConditionDefinition
    {
        [AbilityFieldDoc("目标模式", "要检查存活状态的目标集合。")]
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        [AbilityFieldDoc("要求存活", "开启时要求目标存活；关闭时要求目标死亡。")]
        public bool RequireAlive = true;
        [HideInInspector, Obsolete("Target-filtering requires filtered execution targets; conditions now always require every resolved target to match.")]
        public bool RequireAllTargets = true;

        public override bool CanExecute(AbilityExecutionContext context, out AbilityExecutionResult result)
        {
            return EvaluateTargets(
                context,
                TargetMode,
                requireAllTargets: true,
                target => context.Services != null && context.Services.IsTargetAlive(target) == RequireAlive,
                out result);
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Condition,
        "敌我关系",
        "按目标与释放者的敌我关系过滤。",
        "常用于只允许攻击敌方、只允许治疗友方，或限制某个效果只能对自己阵营生效。")]
    public sealed class AbilityFactionCondition : AbilityConditionDefinition
    {
        [AbilityFieldDoc("目标模式", "要检查敌我关系的目标集合。")]
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        [AbilityFieldDoc("目标关系", "目标相对释放者必须满足的关系。")]
        public AbilityTargetRelationship RequiredRelationship = AbilityTargetRelationship.Enemy;
        [HideInInspector, Obsolete("Target-filtering requires filtered execution targets; conditions now always require every resolved target to match.")]
        public bool RequireAllTargets = true;

        public override bool CanExecute(AbilityExecutionContext context, out AbilityExecutionResult result)
        {
            if (RequiredRelationship == AbilityTargetRelationship.Any)
            {
                result = default;
                return true;
            }

            return EvaluateTargets(
                context,
                TargetMode,
                requireAllTargets: true,
                target =>
                {
                    if (context.Services == null)
                    {
                        return false;
                    }

                    var allies = context.Services.AreAllies(context.Actor, target);
                    return RequiredRelationship == AbilityTargetRelationship.Ally ? allies : !allies;
                },
                out result);
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Condition,
        "概率",
        "按指定概率决定本次技能是否继续执行。",
        "适合做概率附加效果或早期 Demo 随机触发。Chance 使用 0 到 1 的比例，1 表示必定通过。")]
    public sealed class AbilityProbabilityCondition : AbilityConditionDefinition
    {
        [AbilityFieldDoc("通过概率", "0 到 1 的触发概率；1 表示必定通过，0 表示必定失败。")]
        [Range(0f, 1f)]
        public float Chance = 1f;

        public override bool CanExecute(AbilityExecutionContext context, out AbilityExecutionResult result)
        {
            var chance = Mathf.Clamp01(Chance);
            if (chance >= 1f)
            {
                result = default;
                return true;
            }

            if (chance <= 0f || context.Services == null || !context.Services.RollChance(chance))
            {
                result = Failed(context);
                return false;
            }

            result = default;
            return true;
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Condition,
        "Buff 条件",
        "检查目标是否拥有或没有指定 Buff。",
        "适合做流派联动、先上标记再引爆、或只对没有某状态的目标生效。")]
    public sealed class AbilityBuffCondition : AbilityConditionDefinition
    {
        [AbilityFieldDoc("目标模式", "要检查 Buff 的目标集合。")]
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        [AbilityFieldDoc("Buff 资产", "项目侧 Buff ScriptableObject 引用，由 runtime service adapter 解释。")]
        public ScriptableObject BuffData;
        [AbilityFieldDoc("要求存在", "开启时要求目标已有该 Buff；关闭时要求目标没有该 Buff。")]
        public bool RequirePresent = true;
        [HideInInspector, Obsolete("Target-filtering requires filtered execution targets; conditions now always require every resolved target to match.")]
        public bool RequireAllTargets = true;

        public override bool CanExecute(AbilityExecutionContext context, out AbilityExecutionResult result)
        {
            if (BuffData == null)
            {
                result = Failed(context);
                return false;
            }

            return EvaluateTargets(
                context,
                TargetMode,
                requireAllTargets: true,
                target => context.Services != null
                          && context.Services.HasBuff(context, target, BuffData) == RequirePresent,
                out result);
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Condition,
        "属性条件",
        "按释放者或目标的通用 StatSystem 属性决定技能是否可执行。",
        "适合生命比例门槛、内力阈值、破防前置、AI 技能筛选和 RPG 编辑器数据校验。")]
    public sealed class AbilityStatCondition : AbilityConditionDefinition
    {
        [AbilityFieldDoc("目标模式", "要读取属性的对象集合。")]
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        [AbilityFieldDoc("属性 ID", "要从 ZE StatSystem 读取的稳定属性 ID，例如 core.max_hp 或 offense.attack。")]
        public StatId StatId;
        [AbilityFieldDoc("比较方式", "属性值与阈值的比较规则。")]
        public AbilityStatComparison Comparison = AbilityStatComparison.GreaterOrEqual;
        [AbilityFieldDoc("阈值", "用于比较的固定数值。")]
        public float Threshold;
        [AbilityFieldDoc("要求全部目标通过", "开启时全部目标都必须满足；关闭时任意一个目标满足即可。")]
        public bool RequireAllTargets = true;

        public override bool CanExecute(AbilityExecutionContext context, out AbilityExecutionResult result)
        {
            return EvaluateTargets(
                context,
                TargetMode,
                RequireAllTargets,
                target => context.Services is IAbilityStatRuntimeServices statServices
                          && statServices.TryGetStatValue(target, StatId, out var value)
                          && Compare(value, Threshold, Comparison),
                out result);
        }

        private static bool Compare(float value, float threshold, AbilityStatComparison comparison)
        {
            switch (comparison)
            {
                case AbilityStatComparison.Less:
                    return value < threshold;
                case AbilityStatComparison.LessOrEqual:
                    return value <= threshold;
                case AbilityStatComparison.Equal:
                    return Math.Abs(value - threshold) <= 0.0001f;
                case AbilityStatComparison.GreaterOrEqual:
                    return value >= threshold;
                case AbilityStatComparison.Greater:
                    return value > threshold;
                default:
                    return false;
            }
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Effect,
        "伤害",
        "对目标造成一次或多次伤害，并可附带破盾。",
        "Power 使用百分比威力；HitCount 控制多段；UseMagicAttack 决定由项目 adapter 选择物理或法术结算。")]
    public sealed class AbilityDamageEffect : AbilityEffectDefinition
    {
        [AbilityFieldDoc("伤害威力", "伤害公式使用的基础威力百分比，100 表示一倍标准威力。")]
        public int Power = 100;
        [AbilityFieldDoc("攻击段数", "同一目标执行伤害结算的次数，至少按 1 段处理。")]
        public int HitCount = 1;
        [AbilityFieldDoc("法术攻击", "开启后项目 adapter 可按法术/内功公式计算伤害。")]
        public bool UseMagicAttack;
        [AbilityFieldDoc("破盾值", "伤害后额外削减的护盾值，0 表示不附带破盾。")]
        public int ShieldDamage;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            if (context.Services == null || target == null)
            {
                return;
            }

            var multiplier = context.Ability.GetPowerMultiplier(context.BoostLevel);
            var hits = Mathf.Max(1, HitCount);
            for (var i = 0; i < hits; i++)
            {
                var damage = context.Services.CalculateDamage(context, target, this, multiplier);
                var actual = context.Services.ApplyDamage(context, target, damage, this);
                results.Add(AbilityExecutionResult.Damage(context.Actor, target, actual, context.Ability.AbilityId));
            }

            if (ShieldDamage > 0)
            {
                context.Services.ApplyShieldDamage(context, target, ShieldDamage);
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.ShieldDamage,
                    context.Actor,
                    target,
                    ShieldDamage,
                    context.Ability.AbilityId));
            }
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Effect,
        "治疗",
        "按威力为目标恢复生命。",
        "具体治疗公式由项目 runtime services 决定，ZE 只负责执行顺序和结构化结果。")]
    public sealed class AbilityHealEffect : AbilityEffectDefinition
    {
        [AbilityFieldDoc("治疗威力", "治疗公式使用的基础威力百分比，100 表示一倍标准治疗量。")]
        public int Power = 100;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            if (context.Services == null || target == null)
            {
                return;
            }

            var heal = context.Services.CalculateHeal(context, target, this, context.Ability.GetPowerMultiplier(context.BoostLevel));
            var actual = context.Services.ApplyHeal(context, target, heal, this);
            results.Add(AbilityExecutionResult.Heal(context.Actor, target, actual, context.Ability.AbilityId));
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Effect,
        "破盾",
        "直接削减目标护盾值。",
        "用于战棋/破防规则，具体护盾对象和破盾后状态由项目 adapter 处理。")]
    public sealed class AbilityShieldDamageEffect : AbilityEffectDefinition
    {
        [AbilityFieldDoc("破盾值", "直接削减目标护盾的数值。")]
        public int Amount = 1;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            if (context.Services == null || target == null || Amount <= 0)
            {
                return;
            }

            context.Services.ApplyShieldDamage(context, target, Amount);
            results.Add(AbilityExecutionResult.Simple(
                AbilityExecutionResultType.ShieldDamage,
                context.Actor,
                target,
                Amount,
                context.Ability.AbilityId));
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Effect,
        "施加 Buff",
        "向目标添加一个项目侧 Buff 资产。",
        "ZE 只保存 ScriptableObject 引用和持续时间；Buff 类型、叠加和表现由项目 adapter 处理。")]
    public sealed class AbilityBuffEffect : AbilityEffectDefinition
    {
        [AbilityFieldDoc("Buff 资产", "要施加到目标身上的项目侧 Buff ScriptableObject。")]
        public ScriptableObject BuffData;
        [AbilityFieldDoc("持续回合", "Buff 持续的回合数；具体 0 或负数语义由项目 adapter 决定。")]
        public int Duration;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            if (context.Services == null || target == null || BuffData == null)
            {
                return;
            }

            context.Services.ApplyBuff(context, target, BuffData, Duration);
            results.Add(AbilityExecutionResult.Simple(
                AbilityExecutionResultType.BuffApplied,
                context.Actor,
                target,
                Duration,
                context.Ability.AbilityId,
                BuffData));
        }
    }

    [Serializable]
    [AbilityComponentDoc(
        AbilityComponentDocCategory.Effect,
        "移除 Buff",
        "从目标身上移除指定 Buff，或交给项目侧清理所有可驱散 Buff。",
        "BuffData 为空且 RemoveAllDispellable 开启时表示净化/驱散类效果；实际筛选规则由项目 adapter 决定。")]
    public sealed class AbilityRemoveBuffEffect : AbilityEffectDefinition
    {
        [AbilityFieldDoc("Buff 资产", "要移除的指定 Buff；留空且开启净化时表示清理所有可驱散 Buff。")]
        public ScriptableObject BuffData;
        [AbilityFieldDoc("净化可驱散 Buff", "开启后交给项目 adapter 移除目标身上所有可驱散 Buff。")]
        public bool RemoveAllDispellable;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            if (context.Services == null || target == null)
            {
                return;
            }

            if (BuffData == null && !RemoveAllDispellable)
            {
                return;
            }

            var removed = context.Services.RemoveBuff(context, target, BuffData, RemoveAllDispellable);
            if (removed <= 0)
            {
                return;
            }

            results.Add(AbilityExecutionResult.Simple(
                AbilityExecutionResultType.BuffRemoved,
                context.Actor,
                target,
                removed,
                context.Ability.AbilityId,
                BuffData));
        }
    }

    public readonly struct AbilityExecutionContext
    {
        public AbilityExecutionContext(
            AbilityDefinition ability,
            object actor,
            IEnumerable<object> targets,
            IAbilityRuntimeServices services,
            int boostLevel = 0,
            object abilityKey = null)
        {
            Ability = ability;
            Actor = actor;
            Targets = targets != null ? new List<object>(targets) : new List<object>();
            Services = services;
            BoostLevel = Mathf.Max(0, boostLevel);
            AbilityKey = abilityKey ?? ability;
        }

        public AbilityDefinition Ability { get; }
        public object Actor { get; }
        public IReadOnlyList<object> Targets { get; }
        public IAbilityRuntimeServices Services { get; }
        public int BoostLevel { get; }
        public object AbilityKey { get; }
    }

    public sealed class AbilityExecutionSummary
    {
        public AbilityExecutionSummary(bool succeeded, List<AbilityExecutionResult> results)
        {
            Succeeded = succeeded;
            Results = results ?? new List<AbilityExecutionResult>();
        }

        public bool Succeeded { get; }
        public List<AbilityExecutionResult> Results { get; }
    }

    public readonly struct AbilityExecutionResult
    {
        public AbilityExecutionResult(
            AbilityExecutionResultType type,
            object actor,
            object target,
            int amount,
            string abilityId,
            object payload = null)
        {
            Type = type;
            Actor = actor;
            Target = target;
            Amount = amount;
            AbilityId = abilityId;
            Payload = payload;
        }

        public AbilityExecutionResultType Type { get; }
        public object Actor { get; }
        public object Target { get; }
        public int Amount { get; }
        public string AbilityId { get; }
        public object Payload { get; }

        public static AbilityExecutionResult Simple(
            AbilityExecutionResultType type,
            object actor,
            object target = null,
            int amount = 0,
            string abilityId = null,
            object payload = null)
        {
            return new AbilityExecutionResult(type, actor, target, amount, abilityId, payload);
        }

        public static AbilityExecutionResult Damage(object actor, object target, int amount, string abilityId)
        {
            return new AbilityExecutionResult(AbilityExecutionResultType.Damage, actor, target, amount, abilityId);
        }

        public static AbilityExecutionResult Heal(object actor, object target, int amount, string abilityId)
        {
            return new AbilityExecutionResult(AbilityExecutionResultType.Heal, actor, target, amount, abilityId);
        }
    }

    public interface IAbilityRuntimeServices
    {
        bool HasResource(object actor, int amount);
        bool ConsumeResource(object actor, int amount);
        bool HasBoost(object actor, int amount);
        bool ConsumeBoost(object actor, int amount);
        int GetLevel(object actor);
        int GetCooldown(object actor, object abilityKey);
        void SetCooldown(object actor, object abilityKey, int turns);
        int CalculateDamage(AbilityExecutionContext context, object target, AbilityDamageEffect effect, float powerMultiplier);
        int CalculateHeal(AbilityExecutionContext context, object target, AbilityHealEffect effect, float powerMultiplier);
        int ApplyDamage(AbilityExecutionContext context, object target, int amount, AbilityDamageEffect effect);
        int ApplyHeal(AbilityExecutionContext context, object target, int amount, AbilityHealEffect effect);
        void ApplyShieldDamage(AbilityExecutionContext context, object target, int amount);
        void ApplyBuff(AbilityExecutionContext context, object target, ScriptableObject buffData, int duration);
        bool IsTargetAlive(object target);
        bool AreAllies(object actor, object target);
        bool HasBuff(AbilityExecutionContext context, object target, ScriptableObject buffData);
        int RemoveBuff(AbilityExecutionContext context, object target, ScriptableObject buffData, bool removeAllDispellable);
        bool RollChance(float chance);
    }

    public interface IAbilityStatRuntimeServices
    {
        bool TryGetStatValue(object target, StatId statId, out float value);
    }

    public static class AbilityTargetResolver
    {
        public static IEnumerable<object> ResolveTargets(AbilityExecutionContext context, AbilityTargetMode targetMode)
        {
            var mode = targetMode == AbilityTargetMode.SelectedTargets
                ? context.Ability.TargetMode
                : targetMode;
            if (mode == AbilityTargetMode.Self)
            {
                yield return context.Actor;
                yield break;
            }

            foreach (var target in context.Targets)
            {
                if (target != null)
                {
                    yield return target;
                }
            }
        }
    }

    public static class AbilityExecutor
    {
        public static AbilityExecutionSummary Execute(AbilityExecutionContext context)
        {
            var results = new List<AbilityExecutionResult>();
            if (context.Ability == null || context.Actor == null || context.Services == null)
            {
                results.Add(AbilityExecutionResult.Simple(AbilityExecutionResultType.Failed, context.Actor));
                return new AbilityExecutionSummary(false, results);
            }

            if (!HasExecutableTarget(context))
            {
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.NoValidTargets,
                    context.Actor,
                    abilityId: context.Ability.AbilityId));
                return new AbilityExecutionSummary(false, results);
            }

            if (!CanPayCosts(context, results))
            {
                return new AbilityExecutionSummary(false, results);
            }

            foreach (var condition in context.Ability.Conditions ?? EmptyConditions)
            {
                if (condition != null && !condition.CanExecute(context, out var failure))
                {
                    results.Add(failure);
                    return new AbilityExecutionSummary(false, results);
                }
            }

            if (context.Ability.ResourceCost > 0)
            {
                context.Services.ConsumeResource(context.Actor, context.Ability.ResourceCost);
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.ResourceConsumed,
                    context.Actor,
                    amount: context.Ability.ResourceCost,
                    abilityId: context.Ability.AbilityId));
            }

            if (context.BoostLevel > 0 && context.Ability.CanBoost)
            {
                context.Services.ConsumeBoost(context.Actor, context.BoostLevel);
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.BoostConsumed,
                    context.Actor,
                    amount: context.BoostLevel,
                    abilityId: context.Ability.AbilityId));
            }

            foreach (var effect in context.Ability.Effects ?? EmptyEffects)
            {
                if (effect == null)
                {
                    continue;
                }

                foreach (var target in AbilityTargetResolver.ResolveTargets(context, effect.TargetMode))
                {
                    effect.Execute(context, target, results);
                }
            }

            if (context.Ability.CooldownTurns > 0)
            {
                context.Services.SetCooldown(context.Actor, context.AbilityKey, context.Ability.CooldownTurns);
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.CooldownStarted,
                    context.Actor,
                    amount: context.Ability.CooldownTurns,
                    abilityId: context.Ability.AbilityId));
            }

            return new AbilityExecutionSummary(true, results);
        }

        private static readonly List<AbilityConditionDefinition> EmptyConditions = new();
        private static readonly List<AbilityEffectDefinition> EmptyEffects = new();

        private static bool HasExecutableTarget(AbilityExecutionContext context)
        {
            var hasEffect = false;
            foreach (var effect in context.Ability.Effects ?? EmptyEffects)
            {
                if (effect == null)
                {
                    continue;
                }

                hasEffect = true;
                foreach (var target in AbilityTargetResolver.ResolveTargets(context, effect.TargetMode))
                {
                    if (target != null)
                    {
                        return true;
                    }
                }
            }

            return !hasEffect;
        }

        private static bool CanPayCosts(AbilityExecutionContext context, List<AbilityExecutionResult> results)
        {
            if (context.Services.GetLevel(context.Actor) < Mathf.Max(1, context.Ability.RequiredLevel))
            {
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.LevelBlocked,
                    context.Actor,
                    amount: context.Ability.RequiredLevel,
                    abilityId: context.Ability.AbilityId));
                return false;
            }

            if (context.Ability.CooldownTurns > 0 && context.Services.GetCooldown(context.Actor, context.AbilityKey) > 0)
            {
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.CooldownBlocked,
                    context.Actor,
                    amount: context.Services.GetCooldown(context.Actor, context.AbilityKey),
                    abilityId: context.Ability.AbilityId));
                return false;
            }

            if (context.Ability.ResourceCost > 0 && !context.Services.HasResource(context.Actor, context.Ability.ResourceCost))
            {
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.ResourceInsufficient,
                    context.Actor,
                    amount: context.Ability.ResourceCost,
                    abilityId: context.Ability.AbilityId));
                return false;
            }

            if (context.BoostLevel > 0 && context.Ability.CanBoost && !context.Services.HasBoost(context.Actor, context.BoostLevel))
            {
                results.Add(AbilityExecutionResult.Simple(
                    AbilityExecutionResultType.BoostInsufficient,
                    context.Actor,
                    amount: context.BoostLevel,
                    abilityId: context.Ability.AbilityId));
                return false;
            }

            return true;
        }

    }
}
