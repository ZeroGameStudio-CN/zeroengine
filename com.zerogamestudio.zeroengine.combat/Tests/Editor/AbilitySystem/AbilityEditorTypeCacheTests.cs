using System;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilityEditorTypeCacheTests
    {
        [Test]
        public void GetComponentTypes_ReturnsPublicConcreteRuntimeComponentsOnly()
        {
            var effects = AbilityComponentTypeCache.GetComponentTypes(typeof(AbilityEffectDefinition)).ToArray();

            CollectionAssert.Contains(effects, typeof(AbilityDamageEffect));
            CollectionAssert.Contains(effects, typeof(AbilityRemoveBuffEffect));
            CollectionAssert.DoesNotContain(effects, typeof(ObsoleteTestEffect));
            CollectionAssert.DoesNotContain(effects, typeof(PrivateTestEffect));
        }

        [Test]
        public void StateKey_IncludesTargetIdAndPropertyPath()
        {
            var first = AbilityEditorState.Get(1, "_ability.Effects");
            var second = AbilityEditorState.Get(2, "_ability.Effects");
            var third = AbilityEditorState.Get(1, "_otherAbility.Effects");

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first, third);
        }

        [Obsolete]
        public sealed class ObsoleteTestEffect : AbilityEffectDefinition
        {
            public override void Execute(
                AbilityExecutionContext context,
                object target,
                System.Collections.Generic.List<AbilityExecutionResult> results)
            {
            }
        }

        private sealed class PrivateTestEffect : AbilityEffectDefinition
        {
            public override void Execute(
                AbilityExecutionContext context,
                object target,
                System.Collections.Generic.List<AbilityExecutionResult> results)
            {
            }
        }
    }
}
