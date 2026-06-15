using NUnit.Framework;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphLaneModelTests
    {
        [Test]
        public void AddComponent_AddsToMatchingLane()
        {
            var graph = new TceGraph();

            TceGraphLaneModel.AddComponent(graph, TceGraphLane.Trigger, new OnInstallTriggerData());
            TceGraphLaneModel.AddComponent(graph, TceGraphLane.Condition, new CooldownConditionData());
            TceGraphLaneModel.AddComponent(graph, TceGraphLane.Effect, new DebugLogEffectData());

            Assert.AreEqual(1, graph.Triggers.Count);
            Assert.AreEqual(1, graph.Conditions.Count);
            Assert.AreEqual(1, graph.Effects.Count);
        }

        [Test]
        public void AddComponent_RejectsMismatchedLane()
        {
            var graph = new TceGraph();

            Assert.Throws<System.ArgumentException>(() =>
                TceGraphLaneModel.AddComponent(graph, TceGraphLane.Trigger, new DebugLogEffectData()));
        }

        [Test]
        public void Remove_RemovesFromLane()
        {
            var graph = new TceGraph();
            graph.AddEffect(new DebugLogEffectData { Message = "a" });
            graph.AddEffect(new DebugLogEffectData { Message = "b" });

            TceGraphLaneModel.Remove(graph, TceGraphLane.Effect, 0);

            Assert.AreEqual(1, graph.Effects.Count);
            Assert.AreEqual("b", ((DebugLogEffectData)graph.Effects[0]).Message);
        }

        [Test]
        public void Move_ReordersWithinLane()
        {
            var graph = new TceGraph();
            graph.AddCondition(new CooldownConditionData { Duration = 1f });
            graph.AddCondition(new ExecutionCountConditionData { MaxAcceptedExecutions = 3 });

            TceGraphLaneModel.Move(graph, TceGraphLane.Condition, 1, 0);

            Assert.IsInstanceOf<ExecutionCountConditionData>(graph.Conditions[0]);
            Assert.IsInstanceOf<CooldownConditionData>(graph.Conditions[1]);
        }
    }
}
