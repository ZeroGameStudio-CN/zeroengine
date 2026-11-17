using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleMacroCompletionTests
    {
        private static readonly ConsoleCaller Editor = new ConsoleCaller(ConsoleEnvironment.Editor);
        private enum Mode { Alpha, Beta }
        private static ConsoleBinding Binding(ConsoleCompleter completer = null, string[] capabilities = null) => new ConsoleBinding(
            new ConsoleCommand("configure", "configure", "macros", "Configure safely.", new[] {
                new ConsoleParameter("count", typeof(int)), new ConsoleParameter("enabled", typeof(bool)), new ConsoleParameter("mode", typeof(Mode)) },
                capabilities: capabilities), (context, arguments, token) => throw new AssertionException("Completion invoked a handler."), completer);

        [Test]
        public async Task ExpandedCommandPrefix_UsesActualParameterHintsAndOriginalReplacementSpans()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry) { Macros = new ConsoleMacros() };
            using var lease = registry.Register("macros", new[] { Binding() });
            session.Macros.Define("preset", "configure 3");
            string line = "#preset tr Alpha";
            int caret = line.IndexOf(" Alpha", StringComparison.Ordinal);
            var result = await session.CompleteAsync(line, caret, Editor);
            Assert.That(result.Hint.Parameter.Name, Is.EqualTo("enabled"));
            Assert.That(result.Items.Select(item => item.Value), Is.EqualTo(new[] { "true" }));
            Assert.That(result.Apply(line, 0, out int insertedCaret), Is.EqualTo("#preset true Alpha"));
            Assert.That(insertedCaret, Is.EqualTo("#preset true".Length));
            Assert.That(session.GetHint(line, caret, Editor).Parameter.Name, Is.EqualTo("enabled"));
            Assert.That(session.GetHistory(), Is.Empty);
        }

        [Test]
        public async Task NestedArgumentAndMultipleTokenMacros_KeepTheCorrectParameterIndex()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry) { Macros = new ConsoleMacros() };
            using var lease = registry.Register("macros", new[] { Binding() });
            session.Macros.Define("count", "3");
            session.Macros.Define("preset", "configure #count true");
            var result = await session.CompleteAsync("#preset Al", 10, Editor);
            Assert.That(result.Hint.ParameterIndex, Is.EqualTo(2));
            Assert.That(result.Apply("#preset Al", 0), Is.EqualTo("#preset Alpha"));
            result = await session.CompleteAsync("configure #count tr", 19, Editor);
            Assert.That(result.Hint.Parameter.Name, Is.EqualTo("enabled"));
            Assert.That(result.Apply("configure #count tr", 0), Is.EqualTo("configure #count true"));
        }

        [Test]
        public async Task MacroNameCompletion_ReplacesOnlyItsNameAndPreservesQuotesAndTail()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry) { Macros = new ConsoleMacros() };
            using var lease = registry.Register("macros", new[] { Binding() });
            session.Macros.Define("value", "3");
            string line = "configure #vaZZ true Alpha";
            var result = await session.CompleteAsync(line, "configure #va".Length, Editor);
            Assert.That(result.Items.Single().Value, Is.EqualTo("#value"));
            Assert.That(result.Apply(line, 0), Is.EqualTo("configure #value true Alpha"));
            line = "configure \"#vaZZ\" true Alpha";
            result = await session.CompleteAsync(line, "configure \"#va".Length, Editor);
            Assert.That(result.Apply(line, 0), Is.EqualTo("configure \"#value\" true Alpha"));
        }

        [Test]
        public async Task DeniedCommandsAndCycles_DoNotExposeNamesOrHints()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry) { Macros = new ConsoleMacros() };
            using var lease = registry.Register("macros", new[] { Binding(capabilities: new[] { "private" }) });
            session.Macros.Define("private", "configure 3");
            session.Macros.Define("loop", "#loop");
            Assert.That((await session.CompleteAsync("#pr", 3, Editor)).Items, Is.Empty);
            Assert.That((await session.CompleteAsync("#private tr", 11, Editor)).Hint, Is.Null);
            Assert.That((await session.CompleteAsync("#loop tr", 8, Editor)).Items, Is.Empty);
            Assert.That(session.GetHint("#loop tr", 8, Editor), Is.Null);
        }

        [Test]
        public async Task ChangingMacrosDuringAnAsyncProvider_DiscardsItsStaleProjection()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry) { Macros = new ConsoleMacros() };
            var entered = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            using var lease = registry.Register("macros", new[] { Binding(async (context, token) => {
                Assert.That(context.Line, Is.EqualTo("configure 3 tr"));
                entered.SetResult(true); await release.Task;
                return new[] { new ConsoleCompletionItem("true", "true") };
            }) });
            session.Macros.Define("preset", "configure 3");
            var pending = session.CompleteAsync("#preset tr", 10, Editor);
            await entered.Task;
            session.Macros.Define("preset", "configure 4");
            release.SetResult(true);
            var result = await pending;
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.Hint, Is.Null);
        }

        [Test]
        public async Task NameCache_TracksDefinitionsAndEscapedNamesAreNotCompleted()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry) { Macros = new ConsoleMacros() };
            using var lease = registry.Register("macros", new[] { Binding() });
            session.Macros.Define("first", "configure 3");
            Assert.That((await session.CompleteAsync("#f", 2, Editor)).Items.Single().Value, Is.EqualTo("#first"));
            session.Macros.Remove("first"); session.Macros.Define("fresh", "configure 4");
            Assert.That((await session.CompleteAsync("#f", 2, Editor)).Items.Single().Value, Is.EqualTo("#fresh"));
            Assert.That((await session.CompleteAsync(@"configure \#f", 13, Editor)).Items, Is.Empty);
        }

        [Test]
        public async Task DefiningAPreviouslyUnknownMacro_AlsoInvalidatesAnAsyncCompletion()
        {
            using var registry = new ConsoleRegistry();
            using var session = new ConsoleSession(registry) { Macros = new ConsoleMacros() };
            var entered = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            using var lease = registry.Register("macros", new[] { Binding(async (context, token) => {
                entered.SetResult(true); await release.Task;
                return new[] { new ConsoleCompletionItem("true", "true") };
            }) });
            string line = "configure #late tr";
            var pending = session.CompleteAsync(line, line.Length, Editor);
            await entered.Task;
            session.Macros.Define("late", "3 true");
            release.SetResult(true);
            Assert.That((await pending).Items, Is.Empty);
        }

        [Test]
        public void DefinitionLimit_AllowsUpdatesAndSlotReuse()
        {
            var macros = new ConsoleMacros();
            for (int i = 0; i < ConsoleMacros.MaximumDefinitions; i++) macros.Define("name" + i, "value");
            Assert.Throws<InvalidOperationException>(() => macros.Define("overflow", "value"));
            macros.Define("name0", "updated");
            Assert.That(macros.Remove("name1"), Is.True);
            macros.Define("replacement", "new");
            Assert.That(macros.Snapshot(), Has.Count.EqualTo(ConsoleMacros.MaximumDefinitions));
        }
    }
}
