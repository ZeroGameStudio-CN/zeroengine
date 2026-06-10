using NUnit.Framework;

namespace ZeroEngine.TCE.EditorTesting
{
    public static class TceAdapterContractAssertions
    {
        public static void AssertCoreAdapterContract(ITceAdapterContractFixture fixture)
        {
            Assert.NotNull(fixture, "Adapter contract fixture must not be null.");

            ITceActor aliveActor = fixture.CreateAliveActor();
            Assert.NotNull(aliveActor, "CreateAliveActor must return an actor.");
            Assert.IsTrue(aliveActor.IsAlive, "CreateAliveActor must return an alive actor.");

            ITceActor deadActor = fixture.CreateDeadActor();
            Assert.NotNull(deadActor, "CreateDeadActor must return an actor.");
            Assert.IsFalse(deadActor.IsAlive, "CreateDeadActor must return an actor with IsAlive == false.");

            ITceClock clock = fixture.CreateClock(0f);
            Assert.NotNull(clock, "CreateClock must return a clock.");
            Assert.AreEqual(0f, clock.Now, 0.0001f, "CreateClock must honor the initial time.");
            fixture.SetClockTime(clock, 12.5f);
            Assert.AreEqual(12.5f, clock.Now, 0.0001f, "SetClockTime must update the clock time.");

            AssertRuntimeExecutesForAliveActor(aliveActor, clock);
            AssertDeadActorDoesNotTrigger(deadActor, clock);
            AssertUninstallDetachesTrigger(aliveActor, clock);
        }

        private static void AssertRuntimeExecutesForAliveActor(ITceActor actor, ITceClock clock)
        {
            ContractCountingEffect.ExecuteCount = 0;
            ContractManualTrigger.LastInstance = null;

            var graph = new TceGraph();
            graph.AddTrigger(new ContractManualTriggerData());
            graph.AddEffect(new ContractCountingEffectData());

            var runtime = new TceRuntime();
            runtime.Install(null, actor, graph, clock);

            ContractManualTrigger.LastInstance.Fire(actor, null);

            Assert.AreEqual(1, ContractCountingEffect.ExecuteCount, "Alive actor should execute one effect.");
        }

        private static void AssertDeadActorDoesNotTrigger(ITceActor actor, ITceClock clock)
        {
            ContractCountingEffect.ExecuteCount = 0;
            ContractManualTrigger.LastInstance = null;

            var graph = new TceGraph();
            graph.AddTrigger(new ContractManualTriggerData());
            graph.AddEffect(new ContractCountingEffectData());

            var runtime = new TceRuntime();
            runtime.Install(null, actor, graph, clock);

            ContractManualTrigger.LastInstance.Fire(actor, null);

            Assert.AreEqual(0, ContractCountingEffect.ExecuteCount, "Dead actor should not execute effects.");
        }

        private static void AssertUninstallDetachesTrigger(ITceActor actor, ITceClock clock)
        {
            ContractCountingEffect.ExecuteCount = 0;
            ContractManualTrigger.LastInstance = null;

            var graph = new TceGraph();
            graph.AddTrigger(new ContractManualTriggerData());
            graph.AddEffect(new ContractCountingEffectData());

            var runtime = new TceRuntime();
            runtime.Install(null, actor, graph, clock);
            ContractManualTrigger trigger = ContractManualTrigger.LastInstance;

            runtime.Uninstall();
            trigger.Fire(actor, null);

            Assert.AreEqual(0, ContractCountingEffect.ExecuteCount, "Uninstall should detach trigger subscriptions.");
        }

        private sealed class ContractManualTriggerData : TceTriggerData<ContractManualTrigger>
        {
        }

        private sealed class ContractManualTrigger : TceTrigger<ContractManualTriggerData>
        {
            public static ContractManualTrigger LastInstance;

            public override void OnInstall()
            {
                LastInstance = this;
            }

            public void Fire(ITceActor target, object source)
            {
                Trigger(target, source);
            }
        }

        private sealed class ContractCountingEffectData : TceEffectData<ContractCountingEffect>
        {
        }

        private sealed class ContractCountingEffect : TceEffect<ContractCountingEffectData>
        {
            public static int ExecuteCount;

            public override void Execute(ITceActor target, object source)
            {
                ExecuteCount++;
            }
        }
    }
}
