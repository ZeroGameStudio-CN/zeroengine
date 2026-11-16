using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsolePresentationTests
    {
        private static readonly ConsoleCaller Editor = new ConsoleCaller(ConsoleEnvironment.Editor);
        private static Task<ConsoleResult> Ok(ConsoleInvocation context, ConsoleArguments args, CancellationToken token) => Task.FromResult(ConsoleResult.Success());

        [Test]
        public async Task Hints_TrackQuotedArgumentsAliasesOptionalDefaultsAndVariadicValues()
        {
            using var registry = new ConsoleRegistry();
            var command = new ConsoleCommand("test.make", "make", "test", "Create an item.", new[] {
                new ConsoleParameter("name", typeof(string), "Item name"),
                new ConsoleParameter("count", typeof(int), "Item count", optional: true, defaultValue: 1),
                new ConsoleParameter("tags", typeof(string[]), "Tags", variadic: true) }, aliases: new[] { "mk" });
            using var module = registry.Register("test", new[] { new ConsoleBinding(command, Ok) });
            using var session = new ConsoleSession(registry);
            Assert.That(command.Signature, Is.EqualTo("make <name: string> [count: int = 1] [tags: string[]...]"));
            string line = "mk \"two words\" 2 first ";
            var hint = session.GetHint(line, line.Length, Editor);
            Assert.That(hint.ParameterIndex, Is.EqualTo(2));
            Assert.That(hint.Detail, Does.Contain("Tags"));
            Assert.That(session.GetHint(line, 8, Editor).ParameterIndex, Is.Zero);
            Assert.That((await session.CompleteAsync(line, 8, Editor)).Hint.ParameterIndex, Is.Zero);
            Assert.That(session.GetHint("make", 4, Editor).ParameterIndex, Is.EqualTo(-1));
            Assert.That((await session.CompleteAsync("mk ", 3, Editor)).Hint.Parameter.Name, Is.EqualTo("name"));
            module.Dispose();
            Assert.That(session.GetHint(line, line.Length, Editor), Is.Null);
        }

        [Test]
        public async Task Hints_AndPendingProvidersHonorLivePolicy()
        {
            using var registry = new ConsoleRegistry();
            var policy = new Gate();
            var supplied = new TaskCompletionSource<IReadOnlyList<ConsoleCompletionItem>>();
            using var module = registry.Register("test", new[] { new ConsoleBinding(new ConsoleCommand("test.secure", "secure", "test", "Private",
                new[] { new ConsoleParameter("value", typeof(string)) }), Ok, (context, token) => supplied.Task) });
            using var session = new ConsoleSession(registry, policy: policy);
            Assert.That(session.GetHint("secure ", 7, Editor), Is.Not.Null);
            var pending = session.CompleteAsync("secure ", 7, Editor);
            policy.Allowed = false;
            supplied.SetResult(new[] { new ConsoleCompletionItem("private", "private") });
            var denied = await pending;
            Assert.That(denied.Items, Is.Empty);
            Assert.That(denied.Hint, Is.Null);
            Assert.That(session.GetHint("secure ", 7, Editor), Is.Null);
        }

        private sealed class Gate : IConsolePolicy
        {
            public bool Allowed = true;
            public ConsoleResult Check(ConsoleCommand command, ConsoleCaller caller) => Allowed ? ConsoleResult.Success() : ConsoleResult.Failure("denied", "Denied");
        }

        [Test]
        public async Task Completion_MiddleReplacementKeepsTailAndReturnsInsertionCaret()
        {
            using var registry = new ConsoleRegistry();
            using var module = registry.Register("test", new[] { new ConsoleBinding(new ConsoleCommand("test.echo", "echo", "test", "Echo",
                new[] { new ConsoleParameter("value", typeof(string)) }), Ok,
                (context, token) => Task.FromResult<IReadOnlyList<ConsoleCompletionItem>>(new[] { new ConsoleCompletionItem("target", "two words") })) });
            using var session = new ConsoleSession(registry);
            const string line = "echo trailed tail";
            var result = await session.CompleteAsync(line, 6, Editor);
            Assert.That(result.Apply(line, 0, out int caret), Is.EqualTo("echo \"two words\" tail"));
            Assert.That(caret, Is.EqualTo("echo \"two words\"".Length));
            Assert.Throws<InvalidOperationException>(() => result.Apply("echo changed", 0, out _));
        }

        [Test]
        public void Formatting_IsInvariantAndPreservesCollectionMeaning()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var formatter = new ConsoleValueFormatter();
                Assert.That(formatter.Format(1.25), Is.EqualTo("1.25"));
                Assert.That(formatter.Format(new object[] { null, "中文 \"引号\"", true, new[] { 1, 2 } }),
                    Is.EqualTo("[null, \"中文 \\\"引号\\\"\", true, [1, 2]]"));
                Assert.That(formatter.Format((3, "value", new[] { 4 })), Is.EqualTo("(3, \"value\", [4])"));
                Assert.That(formatter.Format(new Dictionary<string, object> { { "value", 2 }, { "empty", null } }),
                    Is.EqualTo("{\"value\": 2, \"empty\": null}"));
                Assert.That(formatter.Format(""), Is.EqualTo("\"\""));
                Assert.That(formatter.Format(new Opaque()), Is.EqualTo("(Opaque)"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        private sealed class Opaque { public override string ToString() => throw new InvalidOperationException("Must not be inspected."); }

        [Test]
        public void Signature_DoesNotEnumerateProjectDefaultValues()
        {
            var values = new Infinite();
            var command = new ConsoleCommand("test.defaults", "defaults", "test", "Describe defaults.", new[] {
                new ConsoleParameter("values", typeof(IEnumerable), optional: true, defaultValue: values) });
            Assert.That(command.Signature, Does.Contain("IEnumerable"));
            Assert.That(values.Moves, Is.Zero);
            var text = new ConsoleCommand("test.text", "text", "test", "Text defaults.", new[] {
                new ConsoleParameter("filter", typeof(string), optional: true, defaultValue: "") });
            Assert.That(text.Signature, Is.EqualTo("text [filter: string = \"\"]"));
        }

        [Test]
        public void Formatting_BoundsDepthItemsAndSurrogatesAndDisposesEnumerators()
        {
            var cycle = new ArrayList(); cycle.Add(cycle);
            Assert.That(ConsoleValueFormatter.Default.Format(cycle), Does.Contain("(cycle)"));
            Assert.That(new ConsoleValueFormatter(maximumDepth: 1).Format(new[] { new[] { 1 } }), Is.EqualTo("[…]") );
            var values = new Infinite();
            Assert.That(new ConsoleValueFormatter(maximumItems: 3).Format(values), Is.EqualTo("[1, 2, 3, …]"));
            Assert.That(values.Moves, Is.EqualTo(4));
            Assert.That(values.Disposed, Is.True);
            string limited = new ConsoleValueFormatter(maximumLength: 16).Format(new string('a', 14) + "😀b");
            Assert.That(limited.Length, Is.LessThanOrEqualTo(16));
            Assert.That(limited.EndsWith("…", StringComparison.Ordinal), Is.True);
            Assert.That(limited.Length < 2 || char.IsHighSurrogate(limited[limited.Length - 2]), Is.False);
        }

        private sealed class Infinite : IEnumerable, IEnumerator, IDisposable
        {
            public int Moves; public bool Disposed;
            public object Current => Moves;
            public IEnumerator GetEnumerator() => this;
            public bool MoveNext() { Moves++; return true; }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
        }

        [Test]
        public async Task ResultFormatting_LeavesBusinessResultAndExistingMessagesIntact()
        {
            using var registry = new ConsoleRegistry();
            object data = new[] { 1, 2 };
            using var module = registry.Register("test", new[] {
                new ConsoleBinding(new ConsoleCommand("test.value", "value", "test", "Return data"), (context, args, token) => Task.FromResult(ConsoleResult.Success(data: data))),
                new ConsoleBinding(new ConsoleCommand("test.message", "message", "test", "Return authoritative text"), (context, args, token) => Task.FromResult(ConsoleResult.Success("already shown", data))) });
            using var session = new ConsoleSession(registry);
            await session.ExecuteLineAsync("value", Editor);
            Assert.That(session.GetOutput(), Is.Empty);
            session.ResultFormatter = ConsoleValueFormatter.Default;
            var result = await session.ExecuteLineAsync("value", Editor);
            Assert.That(result.Data, Is.SameAs(data));
            Assert.That(session.GetOutput().Single().Message, Is.EqualTo("[1, 2]"));
            Assert.That(session.GetOutput().Single().InvocationId, Is.Not.Empty);
            session.ClearOutput();
            await session.ExecuteLineAsync("message", Editor);
            Assert.That(session.GetOutput().Single().Message, Is.EqualTo("already shown"));
            session.ResultFormatter = new BrokenFormatter();
            result = await session.ExecuteLineAsync("value", Editor);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.SameAs(data));
            Assert.That(session.GetOutput().Last().Level, Is.EqualTo(ConsoleOutputLevel.Warning));
        }

        private sealed class BrokenFormatter : IConsoleValueFormatter { public string Format(object value) => throw new InvalidOperationException(); }
    }
}
