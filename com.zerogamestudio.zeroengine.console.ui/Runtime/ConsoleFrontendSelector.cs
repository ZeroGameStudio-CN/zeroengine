using System;
using UnityEngine;
using ZeroEngine.Console.Unity;

namespace ZeroEngine.Console.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConsoleHost), typeof(NativeConsole))]
    public sealed class ConsoleFrontendSelector : MonoBehaviour
    {
        [SerializeField] private ConsoleFrontendPreference preference = ConsoleFrontendPreference.Auto;
        [SerializeField] private MonoBehaviour quantumFrontend;
        private ConsoleHost host;
        private NativeConsole native;
        private IConsoleFrontend current;
        private bool previousNativeShortcut;
        private Func<IDisposable> previousNativeInputIsolation;
        private Func<IDisposable> previousCurrentInputIsolation;
        private bool notifiedUnavailable;
        public IConsoleFrontend Current => current;
        public string SelectionReason { get; private set; } = string.Empty;
        public Func<IDisposable> AcquireInputIsolation { get; set; }

        private void OnEnable()
        {
            host = GetComponent<ConsoleHost>();
            native = GetComponent<NativeConsole>();
            previousNativeShortcut = native.KeyboardShortcutEnabled;
            previousNativeInputIsolation = native.AcquireInputIsolation;
            native.KeyboardShortcutEnabled = false;
            Select(preference);
        }

        public void ConfigureQuantum(MonoBehaviour provider)
        {
            quantumFrontend = provider;
            if (isActiveAndEnabled) Select(preference);
        }

        public void Select(ConsoleFrontendPreference value)
        {
            preference = value;
            bool reopen = current?.IsOpen ?? false;
            current?.Close();
            if (current != null) current.AcquireInputIsolation = previousCurrentInputIsolation;
            IConsoleFrontend next = null;
            string reason = string.Empty;
            if (value != ConsoleFrontendPreference.Native)
            {
                var quantum = quantumFrontend as IConsoleFrontend;
                if (quantum != null && quantum.TryInitialize(host, out reason)) next = quantum;
                else if (quantum == null) reason = "No Quantum frontend provider is configured.";
            }
            if (next == null)
            {
                native.TryInitialize(host, out string nativeReason);
                next = native;
                if (value == ConsoleFrontendPreference.Native) reason = nativeReason;
                if (!string.IsNullOrEmpty(reason) && !notifiedUnavailable)
                {
                    host.Session?.Write(new ConsoleOutput("frontend", ConsoleOutputLevel.Warning, reason + " Using Native."));
                    notifiedUnavailable = true;
                }
            }
            SelectionReason = reason;
            current = next;
            previousCurrentInputIsolation = current.AcquireInputIsolation;
            current.AcquireInputIsolation = () => (AcquireInputIsolation ?? previousNativeInputIsolation)?.Invoke();
            if (reopen) Open();
        }

        private void Update()
        {
            if (current?.IsComposing == true || !ConsoleKeyboard.Pressed(KeyCode.BackQuote)) return;
            if (current?.IsOpen == true) Close(); else Open();
        }

        public void Open()
        {
            try { current?.Open(); }
            catch (Exception exception) when (current != null && !ReferenceEquals(current, native))
            {
                current.Close();
                current.AcquireInputIsolation = previousCurrentInputIsolation;
                SelectionReason = "Quantum frontend could not open: " + exception.GetBaseException().Message;
                if (!notifiedUnavailable)
                {
                    host.Session?.Write(new ConsoleOutput("frontend", ConsoleOutputLevel.Warning, SelectionReason + " Using Native."));
                    notifiedUnavailable = true;
                }
                current = native;
                previousCurrentInputIsolation = native.AcquireInputIsolation;
                native.AcquireInputIsolation = () => (AcquireInputIsolation ?? previousNativeInputIsolation)?.Invoke();
                native.Open();
            }
        }
        public void Close() => current?.Close();
        private void OnDisable()
        {
            current?.Close();
            if (current != null) current.AcquireInputIsolation = previousCurrentInputIsolation;
            current = null;
            if (native) { native.KeyboardShortcutEnabled = previousNativeShortcut; native.AcquireInputIsolation = previousNativeInputIsolation; }
        }
    }
}
