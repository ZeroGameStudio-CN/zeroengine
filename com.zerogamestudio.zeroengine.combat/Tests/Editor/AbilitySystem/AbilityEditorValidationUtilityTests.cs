using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilityEditorValidationUtilityTests
    {
        [Test]
        public void Validate_EmptyAbility_ReturnsMissingIdAndMissingEffect()
        {
            var ability = new AbilityDefinition();

            var issues = AbilityEditorValidationUtility.Validate(ability).ToArray();

            Assert.IsTrue(issues.Any(issue => issue.Code == "ABILITY_ID_EMPTY"));
            Assert.IsTrue(issues.Any(issue => issue.Code == "ABILITY_EFFECTS_EMPTY"));
        }

        [Test]
        public void Validate_NullEffect_ReturnsNullComponentIssue()
        {
            var ability = new AbilityDefinition { AbilityId = "test" };
            ability.Effects.Add(null);

            var issues = AbilityEditorValidationUtility.Validate(ability).ToArray();

            Assert.IsTrue(issues.Any(issue => issue.Code == "ABILITY_COMPONENT_NULL"));
        }

        [Test]
        public void Validate_RemoveBuffWithoutMode_ReturnsConfigurationIssue()
        {
            var ability = new AbilityDefinition { AbilityId = "test" };
            ability.Effects.Add(new AbilityRemoveBuffEffect());

            var issues = AbilityEditorValidationUtility.Validate(ability).ToArray();

            Assert.IsTrue(issues.Any(issue => issue.Code == "REMOVE_BUFF_EMPTY"));
        }
    }
}
