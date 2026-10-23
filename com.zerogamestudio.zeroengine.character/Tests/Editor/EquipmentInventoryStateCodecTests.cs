using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Equipment;
using ZeroEngine.Inventory;
using ZeroEngine.StatSystem;
using Object = UnityEngine.Object;

namespace ZeroEngine.Tests.Character
{
    /// <summary>Permanent contract for unique equipment payloads and isolated save snapshots.</summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class EquipmentInventoryStateCodecTests
    {
        private EquipmentDataSO _definition;
        private Texture2D _texture;
        private Sprite _icon;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<EquipmentDataSO>();
            _definition.Id = "equipment.synthetic";
            _definition.MaxEnhanceLevel = 3;
            _definition.MaxRefineLevel = 3;
            _definition.GemSlotCount = 2;
            _definition.GemSlotsPerRefine = 1;
            _definition.BaseStats.Add(Modifier(StatType.Attack, 5));
            _texture = new Texture2D(1, 1);
            _icon = Sprite.Create(_texture, new Rect(0, 0, 1, 1), Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_icon);
            Object.DestroyImmediate(_texture);
            Object.DestroyImmediate(_definition);
        }

        [Test]
        public void CaptureAndRestore_EnhancedEnchantedSocketedItem_PreservesIdentityAndGameplayState()
        {
            var original = NewItem();
            var payload = EquipmentInventoryStateCodec.Capture(original);

            Assert.That(payload.InstanceId, Is.EqualTo(original.InstanceId));
            Assert.That(payload.PayloadType, Is.EqualTo("zeroengine.equipment"));
            Assert.That(payload.PayloadVersion, Is.EqualTo(1));
            Assert.That(payload.PayloadJson, Does.Not.Contain("Icon"));
            Assert.That(payload.PayloadJson, Does.Not.Contain("instanceID"));
            Assert.That(EquipmentInventoryStateCodec.TryRestore(_definition, payload, out var restored), Is.True);
            Assert.That(restored, Is.Not.SameAs(original));
            Assert.That(restored.Data, Is.SameAs(_definition));
            Assert.That(restored.InstanceId, Is.EqualTo(original.InstanceId));
            Assert.That(restored.EnhanceLevel, Is.EqualTo(original.EnhanceLevel));
            Assert.That(restored.RefineLevel, Is.EqualTo(original.RefineLevel));
            Assert.That(restored.Enchantments[0].EnchantmentId, Is.EqualTo("enchant.synthetic"));
            Assert.That(restored.Enchantments[0].Tier, Is.EqualTo(2));
            Assert.That(restored.Enchantments[0].Effect.StatType, Is.EqualTo(StatType.Speed));
            Assert.That(restored.Enchantments[0].Effect.BaseValue, Is.EqualTo(7));
            Assert.That(restored.GemSlots[0].State, Is.EqualTo(GemSlotState.Socketed));
            Assert.That(restored.GemSlots[0].SocketedGem.GemId, Is.EqualTo("gem.synthetic"));
            Assert.That(restored.GemSlots[0].SocketedGem.GemName, Is.EqualTo("Synthetic Gem"));
            Assert.That(restored.GemSlots[0].SocketedGem.Effects[0].BaseValue, Is.EqualTo(11));
            Assert.That(restored.GemSlots[0].SocketedGem.Icon, Is.Null);
            Assert.That(restored.GemSlots[1].SlotIndex, Is.EqualTo(1));
            Assert.That(restored.GemSlots[1].State, Is.EqualTo(GemSlotState.Empty));
        }

        [Test]
        public void ExportSaveData_LiveNestedStateChanges_DoNotChangeSnapshot()
        {
            var item = NewItem();
            var snapshot = item.ExportSaveData();

            MutateNestedState(item.Enchantments, item.GemSlots);

            AssertOriginalNestedState(snapshot.Enchantments, snapshot.GemSlots);
            Assert.That(snapshot.GemSlots[0].SocketedGem.Icon, Is.SameAs(_icon));
        }

