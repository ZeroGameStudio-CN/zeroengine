using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilityExecutorStatConditionTests
    {
        private static readonly StatId MaxHp = "core.max_hp";

        [Test]
        public void Execute_StatCondition_UsesRuntimeStatValue()
        {
            var ability = CreateAbility(AbilityStatComparison.GreaterOrEqual, 100f);
            var services = new TestAbilityRuntimeServices();
            var actor = new object();
            var target = new object();
            services.Resource[actor] = 5;
            services.Stats[(target, MaxHp)] = 100f;

            var result = AbilityExecutor.Execute(new AbilityExecutionContext(
                ability,
                actor,
                new[] { target },
                services,
                abilityKey: ability));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, services.Resource[actor]);
            Assert.AreEqual(7, services.DamageApplied[target]);
        }

        [Test]
        public void Execute_StatConditionMissingStat_BlocksBeforeCosts()
        {
            var ability = CreateAbility(AbilityStatComparison.GreaterOrEqual, 100f);
            var services = new TestAbilityRuntimeServices();
            var actor = new object();
            var target = new object();
            services.Resource[actor] = 5;

            var result = AbilityExecutor.Execute(new AbilityExecutionContext(
                ability,
                actor,
                new[] { target },
                services,
                abilityKey: ability));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(5, services.Resource[actor]);
            Assert.IsFalse(services.DamageApplied.ContainsKey(target));
            Assert.AreEqual(AbilityExecutionResultType.ConditionFailed, result.Results[0].Type);
        }

        private static AbilityDefinition CreateAbility(AbilityStatComparison comparison, float threshold)
        {
            return new AbilityDefinition
            {
                AbilityId = "stat_gate",
                ResourceCost = 3,
                Conditions =
                {
                    new AbilityStatCondition
                    {
                        TargetMode = AbilityTargetMode.SelectedTargets,
                        StatId = MaxHp,
                        Comparison = comparison,
                        Threshold = threshold
                    }
                },
                Effects =
                {
                    new AbilityDamageEffect { Power = 100 }
                }
            };
        }

        private sealed class TestAbilityRuntimeServices : IAbilityRuntimeServices, IAbilityStatRuntimeServices
        {
            public readonly Dictionary<object, int> Resource = new();
            public readonly Dictionary<(object Target, StatId Id), float> Stats = new();
            public readonly Dictionary<object, int> DamageApplied = new();

            public bool HasResource(object actor, int amount)
            {
                return amount <= 0 || Resource.TryGetValue(actor, out var value) && value >= amount;
            }

            public bool ConsumeResource(object actor, int amount)
            {
                if (!HasResource(actor, amount))
                {
                    return false;
                }

                Resource[actor] -= amount;
                return true;
            }

            public bool HasBoost(object actor, int amount) => true;
            public bool ConsumeBoost(object actor, int amount) => true;
            public int GetLevel(object actor) => 1;
            public int GetCooldown(object actor, object abilityKey) => 0;
            public void SetCooldown(object actor, object abilityKey, int turns) { }
            public int CalculateDamage(AbilityExecutionContext context, object target, AbilityDamageEffect effect, float powerMultiplier) => 7;
            public int CalculateHeal(AbilityExecutionContext context, object target, AbilityHealEffect effect, float powerMultiplier) => 0;

            public int ApplyDamage(AbilityExecutionContext context, object target, int amount, AbilityDamageEffect effect)
            {
                DamageApplied[target] = amount;
                return amount;
            }

            public int ApplyHeal(AbilityExecutionContext context, object target, int amount, AbilityHealEffect effect) => 0;
            public void ApplyShieldDamage(AbilityExecutionContext context, object target, int amount) { }
            public void ApplyBuff(AbilityExecutionContext context, object target, ScriptableObject buffData, int duration) { }
            public bool IsTargetAlive(object target) => true;
            public bool AreAllies(object actor, object target) => false;
            public bool HasBuff(AbilityExecutionContext context, object target, ScriptableObject buffData) => false;
            public int RemoveBuff(AbilityExecutionContext context, object target, ScriptableObject buffData, bool removeAllDispellable) => 0;
            public bool RollChance(float chance) => true;
            public bool TryGetStatValue(object target, StatId statId, out float value) => Stats.TryGetValue((target, statId), out value);
        }
    }
}
