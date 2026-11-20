using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Console.UI
{
    // The optional integration assembly registers public input APIs here.
    // The native view itself keeps no reference to an input-system package.
    public static class ConsoleInputBackend
    {
        private static Func<KeyCode, bool> pressed;
        private static Action<GameObject> addModule;
        private static Func<GameObject, BaseInput> createInput;
        private static Func<string> activationText;
        private static Func<Vector2?> activationPointer;
        public static bool NeedsInputOverride { get; private set; }

        public static void Configure(Func<KeyCode, bool> readKey, Action<GameObject> addInputModule,
            Func<GameObject, BaseInput> createInputSource, bool needsInputOverride)
        {
            pressed = readKey ?? throw new ArgumentNullException(nameof(readKey));
            addModule = addInputModule ?? throw new ArgumentNullException(nameof(addInputModule));
            createInput = createInputSource ?? throw new ArgumentNullException(nameof(createInputSource));
            NeedsInputOverride = needsInputOverride;
        }

        internal static bool Pressed(KeyCode key)
        {
            if (pressed != null) return pressed(key);
            try { return Input.GetKeyDown(key); }
            catch (InvalidOperationException) { return false; }
        }
        internal static void AddInputModule(GameObject owner)
        {
            if (addModule != null) addModule(owner);
            else owner.AddComponent<StandaloneInputModule>();
        }
        internal static BaseInput CreateInputSource(GameObject owner) => createInput?.Invoke(owner);

        internal static float CompletionDelay(string text)
        {
            // Command names can be looked up locally. Coalesce edits that may reach an argument provider.
            foreach (char character in text)
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.')
                    return .04f;
            return text.Length == 0 ? .04f : 0f;
        }

        public static void ConfigureActivationInput(Func<string> readText, Func<Vector2?> readPointerDown)
        { activationText = readText; activationPointer = readPointerDown; }

        internal static string ReadActivationText()
        {
            if (activationText != null) return activationText() ?? string.Empty;
            try { return Input.inputString; } catch (InvalidOperationException) { return string.Empty; }
        }

        internal static bool TryReadActivationPointer(out Vector2 point)
        {
            if (activationPointer != null)
            {
                var value = activationPointer(); point = value ?? Vector2.zero; return value.HasValue;
            }
            try { point = Input.mousePosition; return Input.GetMouseButtonDown(0); }
            catch (InvalidOperationException) { point = Vector2.zero; return false; }
        }
    }
}