        [Test]
        public void ImportSaveData_SourceNestedStateChanges_DoNotChangeImportedInstance()
        {
            var source = NewItem().ExportSaveData();
            var item = new EquipmentInstance();
            item.ImportSaveData(source, _definition);

            MutateNestedState(source.Enchantments, source.GemSlots);

            AssertOriginalNestedState(item.Enchantments, item.GemSlots);
            Assert.That(item.GemSlots[0].SocketedGem.Icon, Is.SameAs(_icon));
        }

        [TestCase("data-id")]
        [TestCase("instance-id")]
        [TestCase("negative-enhance")]
        [TestCase("high-enhance")]
        [TestCase("negative-refine")]
        [TestCase("high-refine")]
        [TestCase("null-enchants")]
        [TestCase("null-slots")]
        [TestCase("null-enchant")]
        [TestCase("empty-enchant-id")]
        [TestCase("negative-tier")]
        [TestCase("null-effect")]
        [TestCase("stat-enum")]
        [TestCase("modifier-enum")]
        [TestCase("nan")]
        [TestCase("infinity")]
        [TestCase("duplicate-slot")]
        [TestCase("out-of-range-slot")]
        [TestCase("missing-slot")]
        [TestCase("slot-enum")]
        [TestCase("locked-unlocked-slot")]
        [TestCase("empty-with-gem")]
        [TestCase("socketed-without-gem")]
        [TestCase("empty-gem-id")]
        [TestCase("null-gem-effects")]
        [TestCase("null-gem-effect")]
        public void ImportSaveData_InvalidState_RejectsWithoutChangingExistingInstance(string corruption)
        {
            var item = NewItem();
            var existingId = item.InstanceId;
            var beforeModifiers = item.GetStatModifiers();
            var beforeValue = beforeModifiers[0].Modifier.Value;
            var invalid = item.ExportSaveData();
            Corrupt(invalid, corruption);

            Assert.Throws<ArgumentException>(() => item.ImportSaveData(invalid, _definition));
            Assert.That(item.InstanceId, Is.EqualTo(existingId));
            Assert.That(item.EnhanceLevel, Is.EqualTo(1));
            AssertOriginalNestedState(item.Enchantments, item.GemSlots);
            Assert.That(item.GetStatModifiers()[0].Modifier.Value, Is.EqualTo(beforeValue));
        }

        [TestCase("kind")]
        [TestCase("version")]
        [TestCase("outer-id")]
        [TestCase("data-id")]
        [TestCase("empty-id")]
        [TestCase("enhance")]
        [TestCase("refine")]
        [TestCase("marker")]
        [TestCase("null-enchants")]
        [TestCase("null-slots")]
        [TestCase("nan")]
        [TestCase("infinity")]
        [TestCase("missing-level")]
        [TestCase("broken-json")]
        [TestCase("empty-json")]
        public void TryRestore_InvalidPayload_RejectsWithoutTemplateFallback(string corruption)
        {
            var valid = EquipmentInventoryStateCodec.Capture(NewItem());
            var id = valid.InstanceId;
            var type = valid.PayloadType;
            var version = valid.PayloadVersion;
            var json = valid.PayloadJson;
            switch (corruption)
            {
                case "kind": type = "other.domain"; break;
                case "version": version++; break;
                case "outer-id": id = "different-instance"; break;
                case "data-id": json = json.Replace(_definition.Id, "different-definition"); break;
                case "empty-id": json = json.Replace(valid.InstanceId, ""); break;
                case "enhance": json = ReplaceNumber(json, "EnhanceLevel", "-1"); break;
                case "refine": json = ReplaceNumber(json, "RefineLevel", "999"); break;
                case "marker": json = json.Replace("\"HasState\":true", "\"HasState\":false"); break;
                case "null-enchants": json = json.Replace("\"Enchantments\"", "\"MissingEnchants\""); break;
                case "null-slots": json = json.Replace("\"GemSlots\"", "\"MissingSlots\""); break;
                case "nan": json = ReplaceNumber(json, "BaseValue", "NaN"); break;
                case "infinity": json = ReplaceNumber(json, "BaseValue", "Infinity"); break;
                case "missing-level": json = json.Replace("\"EnhanceLevel\"", "\"MissingEnhance\""); break;
                case "broken-json": json = "{broken}"; break;
                case "empty-json": json = "{}"; break;
                default: Assert.Fail("Unknown corruption fixture."); break;
            }

            var invalid = new InventoryInstanceData(id, type, version, json);
            Assert.That(EquipmentInventoryStateCodec.TryRestore(_definition, invalid, out var item), Is.False);
            Assert.That(item, Is.Null);
        }

