using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ZeroEngine.Console.UI
{
    internal static class InputSystemConsoleBackend
    {
        private static Keyboard activationKeyboard;
        private static readonly System.Text.StringBuilder activationText = new System.Text.StringBuilder(128);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            ConsoleInputBackend.Configure(Pressed,
                owner => owner.AddComponent<InputSystemUIInputModule>().AssignDefaultActions(),
                owner => owner.AddComponent<InputSystemConsoleInput>(),
#if ENABLE_LEGACY_INPUT_MANAGER
                false
#else
                true
#endif
            );
            ConsoleInputBackend.ConfigureActivationInput(ReadText, PointerDown);
        }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor() => Initialize();
#endif

        private static bool Pressed(KeyCode key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            switch (key)
            {
                case KeyCode.BackQuote: return keyboard.backquoteKey.wasPressedThisFrame;
                case KeyCode.Return: return keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
                case KeyCode.KeypadEnter: return keyboard.numpadEnterKey.wasPressedThisFrame;
                case KeyCode.Escape: return keyboard.escapeKey.wasPressedThisFrame;
                case KeyCode.Tab: return keyboard.tabKey.wasPressedThisFrame;
                case KeyCode.UpArrow: return keyboard.upArrowKey.wasPressedThisFrame;
                case KeyCode.DownArrow: return keyboard.downArrowKey.wasPressedThisFrame;
                default: return false;
            }
        }

        private static string ReadText()
        {
            if (activationKeyboard != Keyboard.current)
            {
                if (activationKeyboard != null) activationKeyboard.onTextInput -= ReceiveText;
                activationKeyboard = Keyboard.current; activationText.Clear();
                if (activationKeyboard != null) activationKeyboard.onTextInput += ReceiveText;
            }
            if (activationText.Length == 0) return string.Empty;
            string value = activationText.ToString(); activationText.Clear(); return value;
        }

        private static void ReceiveText(char value)
        {
            if (activationText.Length == 128) activationText.Clear();
            activationText.Append(value);
        }

        private static Vector2? PointerDown()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return touch.primaryTouch.position.ReadValue();
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame ? mouse.position.ReadValue() : (Vector2?)null;
        }
    }
}
