using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    public sealed class ConsoleConfirmation
    {
        public string Message { get; }
        internal ConsoleInvocation Invocation { get; }
        internal TaskCompletionSource<bool> Response { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConsoleConfirmation(ConsoleInvocation invocation, string message)
        {
            Invocation = invocation;
            Message = message;
        }
    }

    /// <summary>Exclusive access to confirmations for the active console frontend.</summary>
    public sealed class ConsoleInteraction : IDisposable
    {
        private ConsoleSession owner;

        internal ConsoleInteraction(ConsoleSession owner) => this.owner = owner;

        public ConsoleConfirmation Pending => Volatile.Read(ref owner)?.ReadConfirmation(this);

        public bool Respond(ConsoleConfirmation request, ConsoleCaller caller, bool accept) =>
            Volatile.Read(ref owner)?.RespondToConfirmation(this, request, caller, accept) == true;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ReleaseInteraction(this);
    }
}
