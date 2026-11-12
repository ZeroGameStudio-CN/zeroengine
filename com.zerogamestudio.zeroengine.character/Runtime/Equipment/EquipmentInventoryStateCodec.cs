using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using ZeroEngine.Inventory;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Equipment
{
    /// <summary>Versioned, pure-state inventory payload for a unique equipment instance.</summary>
    public static class EquipmentInventoryStateCodec
    {
        public const string PayloadType = "zeroengine.equipment";
        public const int PayloadVersion = 1;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Error,
            CheckAdditionalContent = true,
            Culture = CultureInfo.InvariantCulture
        };

        /// <summary>
        /// Captures identity and gameplay state. Invalid live state is rejected rather than downgraded to a definition.
        /// Sprite assets are presentation data and are never encoded as Unity instance IDs.
        /// </summary>
        public static InventoryInstanceData Capture(EquipmentInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            var snapshot = instance.ExportSaveData();
            if (!EquipmentInstance.IsValidSaveData(snapshot, instance.Data))
                throw new ArgumentException("Cannot capture invalid equipment state.", nameof(instance));

            var document = new PayloadDocument
            {
                PayloadType = PayloadType,
                PayloadVersion = PayloadVersion,
                HasState = true,
                DataId = snapshot.DataId,
                InstanceId = snapshot.InstanceId,
                EnhanceLevel = snapshot.EnhanceLevel,
                RefineLevel = snapshot.RefineLevel,
                Enchantments = new List<EnchantmentRecord>(snapshot.Enchantments.Count),
                GemSlots = new List<SlotRecord>(snapshot.GemSlots.Count)
            };
            foreach (var enchantment in snapshot.Enchantments)
            {
                document.Enchantments.Add(new EnchantmentRecord
                {
                    EnchantmentId = enchantment.EnchantmentId,
                    Effect = CaptureModifier(enchantment.Effect),
                    Tier = enchantment.Tier
                });
            }
            foreach (var slot in snapshot.GemSlots)
            {
                var gem = slot.SocketedGem;
                GemRecord gemRecord = null;
                if (gem != null)
                {
                    gemRecord = new GemRecord
                    {
                        HasState = true,
                        GemId = gem.GemId,
                        GemName = gem.GemName,
                        Effects = new List<ModifierRecord>(gem.Effects.Count)
                    };
                    foreach (var effect in gem.Effects) gemRecord.Effects.Add(CaptureModifier(effect));
                }
                document.GemSlots.Add(new SlotRecord
                {
                    HasState = true,
                    SlotIndex = slot.SlotIndex,
                    State = slot.State,
                    Gem = gemRecord
                });
            }
            return new InventoryInstanceData(snapshot.InstanceId, PayloadType, PayloadVersion,
                JsonConvert.SerializeObject(document, JsonSettings));
        }

        /// <summary>Restores only exact matching identities and the supported schema; never creates a template fallback.</summary>
        public static bool TryRestore(EquipmentDataSO definition, InventoryInstanceData payload, out EquipmentInstance instance)
        {
            instance = null;
            if (definition == null || payload == null || !payload.IsValid ||
                !string.Equals(payload.PayloadType, PayloadType, StringComparison.Ordinal) ||
                payload.PayloadVersion != PayloadVersion) return false;

            try
            {
                // Required fields are checked by the JSON reader, including the presence of empty arrays.
                // DTOs have no Unity object fields or polymorphic types; CLR metadata is never interpreted.
                var document = JsonConvert.DeserializeObject<PayloadDocument>(payload.PayloadJson, JsonSettings);
                if (document == null || !document.HasState || document.PayloadVersion != PayloadVersion ||
                    !string.Equals(document.PayloadType, PayloadType, StringComparison.Ordinal) ||
                    !string.Equals(document.InstanceId, payload.InstanceId, StringComparison.Ordinal)) return false;

                var state = new EquipmentSaveData
                {
                    DataId = document.DataId,
                    InstanceId = document.InstanceId,
                    EnhanceLevel = document.EnhanceLevel,
                    RefineLevel = document.RefineLevel,
                    Enchantments = new List<EnchantmentData>(document.Enchantments.Count),
                    GemSlots = new List<GemSlot>(document.GemSlots.Count)
                };
                foreach (var enchantment in document.Enchantments)
                {
                    if (enchantment == null) return false;
                    state.Enchantments.Add(new EnchantmentData
                    {
                        EnchantmentId = enchantment.EnchantmentId,
                        Effect = RestoreModifier(enchantment.Effect),
                        Tier = enchantment.Tier
                    });
                }
                foreach (var slot in document.GemSlots)
                {
                    if (slot == null || !slot.HasState || (slot.Gem != null && !slot.Gem.HasState)) return false;
                    GemData gem = null;
                    if (slot.Gem != null)
                    {
                        gem = new GemData
                        {
                            GemId = slot.Gem.GemId,
                            GemName = slot.Gem.GemName,
                            Effects = new List<StatModifierData>(slot.Gem.Effects.Count)
                        };
                        foreach (var effect in slot.Gem.Effects) gem.Effects.Add(RestoreModifier(effect));
                    }
                    state.GemSlots.Add(new GemSlot { SlotIndex = slot.SlotIndex, State = slot.State, SocketedGem = gem });
                }
                if (!EquipmentInstance.IsValidSaveData(state, definition)) return false;

                var restored = new EquipmentInstance();
                restored.ImportSaveData(state, definition);
                instance = restored;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static ModifierRecord CaptureModifier(StatModifierData modifier)
        {
            return new ModifierRecord
            {
                StatType = modifier.StatType,
                ModType = modifier.ModType,
                BaseValue = modifier.BaseValue,
                ValuePerLevel = modifier.ValuePerLevel
            };
        }

        private static StatModifierData RestoreModifier(ModifierRecord modifier)
        {
            return modifier == null ? null : new StatModifierData
            {
                StatType = modifier.StatType,
                ModType = modifier.ModType,
                BaseValue = modifier.BaseValue,
                ValuePerLevel = modifier.ValuePerLevel
            };
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class PayloadDocument
        {
            [JsonProperty(Required = Required.Always)] public string PayloadType;
            [JsonProperty(Required = Required.Always)] public int PayloadVersion;
            [JsonProperty(Required = Required.Always)] public bool HasState;
            [JsonProperty(Required = Required.Always)] public string DataId;
            [JsonProperty(Required = Required.Always)] public string InstanceId;
            [JsonProperty(Required = Required.Always)] public int EnhanceLevel;
            [JsonProperty(Required = Required.Always)] public int RefineLevel;
            [JsonProperty(Required = Required.Always)] public List<EnchantmentRecord> Enchantments;
            [JsonProperty(Required = Required.Always)] public List<SlotRecord> GemSlots;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class EnchantmentRecord
        {
            [JsonProperty(Required = Required.Always)] public string EnchantmentId;
            [JsonProperty(Required = Required.Always)] public ModifierRecord Effect;
            [JsonProperty(Required = Required.Always)] public int Tier;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class SlotRecord
        {
            [JsonProperty(Required = Required.Always)] public bool HasState;
            [JsonProperty(Required = Required.Always)] public int SlotIndex;
            [JsonProperty(Required = Required.Always)] public GemSlotState State;
            // Null is an explicit empty slot. Required.AllowNull rejects a missing field without materializing a gem.
            [JsonProperty(Required = Required.AllowNull)] public GemRecord Gem;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class GemRecord
        {
            [JsonProperty(Required = Required.Always)] public bool HasState;
            [JsonProperty(Required = Required.Always)] public string GemId;
            [JsonProperty(Required = Required.AllowNull)] public string GemName;
            [JsonProperty(Required = Required.Always)] public List<ModifierRecord> Effects;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class ModifierRecord
        {
            [JsonProperty(Required = Required.Always)] public StatType StatType;
            [JsonProperty(Required = Required.Always)] public StatModType ModType;
            [JsonProperty(Required = Required.Always)] public float BaseValue;
            [JsonProperty(Required = Required.Always)] public float ValuePerLevel;
        }
    }
}
