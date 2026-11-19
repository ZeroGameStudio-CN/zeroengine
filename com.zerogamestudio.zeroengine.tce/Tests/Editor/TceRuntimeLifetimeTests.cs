using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceRuntimeLifetimeTests
    {
        [TestCase("condition")]
        [TestCase("first.accepted")]
        [TestCase("first.effect")]
        [TestCase("first.observed")]
        public void UninstallInsideDispatch_StopsRemainingCallbacks(string stopAt)
        {
            var trace = new List<string>();
            var runtime = new TceRuntime();
            var owner = new Actor();
            Action<TceComponentContext, string> callback = (context, stage) =>
            {
                trace.Add(stage);
                if (stage == stopAt) context.Runtime.Uninstall();
            };
            var graph = MakeGraph(callback);
            runtime.Executed += (_, _) => trace.Add("completed");
            runtime.Install(null, owner, graph);
            var trigger = ManualTrigger.Last;
            try
            {
                Assert.DoesNotThrow(() => trigger.Fire(owner));
                var normal = new[] { "condition", "first.accepted", "tail.accepted", "first.effect",
                    "tail.effect", "first.observed", "tail.observed", "completed" };
                CollectionAssert.AreEqual(normal.Take(Array.IndexOf(normal, stopAt) + 1), trace);
                var count = trace.Count;
                trigger.Fire(owner);
                Assert.That(trace.Count, Is.EqualTo(count), "The removed trigger must be detached.");
            }
            finally { runtime.Uninstall(); }
        }

        [Test]
        public void ReplaceInsideEffect_OldDispatchAndCapturedTriggerCannotRunNewGraph()
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            var trace = new List<string>();
            var replacement = MakeGraph((_, stage) => trace.Add("new." + stage));
            var original = MakeGraph((context, stage) =>
            {
                trace.Add("old." + stage);
                if (stage == "first.effect") context.Runtime.Install("replacement", owner, replacement);
            });
            runtime.Executed += (_, _) => trace.Add("completed");
            runtime.Install(null, owner, original);
            var old = ManualTrigger.Last;
            var captured = old.Capture();
            try
            {
                Assert.DoesNotThrow(() => old.Fire(owner));
                Assert.That(trace, Does.Not.Contain("old.tail.effect"));
                Assert.That(trace, Does.Not.Contain("completed"));
                int before = trace.Count;
                captured(owner, null);
                Assert.That(trace.Count, Is.EqualTo(before), "A captured old installation callback must not target the new graph.");
                ManualTrigger.Last.Fire(owner);
                Assert.That(trace, Does.Contain("new.tail.effect"));
                Assert.That(trace.Count(x => x == "completed"), Is.EqualTo(1));
            }
            finally { runtime.Uninstall(); }
        }

        [Test]
        public void ReentrantTrigger_WithoutLifetimeChange_PreservesBothExecutions()
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            int effects = 0, completed = 0;
            bool nested = false;
            var graph = MakeGraph((_, stage) =>
            {
                if (stage == "tail.effect") effects++;
                if (stage == "first.effect" && !nested)
                {
                    nested = true;
                    ManualTrigger.Last.Fire(owner);
                }
            });
            runtime.Executed += (_, _) => completed++;
            runtime.Install(null, owner, graph);
            try
            {
                ManualTrigger.Last.Fire(owner);
                Assert.That(effects, Is.EqualTo(2));
                Assert.That(completed, Is.EqualTo(2));
            }
            finally { runtime.Uninstall(); }
        }

        [TestCase("before")]
        [TestCase("install")]
        public void UninstallDuringStartup_DoesNotContinueAbandonedInstallation(string stopAt)
        {
            var runtime = new TceRuntime();
            var trace = new List<string>();
            int released = 0;
            var graph = new TceGraph();
            graph.AddEffect(new CallbackEffectData
            {
                Id = "first",
                Before = context => { trace.Add("before-first"); if (stopAt == "before") context.Runtime.Uninstall(); },
                Install = context => { trace.Add("install-first"); if (stopAt == "install") context.Runtime.Uninstall(); },
                Release = _ => released++
            });
            graph.AddEffect(new CallbackEffectData
            {
                Id = "tail", Before = _ => trace.Add("before-tail"),
                Install = _ => trace.Add("install-tail"), Release = _ => released++
            });
            try
            {
                Assert.DoesNotThrow(() => runtime.Install(null, new Actor(), graph));
                CollectionAssert.AreEqual(stopAt == "before" ? new[] { "before-first" }
                    : new[] { "before-first", "before-tail", "install-first" }, trace);
                Assert.That(released, Is.EqualTo(2));
            }
            finally { runtime.Uninstall(); }
        }

        [Test]
        public void UninstallHook_ReentrantUninstall_ReleasesEachComponentOnlyOnce()
        {
            var runtime = new TceRuntime();
            int first = 0, tail = 0;
            bool entered = false;
            var graph = new TceGraph();
            graph.AddEffect(new CallbackEffectData { Release = context =>
            {
                first++;
                if (!entered) { entered = true; context.Runtime.Uninstall(); }
            }});
            graph.AddEffect(new CallbackEffectData { Release = _ => tail++ });
            runtime.Install(null, new Actor(), graph);
            runtime.Uninstall();
            runtime.Uninstall();
            Assert.That(first, Is.EqualTo(1));
            Assert.That(tail, Is.EqualTo(1));
        }

        [Test]
        public void UninstallHook_InstallsReplacement_OldCleanupPreservesNewOwner()
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            int count = 0;
            bool replaced = false;
            var next = MakeGraph((_, stage) => { if (stage == "tail.effect") count++; });
            var old = new TceGraph();
            old.AddEffect(new CallbackEffectData { Release = context =>
            {
                if (!replaced) { replaced = true; context.Runtime.Install(null, owner, next); }
            }});
            runtime.Install(null, owner, old);
            runtime.Uninstall();
            try
            {
                ManualTrigger.Last.Fire(owner);
                Assert.That(count, Is.EqualTo(1));
            }
            finally { runtime.Uninstall(); }
        }

        [Test]
        public void FailedStartup_DetachesTriggersAndReleasesPartialComponents()
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            int effect = 0, released = 0;
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddEffect(new CallbackEffectData
            {
                Callback = (_, stage) => { if (stage.EndsWith("effect", StringComparison.Ordinal)) effect++; },
                Install = _ => throw new InvalidOperationException("expected startup failure"),
                Release = _ => released++
            });
            Assert.Throws<InvalidOperationException>(() => runtime.Install(null, owner, graph));
            try
            {
                ManualTrigger.Last.Fire(owner);
                Assert.That(effect, Is.Zero);
                Assert.That(released, Is.EqualTo(1));
            }
            finally { runtime.Uninstall(); }
        }

        [Test]
        public void TeardownFailure_StillReleasesOtherComponentsAndLeavesRuntimeReusable()
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            int released = 0, effects = 0;
            var graph = new TceGraph();
            graph.AddEffect(new CallbackEffectData { Release = _ => released++ });
            graph.AddEffect(new CallbackEffectData { Release = _ => throw new InvalidOperationException("expected teardown failure") });
            runtime.Install(null, owner, graph);
            Assert.Throws<InvalidOperationException>(runtime.Uninstall);
            Assert.That(released, Is.EqualTo(1));
            runtime.Install(null, owner, MakeGraph((_, stage) => { if (stage == "tail.effect") effects++; }));
            try { ManualTrigger.Last.Fire(owner); Assert.That(effects, Is.EqualTo(1)); }
            finally { runtime.Uninstall(); }
        }

        [Test]
        public void NewerInstallFromOldTeardown_WinsOverThePendingReplacement()
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            int latest = 0, abandoned = 0;
            var newest = MakeGraph((_, stage) => { if (stage == "tail.effect") latest++; });
            var old = new TceGraph();
            old.AddEffect(new CallbackEffectData { Release = context => context.Runtime.Install(null, owner, newest) });
            runtime.Install(null, owner, old);
            runtime.Install(null, owner, MakeGraph((_, stage) => { if (stage == "tail.effect") abandoned++; }));
            try
            {
                ManualTrigger.Last.Fire(owner);
                Assert.That(latest, Is.EqualTo(1));
                Assert.That(abandoned, Is.Zero);
            }
            finally { runtime.Uninstall(); }
        }

        [Test]
        public void StartupReplacesThenThrows_DoesNotTearDownTheNewInstallation()
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            int latest = 0;
            var next = MakeGraph((_, stage) => { if (stage == "tail.effect") latest++; });
            var old = new TceGraph();
            old.AddEffect(new CallbackEffectData { Before = context =>
            {
                context.Runtime.Install(null, owner, next);
                throw new InvalidOperationException("expected abandoned startup error");
            }});
            Assert.Throws<InvalidOperationException>(() => runtime.Install(null, owner, old));
            try { ManualTrigger.Last.Fire(owner); Assert.That(latest, Is.EqualTo(1)); }
            finally { runtime.Uninstall(); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InvalidInstallArguments_PreserveTheExistingInstallation(bool missingOwner)
        {
            var runtime = new TceRuntime();
            var owner = new Actor();
            int count = 0;
            var graph = MakeGraph((_, stage) => { if (stage == "tail.effect") count++; });
            runtime.Install(null, owner, graph);
            try
            {
                Assert.Throws<ArgumentNullException>(() => runtime.Install(null, missingOwner ? null : owner, missingOwner ? graph : null));
                ManualTrigger.Last.Fire(owner);
                Assert.That(count, Is.EqualTo(1));
            }
            finally { runtime.Uninstall(); }
        }

        private static TceGraph MakeGraph(Action<TceComponentContext, string> callback)
        {
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new CallbackConditionData { Callback = callback });
            graph.AddEffect(new CallbackEffectData { Id = "first", Callback = callback });
            graph.AddEffect(new CallbackEffectData { Id = "tail", Callback = callback });
            return graph;
        }

        public sealed class Actor : ITceActor
        {
            public bool IsAlive => true;
            public float DomainTime => 0f;
            public object NativeObject => this;
        }
        public sealed class ManualTriggerData : TceTriggerData<ManualTrigger> { }
        public sealed class ManualTrigger : TceComponent<ManualTriggerData>, ITceTrigger
        {
            public static ManualTrigger Last;
            public event Action<ITceActor, object> Triggered;
            public override void OnBeforeInstall() => Last = this;
            public Action<ITceActor, object> Capture() => Triggered;
            public void Fire(ITceActor actor) => Triggered?.Invoke(actor, null);
        }
        public sealed class CallbackConditionData : TceConditionData<CallbackCondition>
        {
            public Action<TceComponentContext, string> Callback;
        }
        public sealed class CallbackCondition : TceCondition<CallbackConditionData>
        {
            public override bool Check(ITceActor target, object source) { Data.Callback(Context, "condition"); return true; }
        }
        public sealed class CallbackEffectData : TceEffectData<CallbackEffect>
        {
            public string Id = "effect";
            public Action<TceComponentContext, string> Callback;
            public Action<TceComponentContext> Before, Install, Release;
        }
        public sealed class CallbackEffect : TceEffect<CallbackEffectData>, ITceExecutionAcceptedObserver, ITceExecutionObserver
        {
            public override void OnBeforeInstall() => Data.Before?.Invoke(Context);
            public override void OnInstall() => Data.Install?.Invoke(Context);
            public override void OnUninstall() => Data.Release?.Invoke(Context);
            public override void Execute(ITceActor target, object source) => Data.Callback?.Invoke(Context, Data.Id + ".effect");
            public void OnExecutionAccepted(ITceActor target, object source) => Data.Callback?.Invoke(Context, Data.Id + ".accepted");
            public void OnExecuted(ITceActor target, object source) => Data.Callback?.Invoke(Context, Data.Id + ".observed");
        }
    }
}
