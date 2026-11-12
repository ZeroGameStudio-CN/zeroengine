using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.World.Presentation;

namespace ZeroEngine.World.Tests.Editor.Presentation
{
    public sealed class WorldCameraPresentationCoordinatorTests
    {
        [TestCase(null, "follow", "in", "out", "scope", "source", "profileId")]
        [TestCase(" ", "follow", "in", "out", "scope", "source", "profileId")]
        [TestCase("profile", null, "in", "out", "scope", "source", "followTargetId")]
        [TestCase("profile", "\t", "in", "out", "scope", "source", "followTargetId")]
        [TestCase("profile", "follow", null, "out", "scope", "source", "blendInProfileId")]
        [TestCase("profile", "follow", " ", "out", "scope", "source", "blendInProfileId")]
        [TestCase("profile", "follow", "in", null, "scope", "source", "blendOutProfileId")]
        [TestCase("profile", "follow", "in", "\r\n", "scope", "source", "blendOutProfileId")]
        [TestCase("profile", "follow", "in", "out", null, "source", "scopeId")]
        [TestCase("profile", "follow", "in", "out", " ", "source", "scopeId")]
        [TestCase("profile", "follow", "in", "out", "scope", null, "sourceId")]
        [TestCase("profile", "follow", "in", "out", "scope", "\t", "sourceId")]
        public void Constructor_MissingRequiredId_ThrowsArgumentException(
            string profileId,
            string followTargetId,
            string blendInProfileId,
            string blendOutProfileId,
            string scopeId,
            string sourceId,
            string expectedParameterName)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                new WorldCameraPresentationRequest(
                    profileId,
                    followTargetId,
                    null,
                    blendInProfileId,
                    blendOutProfileId,
                    scopeId,
                    sourceId,
                    null,
                    0));

