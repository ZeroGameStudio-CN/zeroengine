using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem.Tests
{
    internal static class InputActionTestAssetFactory
    {
        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();

            var player = new InputActionMap("Player");
            player.AddAction("Interact", InputActionType.Button)
                .AddBinding("<Keyboard>/e", groups: "Keyboard&Mouse");
            player.FindAction("Interact").AddBinding("<Gamepad>/buttonNorth", groups: "Gamepad");
            player.AddAction("Jump", InputActionType.Button)
                .AddBinding("<Keyboard>/space", groups: "Keyboard&Mouse");
            player.FindAction("Jump").AddBinding("<Gamepad>/buttonSouth", groups: "Gamepad");
            player.AddAction("Cancel", InputActionType.Button)
                .AddBinding("<Keyboard>/escape", groups: "Keyboard&Mouse");
            asset.AddActionMap(player);

            var ui = new InputActionMap("UI");
            ui.AddAction("Submit", InputActionType.Button)
                .AddBinding("<Keyboard>/enter", groups: "Keyboard&Mouse");
            ui.AddAction("Cancel", InputActionType.Button)
                .AddBinding("<Keyboard>/escape", groups: "Keyboard&Mouse");
            asset.AddActionMap(ui);

            return asset;
        }
    }
}
