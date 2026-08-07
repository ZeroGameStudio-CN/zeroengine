using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionCharacterProfileValidator
    {
        public const float MinSearchSpeedMultiplier = 0.25f;
        public const float MaxSearchSpeedMultiplier = 4f;

        public static bool TryValidateConfig(ExtractionCharacterConfig config, out string issue)
        {
            issue = null;
            if (config == null) return Fail("Character config is null.", out issue);
            if (!IsFiniteInRange(
                    config.DefaultSearchSpeedMultiplier,
                    MinSearchSpeedMultiplier,
                    MaxSearchSpeedMultiplier))
            {
                return Fail("Default search speed multiplier must be between 0.25 and 4.", out issue);
            }

            if (config.AttributeDefinitions == null)
                return Fail("Character attribute definitions are null.", out issue);
            if (config.TalentDefinitions == null)
                return Fail("Character talent definitions are null.", out issue);
            if (config.InjuryDefinitions == null)
                return Fail("Character injury definitions are null.", out issue);
            if (config.EquipmentSlotIds == null)
                return Fail("Character equipment slot ids are null.", out issue);
            if (config.BaselineAbilityIds == null)
                return Fail("Character baseline ability ids are null.", out issue);

            var attributeIds = new HashSet<string>();
            foreach (var definition in config.AttributeDefinitions)
            {
                if (definition == null || IsBlank(definition.AttributeId))
                    return Fail("Character attribute id is empty.", out issue);
                if (!attributeIds.Add(definition.AttributeId))
                    return Fail($"Duplicate character attribute id '{definition.AttributeId}'.", out issue);
                if (!IsFinite(definition.MinValue)
                    || !IsFinite(definition.DefaultValue)
                    || !IsFinite(definition.MaxValue)
                    || definition.MinValue < 0f
                    || definition.MaxValue < definition.MinValue
                    || definition.DefaultValue < definition.MinValue
                    || definition.DefaultValue > definition.MaxValue)
                {
                    return Fail($"Character attribute '{definition.AttributeId}' has invalid bounds.", out issue);
                }
            }

            var talentIds = new HashSet<string>();
            foreach (var definition in config.TalentDefinitions)
            {
                if (definition == null || IsBlank(definition.TalentId))
                    return Fail("Character talent id is empty.", out issue);
                if (!talentIds.Add(definition.TalentId))
                    return Fail($"Duplicate character talent id '{definition.TalentId}'.", out issue);
            }

            var injuryIds = new HashSet<string>();
            foreach (var definition in config.InjuryDefinitions)
            {
                if (definition == null || IsBlank(definition.InjuryId) || definition.MaxSeverity <= 0)
                    return Fail("Character injury definition is invalid.", out issue);
                if (!injuryIds.Add(definition.InjuryId))
                    return Fail($"Duplicate character injury id '{definition.InjuryId}'.", out issue);
            }

            if (!TryCollectUniqueIds(config.EquipmentSlotIds, "equipment slot", out issue))
                return false;
            if (!TryCollectUniqueIds(config.BaselineAbilityIds, "baseline ability", out issue))
                return false;

            return true;
        }

        public static bool TryInitializeAndValidate(
            ExtractionCharacterProfile profile,
            ExtractionEquipmentState equipment,
            ExtractionCharacterConfig config,
            out string issue)
        {
            issue = null;
            if (!TryValidateConfig(config, out issue)) return false;
            if (profile == null) return Fail("Character profile is null.", out issue);
            if (equipment == null) return Fail("Character equipment state is null.", out issue);

            profile.EnsureInitialized();
            equipment.EnsureInitialized();

            foreach (var definition in config.AttributeDefinitions)
            {
                if (!ContainsAttribute(profile.AttributeValues, definition.AttributeId))
                {
                    profile.AttributeValues.Add(
                        new ExtractionCharacterValue(definition.AttributeId, definition.DefaultValue));
                }
            }

            if (profile.BaselineAbilityIds.Count == 0)
                profile.BaselineAbilityIds.AddRange(config.BaselineAbilityIds);

            return TryValidateProfile(profile, equipment, config, out issue);
        }

        public static bool TryValidateProfile(
            ExtractionCharacterProfile profile,
            ExtractionEquipmentState equipment,
            ExtractionCharacterConfig config,
            out string issue)
        {
            issue = null;
            if (!TryValidateConfig(config, out issue)) return false;
            if (profile == null) return Fail("Character profile is null.", out issue);
            if (equipment == null) return Fail("Character equipment state is null.", out issue);

            profile.EnsureInitialized();
            equipment.EnsureInitialized();
            if (!IsFiniteInRange(
                    profile.SearchSpeedMultiplier,
                    MinSearchSpeedMultiplier,
                    MaxSearchSpeedMultiplier))
            {
                return Fail("Character search speed multiplier must be between 0.25 and 4.", out issue);
            }

            var attributeIds = new HashSet<string>();
            foreach (var value in profile.AttributeValues)
            {
                if (value == null || IsBlank(value.AttributeId))
                    return Fail("Character attribute value is invalid.", out issue);
                if (!attributeIds.Add(value.AttributeId))
                    return Fail($"Duplicate character attribute value '{value.AttributeId}'.", out issue);
                if (!config.TryGetAttributeDefinition(value.AttributeId, out var definition))
                    return Fail($"Unknown character attribute id '{value.AttributeId}'.", out issue);
                if (!IsFinite(value.Value) || value.Value < definition.MinValue || value.Value > definition.MaxValue)
                    return Fail($"Character attribute '{value.AttributeId}' is outside its configured range.", out issue);
            }

            foreach (var definition in config.AttributeDefinitions)
            {
                if (!attributeIds.Contains(definition.AttributeId))
                    return Fail($"Character attribute '{definition.AttributeId}' is missing.", out issue);
            }

            var talentIds = new HashSet<string>();
            foreach (string talentId in profile.UnlockedTalentIds)
            {
                if (IsBlank(talentId) || !talentIds.Add(talentId))
                    return Fail($"Character talent id '{talentId}' is invalid or duplicated.", out issue);
                if (!config.TryGetTalentDefinition(talentId, out _))
                    return Fail($"Unknown character talent id '{talentId}'.", out issue);
            }

            var injuryIds = new HashSet<string>();
            foreach (var injury in profile.Injuries)
            {
                if (injury == null || IsBlank(injury.InjuryId) || !injuryIds.Add(injury.InjuryId))
                    return Fail("Character injury state is invalid or duplicated.", out issue);
                if (!config.TryGetInjuryDefinition(injury.InjuryId, out var definition))
                    return Fail($"Unknown character injury id '{injury.InjuryId}'.", out issue);
                if (injury.Severity <= 0 || injury.Severity > definition.MaxSeverity)
                    return Fail($"Character injury '{injury.InjuryId}' has invalid severity.", out issue);
            }

            var baselineAbilityIds = new HashSet<string>();
            foreach (string abilityId in profile.BaselineAbilityIds)
            {
                if (IsBlank(abilityId) || !baselineAbilityIds.Add(abilityId))
                    return Fail($"Character baseline ability id '{abilityId}' is invalid or duplicated.", out issue);
                if (!config.BaselineAbilityIds.Contains(abilityId))
                    return Fail($"Unknown character baseline ability id '{abilityId}'.", out issue);
            }

            if (baselineAbilityIds.Count != config.BaselineAbilityIds.Count)
                return Fail("Character baseline abilities do not match the configured baseline.", out issue);

            var slotIds = new HashSet<string>();
            foreach (var slot in equipment.Slots)
            {
                if (slot == null || IsBlank(slot.SlotId) || !slotIds.Add(slot.SlotId))
                    return Fail("Character equipment slot is invalid or duplicated.", out issue);
                if (!config.EquipmentSlotIds.Contains(slot.SlotId))
                    return Fail($"Unknown character equipment slot id '{slot.SlotId}'.", out issue);
            }

            return true;
        }

        private static bool ContainsAttribute(
            List<ExtractionCharacterValue> values,
            string attributeId)
        {
            foreach (var value in values)
            {
                if (value != null && value.AttributeId == attributeId)
                    return true;
            }

            return false;
        }

        private static bool TryCollectUniqueIds(
            List<string> ids,
            string label,
            out string issue)
        {
            issue = null;
            var unique = new HashSet<string>();
            foreach (string id in ids)
            {
                if (IsBlank(id)) return Fail($"Character {label} id is empty.", out issue);
                if (!unique.Add(id)) return Fail($"Duplicate character {label} id '{id}'.", out issue);
            }

            return true;
        }

        private static bool IsFiniteInRange(float value, float min, float max)
        {
            return IsFinite(value) && value >= min && value <= max;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }

        private static bool Fail(string message, out string issue)
        {
            issue = message;
            return false;
        }
    }
}
