using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ZeroEngine.Tutorial
{
    internal static class TutorialInputHelper
    {
        internal static bool GetKeyDown(KeyCode code) => Control(code)?.wasPressedThisFrame == true;
        internal static bool GetKey(KeyCode code) => Control(code)?.isPressed == true;
        private static ButtonControl Control(KeyCode code)
        {
            if ((int)code >= (int)KeyCode.Mouse0 && (int)code <= (int)KeyCode.Mouse4)
            {
                var mouse = Mouse.current;
                if (mouse == null) return null;
                switch ((int)code - (int)KeyCode.Mouse0)
                {
                    case 0: return mouse.leftButton;
                    case 1: return mouse.rightButton;
                    case 2: return mouse.middleButton;
                    case 3: return mouse.forwardButton;
                    case 4: return mouse.backButton;
                }
            }
            if ((int)code >= (int)KeyCode.JoystickButton0 && (int)code <= (int)KeyCode.JoystickButton9)
            {
                var gamepad = Gamepad.current;
                if (gamepad == null) return null;
                switch ((int)code - (int)KeyCode.JoystickButton0)
                {
                    case 0: return gamepad.buttonSouth;
                    case 1: return gamepad.buttonEast;
                    case 2: return gamepad.buttonWest;
                    case 3: return gamepad.buttonNorth;
                    case 4: return gamepad.leftShoulder;
                    case 5: return gamepad.rightShoulder;
                    case 6: return gamepad.selectButton;
                    case 7: return gamepad.startButton;
                    case 8: return gamepad.leftStickButton;
                    case 9: return gamepad.rightStickButton;
                }
            }
            if (Keyboard.current == null) return null;
            string name = code.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal)) name = "Digit" + name.Substring(5);
            else if (name.StartsWith("Keypad", StringComparison.Ordinal)) name = "Numpad" + name.Substring(6);
            switch (code)
            {
                case KeyCode.Return: name = "Enter"; break;
                case KeyCode.LeftControl: name = "LeftCtrl"; break;
                case KeyCode.RightControl: name = "RightCtrl"; break;
                case KeyCode.Numlock: name = "NumLock"; break;
            }
            return Enum.TryParse<Key>(name, true, out var key) && key != Key.None ? Keyboard.current[key] : null;
        }
    }
}
