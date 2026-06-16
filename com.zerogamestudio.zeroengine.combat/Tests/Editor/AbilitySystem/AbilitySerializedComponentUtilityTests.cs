using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilitySerializedComponentUtilityTests
    {
        private sealed class AbilityHolder : ScriptableObject
        {
            public AbilityDefinition Ability = new();
        }

        [Test]
        public void ComponentOperations_MutateManagedReferenceList()
        {
            var holder = ScriptableObject.CreateInstance<AbilityHolder>();
            try
            {
                var serializedObject = new SerializedObject(holder);
                var effects = FindEffects(serializedObject);

                AbilitySerializedComponentUtility.AddComponent(
                    serializedObject,
                    holder,
                    effects,
                    typeof(AbilityDamageEffect));

                Assert.That(holder.Ability.Effects, Has.Count.EqualTo(1));
                Assert.That(holder.Ability.Effects[0], Is.TypeOf<AbilityDamageEffect>());

                serializedObject.Update();
                effects = FindEffects(serializedObject);
                var damageElement = effects.GetArrayElementAtIndex(0);
                damageElement.FindPropertyRelative(nameof(AbilityDamageEffect.Power)).intValue = 250;
                damageElement.FindPropertyRelative(nameof(AbilityDamageEffect.HitCount)).intValue = 3;
                damageElement.FindPropertyRelative(nameof(AbilityDamageEffect.ShieldDamage)).intValue = 2;
                serializedObject.ApplyModifiedProperties();

                serializedObject.Update();
                effects = FindEffects(serializedObject);
                AbilitySerializedComponentUtility.DuplicateComponent(serializedObject, holder, effects, 0);

                Assert.That(holder.Ability.Effects, Has.Count.EqualTo(2));
                var duplicatedDamage = (AbilityDamageEffect)holder.Ability.Effects[1];
                Assert.That(duplicatedDamage.Power, Is.EqualTo(250));
                Assert.That(duplicatedDamage.HitCount, Is.EqualTo(3));
                Assert.That(duplicatedDamage.ShieldDamage, Is.EqualTo(2));

                serializedObject.Update();
                effects = FindEffects(serializedObject);
                AbilitySerializedComponentUtility.AddComponent(
                    serializedObject,
                    holder,
                    effects,
                    typeof(AbilityHealEffect));

                Assert.That(holder.Ability.Effects, Has.Count.EqualTo(3));
                Assert.That(holder.Ability.Effects[2], Is.TypeOf<AbilityHealEffect>());

                serializedObject.Update();
                effects = FindEffects(serializedObject);
                AbilitySerializedComponentUtility.MoveComponent(serializedObject, holder, effects, 2, 0);

                Assert.That(holder.Ability.Effects[0], Is.TypeOf<AbilityHealEffect>());
                Assert.That(holder.Ability.Effects[1], Is.TypeOf<AbilityDamageEffect>());

                serializedObject.Update();
                effects = FindEffects(serializedObject);
                AbilitySerializedComponentUtility.RemoveComponent(serializedObject, holder, effects, 0);

                Assert.That(holder.Ability.Effects, Has.Count.EqualTo(2));
                Assert.That(holder.Ability.Effects[0], Is.TypeOf<AbilityDamageEffect>());
                Assert.That(((AbilityDamageEffect)holder.Ability.Effects[0]).Power, Is.EqualTo(250));
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        private static SerializedProperty FindEffects(SerializedObject serializedObject)
        {
            return serializedObject
                .FindProperty(nameof(AbilityHolder.Ability))
                .FindPropertyRelative(nameof(AbilityDefinition.Effects));
        }
    }
}
