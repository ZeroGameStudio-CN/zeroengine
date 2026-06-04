using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputSettingsModelBuilderTests
    {
        [Test]
        public void Build_ValidCatalog_CreatesOneRowPerBindingGroup()
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
                    sortOrder: 10)
            };

            var model = InputSettingsModelBuilder.Build(asset, entries);
            var keyboard = model.Rows.Single(row => row.BindingGroup == "Keyboard&Mouse");
            var gamepad = model.Rows.Single(row => row.BindingGroup == "Gamepad");

            Assert.That(model.Rows.Count, Is.EqualTo(2));
            Assert.That(keyboard.ActionId, Is.EqualTo("interact"));
            Assert.That(keyboard.DisplayNameKey, Is.EqualTo("input.interact"));
            Assert.That(keyboard.CategoryKey, Is.EqualTo("input.category.gameplay"));
            Assert.That(keyboard.ConflictScope, Is.EqualTo("Gameplay"));
            Assert.That(keyboard.SortOrder, Is.EqualTo(10));
            Assert.That(keyboard.DisplayName, Is.Not.Empty);
            Assert.That(keyboard.EffectivePath, Is.EqualTo("<Keyboard>/e"));
            Assert.That(gamepad.EffectivePath, Is.EqualTo("<Gamepad>/buttonNorth"));
            Object.DestroyImmediate(asset);
        }
    }
}
