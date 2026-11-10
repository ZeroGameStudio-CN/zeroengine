using UnityEngine;
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ZeroEngine.Console.UI
{
    internal static class ConsoleKeyboard
    {
        internal static bool Pressed(KeyCode key)
        {
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            switch (key)
            {
                case KeyCode.BackQuote: return keyboard.backquoteKey.wasPressedThisFrame;
                case KeyCode.Return: return keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
                case KeyCode.Escape: return keyboard.escapeKey.wasPressedThisFrame;
                case KeyCode.Tab: return keyboard.tabKey.wasPressedThisFrame;
                case KeyCode.UpArrow: return keyboard.upArrowKey.wasPressedThisFrame;
                case KeyCode.DownArrow: return keyboard.downArrowKey.wasPressedThisFrame;
                default: return false;
            }
#else
            try { return Input.GetKeyDown(key); }
            catch (System.InvalidOperationException) { return false; }
#endif
        }
    }
}
