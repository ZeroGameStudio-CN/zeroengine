using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.World.Presentation;

namespace ZeroEngine.World.Tests.Editor.Presentation
{
    public sealed class WorldInteriorOccupancyCoordinatorTests
    {
        [TestCase(null, "interior", "source", "subjectId")]
        [TestCase(" ", "interior", "source", "subjectId")]
        [TestCase("subject", null, "source", "interiorId")]
        [TestCase("subject", "\t", "source", "interiorId")]
        [TestCase("subject", "interior", null, "sourceId")]
        [TestCase("subject", "interior", "\r\n", "sourceId")]
        public void Constructor_MissingRequiredId_ThrowsArgumentException(
            string subjectId,
            string interiorId,
            string sourceId,
            string expectedParameterName)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                new WorldInteriorOccupancyRequest(subjectId, interiorId, sourceId, null, 0));

            Assert.That(exception.ParamName, Is.EqualTo(expectedParameterName));
        }

        [Test]
        public void Constructor_ValidValues_NormalizesIdentifiersAndReason()
        {
            var request = new WorldInteriorOccupancyRequest(
                " player ",
                " apothecary ",
                " door-volume ",
                " entering interior ",
                20);

            Assert.That(request.SubjectId, Is.EqualTo("player"));
            Assert.That(request.InteriorId, Is.EqualTo("apothecary"));
            Assert.That(request.SourceId, Is.EqualTo("door-volume"));
            Assert.That(request.Reason, Is.EqualTo("entering interior"));
            Assert.That(request.Priority, Is.EqualTo(20));
        }

        [Test]
        public void Acquire_HigherPriorityThenLatestSamePriority_SelectsPerSubjectWinner()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            using var low = coordinator.Acquire(Request("player", "street", "low", 10));
            using var high = coordinator.Acquire(Request("player", "apothecary", "high", 20));
            using var latestHigh = coordinator.Acquire(Request("player", "kiln", "latest", 20));

            var effective = coordinator.GetEffectiveSnapshot(" player ");
            Assert.That(effective.TokenId, Is.EqualTo(latestHigh.TokenId));
            Assert.That(effective.InteriorId, Is.EqualTo("kiln"));
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(3));
        }

        [Test]
        public void Dispose_WinningLease_RestoresPreviousInterior()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            using var baseline = coordinator.Acquire(Request("player", "street", "baseline", 10));
            using var previous = coordinator.Acquire(Request("player", "apothecary", "previous", 20));
            var latest = coordinator.Acquire(Request("player", "kiln", "latest", 20));

            latest.Dispose();

            Assert.That(coordinator.GetEffectiveSnapshot("player").TokenId, Is.EqualTo(previous.TokenId));
            Assert.That(coordinator.GetEffectiveSnapshot("player").InteriorId, Is.EqualTo("apothecary"));

            previous.Dispose();

            Assert.That(coordinator.GetEffectiveSnapshot("player").TokenId, Is.EqualTo(baseline.TokenId));
            Assert.That(coordinator.GetEffectiveSnapshot("player").InteriorId, Is.EqualTo("street"));
        }

        [Test]
        public void DifferentSubjects_ArbitrateIndependently()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            using var player = coordinator.Acquire(Request("player", "apothecary", "player-source", 10));
            using var companion = coordinator.Acquire(Request("companion", "street", "companion-source", 100));

            Assert.That(coordinator.GetEffectiveSnapshot("player").TokenId, Is.EqualTo(player.TokenId));
            Assert.That(coordinator.GetEffectiveSnapshot("player").InteriorId, Is.EqualTo("apothecary"));
            Assert.That(coordinator.GetEffectiveSnapshot("companion").TokenId, Is.EqualTo(companion.TokenId));
            Assert.That(coordinator.GetEffectiveSnapshot("companion").InteriorId, Is.EqualTo("street"));
        }

        [Test]
        public void LowerPriorityAcquireAndRelease_DoesNotRaiseEffectiveEvent()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            using var winner = coordinator.Acquire(Request("player", "apothecary", "winner", 100));
            var changeCount = 0;
            coordinator.EffectiveSnapshotChanged += (_, _) => changeCount++;

            var lower = coordinator.Acquire(Request("player", "street", "lower", 10));
            lower.Dispose();

            Assert.That(changeCount, Is.Zero);
            Assert.That(coordinator.GetEffectiveSnapshot("player").TokenId, Is.EqualTo(winner.TokenId));
        }

        [Test]
        public void CopyActiveSnapshots_SortsBySubjectThenTokenAndClearsDestination()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            using var zFirst = coordinator.Acquire(Request("z-subject", "one", "z-one", 1));
            using var aFirst = coordinator.Acquire(Request("a-subject", "one", "a-one", 1));
            using var aSecond = coordinator.Acquire(Request("a-subject", "two", "a-two", 1));
            var results = new List<WorldInteriorOccupancySnapshot> { default };

            coordinator.CopyActiveSnapshots(results);

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[0].TokenId, Is.EqualTo(aFirst.TokenId));
            Assert.That(results[1].TokenId, Is.EqualTo(aSecond.TokenId));
            Assert.That(results[2].TokenId, Is.EqualTo(zFirst.TokenId));
        }

        [Test]
        public void Dispose_CalledTwice_IsIdempotent()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            var lease = coordinator.Acquire(Request("player", "apothecary", "source", 1));
            var changeCount = 0;
            coordinator.EffectiveSnapshotChanged += (_, _) => changeCount++;

            lease.Dispose();
            lease.Dispose();

            Assert.That(lease.IsReleased, Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.GetEffectiveSnapshot("player").IsOccupied, Is.False);
            Assert.That(changeCount, Is.EqualTo(1));
        }

        [Test]
        public void Acquire_SubscriberThrows_RollsBackRequestAndRestoresWinner()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            using var baseline = coordinator.Acquire(
                Request("player", "street", "baseline", 10));
            Action<WorldInteriorOccupancySnapshot, WorldInteriorOccupancySnapshot> handler =
                (_, _) => throw new InvalidOperationException("subscriber failed");
            coordinator.EffectiveSnapshotChanged += handler;

            Assert.Throws<InvalidOperationException>(() =>
                coordinator.Acquire(Request("player", "apothecary", "higher", 20)));
            coordinator.EffectiveSnapshotChanged -= handler;

            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(coordinator.GetEffectiveSnapshot("player"), Is.EqualTo(baseline.Snapshot));
        }

        [Test]
        public void ChangeDispatch_ReentrantAcquireAndRelease_AreRejectedWithoutLosingLease()
        {
            var coordinator = new WorldInteriorOccupancyCoordinator();
            var baseline = coordinator.Acquire(Request("player", "street", "baseline", 10));
            Action<WorldInteriorOccupancySnapshot, WorldInteriorOccupancySnapshot> handler = (_, _) =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                    coordinator.Acquire(Request("player", "kiln", "nested", 30)));
                Assert.Throws<InvalidOperationException>(() => baseline.Dispose());
            };
            coordinator.EffectiveSnapshotChanged += handler;

            var higher = coordinator.Acquire(Request("player", "apothecary", "higher", 20));
            coordinator.EffectiveSnapshotChanged -= handler;

            Assert.That(baseline.IsReleased, Is.False);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(2));

            baseline.Dispose();
            higher.Dispose();
        }

        private static WorldInteriorOccupancyRequest Request(
            string subjectId,
            string interiorId,
            string sourceId,
            int priority)
        {
            return new WorldInteriorOccupancyRequest(
                subjectId,
                interiorId,
                sourceId,
                null,
                priority);
        }
    }
}