        [Test]
        public void Capture_NonFiniteLiveAttribute_RejectsBeforeSerializing()
        {
            var item = NewItem();
            item.GemSlots[0].SocketedGem.Effects[0].ValuePerLevel = float.NaN;
            Assert.Throws<ArgumentException>(() => EquipmentInventoryStateCodec.Capture(item));
        }

        [TestCase("enchantments")]
        [TestCase("gem-slots")]
        [TestCase("gem-effects")]
        [TestCase("all")]
        public void CaptureAndRestore_EmptyRequiredCollections_PreservesExplicitEmptyState(string emptyCollection)
        {
            if (emptyCollection == "gem-slots" || emptyCollection == "all") _definition.GemSlotCount = 0;
            var original = NewItem();
            if (emptyCollection == "enchantments" || emptyCollection == "all") original.Enchantments.Clear();
            if (emptyCollection == "gem-effects") original.GemSlots[0].SocketedGem.Effects.Clear();

            var payload = EquipmentInventoryStateCodec.Capture(original);

            Assert.That(EquipmentInventoryStateCodec.TryRestore(_definition, payload, out var restored), Is.True);
            Assert.That(restored.InstanceId, Is.EqualTo(original.InstanceId));
            var expected = original.ExportSaveData();
            foreach (var slot in expected.GemSlots)
                if (slot.SocketedGem != null) slot.SocketedGem.Icon = null;
            Assert.That(JsonUtility.ToJson(restored.ExportSaveData()),
                Is.EqualTo(JsonUtility.ToJson(expected)));
        }

