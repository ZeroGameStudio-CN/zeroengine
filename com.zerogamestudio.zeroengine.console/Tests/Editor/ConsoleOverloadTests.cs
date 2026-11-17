using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleOverloadTests
    {
        private static readonly ConsoleCaller Editor = new ConsoleCaller(ConsoleEnvironment.Editor);
        private enum First { Alpha, Both }
        private enum Second { Beta, Both }

        private static ConsoleBinding Binding(string id, ConsoleParameter[] parameters, List<string> calls,
            ConsoleCompleter completer = null, string[] capabilities = null)
        {
            var command = new ConsoleCommand(id, "pick", "overload", "Choose " + id, parameters,
                aliases: new[] { "choose" }, capabilities: capabilities);
            return new ConsoleBinding(command, (context, arguments, token) =>
            {
                calls.Add(id);
                return Task.FromResult(ConsoleResult.Success(data: arguments.Count == 0 ? null : arguments[0]));
            }, completer);
        }

        [Test]
        public void Registration_IsExplicitAtomicAndCannotShadowAnotherModuleOrId()
        {
            using var registry = new ConsoleRegistry();
            var calls = new List<string>();
            var bindings = new[] { Binding("one", Array.Empty<ConsoleParameter>(), calls),
                Binding("two", new[] { new ConsoleParameter("value", typeof(int)) }, calls) };
            Assert.Throws<InvalidOperationException>(() => registry.Register("overload", bindings));
            using var session = new ConsoleSession(registry);
            Assert.That(session.GetCommands(Editor), Is.Empty);
            using (registry.RegisterOverloads("overload", bindings))
            {
                Assert.That(session.GetCommands(Editor), Has.Length.EqualTo(2));
                Assert.Throws<InvalidOperationException>(() => registry.RegisterOverloads("overload", bindings));
                var collision = new ConsoleBinding(new ConsoleCommand("pick", "another", "other", ""),
                    (context, arguments, token) => Task.FromResult(ConsoleResult.Success()));
                Assert.Throws<InvalidOperationException>(() => registry.Register("other", new[] { collision }));
            }
            Assert.That(session.GetCommands(Editor), Is.Empty);
            using (registry.RegisterOverloads("overload", bindings)) Assert.That(session.GetCommands(Editor), Has.Length.EqualTo(2));
        }

        [Test]
        public async Task TextArityAliasesAndCase_InvokeOnlyTheSelectedHandler()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("one", new[] { new ConsoleParameter("value", typeof(int)) }, calls),
                Binding("two", new[] { new ConsoleParameter("value", typeof(int)), new ConsoleParameter("other", typeof(int)) }, calls) });
            Assert.That((await session.ExecuteLineAsync("CHOOSE 7", Editor)).Data, Is.EqualTo(7));
            Assert.That((await session.ExecuteLineAsync("pick 8 9", Editor)).Data, Is.EqualTo(8));
            Assert.That(calls, Is.EqualTo(new[] { "one", "two" }));
            Assert.That((await session.ExecuteLineAsync("pick", Editor)).Code, Is.EqualTo("no-matching-overload"));
            Assert.That(calls, Has.Count.EqualTo(2));
        }

        [TestCase("pick 12", "integer")]
        [TestCase("pick \"12\"", "text")]
        [TestCase("pick word", "text")]
        public async Task SameArity_UsesTypedValuesBeforeUnquotedStringFallback(string line, string expected)
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("integer", new[] { new ConsoleParameter("value", typeof(int)) }, calls),
                Binding("text", new[] { new ConsoleParameter("value", typeof(string)) }, calls) });
            Assert.That((await session.ExecuteLineAsync(line, Editor)).IsSuccess, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { expected }));
        }

        [Test]
        public async Task AmbiguousConversions_DoNotExecuteEitherHandler_AndExplicitIdsRemainUsable()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("integer", new[] { new ConsoleParameter("value", typeof(int)) }, calls),
                Binding("real", new[] { new ConsoleParameter("value", typeof(double)) }, calls) });
            Assert.That((await session.ExecuteLineAsync("pick 12", Editor)).Code, Is.EqualTo("ambiguous-overload"));
            Assert.That(calls, Is.Empty);
            Assert.That((await session.ExecuteLineAsync("integer 12", Editor)).Data, Is.EqualTo(12));
            Assert.That((await session.ExecuteLineAsync("pick 1.5", Editor)).Data, Is.EqualTo(1.5));
            Assert.That((await session.ExecuteAsync("pick", new object[] { 3 }, Editor)).Data, Is.EqualTo(3));
            Assert.That(calls, Is.EqualTo(new[] { "integer", "real", "integer" }));
        }

        [Test]
        public async Task FixedOptionalAndVariadicArity_HaveDeterministicPriority()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("fixed", new[] { new ConsoleParameter("value", typeof(int)) }, calls),
                Binding("optional", new[] { new ConsoleParameter("value", typeof(int)), new ConsoleParameter("scale", typeof(int), optional: true, defaultValue: 2) }, calls),
                Binding("variadic", new[] { new ConsoleParameter("values", typeof(int[]), variadic: true) }, calls) });
            foreach (string line in new[] { "pick 1", "pick 1 2", "pick 1 2 3", "pick" })
                Assert.That((await session.ExecuteLineAsync(line, Editor)).IsSuccess, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "fixed", "optional", "variadic", "variadic" }));
        }

        [Test]
        public async Task TypedCalls_PreferExactTypesAndPreserveNullAndArrayContracts()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("object", new[] { new ConsoleParameter("value", typeof(object)) }, calls),
                Binding("text", new[] { new ConsoleParameter("value", typeof(string)) }, calls) });
            Assert.That((await session.ExecuteAsync("choose", new object[] { "3" }, Editor)).Data, Is.EqualTo("3"));
            Assert.That((await session.ExecuteAsync("pick", new object[] { null }, Editor)).Data, Is.Null);
            Assert.That(calls, Is.EqualTo(new[] { "text", "text" }));
        }

        [Test]
        public async Task HiddenOverloads_DoNotLeakThroughErrorsNamesHintsOrCompletion()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("secret-integer", new[] { new ConsoleParameter("value", typeof(int)) }, calls, capabilities: new[] { "secret" }),
                Binding("visible-guid", new[] { new ConsoleParameter("value", typeof(Guid)) }, calls) });
            var result = await session.ExecuteLineAsync("pick invalid", Editor);
            Assert.That(result.Code, Is.EqualTo("no-matching-overload"));
            Assert.That(result.Message, Does.Not.Contain("secret-integer"));
            Assert.That(session.GetHint("pick ", 5, Editor).Alternatives, Is.Empty);
            Assert.That((await session.CompleteAsync("pic", 3, Editor)).Items.Select(item => item.Value), Is.EqualTo(new[] { "pick" }));
            Assert.That(session.GetCommands(Editor).Select(command => command.Id), Is.EqualTo(new[] { "visible-guid" }));
            Assert.That(calls, Is.Empty);
        }

        [Test]
        public async Task Completion_MergesVisibleAlternativesWithoutDuplicateInsertionValuesOrExecution()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("first", new[] { new ConsoleParameter("value", typeof(First)) }, calls),
                Binding("second", new[] { new ConsoleParameter("value", typeof(Second)) }, calls) });
            var result = await session.CompleteAsync("pick ", 5, Editor);
            Assert.That(result.Items.Select(item => item.Value), Is.EqualTo(new[] { "Alpha", "Beta", "Both" }));
            Assert.That(result.Hint.Alternatives, Has.Count.EqualTo(1));
            Assert.That(result.Hint.Signature, Does.Contain("First").And.Contain("Second"));
            Assert.That(result.Apply("pick ", 0), Is.EqualTo("pick Alpha"));
            Assert.That(calls, Is.Empty);
        }

        [Test]
        public async Task ArityHints_SelectTheAvailableParameterWithoutCallingConverters()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var converter = session.Values.Register(typeof(int), (ConsoleToken token, Type type, out object value, out string error) =>
                { throw new AssertionException("Hints must use metadata, not conversions."); });
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("one", new[] { new ConsoleParameter("number", typeof(int)) }, calls),
                Binding("two", new[] { new ConsoleParameter("number", typeof(int)), new ConsoleParameter("enabled", typeof(bool)) }, calls) });
            var hint = session.GetHint("pick 1 ", 7, Editor);
            Assert.That(hint.Command.Id, Is.EqualTo("two"));
            Assert.That(hint.Parameter.Name, Is.EqualTo("enabled"));
            Assert.That((await session.CompleteAsync("pick 1 ", 7, Editor)).Items.Select(item => item.Value), Is.EqualTo(new[] { "false", "true" }));
        }

        [Test]
        public async Task SelectedArguments_AreConvertedOnce_AndPrecancelledCallsDoNoConversion()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            int conversions = 0;
            var calls = new List<string>();
            using var converter = session.Values.Register(typeof(Uri), (ConsoleToken token, Type type, out object value, out string error) =>
                { conversions++; value = new Uri("https://example.test/"); error = null; return true; });
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("uri", new[] { new ConsoleParameter("value", typeof(Uri)) }, calls),
                Binding("two", new[] { new ConsoleParameter("one", typeof(string)), new ConsoleParameter("two", typeof(string)) }, calls) });
            Assert.That((await session.ExecuteLineAsync("pick target", Editor)).IsSuccess, Is.True);
            Assert.That(conversions, Is.EqualTo(1));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.That((await session.ExecuteLineAsync("pick target", Editor, cancelled.Token)).Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
            Assert.That(conversions, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(new[] { "uri" }));
        }

        [Test]
        public void Registration_RejectsUnboundedFamiliesWithoutPartialChanges()
        {
            using var registry = new ConsoleRegistry();
            var calls = new List<string>();
            Assert.Throws<InvalidOperationException>(() => registry.RegisterOverloads("overload",
                Enumerable.Range(0, ConsoleRegistry.MaximumOverloads + 1).Select(index => Binding("id" + index, Array.Empty<ConsoleParameter>(), calls))));
            using var session = new ConsoleSession(registry);
            Assert.That(session.GetCommands(Editor), Is.Empty);
        }

        [Test]
        public async Task UnexpectedConverterFailures_DoNotFallThroughToAnotherBusinessHandler()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var calls = new List<string>();
            using var converter = session.Values.Register(typeof(Uri), (ConsoleToken token, Type type, out object value, out string error) =>
                { throw new InvalidOperationException("converter failed"); });
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("a.uri", new[] { new ConsoleParameter("value", typeof(Uri)) }, calls),
                Binding("z.text", new[] { new ConsoleParameter("value", typeof(string)) }, calls) });
            Assert.That((await session.ExecuteLineAsync("pick value", Editor)).Code, Is.EqualTo("overload-conversion-failed"));
            Assert.That(calls, Is.Empty);
        }

        [Test]
        public async Task RelockingDuringConversion_DoesNotExecuteOrExposeOldAlternatives()
        {
            using var registry = new ConsoleRegistry();
            var policy = new MutablePolicy();
            using var session = new ConsoleSession(registry, policy: policy);
            var calls = new List<string>();
            using var converter = session.Values.Register(typeof(Uri), (ConsoleToken token, Type type, out object value, out string error) =>
                { policy.Allowed = false; value = new Uri("https://example.test/"); error = null; return true; });
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("a.private", new[] { new ConsoleParameter("value", typeof(Uri)) }, calls),
                Binding("z.text", new[] { new ConsoleParameter("value", typeof(string)) }, calls) });
            var result = await session.ExecuteLineAsync("pick value", Editor);
            Assert.That(result.Code, Is.EqualTo("locked"));
            Assert.That(result.Message, Does.Not.Contain("a.private"));
            Assert.That(calls, Is.Empty);
        }

        [Test]
        public async Task RelockingDuringAnAsyncProvider_DiscardsTheWholeHint()
        {
            using var registry = new ConsoleRegistry();
            var policy = new MutablePolicy();
            using var session = new ConsoleSession(registry, policy: policy);
            var entered = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            var calls = new List<string>();
            using var lease = registry.RegisterOverloads("overload", new[] {
                Binding("a.flag", new[] { new ConsoleParameter("value", typeof(bool)) }, calls, async (context, token) => {
                    entered.SetResult(true); await release.Task;
                    return new[] { new ConsoleCompletionItem("true", "true") };
                }),
                Binding("b.enum", new[] { new ConsoleParameter("value", typeof(First)) }, calls) });
            var pending = session.CompleteAsync("pick ", 5, Editor);
            await entered.Task;
            policy.Allowed = false; release.SetResult(true);
            var result = await pending;
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.Hint, Is.Null);
            Assert.That(calls, Is.Empty);
        }

        private sealed class MutablePolicy : IConsolePolicy
        {
            public bool Allowed = true;
            public ConsoleResult Check(ConsoleCommand command, ConsoleCaller caller)
                => Allowed ? ConsoleResult.Success() : ConsoleResult.Failure("locked", "Locked.");
        }

        [Test]
        public async Task UnloadingTheFamily_CancelsItsRunningHandlerAndRemovesAllAliases()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            var entered = new TaskCompletionSource<bool>();
            int calls = 0;
            var slow = new ConsoleBinding(new ConsoleCommand("slow", "pick", "overload", "", aliases: new[] { "choose" }),
                async (context, arguments, cancellation) => { calls++; entered.SetResult(true); await Task.Delay(Timeout.Infinite, cancellation); return ConsoleResult.Success(); });
            var other = Binding("other", new[] { new ConsoleParameter("value", typeof(int)) }, new List<string>());
            var lease = registry.RegisterOverloads("overload", new[] { slow, other });
            var pending = session.ExecuteLineAsync("choose", Editor);
            await entered.Task;
            lease.Dispose();
            Assert.That((await pending).Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That((await session.ExecuteLineAsync("pick 1", Editor)).Code, Is.EqualTo("unknown-command"));
            Assert.That((await session.CompleteAsync("cho", 3, Editor)).Items, Is.Empty);
        }
    }
}
