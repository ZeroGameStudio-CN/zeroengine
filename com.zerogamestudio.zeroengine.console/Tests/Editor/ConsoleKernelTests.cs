using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleKernelTests
    {
        private static readonly ConsoleCaller Editor = new ConsoleCaller(ConsoleEnvironment.Editor);
        private static ConsoleCommand Command(string name = "ze-echo", ConsoleParameter[] parameters = null,
            ConsoleEnvironment environments = ConsoleEnvironment.Development, string[] capabilities = null) =>
            new ConsoleCommand("ze.test." + name, name, "test", "Test command", parameters, environments: environments, capabilities: capabilities);
        private static Task<ConsoleResult> Ok(ConsoleInvocation invocation, ConsoleArguments arguments, CancellationToken token) =>
            Task.FromResult(ConsoleResult.Success("done", arguments.Count));

        [TestCase("say \"\" '中文 空格' -12", new[] { "say", "", "中文 空格", "-12" })]
        [TestCase("say a\\ b C:\\temp\\file", new[] { "say", "a b", "C:\\temp\\file" })]
        [TestCase("say [1, 2, [3, 4]] (1, 2, 3)", new[] { "say", "[1, 2, [3, 4]]", "(1, 2, 3)" })]
        [TestCase("say \"a\\\"b\\n\\t\"", new[] { "say", "a\"b\n\t" })]
        public void Lexer_PreservesArgumentMeaning(string line, string[] expected)
        {
            Assert.That(ConsoleLexer.TryRead(line, out var tokens, out var error), Is.True, error?.Message);
            Assert.That(tokens.Select(token => token.Text), Is.EqualTo(expected));
            foreach (var token in tokens) Assert.That(line.Substring(token.Start, token.Length), Is.EqualTo(token.Raw));
        }

        [TestCase("say \"open", "unterminated-quote", 4)]
        [TestCase("say [1,2", "unterminated-group", 4)]
        [TestCase("say [1)", "unexpected-group-end", 6)]
        public void Lexer_ReportsActualErrorSpan(string line, string code, int position)
        {
            Assert.That(ConsoleLexer.TryRead(line, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(code));
            Assert.That(error.Position, Is.EqualTo(position));
        }

        [TestCase("")]
        [TestCase("中文 空格")]
        [TestCase("C:\\files\\test.txt")]
        [TestCase("line\n\"quoted\"\tend")]
        public void Quoting_RoundtripsOneArgument(string value)
        {
            Assert.That(ConsoleLexer.TryRead(ConsoleLexer.Quote(value), out var tokens, out _), Is.True);
            Assert.That(tokens.Count, Is.EqualTo(1));
            Assert.That(tokens[0].Text, Is.EqualTo(value));
        }

        [Test]
        public void Registry_RejectsWholeConflictingModuleAndAllowsCleanReuse()
        {
            using var registry = new ConsoleRegistry();
            var first = registry.Register("test", new[] { new ConsoleBinding(Command(), Ok) });
            Assert.Throws<InvalidOperationException>(() => registry.Register("test", new[] {
                new ConsoleBinding(Command("new"), Ok), new ConsoleBinding(Command(), Ok) }));
            using var session = new ConsoleSession(registry);
            Assert.That(session.GetCommands(Editor).Select(command => command.Name), Is.EqualTo(new[] { "ze-echo" }));
            first.Dispose(); first.Dispose();
            using var replacement = registry.Register("test", new[] { new ConsoleBinding(Command(), Ok) });
            first.Dispose();
            Assert.That(session.GetCommands(Editor), Has.Length.EqualTo(1));
        }

        [Test]
        public void Registry_RejectsIdentifierNameAmbiguity()
        {
            using var registry = new ConsoleRegistry();
            using var first = registry.Register("test", new[] { new ConsoleBinding(Command(), Ok) });
            var colliding = new ConsoleCommand("ze.other", "ze.test.ze-echo", "test", "Ambiguous lookup");
            Assert.Throws<InvalidOperationException>(() => registry.Register("test", new[] { new ConsoleBinding(colliding, Ok) }));
            var caseOnly = new ConsoleCommand("ze.other", "ZE.TEST.ZE-ECHO", "test", "Case folded lookup");
            Assert.Throws<InvalidOperationException>(() => registry.Register("test", new[] { new ConsoleBinding(caseOnly, Ok) }));
        }

        [Test]
        public async Task Policy_IsIdenticalForDiscoveryCompletionTextAndTypedCalls()
        {
            using var registry = new ConsoleRegistry();
            int calls = 0;
            var command = Command(capabilities: new[] { "write" });
            using var module = registry.Register("test", new[] { new ConsoleBinding(command, (context, args, token) => { calls++; return Ok(context, args, token); }) });
            using var session = new ConsoleSession(registry);
            Assert.That(session.GetCommands(Editor), Is.Empty);
            Assert.That((await session.CompleteAsync("ze", 2, Editor)).Items, Is.Empty);
            Assert.That((await session.ExecuteLineAsync(command.Name, Editor)).Code, Is.EqualTo("capability-denied"));
            Assert.That((await session.ExecuteAsync(command.Id, null, Editor)).Code, Is.EqualTo("capability-denied"));
            Assert.That(calls, Is.Zero);
            var permitted = new ConsoleCaller(ConsoleEnvironment.Editor, capabilities: new[] { "write" });
            Assert.That((await session.ExecuteAsync(command.Id, null, permitted)).IsSuccess, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That((await session.ExecuteLineAsync(command.Name, new ConsoleCaller(ConsoleEnvironment.Player, capabilities: new[] { "write" }))).Code, Is.EqualTo("environment-denied"));
        }

        [Test]
        public async Task Binding_IsInvariantTypedAndPreservesNestedArrayStrings()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                using var registry = new ConsoleRegistry();
                var command = Command(parameters: new[] {
                    new ConsoleParameter("number", typeof(float)), new ConsoleParameter("items", typeof(string[])),
                    new ConsoleParameter("enabled", typeof(bool), optional: true, defaultValue: true) });
                using var module = registry.Register("test", new[] { new ConsoleBinding(command, (context, args, token) => {
                    Assert.That(args.Get<float>(0), Is.EqualTo(-1.25f));
                    Assert.That(args.Get<string[]>(1), Is.EqualTo(new[] { "a b", "c,d", "" }));
                    Assert.That(args.Get<bool>(2), Is.True);
                    return Ok(context, args, token);
                }) });
                using var session = new ConsoleSession(registry);
                Assert.That((await session.ExecuteLineAsync("ze-echo -1.25 [\"a b\",\"c,d\",\"\"]", Editor)).IsSuccess, Is.True);
                Assert.That((await session.ExecuteAsync(command.Id, new object[] { "-1.25", new[] { "x" } }, Editor)).Code, Is.EqualTo("invalid-argument-type"));
                Assert.That((await session.ExecuteLineAsync("ze-echo NaN []", Editor)).Code, Is.EqualTo("invalid-argument"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [UnityTest]
        public IEnumerator VariadicAndOptionalBinding_RejectsMissingAndExtraArguments()
        {
            var pending = VerifyVariadicAndOptionalBindingAsync();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!pending.IsCompleted)
            {
                Assert.That(elapsed.Elapsed.TotalSeconds, Is.LessThan(10), "Console binding did not complete.");
                yield return null;
            }
            pending.GetAwaiter().GetResult();
        }

        private static async Task VerifyVariadicAndOptionalBindingAsync()
        {
            using var registry = new ConsoleRegistry();
            var command = Command(parameters: new[] { new ConsoleParameter("id", typeof(string)), new ConsoleParameter("values", typeof(int[]), variadic: true) });
            using var module = registry.Register("test", new[] { new ConsoleBinding(command, (context, args, token) => ConsoleResultTask(args.Get<int[]>(1).Length)) });
            using var session = new ConsoleSession(registry);
            Assert.That((await session.ExecuteLineAsync("ze-echo actor 1 2 -3", Editor)).Data, Is.EqualTo(3));
            Assert.That((await session.ExecuteLineAsync("ze-echo actor", Editor)).Data, Is.EqualTo(0));
            Assert.That((await session.ExecuteLineAsync("ze-echo", Editor)).Code, Is.EqualTo("missing-argument"));
        }

        [Test]
        public async Task Completion_UsesStableIdentityCorrectSpanAndQuotedInsertion()
        {
            using var registry = new ConsoleRegistry();
            using var module = registry.Register("test", new[] { new ConsoleBinding(Command(parameters: new[] { new ConsoleParameter("id", typeof(string)) }), Ok,
                (context, token) => Task.FromResult<IReadOnlyList<ConsoleCompletionItem>>(new[] {
                    new ConsoleCompletionItem("b", "two words"), new ConsoleCompletionItem("a", "target") })) });
            using var session = new ConsoleSession(registry);
            const string line = "ze-echo t";
            var first = await session.CompleteAsync(line, line.Length, Editor);
            var second = await session.CompleteAsync(line, line.Length, Editor);
            Assert.That(first.Items.Select(item => item.Id), Is.EqualTo(second.Items.Select(item => item.Id)));
            Assert.That(first.Start, Is.EqualTo(8));
            Assert.That(first.Apply(line, 1), Is.EqualTo("ze-echo \"two words\""));
            Assert.Throws<InvalidOperationException>(() => first.Apply("ze-echo x", 0));
            var middle = await session.CompleteAsync("ze-echo trailed tail", 9, Editor);
            Assert.That(middle.Apply("ze-echo trailed tail", 0), Is.EqualTo("ze-echo target tail"));
        }

        [Test]
        public async Task Completion_MatchesDisplayNameAndInsertsCanonicalResourceValue()
        {
            using var registry = new ConsoleRegistry();
            using var module = registry.Register("test", new[] { new ConsoleBinding(
                Command(parameters: new[] { new ConsoleParameter("asset", typeof(string)) }), Ok,
                (context, token) => Task.FromResult<IReadOnlyList<ConsoleCompletionItem>>(new[] {
                    new ConsoleCompletionItem("asset:stable", "Assets/Food/Apple Pie.asset", "Apple Pie") })) });
            using var session = new ConsoleSession(registry);
            const string line = "ze-echo app";
            var completions = await session.CompleteAsync(line, line.Length, Editor);
            Assert.That(completions.Items.Count, Is.EqualTo(1));
            Assert.That(completions.Items[0].Id, Is.EqualTo("asset:stable"));
            Assert.That(completions.Apply(line, 0), Is.EqualTo("ze-echo \"Assets/Food/Apple Pie.asset\""));
        }

        [Test]
        public async Task CommonCommands_UsePolicyAndClearWithoutAnEmptyOutputRow()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry);
            using var common = ConsoleCommonCommands.Register(session);
            Assert.That((await session.ExecuteLineAsync("ze-help", Editor)).IsSuccess, Is.True);
            Assert.That(session.GetOutput(), Is.Not.Empty);
            Assert.That((await session.ExecuteLineAsync("ze-clear", Editor)).IsSuccess, Is.True);
            Assert.That(session.GetOutput(), Is.Empty);
            Assert.That((await session.ExecuteLineAsync("ze-session", new ConsoleCaller(ConsoleEnvironment.Player))).IsSuccess, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyOutcome_IsWarningWithoutInventingBusinessSuccess(bool started)
        {
            using var registry = new ConsoleRegistry();
            int calls = 0;
            using var module = registry.Register("test", new[] { new ConsoleBinding(Command(), (context, args, token) =>
            {
                calls++;
                return Task.FromResult(started ? ConsoleResult.Started(data: "legacy") : ConsoleResult.Unconfirmed(data: "legacy"));
            }) });
            using var session = new ConsoleSession(registry);
            ConsoleResult result = await session.ExecuteLineAsync("ze-echo", Editor);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Status, Is.EqualTo(started ? ConsoleResultStatus.Started : ConsoleResultStatus.Unconfirmed));
            Assert.That(result.Data, Is.EqualTo("legacy"));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(session.GetOutput(), Has.Length.EqualTo(1));
            Assert.That(session.GetOutput()[0].Level, Is.EqualTo(ConsoleOutputLevel.Warning));
        }

        [Test]
        public async Task Completion_UnclaimedParameterUsesNullableEnumAndNullBinding()
        {
            using var registry = new ConsoleRegistry();
            var command = Command(parameters: new[] { new ConsoleParameter("day", typeof(DayOfWeek?), optional: true) });
            bool suppliesEmpty = false;
            using var module = registry.Register("test", new[] { new ConsoleBinding(command, (context, args, token) =>
            {
                Assert.That(args.Get<DayOfWeek?>(0), Is.Null);
                return Task.FromResult(ConsoleResult.Success());
            }, (context, token) => Task.FromResult<IReadOnlyList<ConsoleCompletionItem>>(
                suppliesEmpty ? Array.Empty<ConsoleCompletionItem>() : null)) });
            using var session = new ConsoleSession(registry);
            var completion = await session.CompleteAsync("ze-echo ", 8, Editor);
            Assert.That(completion.Items.Select(item => item.Value), Does.Contain("Monday").And.Contain("null"));
            Assert.That(completion.Items.Count, Is.EqualTo(8));
            suppliesEmpty = true;
            Assert.That((await session.CompleteAsync("ze-echo ", 8, Editor)).Items, Is.Empty);
            Assert.That((await session.ExecuteLineAsync("ze-echo null", Editor)).IsSuccess, Is.True);
            Assert.That((await session.ExecuteLineAsync("ze-echo not-a-day", Editor)).Code, Is.EqualTo("invalid-argument"));
        }

        [Test]
        public async Task ModuleDisposal_CancelsOwnedInvocationAndRemovesDiscovery()
        {
            using var registry = new ConsoleRegistry();
            var entered = new TaskCompletionSource<bool>();
            var module = registry.Register("test", new[] { new ConsoleBinding(Command(), async (context, args, token) => {
                entered.SetResult(true); await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return ConsoleResult.Success();
            }) });
            using var session = new ConsoleSession(registry);
            Task<ConsoleResult> execution = session.ExecuteLineAsync("ze-echo", Editor);
            await entered.Task;
            module.Dispose();
            Assert.That((await execution.ConfigureAwait(false)).Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
            Assert.That(session.GetCommands(Editor), Is.Empty);
            Assert.That((await session.ExecuteLineAsync("ze-echo", Editor)).Code, Is.EqualTo("unknown-command"));
        }

        [Test]
        public async Task Cancellation_DoesNotReleaseBusyLeaseOrReplayUncooperativeHandler()
        {
            using var registry = new ConsoleRegistry();
            var finish = new TaskCompletionSource<ConsoleResult>();
            int calls = 0;
            using var module = registry.Register("test", new[] { new ConsoleBinding(Command(), (context, args, token) => { calls++; return finish.Task; }) });
            using var session = new ConsoleSession(registry);
            Task<ConsoleResult> execution = session.ExecuteLineAsync("ze-echo", Editor);
            session.CancelCurrent();
            Assert.That(execution.IsCompleted, Is.False);
            Assert.That((await session.ExecuteLineAsync("ze-echo", Editor)).Code, Is.EqualTo("busy"));
            finish.SetResult(ConsoleResult.Success());
            Assert.That((await execution.ConfigureAwait(false)).Status, Is.EqualTo(ConsoleResultStatus.Cancelled));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task Deadline_CancelsWithoutReclassifyingAResultAsSuccess()
        {
            using var registry = new ConsoleRegistry();
            using var module = registry.Register("test", new[] { new ConsoleBinding(Command(), async (context, args, token) => {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return ConsoleResult.Success();
            }) });
            using var session = new ConsoleSession(registry);
            Assert.That((await session.ExecuteLineAsync("ze-echo", Editor, timeout: TimeSpan.FromMilliseconds(25)).ConfigureAwait(false)).Status, Is.EqualTo(ConsoleResultStatus.TimedOut));
        }

        [Test]
        public async Task OutputAndHistory_HaveFiniteCapacityAndDoNotSplitSurrogates()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry, outputCapacity: 2, historyCapacity: 2, messageLimit: 16);
            session.Write(new ConsoleOutput("x", ConsoleOutputLevel.Info, new string('x', 15) + "😀tail"));
            string truncated = session.GetOutput()[0].Message;
            Assert.That(char.IsHighSurrogate(truncated[truncated.Length - 2]), Is.False);
            await session.ExecuteLineAsync("missing1", Editor); await session.ExecuteLineAsync("missing2", Editor); await session.ExecuteLineAsync("missing3", Editor);
            Assert.That(session.GetOutput(), Has.Length.EqualTo(2));
            Assert.That(session.GetHistory(), Is.EqualTo(new[] { "missing2", "missing3" }));
        }

        [Test]
        public void OutputSnapshot_IdentifiesClearEvenWhenOutputCountStaysTheSame()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry, outputCapacity: 2);
            session.Write(new ConsoleOutput("one", ConsoleOutputLevel.Info, "before"));
            var before = session.GetOutputSnapshot();
            session.ClearOutput();
            session.Write(new ConsoleOutput("two", ConsoleOutputLevel.Info, "after"));
            var after = session.GetOutputSnapshot();
            Assert.That(before.Entries[0].Message, Is.EqualTo("before"));
            Assert.That(after.Entries[0].Message, Is.EqualTo("after"));
            Assert.That(after.ClearRevision, Is.GreaterThan(before.Revision));
            Assert.That(after.Revision, Is.GreaterThan(after.ClearRevision));
        }

        private static Task<ConsoleResult> ConsoleResultTask(object data) => Task.FromResult(ConsoleResult.Success(data: data));
    }
}
