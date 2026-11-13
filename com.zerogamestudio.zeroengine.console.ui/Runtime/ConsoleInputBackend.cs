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
    }
}
