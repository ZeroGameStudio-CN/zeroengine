using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputBindingConflictValidatorTests
    {
        [Test]
        public void Validate_SameScopeDuplicateBinding_ReportsBlockingConflict()
        {
            var asset = InputActionTestAssetFactory.Create();
            InputBindingOverrideService.ApplyOverride(
                asset,
                new InputBindingKey("Player", "Jump", "Keyboard&Mouse"),
                "<Keyboard>/e");

            var conflicts = InputBindingConflictValidator.Validate(
                asset,
                new[]
                {
                    new InputBindingConflictDescriptor(
                        new InputBindingKey("Player", "Interact", "Keyboard&Mouse"),
                        "gameplay"),
                    new InputBindingConflictDescriptor(
                        new InputBindingKey("Player", "Jump", "Keyboard&Mouse"),
                        "gameplay")
                });

            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual(InputBindingConflictSeverity.Blocking, conflicts[0].Severity);
            Assert.That(conflicts[0].EffectivePath, Is.EqualTo("<Keyboard>/e"));
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void Validate_DifferentScopeDuplicateBinding_DoesNotReportBlockingConflict()
        {
            var asset = InputActionTestAssetFactory.Create();

            var conflicts = InputBindingConflictValidator.Validate(
                asset,
                new[]
                {
                    new InputBindingConflictDescriptor(
                        new InputBindingKey("Player", "Cancel", "Keyboard&Mouse"),
                        "gameplay"),
                    new InputBindingConflictDescriptor(
                        new InputBindingKey("UI", "Cancel", "Keyboard&Mouse"),
                        "ui")
                });

            Assert.False(conflicts.Any(conflict => conflict.Severity == InputBindingConflictSeverity.Blocking));
            Object.DestroyImmediate(asset);
        }
    }
}
