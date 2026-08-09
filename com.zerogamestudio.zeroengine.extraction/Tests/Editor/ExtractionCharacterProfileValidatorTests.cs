using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionCharacterProfileValidatorTests
    {
        [Test]
        public void TryInitializeAndValidate_NewProfile_AppliesDeterministicDefaults()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            var config = CreateConfig();

            bool valid = ExtractionCharacterProfileValidator.TryInitializeAndValidate(
                profile.Character,
                profile.Equipment,
                config,
                out string issue);

            Assert.IsTrue(valid, issue);
            Assert.AreEqual(1f, profile.Character.SearchSpeedMultiplier);
            Assert.AreEqual(2, profile.Character.AttributeValues.Count);
            Assert.AreEqual("health-flat", profile.Character.AttributeValues[0].AttributeId);
            Assert.AreEqual(0f, profile.Character.AttributeValues[0].Value);
            CollectionAssert.AreEqual(new[] { "move", "jump", "dash" }, profile.Character.BaselineAbilityIds);
        }

        [Test]
        public void TryInitializeAndValidate_LegacyProfileWithoutCharacterFields_AppliesDefaults()
        {
            var profile = ExtractionProfileSerialization.FromJson("{\"SchemaVersion\":2}");

            bool valid = ExtractionCharacterProfileValidator.TryInitializeAndValidate(
                profile.Character,
                profile.Equipment,
                CreateConfig(),
                out string issue);

            Assert.IsTrue(valid, issue);
            Assert.AreEqual(2, profile.Character.AttributeValues.Count);
            CollectionAssert.AreEqual(new[] { "move", "jump", "dash" }, profile.Character.BaselineAbilityIds);
        }

        [Test]
        public void TryValidateProfile_NegativeGrowth_ReturnsFalse()
        {
            var profile = CreateInitializedProfile(out var config);
            profile.Character.AttributeValues[0].Value = -1f;

            Assert.IsFalse(ExtractionCharacterProfileValidator.TryValidateProfile(
                profile.Character,
                profile.Equipment,
                config,
                out string issue));
            StringAssert.Contains("outside its configured range", issue);
        }

        [TestCase(0.24f)]
        [TestCase(4.01f)]
        public void TryValidateProfile_InvalidSearchSpeedMultiplier_ReturnsFalse(float multiplier)
        {
            var profile = CreateInitializedProfile(out var config);
            profile.Character.SearchSpeedMultiplier = multiplier;

            Assert.IsFalse(ExtractionCharacterProfileValidator.TryValidateProfile(
                profile.Character,
                profile.Equipment,
                config,
                out _));
        }

        [Test]
        public void TryValidateProfile_UnknownAttributeTalentAndSlotIds_ReturnFalse()
        {
            var attributeProfile = CreateInitializedProfile(out var config);
            attributeProfile.Character.AttributeValues.Add(new ExtractionCharacterValue("unknown-stat", 1f));
            Assert.IsFalse(ExtractionCharacterProfileValidator.TryValidateProfile(
                attributeProfile.Character,
                attributeProfile.Equipment,
                config,
                out string attributeIssue));
            StringAssert.Contains("unknown-stat", attributeIssue);

            var talentProfile = CreateInitializedProfile(out config);
            talentProfile.Character.UnlockedTalentIds.Add("unknown-talent");
            Assert.IsFalse(ExtractionCharacterProfileValidator.TryValidateProfile(
                talentProfile.Character,
                talentProfile.Equipment,
                config,
                out string talentIssue));
            StringAssert.Contains("unknown-talent", talentIssue);

            var slotProfile = CreateInitializedProfile(out config);
            slotProfile.Equipment.Slots.Add(new ExtractionEquipmentSlotState("unknown-slot", "item-1"));
            Assert.IsFalse(ExtractionCharacterProfileValidator.TryValidateProfile(
                slotProfile.Character,
                slotProfile.Equipment,
                config,
                out string slotIssue));
            StringAssert.Contains("unknown-slot", slotIssue);
        }

        [Test]
        public void CharacterProfile_KnownFutureExtension_RoundTripsWithoutChangingValues()
        {
            var config = CreateConfig();
            config.AttributeDefinitions.Add(
                new ExtractionCharacterAttributeDefinition("future-luck", 0f, 0f, 10f));
            var profile = ExtractionProfileSaveData.CreateEmpty();
            Assert.IsTrue(ExtractionCharacterProfileValidator.TryInitializeAndValidate(
                profile.Character,
                profile.Equipment,
                config,
                out string issue), issue);
            profile.Character.AttributeValues[2].Value = 7f;

            string json = ExtractionProfileSerialization.ToJson(profile);
            var roundTripped = ExtractionProfileSerialization.FromJson(json);

            Assert.IsTrue(ExtractionCharacterProfileValidator.TryValidateProfile(
                roundTripped.Character,
                roundTripped.Equipment,
                config,
                out issue), issue);
            Assert.AreEqual("future-luck", roundTripped.Character.AttributeValues[2].AttributeId);
            Assert.AreEqual(7f, roundTripped.Character.AttributeValues[2].Value);
        }

        private static ExtractionProfileSaveData CreateInitializedProfile(
            out ExtractionCharacterConfig config)
        {
            config = CreateConfig();
            var profile = ExtractionProfileSaveData.CreateEmpty();
            Assert.IsTrue(ExtractionCharacterProfileValidator.TryInitializeAndValidate(
                profile.Character,
                profile.Equipment,
                config,
                out string issue), issue);
            return profile;
        }

        private static ExtractionCharacterConfig CreateConfig()
        {
            var config = new ExtractionCharacterConfig
            {
                DefaultSearchSpeedMultiplier = 1f
            };
            config.AttributeDefinitions.Add(
                new ExtractionCharacterAttributeDefinition("health-flat", 0f, 0f, 1000f));
            config.AttributeDefinitions.Add(
                new ExtractionCharacterAttributeDefinition("movement-percent", 0f, 0f, 1f));
            config.TalentDefinitions.Add(new ExtractionCharacterTalentDefinition("double-jump"));
            config.InjuryDefinitions.Add(new ExtractionCharacterInjuryDefinition("wounded", 3));
            config.EquipmentSlotIds.Add("weapon-primary");
            config.BaselineAbilityIds.AddRange(new[] { "move", "jump", "dash" });
            return config;
        }
    }
}
