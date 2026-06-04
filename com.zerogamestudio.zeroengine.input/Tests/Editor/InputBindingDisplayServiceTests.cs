using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputBindingDisplayServiceTests
    {
        [Test]
        public void GetDisplayName_KeyboardBinding_ReturnsReadableName()
        {
            var asset = InputActionTestAssetFactory.Create();

            var display = InputBindingDisplayService.GetDisplayName(
                asset,
                new InputBindingKey("Player", "Interact", "Keyboard&Mouse"));

            Assert.IsTrue(display.Success, display.Diagnostic);
            Assert.That(display.DisplayName, Is.Not.Empty);
            Assert.That(display.EffectivePath, Is.EqualTo("<Keyboard>/e"));
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void GetDisplayName_GamepadBinding_ReturnsReadableName()
        {
            var asset = InputActionTestAssetFactory.Create();

            var display = InputBindingDisplayService.GetDisplayName(
                asset,
                new InputBindingKey("Player", "Interact", "Gamepad"));

            Assert.IsTrue(display.Success, display.Diagnostic);
            Assert.That(display.DisplayName, Is.Not.Empty);
            Assert.That(display.EffectivePath, Is.EqualTo("<Gamepad>/buttonNorth"));
            Object.DestroyImmediate(asset);
        }
    }
}