        [TestCase("enchantments-absent")]
        [TestCase("enchantments-null")]
        [TestCase("gem-slots-absent")]
        [TestCase("gem-slots-null")]
        [TestCase("gem-effects-absent")]
        [TestCase("gem-effects-null")]
        [TestCase("enchantment-record-null")]
        [TestCase("gem-slot-record-null")]
        [TestCase("gem-effect-record-null")]
        [TestCase("enchantment-record-empty")]
        [TestCase("gem-slot-record-empty")]
        [TestCase("gem-effect-record-empty")]
        [TestCase("empty-slot-gem-absent")]
        public void TryRestore_RequiredCollectionOrRecordMissingOrNull_RejectsEvenForEmptyDefinitions(string corruption)
        {
            _definition.GemSlotCount = 0;
            string enchantments = "\"Enchantments\":[]";
            string slots = "\"GemSlots\":[]";
            string effects = "\"Effects\":[]";
            switch (corruption)
            {
                case "enchantments-absent": enchantments = null; break;
                case "enchantments-null": enchantments = "\"Enchantments\":null"; break;
                case "gem-slots-absent": slots = null; break;
                case "gem-slots-null": slots = "\"GemSlots\":null"; break;
                case "enchantment-record-null": enchantments = "\"Enchantments\":[null]"; break;
                case "gem-slot-record-null": slots = "\"GemSlots\":[null]"; _definition.GemSlotCount = 1; break;
                case "enchantment-record-empty": enchantments = "\"Enchantments\":[{}]"; break;
                case "gem-slot-record-empty": slots = "\"GemSlots\":[{}]"; _definition.GemSlotCount = 1; break;
                case "gem-effects-absent": effects = null; break;
                case "gem-effects-null": effects = "\"Effects\":null"; break;
                case "gem-effect-record-null": effects = "\"Effects\":[null]"; break;
                case "gem-effect-record-empty": effects = "\"Effects\":[{}]"; break;
                case "empty-slot-gem-absent":
                    _definition.GemSlotCount = 1;
                    slots = "\"GemSlots\":[{\"HasState\":true,\"SlotIndex\":0,\"State\":1}]";
                    break;
                default: Assert.Fail("Unknown corruption fixture."); break;
            }
            if (corruption.StartsWith("gem-effects-", StringComparison.Ordinal) ||
                corruption.StartsWith("gem-effect-record-", StringComparison.Ordinal))
            {
                _definition.GemSlotCount = 1;
                var gemFields = new List<string>
                {
                    "\"HasState\":true", "\"GemId\":\"gem.synthetic\"", "\"GemName\":null"
                };
                if (effects != null) gemFields.Add(effects);
                slots = "\"GemSlots\":[{\"HasState\":true,\"SlotIndex\":0,\"State\":2,\"Gem\":{" +
                    string.Join(",", gemFields) + "}}]";
            }
            var fields = new List<string>();
            if (enchantments != null) fields.Add(enchantments);
            if (slots != null) fields.Add(slots);
            var payload = MinimalPayload(string.Join(",", fields));

            Assert.That(EquipmentInventoryStateCodec.TryRestore(_definition, payload, out var restored), Is.False);
            Assert.That(restored, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TryRestore_ClrTypeMetadata_RejectsWithoutResolvingRuntimeTypes(bool nested)
        {
            var valid = EquipmentInventoryStateCodec.Capture(NewItem());
            const string metadata = "\"$type\":\"System.Version, System.Private.CoreLib\",";
            var json = nested
                ? valid.PayloadJson.Replace("\"Gem\":{", "\"Gem\":{" + metadata)
                : valid.PayloadJson.Insert(1, metadata);
            Assert.That(json, Is.Not.EqualTo(valid.PayloadJson));
            var payload = new InventoryInstanceData(valid.InstanceId, valid.PayloadType, valid.PayloadVersion, json);

            Assert.That(EquipmentInventoryStateCodec.TryRestore(_definition, payload, out var restored), Is.False);
            Assert.That(restored, Is.Null);
        }

        private static InventoryInstanceData MinimalPayload(string collectionFields)
        {
            const string id = "equipment.instance.synthetic";
            var json = "{\"PayloadType\":\"zeroengine.equipment\",\"PayloadVersion\":1,\"HasState\":true," +
                "\"DataId\":\"equipment.synthetic\",\"InstanceId\":\"" + id + "\",\"EnhanceLevel\":0,\"RefineLevel\":0," +
                collectionFields + "}";
            return new InventoryInstanceData(id, EquipmentInventoryStateCodec.PayloadType,
                EquipmentInventoryStateCodec.PayloadVersion, json);
        }

        private EquipmentInstance NewItem()
        {
            var item = new EquipmentInstance(_definition) { EnhanceLevel = 1, RefineLevel = 1 };
            item.UpdateUnlockedGemSlots();
            item.Enchantments.Add(new EnchantmentData
            {
                EnchantmentId = "enchant.synthetic", Tier = 2, Effect = Modifier(StatType.Speed, 7)
            });
            item.TrySocketGem(0, new GemData
            {
                GemId = "gem.synthetic", GemName = "Synthetic Gem", Icon = _icon,
                Effects = new List<StatModifierData> { Modifier(StatType.MaxHP, 11) }
            });
            return item;
        }

        private static StatModifierData Modifier(StatType type, float value)
        {
            return new StatModifierData { StatType = type, ModType = StatModType.Flat, BaseValue = value, ValuePerLevel = 0.5f };
        }

        private static void MutateNestedState(List<EnchantmentData> enchants, List<GemSlot> slots)
        {
            enchants[0].EnchantmentId = "changed";
            enchants[0].Tier = 0;
            enchants[0].Effect.BaseValue = -100;
            slots[0].State = GemSlotState.Empty;
            slots[0].SocketedGem.GemId = "changed";
            slots[0].SocketedGem.GemName = "changed";
            slots[0].SocketedGem.Effects[0].BaseValue = -100;
            slots[0].SocketedGem.Effects.Clear();
            enchants.Clear();
            slots.Clear();
        }

        private static void AssertOriginalNestedState(List<EnchantmentData> enchants, List<GemSlot> slots)
        {
            Assert.That(enchants.Count, Is.EqualTo(1));
            Assert.That(enchants[0].EnchantmentId, Is.EqualTo("enchant.synthetic"));
            Assert.That(enchants[0].Tier, Is.EqualTo(2));
            Assert.That(enchants[0].Effect.BaseValue, Is.EqualTo(7));
            Assert.That(slots.Count, Is.EqualTo(2));
            Assert.That(slots[0].State, Is.EqualTo(GemSlotState.Socketed));
            Assert.That(slots[0].SocketedGem.GemId, Is.EqualTo("gem.synthetic"));
            Assert.That(slots[0].SocketedGem.GemName, Is.EqualTo("Synthetic Gem"));
            Assert.That(slots[0].SocketedGem.Effects.Count, Is.EqualTo(1));
            Assert.That(slots[0].SocketedGem.Effects[0].BaseValue, Is.EqualTo(11));
        }

        private static string ReplaceNumber(string json, string field, string value)
        {
            var expression = "(\"" + field + "\"\\s*:\\s*)[^,}]+";
            Assert.That(Regex.IsMatch(json, expression), Is.True, "Synthetic payload must contain the field under test.");
            return Regex.Replace(json, expression, match => match.Groups[1].Value + value);
        }

        private static void Corrupt(EquipmentSaveData state, string corruption)
        {
            switch (corruption)
            {
                case "data-id": state.DataId = "different-definition"; break;
                case "instance-id": state.InstanceId = " "; break;
                case "negative-enhance": state.EnhanceLevel = -1; break;
                case "high-enhance": state.EnhanceLevel = 4; break;
                case "negative-refine": state.RefineLevel = -1; break;
                case "high-refine": state.RefineLevel = 4; break;
                case "null-enchants": state.Enchantments = null; break;
                case "null-slots": state.GemSlots = null; break;
                case "null-enchant": state.Enchantments[0] = null; break;
                case "empty-enchant-id": state.Enchantments[0].EnchantmentId = " "; break;
                case "negative-tier": state.Enchantments[0].Tier = -1; break;
                case "null-effect": state.Enchantments[0].Effect = null; break;
                case "stat-enum": state.Enchantments[0].Effect.StatType = (StatType)999; break;
                case "modifier-enum": state.Enchantments[0].Effect.ModType = (StatModType)999; break;
                case "nan": state.Enchantments[0].Effect.BaseValue = float.NaN; break;
                case "infinity": state.GemSlots[0].SocketedGem.Effects[0].ValuePerLevel = float.PositiveInfinity; break;
                case "duplicate-slot": state.GemSlots[1].SlotIndex = 0; break;
                case "out-of-range-slot": state.GemSlots[1].SlotIndex = 2; break;
                case "missing-slot": state.GemSlots.RemoveAt(1); break;
                case "slot-enum": state.GemSlots[1].State = (GemSlotState)999; break;
                case "locked-unlocked-slot": state.GemSlots[1].State = GemSlotState.Locked; break;
                case "empty-with-gem": state.GemSlots[0].State = GemSlotState.Empty; break;
                case "socketed-without-gem": state.GemSlots[0].SocketedGem = null; break;
                case "empty-gem-id": state.GemSlots[0].SocketedGem.GemId = " "; break;
                case "null-gem-effects": state.GemSlots[0].SocketedGem.Effects = null; break;
                case "null-gem-effect": state.GemSlots[0].SocketedGem.Effects[0] = null; break;
                default: Assert.Fail("Unknown corruption fixture."); break;
            }
        }
    }
}
