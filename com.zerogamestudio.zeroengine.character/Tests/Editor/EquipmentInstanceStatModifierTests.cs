using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Equipment;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Tests.Character
{
    [TestFixture]
    [Category("Unit")]
    public sealed class EquipmentInstanceStatModifierTests
    {
        private EquipmentDataSO _data;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<EquipmentDataSO>();
            _data.BaseStats.Add(new StatModifierData { StatType = StatType.Attack, BaseValue = 10f, ValuePerLevel = 2f });
            _data.BaseStats.Add(new StatModifierData { StatType = StatType.Defense, BaseValue = 3f });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_data);
        }

        [Test]
        public void GetStatModifiers_BaseEnchantAndGem_KeepTheirOwnTargetStatAndScaling()
        {
            var item = new EquipmentInstance(_data) { EnhanceLevel = 2 };
            item.Enchantments.Add(new EnchantmentData
            {
                Effect = new StatModifierData { StatType = StatType.Speed, BaseValue = 5f, ValuePerLevel = 100f }
            });
            item.GemSlots.Add(new GemSlot
            {
                State = GemSlotState.Socketed,
                SocketedGem = new GemData
                {
                    Effects = { new StatModifierData { StatType = StatType.MaxHP, BaseValue = 20f } }
                }
            });

            var modifiers = item.GetStatModifiers();

            Assert.That(modifiers.Count, Is.EqualTo(4));
            Assert.That(modifiers[0].StatId, Is.EqualTo(new StatId("attack")));
            Assert.That(modifiers[0].Modifier.Value, Is.EqualTo(14f));
            Assert.That(modifiers[1].StatId, Is.EqualTo(new StatId("defense")));
            Assert.That(modifiers[1].Modifier.Value, Is.EqualTo(3f));
            Assert.That(modifiers[2].StatId, Is.EqualTo(new StatId("speed")));
            Assert.That(modifiers[2].Modifier.Value, Is.EqualTo(5f));
            Assert.That(modifiers[3].StatId, Is.EqualTo(new StatId("maxhp")));
            Assert.That(modifiers[3].Modifier.Value, Is.EqualTo(20f));
            for (var i = 0; i < modifiers.Count; i++)
            {
                Assert.That(modifiers[i].Modifier.Source, Is.SameAs(item));
                Assert.That(item.GetCalculatedModifiers()[i], Is.SameAs(modifiers[i].Modifier));
            }
        }

        [Test]
        public void GetStatModifiers_EnhancementInvalidatesCache_RecalculatesWithoutAppending()
        {
            var item = new EquipmentInstance(_data);
            Assert.That(item.GetStatModifiers()[0].Modifier.Value, Is.EqualTo(10f));
            item.EnhanceLevel = 3;
            item.MarkModifiersDirty();

            Assert.That(item.GetStatModifiers().Count, Is.EqualTo(2));
            Assert.That(item.GetStatModifiers()[0].Modifier.Value, Is.EqualTo(16f));
            Assert.That(item.GetCalculatedModifiers().Count, Is.EqualTo(2));
        }
    }
}
