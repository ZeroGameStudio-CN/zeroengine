using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputActionCatalogValidatorTests
    {
        [Test]
        public void Validate_CompleteCatalog_ReturnsSuccess()
        {
            var asset = InputActionTestAssetFactory.Create();
            var entries = new[]
            {
                new InputActionCatalogEntry(
                    "interact",
                    new InputActionKey("Player", "Interact"),
                    new[] { "Keyboard&Mouse", "Gamepad" },
                    "Gameplay",
                    required: true,
                    configurable: true,
                    displayNameKey: "input.interact",
                    categoryKey: "input.category.gameplay",
                    sortOrder: 0)
            };

            var result = InputActionCatalogValidator.Validate(asset, entries);

            Assert.IsTrue(result.Success, string.Join(", ", result.Issues.Select(issue => issue.Diagnostic)));
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void Validate_DuplicateActionId_ReportsIssue()
        {
            var asset = InputActionTestAssetFactory.Create();
            var entries = new[]
            {
                CreateEntry("interact", "Interact", "Keyboard&Mouse"),
                CreateEntry("interact", "Jump", "Keyboard&Mouse")
            };

            var result = InputActionCatalogValidator.Validate(asset, entries);
            var duplicate = result.Issues.Single(issue =>
                issue.IssueType == InputActionCatalogValidationIssueType.DuplicateActionId);

            Assert.IsFalse(result.Success);
            Assert.That(duplicate.ActionId, Is.EqualTo("interact"));
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void Validate_MissingRequiredBindingGroup_ReportsIssue()
        {
            var asset = InputActionTestAssetFactory.Create();
            var entries = new[]
            {
                CreateEntry("cancel", "Cancel", "Gamepad")
            };

            var result = InputActionCatalogValidator.Validate(asset, entries);
            var missing = result.Issues.Single(issue =>
                issue.IssueType == InputActionCatalogValidationIssueType.MissingBindingGroup);

            Assert.IsFalse(result.Success);
            Assert.That(missing.ActionId, Is.EqualTo("cancel"));
            Assert.That(missing.BindingGroup, Is.EqualTo("Gamepad"));
            Object.DestroyImmediate(asset);
        }

        private static InputActionCatalogEntry CreateEntry(
            string actionId,
            string actionName,
            params string[] bindingGroups)
        {
            return new InputActionCatalogEntry(
                actionId,
                new InputActionKey("Player", actionName),
                bindingGroups,
                "Gameplay",
                required: true,
                configurable: true,
                displayNameKey: $"input.{actionId}",
                categoryKey: "input.category.gameplay",
                sortOrder: 0);
        }
    }
}
