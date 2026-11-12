using System;
using UnityEngine;
using ZeroEngine.Console.Unity;

namespace ZeroEngine.Console.UI.Tests
{
    // Exercises the public optional-frontend boundary without a vendor SDK.
    public sealed class OpeningFailureFrontend : MonoBehaviour, IConsoleFrontend
    {
        private IDisposable lease;
        public string BackendId => "opening-failure";
        public bool IsOpen { get; private set; }
        public bool IsComposing => false;
        public int OpenCount { get; private set; }
        public Func<IDisposable> AcquireInputIsolation { get; set; }
        public bool TryInitialize(ConsoleHost host, out string reason) { reason = string.Empty; return true; }
        public void Open()
        {
            OpenCount++;
            lease = AcquireInputIsolation?.Invoke();
            IsOpen = true;
            throw new InvalidOperationException("Synthetic presentation failure.");
        }
        public void Close() { IsOpen = false; lease?.Dispose(); lease = null; }
        private void OnDisable() => Close();
    }
}
