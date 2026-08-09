using System.Collections.Generic;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceComponentWhitelistTests
    {
        [SetUp]
        public void SetUp()
        {
            ManualTrigger.LastInstance = null;
            CountingEffect.ExecuteCount = 0;
        }

        [Test]
        public void ExecutionCountCondition_AllowsOnlyConfiguredAcceptedExecutions()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new ExecutionCountConditionData { MaxAcceptedExecutions = 2 });
            graph.AddEffect(new CountingEffectData());

            new TceRuntime().Install(null, new TestActor(), graph);

            ManualTrigger.LastInstance.Fire(new TestActor(), null);
            ManualTrigger.LastInstance.Fire(new TestActor(), null);
            ManualTrigger.LastInstance.Fire(new TestActor(), null);

            Assert.AreEqual(2, CountingEffect.ExecuteCount);
        }

        [Test]
        public void FlagCondition_ChecksTriggerSourceFlag()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new FlagConditionData
            {
                FlagId = "burning",
                LookupTarget = TceFlagLookupTarget.Source
            });
            graph.AddEffect(new CountingEffectData());

            new TceRuntime().Install(null, new TestActor(), graph);

            ManualTrigger.LastInstance.Fire(new TestActor(), new FlagSource("burning"));
            ManualTrigger.LastInstance.Fire(new TestActor(), new FlagSource("frozen"));

            Assert.AreEqual(1, CountingEffect.ExecuteCount);
        }

        [Test]
        public void FlagCondition_InvertStillRequiresResolvedFlagSource()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new FlagConditionData
            {
                FlagId = "burning",
                LookupTarget = TceFlagLookupTarget.Source,
                Invert = true
            });
            graph.AddEffect(new CountingEffectData());

            new TceRuntime().Install(null, new TestActor(), graph);

            ManualTrigger.LastInstance.Fire(new TestActor(), null);
            ManualTrigger.LastInstance.Fire(new TestActor(), new FlagSource("frozen"));

            Assert.AreEqual(1, CountingEffect.ExecuteCount);
        }

        [Test]
        public void ChanceCondition_UsesTriggerSourceRandomSource()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new ManualTriggerData());
            graph.AddCondition(new ChanceConditionData
            {
                Chance = 0.5f,
                LookupTarget = TceRandomLookupTarget.TriggerSource
            });
            graph.AddEffect(new CountingEffectData());

            new TceRuntime().Install(null, new TestActor(), graph);

            ManualTrigger.LastInstance.Fire(new TestActor(), new RandomSource(0.25f));
            ManualTrigger.LastInstance.Fire(new TestActor(), new RandomSource(0.75f));

            Assert.AreEqual(1, CountingEffect.ExecuteCount);
        }

        [Test]
        public void ChanceCondition_HandlesZeroAndOneBoundaries()
        {
            var zeroGraph = new TceGraph();
            zeroGraph.AddTrigger(new ManualTriggerData());
            zeroGraph.AddCondition(new ChanceConditionData
            {
                Chance = 0f,
                LookupTarget = TceRandomLookupTarget.TriggerSource
            });
            zeroGraph.AddEffect(new CountingEffectData());

            new TceRuntime().Install(null, new TestActor(), zeroGraph);
            ManualTrigger.LastInstance.Fire(new TestActor(), new RandomSource(0f));

            var oneGraph = new TceGraph();
            oneGraph.AddTrigger(new ManualTriggerData());
            oneGraph.AddCondition(new ChanceConditionData
            {
                Chance = 1f,
                LookupTarget = TceRandomLookupTarget.TriggerSource
            });
            oneGraph.AddEffect(new CountingEffectData());

            new TceRuntime().Install(null, new TestActor(), oneGraph);
            ManualTrigger.LastInstance.Fire(new TestActor(), new RandomSource(1f));

            Assert.AreEqual(1, CountingEffect.ExecuteCount);
        }

        [Test]
        public void ComponentValidation_ReportsInvalidWhitelistFields()
        {
            AssertHasIssue(new ExecutionCountConditionData { MaxAcceptedExecutions = 0 }, "conditions[0].MaxAcceptedExecutions", TceValidationCodes.InvalidField);
            AssertHasIssue(new FlagConditionData { FlagId = string.Empty }, "conditions[0].FlagId", TceValidationCodes.InvalidField);
            AssertHasIssue(new FlagConditionData { FlagId = "x", LookupTarget = (TceFlagLookupTarget)999 }, "conditions[0].LookupTarget", TceValidationCodes.InvalidEnumValue);
            AssertHasIssue(new ChanceConditionData { Chance = -0.1f }, "conditions[0].Chance", TceValidationCodes.InvalidField);
            AssertHasIssue(new ChanceConditionData { Chance = 0.5f, LookupTarget = (TceRandomLookupTarget)999 }, "conditions[0].LookupTarget", TceValidationCodes.InvalidEnumValue);
            AssertHasIssue(new CooldownConditionData { Duration = -1f }, "conditions[0].Duration", TceValidationCodes.InvalidField);
            AssertHasIssue(new NumericSourceConditionData { Comparison = (TceComparison)999 }, "conditions[0].Comparison", TceValidationCodes.InvalidEnumValue);
        }

        private static void AssertHasIssue(TceConditionData condition, string path, string code)
        {
            var graph = new TceGraph();
            graph.AddTrigger(new OnInstallTriggerData());
            graph.AddCondition(condition);
            graph.AddEffect(new DebugLogEffectData());

            IReadOnlyList<TceValidationIssue> issues = TceGraphValidator.Validate(graph);

            foreach (TceValidationIssue issue in issues)
            {
                if (issue.Code == code && issue.Path == path && issue.Severity == TceValidationSeverity.Error)
                    return;
            }

            Assert.Fail($"Expected validation issue {code} at {path}.");
        }

        private sealed class TestActor : ITceActor
        {
            public bool IsAlive => true;
            public float DomainTime => 0f;
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

        private sealed class CountingEffectData : TceEffectData<CountingEffect>
        {
        }

        private sealed class CountingEffect : TceEffect<CountingEffectData>
        {
            public static int ExecuteCount;

            public override void Execute(ITceActor target, object source)
            {
                ExecuteCount++;
            }
        }

        private sealed class FlagSource : ITceFlagSource
        {
            private readonly string flagId;

            public FlagSource(string flagId)
            {
                this.flagId = flagId;
            }

            public bool HasFlag(string flagId)
            {
                return this.flagId == flagId;
            }
        }

        private sealed class RandomSource : ITceRandomSource
        {
            private readonly float value;

            public RandomSource(float value)
            {
                this.value = value;
            }

            public float Next01()
            {
                return value;
            }
        }
    }
}
