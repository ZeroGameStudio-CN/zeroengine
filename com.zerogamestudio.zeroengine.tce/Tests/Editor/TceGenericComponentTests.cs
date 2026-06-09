using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGenericComponentTests
    {
        [SetUp]
        public void SetUp()
        {
            TceLog.Handler = _ => { };
            ManualTrigger.LastInstance = null;
            ReentrantEffect.ExecuteCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            TceLog.Handler = UnityEngine.Debug.Log;
        }

        [Test]
        public void OnInstall_WithNumericCondition_ExecutesDebugLog()
        {
            var actor = new TestActor();
            var graph = new TceGraph();
            graph.AddTrigger(new OnInstallTriggerData());
            graph.AddCondition(new NumericSourceConditionData
            {
                RequiredValue = 10f,
                Comparison = TceComparison.GreaterThanOrEqualTo
            });
            graph.AddEffect(new DebugLogEffectData { Message = "accepted" });

            string logged = null;
            TceLog.Handler = message => logged = message;

            new TceRuntime().Install(new NumericValueSource(12f), actor, graph);

            Assert.AreEqual("accepted", logged);
        }

        [Test]
        public void Cooldown_StartsOnlyAfterAllConditionsPass()
        {
            var actor = new TestActor();
            var clock = new ManualClock();
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new CooldownConditionData { Duration = 3f });
            graph.AddCondition(new NumericSourceConditionData
            {
                RequiredValue = 1f,
                Comparison = TceComparison.GreaterThanOrEqualTo
            });
            graph.AddEffect(new ReentrantEffectData());

            new TceRuntime().Install(null, actor, graph, clock);

            ManualTrigger.LastInstance.Fire(actor, new NumericValueSource(0f));
            ManualTrigger.LastInstance.Fire(actor, new NumericValueSource(2f));

            Assert.AreEqual(1, ReentrantEffect.ExecuteCount);
        }

        [Test]
        public void Cooldown_BlocksSynchronousReentryAfterConditionsPass()
        {
            var actor = new TestActor();
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new CooldownConditionData { Duration = 3f });
            graph.AddEffect(new ReentrantEffectData { Reenter = true });

            new TceRuntime().Install(null, actor, graph, new ManualClock());
            ManualTrigger.LastInstance.Fire(actor, null);

            Assert.AreEqual(1, ReentrantEffect.ExecuteCount);
        }

        private sealed class TestActor : ITceActor
        {
            public bool IsAlive => true;
            public float DomainTime => 0f;
            public object NativeObject => this;
        }

        private sealed class ManualClock : ITceClock
        {
            public float Now { get; set; }
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

        private sealed class ReentrantEffectData : TceEffectData<ReentrantEffect>
        {
            public bool Reenter;
        }

        private sealed class ReentrantEffect : TceEffect<ReentrantEffectData>
        {
            public static int ExecuteCount;

            public override void Execute(ITceActor target, object source)
            {
                ExecuteCount++;
                if (Data.Reenter && ExecuteCount == 1)
                    ManualTrigger.LastInstance.Fire(target, source);
            }
        }
    }
}
