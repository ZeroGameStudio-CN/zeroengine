using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    public sealed class ExecutionCountStateTests
    {
        [Test]
        public void ReinstallingOrChangingTheComponentDoesNotResetHostOwnedCount()
        {
            var host = new Host();
            var graph = Graph("one");
            var runtime = new TceRuntime();
            Effect.Count = 0;
            runtime.Install(host, host, graph);
            runtime.Install(host, host, graph);
            Assert.That(Effect.Count, Is.EqualTo(1));
            Assert.That(host.State.ReadNumber("one"), Is.EqualTo(1));
            runtime.Uninstall();
        }

        [Test]
        public void FailedLaterConditionDoesNotConsumeTheHostState()
        {
            var host = new Host();
            var graph = Graph("one");
            graph.AddCondition(new GateData());
            var runtime = new TceRuntime();
            runtime.Install(host, host, graph);
            Assert.That(host.State.ReadNumber("one"), Is.Zero);
            runtime.Uninstall();
        }

        [Test]
        public void SeparateKeysAndOwnersDoNotShareCounts()
        {
            var one = new Host();
            var two = new Host();
            var runtime = new TceRuntime();
            runtime.Install(one, one, Graph("first"));
            runtime.Install(one, one, Graph("second"));
            runtime.Install(two, two, Graph("first"));
            Assert.That(one.State.ReadNumber("first"), Is.EqualTo(1));
            Assert.That(one.State.ReadNumber("second"), Is.EqualTo(1));
            Assert.That(two.State.ReadNumber("first"), Is.EqualTo(1));
            runtime.Uninstall();
        }

        [Test]
        public void AConfiguredStateKeyCannotSilentlyFallBackToAnEphemeralCounter()
        {
            var runtime = new TceRuntime();
            Assert.Throws<InvalidOperationException>(() => runtime.Install(null, new Actor(), Graph("one")));
            runtime.Uninstall();
        }

        [Test]
        public void EmptyKeyRetainsExistingInstallationLocalBehavior()
        {
            var host = new Host();
            var runtime = new TceRuntime();
            Effect.Count = 0;
            runtime.Install(null, host, Graph(""));
            runtime.Install(null, host, Graph(""));
            Assert.That(Effect.Count, Is.EqualTo(2));
            Assert.That(host.Numbers, Is.Empty);
            runtime.Uninstall();
        }

        private static TceGraph Graph(string key)
        {
            var graph = new TceGraph();
            graph.AddTrigger(new OnInstallTriggerData());
            graph.AddCondition(new ExecutionCountConditionData { StateKey = key });
            graph.AddEffect(new EffectData());
            return graph;
        }

        private class Actor : ITceActor
        {
            public bool IsAlive => true;
            public float DomainTime => 0;
            public object NativeObject => this;
        }

        private sealed class Host : Actor, ITceStateSource, ITceStateStore
        {
            public readonly Dictionary<string, long> Numbers = new();
            public ITceStateStore State => this;
            public long ReadNumber(string key, long fallback = 0) => Numbers.TryGetValue(key, out var value) ? value : fallback;
            public string ReadText(string key, string fallback = null) => fallback;
            public void WriteNumber(string key, long value) => Numbers[key] = value;
            public void WriteText(string key, string value) { }
        }

        private sealed class GateData : TceConditionData<Gate> { }
        private sealed class Gate : TceCondition<GateData>
        {
            public override bool Check(ITceActor target, object source) => false;
        }
        private sealed class EffectData : TceEffectData<Effect> { }
        private sealed class Effect : TceEffect<EffectData>
        {
            public static int Count;
            public override void Execute(ITceActor target, object source) => Count++;
        }
    }
}
