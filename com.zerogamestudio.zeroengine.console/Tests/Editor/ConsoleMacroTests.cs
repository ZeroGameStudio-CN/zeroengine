using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleMacroTests
    {
        [Test]
        public void EmptyRegistry_PreservesTheOriginalInputInstance()
        {
            string line = "sample #unknown";
            Assert.That(new ConsoleMacros().TryExpand(line, out var expanded, out _), Is.True);
            Assert.That(expanded, Is.SameAs(line));
        }

        [Test]
        public void Names_AreExactAndNestedDefinitionsCanBeChangedOrRemoved()
        {
            var macros = new ConsoleMacros();
            macros.Define("item", "\"中文 空格\"");
            macros.Define("give", "sample-give #item");
            Assert.That(macros.TryExpand("#give 2", out var expanded, out _), Is.True);
            Assert.That(expanded, Is.EqualTo("sample-give \"中文 空格\" 2"));
            Assert.That(macros.TryExpand("#item-more", out expanded, out _), Is.True);
            Assert.That(expanded, Is.EqualTo("#item-more"));
            macros.Define("item", "changed");
            Assert.That(macros.TryExpand("#give", out expanded, out _), Is.True);
            Assert.That(expanded, Is.EqualTo("sample-give changed"));
            Assert.That(macros.Remove("item"), Is.True);
            Assert.That(macros.TryExpand("#give", out expanded, out _), Is.True);
            Assert.That(expanded, Is.EqualTo("sample-give #item"));
        }

        [Test]
        public void CyclesAndGrowth_AreRejectedWithoutPoisoningFutureExpansions()
        {
            var macros = new ConsoleMacros();
            macros.Define("a", "#b"); macros.Define("b", "#a");
            Assert.That(macros.TryExpand("#a", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("recursion"));
            macros.Define("a", "ok");
            Assert.That(macros.TryExpand("#b", out var expanded, out _), Is.True);
            Assert.That(expanded, Is.EqualTo("ok"));
            macros.Define("large", new string('x', ConsoleLexer.MaximumLineLength));
            Assert.That(macros.TryExpand("#large #large", out _, out error), Is.False);
            Assert.That(error, Does.Contain("limit"));
        }

        [Test]
        public void RegistryAndSnapshots_RemainOwnerLocal()
        {
            var first = new ConsoleMacros(); var second = new ConsoleMacros();
            first.Define("one", "1");
            var snapshot = first.Snapshot(); first.Define("one", "2");
            Assert.That(snapshot["one"], Is.EqualTo("1"));
            Assert.That(second.TryExpand("#one", out var expanded, out _), Is.True);
            Assert.That(expanded, Is.EqualTo("#one"));
            first.Clear();
            Assert.That(first.Snapshot(), Is.Empty);
        }

        [TestCase("")]
        [TestCase("with space")]
        [TestCase("#name")]
        public void InvalidNames_AreRejected(string name)
            => Assert.Throws<ArgumentException>(() => new ConsoleMacros().Define(name, "value"));

        [Test]
        public void MultiLineReplacement_IsRejected()
            => Assert.Throws<ArgumentException>(() => new ConsoleMacros().Define("name", "one\ntwo"));

        [Test]
        public async Task Session_ExpandsBeforePolicyAndPreservesRawHistoryWithoutDoubleExecution()
        {
            using (var registry = new ConsoleRegistry())
            using (var session = new ConsoleSession(registry))
            {
                int calls = 0;
                using (registry.Register("sample", new[] { new ConsoleBinding(
                    new ConsoleCommand("sample.read", "sample-read", "sample", "Read a value.",
                        new[] { new ConsoleParameter("value", typeof(string)) }, capabilities: new[] { "read" }),
                    (context, arguments, token) => { calls++; return Task.FromResult(ConsoleResult.Success(data: arguments.Get<string>(0))); }) }))
                {
                    session.Macros = new ConsoleMacros();
                    session.Macros.Define("read", "sample-read \"中文 value\"");
                    var denied = await session.ExecuteLineAsync("#read", new ConsoleCaller(ConsoleEnvironment.Editor));
                    Assert.That(denied.Code, Is.EqualTo("capability-denied"));
                    Assert.That(calls, Is.Zero);
                    var result = await session.ExecuteLineAsync("#read", new ConsoleCaller(ConsoleEnvironment.Editor, capabilities: new[] { "read" }));
                    Assert.That(result.Data, Is.EqualTo("中文 value"));
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(session.GetHistory(), Is.EqualTo(new[] { "#read", "#read" }));
                    session.Macros.Define("loop", "#loop");
                    var invalid = await session.ExecuteLineAsync("#loop", new ConsoleCaller(ConsoleEnvironment.Editor));
                    Assert.That(invalid.Code, Is.EqualTo("macro-expansion-failed"));
                    Assert.That(calls, Is.EqualTo(1));
                }
            }
        }
    }
}
