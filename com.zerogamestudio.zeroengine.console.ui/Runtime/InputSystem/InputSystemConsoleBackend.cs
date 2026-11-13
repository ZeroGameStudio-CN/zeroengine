using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ZeroEngine.Console.UI
{
    internal static class InputSystemConsoleBackend
    {
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
                case KeyCode.PageUp: return keyboard.pageUpKey.wasPressedThisFrame;
                case KeyCode.PageDown: return keyboard.pageDownKey.wasPressedThisFrame;
                default: return false;
            }
        }
    }
}
