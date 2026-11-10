using System;

namespace ZeroEngine.Console.Unity
{
    public interface IConsoleFrontend
    {
        string BackendId { get; }
        bool IsOpen { get; }
        bool IsComposing { get; }
        bool TryInitialize(ConsoleHost host, out string reason);
        void Open();
        void Close();
        Func<IDisposable> AcquireInputIsolation { get; set; }
    }

    public enum ConsoleFrontendPreference { Auto, Native, Quantum }
}
