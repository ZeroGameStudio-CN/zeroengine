using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using ZeroEngine.InputSystem;

namespace ZeroEngine.Input.Editor.Tests
{
    public sealed class InputManagerTests
    {
        [Test]
        public void ModeSwitchesEnableExpectedActionMaps()
        {
            var gameObject = new GameObject("InputManagerTest");
            try
            {
                var manager = gameObject.AddComponent<InputManager>();
                var player = new InputActionMap("Player");
                player.AddAction("Move", binding: "<Keyboard>/w");
                var ui = new InputActionMap("UI");
                ui.AddAction("Submit", binding: "<Keyboard>/enter");
                SetActionMaps(manager, player, ui);

                manager.SwitchToGameplayMode();

                Assert.IsTrue(player.enabled);
                Assert.IsFalse(ui.enabled);

                manager.SwitchToUIMode();

                Assert.IsFalse(player.enabled);
                Assert.IsTrue(ui.enabled);

                manager.DisableAllActions();

                Assert.IsFalse(player.enabled);
                Assert.IsFalse(ui.enabled);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static void SetActionMaps(InputManager manager, InputActionMap player, InputActionMap ui)
        {
            SetPrivateProperty(manager, nameof(InputManager.PlayerActions), player);
            SetPrivateProperty(manager, nameof(InputManager.UIActions), ui);
        }

        private static void SetPrivateProperty<TValue>(InputManager manager, string propertyName, TValue value)
        {
            var property = typeof(InputManager).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            property.GetSetMethod(nonPublic: true).Invoke(manager, new object[] { value });
        }
    }
}
