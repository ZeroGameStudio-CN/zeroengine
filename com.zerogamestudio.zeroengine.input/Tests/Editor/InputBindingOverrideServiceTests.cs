using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputBindingOverrideServiceTests
    {
        [Test]
        public void ApplyOverride_BindingGroup_ChangesOnlyMatchingGroup()
        {
            var asset = InputActionTestAssetFactory.Create();
            var keyboardKey = new InputBindingKey("Player", "Interact", "Keyboard&Mouse");
            var gamepadKey = new InputBindingKey("Player", "Interact", "Gamepad");

            var result = InputBindingOverrideService.ApplyOverride(asset, keyboardKey, "<Keyboard>/f");
            var keyboardDisplay = InputBindingDisplayService.GetDisplayName(asset, keyboardKey);
            var gamepadDisplay = InputBindingDisplayService.GetDisplayName(asset, gamepadKey);

            Assert.IsTrue(result.Success, result.Diagnostic);
            Assert.That(keyboardDisplay.DisplayName, Does.Contain("F").IgnoreCase);
            Assert.That(gamepadDisplay.DisplayName, Is.Not.Empty);
            Assert.That(gamepadDisplay.EffectivePath, Does.Contain("buttonNorth"));
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void SaveAndLoadOverrides_RestoresOverrideOnFreshAsset()
        {
            var asset = InputActionTestAssetFactory.Create();
            var key = new InputBindingKey("Player", "Interact", "Keyboard&Mouse");
            InputBindingOverrideService.ApplyOverride(asset, key, "<Keyboard>/f");

            var json = InputBindingOverrideService.SaveOverridesAsJson(asset);
            var fresh = InputActionTestAssetFactory.Create();
            InputBindingOverrideService.LoadOverridesFromJson(fresh, json);
            var display = InputBindingDisplayService.GetDisplayName(fresh, key);

            Assert.That(json, Is.Not.Empty);
            Assert.That(display.EffectivePath, Is.EqualTo("<Keyboard>/f"));
            Object.DestroyImmediate(asset);
            Object.DestroyImmediate(fresh);
        }

        [Test]
        public void ResetBinding_RemovesOverrideAndKeepsDefaultPath()
        {
            var asset = InputActionTestAssetFactory.Create();
            var key = new InputBindingKey("Player", "Interact", "Keyboard&Mouse");
            InputBindingOverrideService.ApplyOverride(asset, key, "<Keyboard>/f");

            var reset = InputBindingOverrideService.ResetBinding(asset, key);
            var display = InputBindingDisplayService.GetDisplayName(asset, key);

            Assert.IsTrue(reset.Success, reset.Diagnostic);
            Assert.That(display.EffectivePath, Is.EqualTo("<Keyboard>/e"));
            Object.DestroyImmediate(asset);
        }
    }
}
