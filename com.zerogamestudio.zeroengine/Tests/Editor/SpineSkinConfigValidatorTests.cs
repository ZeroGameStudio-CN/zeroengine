using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Editor;
using ZeroEngine.SpineSkin;
using Object = UnityEngine.Object;

namespace ZeroEngine.Tests
{
    public sealed class SpineSkinConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenSpineSkinConfig()
        {
            var config = ScriptableObject.CreateInstance<SpineSkinConfig>();

            try
            {
                config.SkinNamePattern = "{gender}/{name}";
                config.GenderNames.Clear();
                config.DefaultGenderIndex = 2;
                config.AnimationDuration = -1f;
                config.ButtonAppearDelay = -1f;
                config.SkinSlots.Add(new SkinSlotConfig { SlotId = "body", DisplayName = string.Empty, IsRequired = true });
                config.SkinSlots.Add(new SkinSlotConfig { SlotId = "body", DisplayName = "Body" });

                var issues = SpineSkinConfigValidator.Validate(new[] { config });

                AssertError(issues, "Skin name pattern must include the {slot} token.");
                AssertError(issues, "Spine skin config must define at least one gender name.");
                AssertError(issues, "Animation duration cannot be negative.");
                AssertError(issues, "Button appear delay cannot be negative.");
                AssertError(issues, "Skin slot must have a display name.");
                AssertError(issues, "Required skin slots must define a default skin.");
                AssertError(issues, "Duplicate skin slot ID 'body'.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private static void AssertError(IReadOnlyList<SpineSkinValidationIssue> issues, string expectedMessage)
        {
            Assert.That(issues.Any(issue => issue.Severity == SpineSkinValidationSeverity.Error && issue.Message == expectedMessage), Is.True);
        }
    }
}
