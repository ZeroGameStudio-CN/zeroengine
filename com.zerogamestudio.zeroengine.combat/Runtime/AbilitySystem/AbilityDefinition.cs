using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.AbilitySystem
{
    public enum AbilityTargetMode
    {
        Self,
        SelectedTargets
    }

    [Serializable]
    public class AbilityDefinition
    {
        public string AbilityId;
        public string DisplayName;
        [TextArea] public string Description;
        public Sprite Icon;
        [Min(0)] public int ResourceCost;
        [Min(0)] public int CooldownTurns;
        [Min(1)] public int RequiredLevel = 1;
        public bool CanBoost = true;
        public float BoostPowerMultiplier = 0.5f;
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        [SerializeReference] public List<AbilityTriggerDefinition> Triggers = new();
        [SerializeReference] public List<AbilityConditionDefinition> Conditions = new();
        [SerializeReference] public List<AbilityEffectDefinition> Effects = new();
    }

    [Serializable]
    public abstract class AbilityTriggerDefinition
    {
    }

    [Serializable]
    public sealed class ManualAbilityTrigger : AbilityTriggerDefinition
    {
    }

    [Serializable]
    public abstract class AbilityConditionDefinition
    {
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        public abstract bool Evaluate(AbilityExecutionContext context, object target);
    }

    [Serializable]
    public sealed class AbilityTargetAliveCondition : AbilityConditionDefinition
    {
        public override bool Evaluate(AbilityExecutionContext context, object target)
        {
            return context.Services.IsTargetAlive(target);
        }
    }

    public enum AbilityTargetRelationship
    {
        Any,
        Ally,
        Enemy
    }

    [Serializable]
    public sealed class AbilityFactionCondition : AbilityConditionDefinition
    {
        public AbilityTargetRelationship RequiredRelationship = AbilityTargetRelationship.Any;

        public override bool Evaluate(AbilityExecutionContext context, object target)
        {
            if (RequiredRelationship == AbilityTargetRelationship.Any)
            {
                return true;
            }

            var allies = context.Services.AreAllies(context.Actor, target);
            return RequiredRelationship == AbilityTargetRelationship.Ally ? allies : !allies;
        }
    }

    [Serializable]
    public sealed class AbilityBuffCondition : AbilityConditionDefinition
    {
        public ScriptableObject BuffData;
        public bool RequirePresent = true;

        public override bool Evaluate(AbilityExecutionContext context, object target)
        {
            return context.Services.HasBuff(context, target, BuffData) == RequirePresent;
        }
    }

    [Serializable]
    public sealed class AbilityProbabilityCondition : AbilityConditionDefinition
    {
        [Range(0f, 1f)] public float Chance = 1f;

        public override bool Evaluate(AbilityExecutionContext context, object target)
        {
            return context.Services.RollChance(Chance);
        }
    }

    public enum AbilityStatComparison
    {
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
        Equal,
        NotEqual
    }

    [Serializable]
    public sealed class AbilityStatCondition : AbilityConditionDefinition
    {
        public StatId StatId;
        public AbilityStatComparison Comparison = AbilityStatComparison.GreaterOrEqual;
        public float Threshold;

        public override bool Evaluate(AbilityExecutionContext context, object target)
        {
            if (context.Services is not IAbilityStatRuntimeServices statServices
                || !statServices.TryGetStatValue(target, StatId, out var value))
            {
                return false;
            }

            return Comparison switch
            {
                AbilityStatComparison.Greater => value > Threshold,
                AbilityStatComparison.GreaterOrEqual => value >= Threshold,
                AbilityStatComparison.Less => value < Threshold,
                AbilityStatComparison.LessOrEqual => value <= Threshold,
                AbilityStatComparison.Equal => Mathf.Approximately(value, Threshold),
                AbilityStatComparison.NotEqual => !Mathf.Approximately(value, Threshold),
                _ => false
            };
        }
    }

    [Serializable]
    public abstract class AbilityEffectDefinition
    {
        public AbilityTargetMode TargetMode = AbilityTargetMode.SelectedTargets;
        public abstract void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results);
    }

    [Serializable]
    public sealed class AbilityDamageEffect : AbilityEffectDefinition
    {
        [Min(0)] public int Power = 100;
        [Min(1)] public int HitCount = 1;
        public bool UseMagicAttack;
        [Min(0)] public int ShieldDamage;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            var hits = Mathf.Max(1, HitCount);
            for (var i = 0; i < hits; i++)
            {
                var amount = context.Services.CalculateDamage(context, target, this, context.PowerMultiplier);
                amount = context.Services.ApplyDamage(context, target, amount, this);
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.Damage, target, amount, this));
            }

            if (ShieldDamage > 0)
            {
                context.Services.ApplyShieldDamage(context, target, ShieldDamage);
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.ShieldDamage, target, ShieldDamage, this));
            }
        }
    }

    [Serializable]
    public sealed class AbilityHealEffect : AbilityEffectDefinition
    {
        [Min(0)] public int Power = 100;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            var amount = context.Services.CalculateHeal(context, target, this, context.PowerMultiplier);
            amount = context.Services.ApplyHeal(context, target, amount, this);
            results.Add(new AbilityExecutionResult(AbilityExecutionResultType.Heal, target, amount, this));
        }
    }

    [Serializable]
    public sealed class AbilityShieldDamageEffect : AbilityEffectDefinition
    {
        [Min(0)] public int Amount = 1;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            context.Services.ApplyShieldDamage(context, target, Amount);
            results.Add(new AbilityExecutionResult(AbilityExecutionResultType.ShieldDamage, target, Amount, this));
        }
    }

    [Serializable]
    public sealed class AbilityBuffEffect : AbilityEffectDefinition
    {
        public ScriptableObject BuffData;
        [Min(0)] public int Duration = 1;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            context.Services.ApplyBuff(context, target, BuffData, Duration);
            results.Add(new AbilityExecutionResult(AbilityExecutionResultType.BuffApplied, target, Duration, BuffData));
        }
    }

    [Serializable]
    public sealed class AbilityRemoveBuffEffect : AbilityEffectDefinition
    {
        public ScriptableObject BuffData;
        public bool RemoveAllDispellable;

        public override void Execute(AbilityExecutionContext context, object target, List<AbilityExecutionResult> results)
        {
            var removed = context.Services.RemoveBuff(context, target, BuffData, RemoveAllDispellable);
            results.Add(new AbilityExecutionResult(AbilityExecutionResultType.BuffRemoved, target, removed, BuffData));
        }
    }

    public sealed class AbilityExecutionContext
    {
        private readonly List<object> _targets;

        public AbilityExecutionContext(
            AbilityDefinition ability,
            object actor,
            IEnumerable<object> targets,
            IAbilityRuntimeServices services,
            int boostLevel,
            object abilityKey)
        {
            Ability = ability;
            Actor = actor;
            Services = services;
            BoostLevel = Mathf.Max(0, boostLevel);
            AbilityKey = abilityKey ?? ability;
            _targets = new List<object>();
            if (targets != null)
            {
                foreach (var target in targets)
                {
                    if (target != null)
                    {
                        _targets.Add(target);
                    }
                }
            }

            var boostMultiplier = ability != null && ability.CanBoost ? ability.BoostPowerMultiplier : 0f;
            PowerMultiplier = 1f + BoostLevel * boostMultiplier;
        }

        public AbilityDefinition Ability { get; }
        public object Actor { get; }
        public IReadOnlyList<object> Targets => _targets;
        public IAbilityRuntimeServices Services { get; }
        public int BoostLevel { get; }
        public float PowerMultiplier { get; }
        public object AbilityKey { get; }
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

    public enum AbilityExecutionResultType
    {
        ResourceConsumed,
        BoostConsumed,
        Damage,
        Heal,
        ShieldDamage,
        BuffApplied,
        BuffRemoved,
        CooldownStarted,
        ResourceInsufficient,
        BoostInsufficient,
        CooldownBlocked,
        LevelInsufficient,
        NoValidTargets,
        ConditionFailed
    }

    public sealed class AbilityExecutionResult
    {
        public AbilityExecutionResult(AbilityExecutionResultType type, object target = null, int amount = 0, object payload = null)
        {
            Type = type;
            Target = target;
            Amount = amount;
            Payload = payload;
        }

        public AbilityExecutionResultType Type { get; }
        public object Target { get; }
        public int Amount { get; }
        public object Payload { get; }
    }

    public sealed class AbilityExecutionReport
    {
        public AbilityExecutionReport(bool succeeded, List<AbilityExecutionResult> results)
        {
            Succeeded = succeeded;
            Results = results ?? new List<AbilityExecutionResult>();
        }

        public bool Succeeded { get; }
        public List<AbilityExecutionResult> Results { get; }
    }

    public static class AbilityExecutor
    {
        public static AbilityExecutionReport Execute(AbilityExecutionContext context)
        {
            var results = new List<AbilityExecutionResult>();
            if (context?.Ability == null || context.Services == null)
            {
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.ConditionFailed));
                return new AbilityExecutionReport(false, results);
            }

            var ability = context.Ability;
            var targets = GetTargets(context, ability.TargetMode);
            if (ability.TargetMode == AbilityTargetMode.SelectedTargets && targets.Count == 0)
            {
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.NoValidTargets));
                return new AbilityExecutionReport(false, results);
            }

            if (context.Services.GetCooldown(context.Actor, context.AbilityKey) > 0)
            {
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.CooldownBlocked));
                return new AbilityExecutionReport(false, results);
            }

            if (context.Services.GetLevel(context.Actor) < Mathf.Max(1, ability.RequiredLevel))
            {
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.LevelInsufficient));
                return new AbilityExecutionReport(false, results);
            }

            if (!EvaluateConditions(context, results))
            {
                return new AbilityExecutionReport(false, results);
            }

            if (!context.Services.HasResource(context.Actor, ability.ResourceCost))
            {
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.ResourceInsufficient));
                return new AbilityExecutionReport(false, results);
            }

            if (context.BoostLevel > 0 && !context.Services.HasBoost(context.Actor, context.BoostLevel))
            {
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.BoostInsufficient));
                return new AbilityExecutionReport(false, results);
            }

            if (ability.ResourceCost > 0)
            {
                if (!context.Services.ConsumeResource(context.Actor, ability.ResourceCost))
                {
                    results.Add(new AbilityExecutionResult(AbilityExecutionResultType.ResourceInsufficient));
                    return new AbilityExecutionReport(false, results);
                }

                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.ResourceConsumed, context.Actor, ability.ResourceCost));
            }

            if (context.BoostLevel > 0)
            {
                if (!context.Services.ConsumeBoost(context.Actor, context.BoostLevel))
                {
                    results.Add(new AbilityExecutionResult(AbilityExecutionResultType.BoostInsufficient));
                    return new AbilityExecutionReport(false, results);
                }

                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.BoostConsumed, context.Actor, context.BoostLevel));
            }

            foreach (var effect in ability.Effects)
            {
                if (effect == null)
                {
                    continue;
                }

                var effectTargets = GetTargets(context, effect.TargetMode);
                foreach (var target in effectTargets)
                {
                    effect.Execute(context, target, results);
                }
            }

            if (ability.CooldownTurns > 0)
            {
                context.Services.SetCooldown(context.Actor, context.AbilityKey, ability.CooldownTurns);
                results.Add(new AbilityExecutionResult(AbilityExecutionResultType.CooldownStarted, context.Actor, ability.CooldownTurns));
            }

            return new AbilityExecutionReport(true, results);
        }

        private static bool EvaluateConditions(AbilityExecutionContext context, List<AbilityExecutionResult> results)
        {
            foreach (var condition in context.Ability.Conditions)
            {
                if (condition == null)
                {
                    continue;
                }

                var targets = GetTargets(context, condition.TargetMode);
                if (targets.Count == 0)
                {
                    results.Add(new AbilityExecutionResult(AbilityExecutionResultType.NoValidTargets));
                    return false;
                }

                foreach (var target in targets)
                {
                    if (!condition.Evaluate(context, target))
                    {
                        results.Add(new AbilityExecutionResult(AbilityExecutionResultType.ConditionFailed, target, 0, condition));
                        return false;
                    }
                }
            }

            return true;
        }

        private static List<object> GetTargets(AbilityExecutionContext context, AbilityTargetMode targetMode)
        {
            var targets = new List<object>();
            if (targetMode == AbilityTargetMode.Self)
            {
                if (context.Actor != null)
                {
                    targets.Add(context.Actor);
                }

                return targets;
            }

            foreach (var target in context.Targets)
            {
                if (target != null)
                {
                    targets.Add(target);
                }
            }

            return targets;
        }
    }

    public enum AbilityValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class AbilityValidationIssue
    {
        public AbilityValidationIssue(AbilityValidationSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public AbilityValidationSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
    }

    public static class AbilityDefinitionValidator
    {
        public static IReadOnlyList<AbilityValidationIssue> Validate(AbilityDefinition ability)
        {
            var issues = new List<AbilityValidationIssue>();
            if (ability == null)
            {
                issues.Add(new AbilityValidationIssue(AbilityValidationSeverity.Error, "ABILITY_NULL", "Ability definition is null."));
                return issues;
            }

            if (ability.Effects == null || ability.Effects.Count == 0)
            {
                issues.Add(new AbilityValidationIssue(AbilityValidationSeverity.Error, "ABILITY_EFFECTS_EMPTY", "Ability has no effects."));
            }

            if (ability.RequiredLevel < 1)
            {
                issues.Add(new AbilityValidationIssue(AbilityValidationSeverity.Error, "ABILITY_REQUIRED_LEVEL_INVALID", "Required level must be at least 1."));
            }

            return issues;
        }
    }

    public enum AbilityComponentDocCategory
    {
        Trigger,
        Condition,
        Effect,
        Unknown
    }

    public sealed class AbilityComponentDoc
    {
        public AbilityComponentDoc(AbilityComponentDocCategory category, string displayName, bool hasDocumentation)
        {
            Category = category;
            DisplayName = displayName;
            HasDocumentation = hasDocumentation;
        }

        public AbilityComponentDocCategory Category { get; }
        public string DisplayName { get; }
        public bool HasDocumentation { get; }
    }

    public static class AbilityComponentDocUtility
    {
        public static IEnumerable<Type> GetConcreteComponentDefinitionTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract)
                    {
                        continue;
                    }

                    if (typeof(AbilityTriggerDefinition).IsAssignableFrom(type)
                        || typeof(AbilityConditionDefinition).IsAssignableFrom(type)
                        || typeof(AbilityEffectDefinition).IsAssignableFrom(type))
                    {
                        yield return type;
                    }
                }
            }
        }

        public static AbilityComponentDoc GetDoc(Type type)
        {
            var category = GetCategory(type);
            if (type == typeof(ManualAbilityTrigger))
            {
                return new AbilityComponentDoc(category, $"手动触发 {type.Name}", true);
            }

            if (type == typeof(AbilityTargetAliveCondition))
            {
                return new AbilityComponentDoc(category, $"存活条件 {type.Name}", true);
            }

            if (type == typeof(AbilityDamageEffect))
            {
                return new AbilityComponentDoc(category, $"伤害 {type.Name}", true);
            }

            return new AbilityComponentDoc(category, SplitPascalName(type?.Name ?? string.Empty), false);
        }

        private static AbilityComponentDocCategory GetCategory(Type type)
        {
            if (typeof(AbilityTriggerDefinition).IsAssignableFrom(type))
            {
                return AbilityComponentDocCategory.Trigger;
            }

            if (typeof(AbilityConditionDefinition).IsAssignableFrom(type))
            {
                return AbilityComponentDocCategory.Condition;
            }

            return typeof(AbilityEffectDefinition).IsAssignableFrom(type)
                ? AbilityComponentDocCategory.Effect
                : AbilityComponentDocCategory.Unknown;
        }

        private static string SplitPascalName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (i > 0 && char.IsUpper(character) && !char.IsUpper(value[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}
