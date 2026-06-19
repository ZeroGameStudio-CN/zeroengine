using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Persistence.Editor;
using ZeroEngine.Settings;
using Object = UnityEngine.Object;

namespace ZeroEngine.Persistence.Editor.Tests
{
    public sealed class PersistenceConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenSettingsDefinition()
        {
            var definition = ScriptableObject.CreateInstance<SettingsDefinitionSO>();

            try
            {
                definition.name = "InvalidSettings";
                definition.Settings.Add(new SettingDefinition
                {
                    Key = "volume",
                    DisplayName = string.Empty,
                    ValueType = SettingValueType.Slider,
                    DefaultValue = "bad",
                    MinValue = 10f,
                    MaxValue = 1f,
                    Step = 0f,
                    DependsOnKey = "missing",
                    DependsOnValue = string.Empty
                });
                definition.Settings.Add(new SettingDefinition
                {
                    Key = "volume",
                    DisplayName = "Volume",
                    ValueType = SettingValueType.Enum
                });

                var issues = PersistenceConfigValidator.Validate(new[] { definition });

                AssertError(issues, "Setting must have a display name.");
                AssertError(issues, "Minimum value cannot exceed maximum value.");
                AssertError(issues, "Numeric setting step must be positive.");
                AssertError(issues, "Float or slider setting default value must parse as a number.");
                AssertError(issues, "Duplicate setting key 'volume'.");
                AssertError(issues, "Enum setting must define at least one option.");
                AssertError(issues, "DependsOnKey 'missing' does not exist in this definition.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        private static void AssertError(IReadOnlyList<PersistenceValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == PersistenceValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
