using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.Combat;

namespace ZeroEngine.Combat.Editor.Tests
{
    public sealed class ThreatTableTests
    {
        [Test]
        public void AddSetAndSortThreatReturnsHighestSource()
        {
            var table = new ThreatTable();

            table.AddThreat("mage", 10f);
            table.AddThreat("tank", 5f);
            table.SetThreat("tank", 20f);

            Assert.AreEqual(("tank", 20f), table.GetHighestThreat());
            Assert.AreEqual(new[] { "tank", "mage" }, table.GetSorted().Select(pair => pair.Key).ToArray());
        }

        [Test]
        public void MultiplyAllRemovesZeroOrNegativeThreat()
        {
            var table = new ThreatTable();
            table.AddThreat("rogue", 10f);

            table.MultiplyAll(0f);

            Assert.IsTrue(table.IsEmpty);
            Assert.IsFalse(table.HasThreatFrom("rogue"));
        }

        [Test]
        public void TickDecaysAndPrunesLowThreat()
        {
            var table = new ThreatTable();
            table.AddThreat("dot", 1f);

            table.Tick(new ThreatModifier
            {
                DecayRate = 0.05f,
                PruneThreshold = 0.1f
            });

            Assert.IsFalse(table.HasThreatFrom("dot"));
        }

        [Test]
        public void TransferToAddsRatioOfExistingThreat()
        {
            var source = new ThreatTable();
            var receiver = new ThreatTable();
            source.AddThreat("healer", 40f);

            source.TransferTo(receiver, 0.25f);

            Assert.AreEqual(10f, receiver.GetThreat("healer"));
            Assert.AreEqual(40f, source.GetThreat("healer"));
        }

        [Test]
        public void GetTopNWritesSortedIdsToProvidedList()
        {
            var table = new ThreatTable();
            var result = new List<string>();
            table.AddThreat("a", 1f);
            table.AddThreat("b", 3f);
            table.AddThreat("c", 2f);

            table.GetTopN(2, result);

            Assert.AreEqual(new[] { "b", "c" }, result);
        }
    }
}
