using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceRuntimeTests
    {
        [SetUp]
        public void SetUp()
        {
            TestEffect.ExecuteCount = 0;
            TestEffect.LastSource = null;
            TestEffect.LastTarget = null;
            ManualTrigger.LastInstance = null;
        }

        [Test]
        public void Install_WithOnInstallTrigger_ExecutesEffect()
        {
            var actor = new TestActor();
            var graph = new TceGraph();
            graph.AddTrigger(new OnInstallTriggerData());
            graph.AddEffect(new TestEffectData());

            var runtime = new TceRuntime();
            runtime.Install("install-source", actor, graph);

            Assert.AreEqual(1, TestEffect.ExecuteCount);
            Assert.AreSame(actor, TestEffect.LastTarget);
            Assert.AreEqual("install-source", TestEffect.LastSource);
        }

        [Test]
        public void Trigger_WhenConditionFails_DoesNotExecuteEffect()
        {
            var actor = new TestActor();
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new GateConditionData { Allowed = false });
            graph.AddEffect(new TestEffectData());

            var runtime = new TceRuntime();
            runtime.Install(null, actor, graph);

            ManualTrigger.LastInstance.Fire(actor, "blocked");

            Assert.AreEqual(0, TestEffect.ExecuteCount);
        }

        [Test]
        public void Uninstall_RemovesTriggerSubscription()
        {
            var actor = new TestActor();
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddEffect(new TestEffectData());

            var runtime = new TceRuntime();
            runtime.Install(null, actor, graph);
            ManualTrigger trigger = ManualTrigger.LastInstance;

            runtime.Uninstall();
            trigger.Fire(actor, null);

            Assert.AreEqual(0, TestEffect.ExecuteCount);
        }

        private sealed class TestActor : ITceActor
        {
            public bool IsAlive { get; set; } = true;
            public float DomainTime { get; set; }
            public object NativeObject => this;
        }

        private sealed class ManualTriggerData : TceTriggerData<ManualTrigger>
        {
        }

        private sealed class ManualTrigger : TceTrigger<ManualTriggerData>
        {
            public static ManualTrigger LastInstance;

            public override void OnInstall()
            {
                LastInstance = this;
            }

            public void Fire(ITceActor target, object source)
            {
                Trigger(target, source);
            }
        }

        private sealed class GateConditionData : TceConditionData<GateCondition>
        {
            public bool Allowed = true;
        }

        private sealed class GateCondition : TceCondition<GateConditionData>
        {
            public override bool Check(ITceActor target, object source)
            {
                return Data.Allowed;
            }
        }

        private sealed class TestEffectData : TceEffectData<TestEffect>
        {
        }

        private sealed class TestEffect : TceEffect<TestEffectData>
        {
            public static int ExecuteCount;
            public static ITceActor LastTarget;
            public static object LastSource;

            public override void Execute(ITceActor target, object source)
            {
                ExecuteCount++;
                LastTarget = ResolveTarget(target, source);
                LastSource = source;
            }
        }
    }
}
