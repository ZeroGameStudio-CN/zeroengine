using UnityEngine;
using UnityEngine.EventSystems;

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

        public static bool Pressed(KeyCode key) => ConsoleInputBackend.Pressed(key);
    }
}