            Assert.That(exception.ParamName, Is.EqualTo(expectedParameterName));
        }

        [Test]
        public void Constructor_ValidValues_NormalizesIdentifiersAndReason()
        {
            var request = new WorldCameraPresentationRequest(
                "  exploration  ",
                " player-root ",
                " focus-anchor ",
                " enter-interior ",
                " exit-interior ",
                " world-map ",
                "  apothecary-door ",
                "  entering interior  ",
                30);

            Assert.That(request.ProfileId, Is.EqualTo("exploration"));
            Assert.That(request.FollowTargetId, Is.EqualTo("player-root"));
            Assert.That(request.LookTargetId, Is.EqualTo("focus-anchor"));
            Assert.That(request.BlendInProfileId, Is.EqualTo("enter-interior"));
            Assert.That(request.BlendOutProfileId, Is.EqualTo("exit-interior"));
            Assert.That(request.ScopeId, Is.EqualTo("world-map"));
            Assert.That(request.SourceId, Is.EqualTo("apothecary-door"));
            Assert.That(request.Reason, Is.EqualTo("entering interior"));
            Assert.That(request.Priority, Is.EqualTo(30));

            var nullReason = new WorldCameraPresentationRequest(
                "p",
                "follow",
                " ",
                "in",
                "out",
                "s",
                "o",
                null,
                0);
            Assert.That(nullReason.LookTargetId, Is.Empty);
            Assert.That(nullReason.Reason, Is.Empty);
        }

        [Test]
        public void Acquire_HigherPriorityThenEarliestSamePriority_SelectsExpectedWinner()
        {
            var coordinator = new WorldCameraPresentationCoordinator();
            using var low = coordinator.Acquire(Request("low", 10));
            using var high = coordinator.Acquire(Request("high", 20));
            using var latestHigh = coordinator.Acquire(Request("latest-high", 20));

            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(3));
            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(high.TokenId));
            Assert.That(coordinator.EffectiveSnapshot.ProfileId, Is.EqualTo("high"));
        }

        [Test]
        public void Dispose_WinningLease_RestoresPreviousWinnerPrecisely()
        {
            var coordinator = new WorldCameraPresentationCoordinator();
            using var baseline = coordinator.Acquire(Request("baseline", 10));
            var firstHigh = coordinator.Acquire(Request("first-high", 20));
            var latestHigh = coordinator.Acquire(Request("latest-high", 20));

            firstHigh.Dispose();

            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(latestHigh.TokenId));
            Assert.That(coordinator.EffectiveSnapshot.ProfileId, Is.EqualTo("latest-high"));

            latestHigh.Dispose();

            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(baseline.TokenId));
            Assert.That(coordinator.EffectiveSnapshot.ProfileId, Is.EqualTo("baseline"));
        }

        [Test]
        public void LowerPriorityAcquireAndRelease_DoesNotRaiseEffectiveEvent()
        {
            var coordinator = new WorldCameraPresentationCoordinator();
            using var winner = coordinator.Acquire(Request("winner", 100));
            var changeCount = 0;
            coordinator.EffectiveSnapshotChanged += (_, _) => changeCount++;

            var lower = coordinator.Acquire(Request("lower", 10));
            lower.Dispose();

            Assert.That(changeCount, Is.Zero);
            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(winner.TokenId));
        }

        [Test]
        public void Dispose_CalledTwice_IsIdempotent()
        {
            var coordinator = new WorldCameraPresentationCoordinator();
            var lease = coordinator.Acquire(Request("winner", 10));
            var changeCount = 0;
            coordinator.EffectiveSnapshotChanged += (_, _) => changeCount++;

            Assert.That(coordinator.ContainsToken(lease.TokenId), Is.True);
            lease.Dispose();
            lease.Dispose();

            Assert.That(lease.IsReleased, Is.True);
            Assert.That(coordinator.ContainsToken(lease.TokenId), Is.False);
            Assert.That(coordinator.ContainsToken(0), Is.False);
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.EffectiveSnapshot.IsActive, Is.False);
            Assert.That(changeCount, Is.EqualTo(1));
        }

        [Test]
        public void CopyActiveSnapshots_ReturnsStableTokenOrderAndClearsDestination()
        {
            var coordinator = new WorldCameraPresentationCoordinator();
            using var first = coordinator.Acquire(Request("first", 30));
            using var second = coordinator.Acquire(Request("second", 10));
            using var third = coordinator.Acquire(Request("third", 20));
            var results = new List<WorldCameraPresentationSnapshot> { default };

            coordinator.CopyActiveSnapshots(results);

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[0].TokenId, Is.EqualTo(first.TokenId));
            Assert.That(results[1].TokenId, Is.EqualTo(second.TokenId));
            Assert.That(results[2].TokenId, Is.EqualTo(third.TokenId));
            Assert.That(results[0], Is.EqualTo(first.Snapshot));
            Assert.That(results[0].FollowTargetId, Is.EqualTo("follow-first"));
            Assert.That(results[0].LookTargetId, Is.EqualTo("look-first"));
            Assert.That(results[0].BlendInProfileId, Is.EqualTo("blend-in"));
            Assert.That(results[0].BlendOutProfileId, Is.EqualTo("blend-out"));
            Assert.That(results[0].GetHashCode(), Is.EqualTo(first.Snapshot.GetHashCode()));
        }

        [Test]
        public void Acquire_SubscriberThrows_RollsBackRequestAndRestoresWinner()
        {
            var coordinator = new WorldCameraPresentationCoordinator();
            using var baseline = coordinator.Acquire(Request("baseline", 10));
            Action<WorldCameraPresentationSnapshot, WorldCameraPresentationSnapshot> handler =
                (_, _) => throw new InvalidOperationException("subscriber failed");
            coordinator.EffectiveSnapshotChanged += handler;

            Assert.Throws<InvalidOperationException>(() => coordinator.Acquire(Request("higher", 20)));
            coordinator.EffectiveSnapshotChanged -= handler;

            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(coordinator.EffectiveSnapshot, Is.EqualTo(baseline.Snapshot));
        }

        [Test]
        public void ChangeDispatch_ReentrantAcquireAndRelease_AreRejectedWithoutLosingLease()
        {
            var coordinator = new WorldCameraPresentationCoordinator();
            var baseline = coordinator.Acquire(Request("baseline", 10));
            Action<WorldCameraPresentationSnapshot, WorldCameraPresentationSnapshot> handler = (_, _) =>
            {
                Assert.Throws<InvalidOperationException>(() => coordinator.Acquire(Request("nested", 30)));
                Assert.Throws<InvalidOperationException>(() => baseline.Dispose());
            };
            coordinator.EffectiveSnapshotChanged += handler;

            var higher = coordinator.Acquire(Request("higher", 20));
            coordinator.EffectiveSnapshotChanged -= handler;

            Assert.That(baseline.IsReleased, Is.False);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(2));

            baseline.Dispose();
            higher.Dispose();
        }

        private static WorldCameraPresentationRequest Request(string profileId, int priority)
        {
            return new WorldCameraPresentationRequest(
                profileId,
                $"follow-{profileId}",
                $"look-{profileId}",
                "blend-in",
                "blend-out",
                "world-map",
                $"source-{profileId}",
                null,
                priority);
        }
    }
}
