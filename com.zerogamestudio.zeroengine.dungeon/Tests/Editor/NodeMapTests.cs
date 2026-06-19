using System.Linq;
using NUnit.Framework;
using ZeroEngine.Dungeon.Map;

namespace ZeroEngine.Dungeon.Editor.Tests
{
    public sealed class NodeMapTests
    {
        [Test]
        public void MoveRequiresCurrentNodeCompletionAndConnection()
        {
            var map = new NodeMap("floor_1", floor: 1);
            var start = new MapNode("start", NodeType.Start);
            var battle = new MapNode("battle", NodeType.Battle);
            var boss = new MapNode("boss", NodeType.Boss);
            map.AddNode(start);
            map.AddNode(battle);
            map.AddNode(boss);
            map.SetStartNode(start);
            map.SetBossNode(boss);
            map.ConnectNodes(start, battle);

            Assert.IsFalse(map.MoveToNode(boss));
            Assert.IsFalse(map.MoveToNode(battle));

            map.CompleteCurrentNode();

            Assert.AreEqual(new[] { battle }, map.GetAvailableNodes().ToArray());
            Assert.IsTrue(map.MoveToNode(battle));
            Assert.AreSame(battle, map.CurrentNode);
            Assert.IsTrue(battle.IsVisited);
        }

        [Test]
        public void CompletingBossMarksMapCompleted()
        {
            var map = new NodeMap("floor_1", floor: 1);
            var boss = new MapNode("boss", NodeType.Boss);
            map.AddNode(boss);
            map.SetStartNode(boss);
            map.SetBossNode(boss);

            Assert.IsTrue(map.IsAtBoss());
            Assert.IsFalse(map.IsCompleted());

            map.CompleteCurrentNode();

            Assert.IsTrue(map.IsCompleted());
        }

        [Test]
        public void AddConnectionAvoidsDuplicates()
        {
            var start = new MapNode("start", NodeType.Start);
            var battle = new MapNode("battle", NodeType.Battle);

            start.AddConnection(battle);
            start.AddConnection(battle);

            Assert.AreEqual(1, start.ConnectedNodes.Count);
            Assert.IsTrue(start.IsConnectedTo(battle));
        }
    }
}
