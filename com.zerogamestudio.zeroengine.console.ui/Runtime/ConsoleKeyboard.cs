using UnityEngine;
using UnityEngine.EventSystems;
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ZeroEngine.Console.UI
{
    public static class ConsoleKeyboard
    {
        public static bool IsComposing
        {
            get
            {
                try
                {
                    var events = EventSystem.current;
                    var module = events ? events.currentInputModule : null;
                    if (!module && events) module = events.GetComponent<BaseInputModule>();
                    var source = module ? module.input : null;
                    if (source && source.isActiveAndEnabled) return !string.IsNullOrEmpty(source.compositionString);
                    return !string.IsNullOrEmpty(Input.compositionString);
                }
                catch (System.InvalidOperationException) { return false; }
            }
        }

        public static bool Pressed(KeyCode key)
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
