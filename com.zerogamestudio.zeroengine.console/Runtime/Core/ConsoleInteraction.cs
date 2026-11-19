using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    public class ConsolePrompt
    {
        public string Message { get; }
        public IReadOnlyList<ConsoleCompletionItem> Options { get; }
        internal ConsoleInvocation Invocation { get; }
        internal TaskCompletionSource<string> Response { get; } = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConsolePrompt(ConsoleInvocation invocation, string message, IReadOnlyList<ConsoleCompletionItem> options)
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 256)
                throw new ArgumentException("Use a prompt of 1 to 256 characters.", nameof(message));
            Invocation = invocation;
            Message = message;
            Options = options;
        }

        internal static ConsolePrompt Choice(ConsoleInvocation invocation, string message, IReadOnlyList<ConsoleCompletionItem> options)
        {
            if (options == null || options.Count < 2 || options.Count > 8)
                throw new ArgumentException("Supply two to eight choices.", nameof(options));
            var snapshot = new ConsoleCompletionItem[options.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.Length; i++)
            {
                var option = options[i];
                if (option == null || string.IsNullOrWhiteSpace(option.Id) || option.Id.Length > 64 ||
                    string.IsNullOrWhiteSpace(option.Label) || option.Label.Length > 80 || !ids.Add(option.Id))
                    throw new ArgumentException("Choices need unique IDs and readable labels.", nameof(options));
                snapshot[i] = option;
            }
            return new ConsolePrompt(invocation, message, Array.AsReadOnly(snapshot));
        }

        internal bool Allows(string choice)
        {
            if (choice == null) return true;
            if (this is ConsoleConfirmation) return choice == "yes";
            foreach (var option in Options)
                if (string.Equals(choice, option.Id, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool TryGetNumberedChoice(string input, out string choice)
        {
            choice = null;
            if (!int.TryParse(input?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                index < 1 || index > Options.Count) return false;
            choice = Options[index - 1].Id;
            return true;
        }
    }

    public sealed class ConsoleConfirmation : ConsolePrompt
    {
        internal ConsoleConfirmation(ConsoleInvocation invocation, string message)
            : base(invocation, message, Array.Empty<ConsoleCompletionItem>()) { }
    }

    /// <summary>Exclusive access to prompts for the active console frontend.</summary>
    public sealed class ConsoleInteraction : IDisposable
    {
        private ConsoleSession owner;

        internal ConsoleInteraction(ConsoleSession owner) => this.owner = owner;

        public ConsolePrompt PendingPrompt => Volatile.Read(ref owner)?.ReadPrompt(this);
        public ConsoleConfirmation Pending => PendingPrompt as ConsoleConfirmation;

        public bool Respond(ConsoleConfirmation request, ConsoleCaller caller, bool accept) =>
            Respond(request, caller, accept ? "yes" : null);

        public bool Respond(ConsolePrompt request, ConsoleCaller caller, string choiceId) =>
            Volatile.Read(ref owner)?.RespondToPrompt(this, request, caller, choiceId) == true;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ReleaseInteraction(this);
    }
}
