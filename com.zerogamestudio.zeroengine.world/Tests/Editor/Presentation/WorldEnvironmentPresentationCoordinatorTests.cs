using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroEngine.World.Presentation;

namespace ZeroEngine.World.Tests.Editor.Presentation
{
    public sealed class WorldEnvironmentPresentationCoordinatorTests
    {
        [TestCase(null, "in", "out", "scope", "source", "profileId")]
        [TestCase(" ", "in", "out", "scope", "source", "profileId")]
        [TestCase("profile", null, "out", "scope", "source", "blendInProfileId")]
        [TestCase("profile", "in", "\t", "scope", "source", "blendOutProfileId")]
        [TestCase("profile", "in", "out", null, "source", "scopeId")]
        [TestCase("profile", "in", "out", "scope", "\r\n", "sourceId")]
        public void Constructor_MissingRequiredId_ThrowsArgumentException(
            string profileId,
            string blendInProfileId,
            string blendOutProfileId,
            string scopeId,
            string sourceId,
            string expectedParameterName)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                new WorldEnvironmentPresentationRequest(
                    profileId,
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
            var request = new WorldEnvironmentPresentationRequest(
                " longleji-indoor ",
                " enter-interior ",
                " exit-interior ",
                " world-map ",
                " apothecary-zone ",
                " entering interior ",
                30);

            Assert.That(request.ProfileId, Is.EqualTo("longleji-indoor"));
            Assert.That(request.BlendInProfileId, Is.EqualTo("enter-interior"));
            Assert.That(request.BlendOutProfileId, Is.EqualTo("exit-interior"));
            Assert.That(request.ScopeId, Is.EqualTo("world-map"));
            Assert.That(request.SourceId, Is.EqualTo("apothecary-zone"));
            Assert.That(request.Reason, Is.EqualTo("entering interior"));
            Assert.That(request.Priority, Is.EqualTo(30));

            var nullReason = new WorldEnvironmentPresentationRequest(
                "profile",
                "in",
                "out",
                "scope",
                "source",
                null,
                0);
            Assert.That(nullReason.Reason, Is.Empty);
        }

        [Test]
        public void DefaultSnapshot_IsInactiveAndContainsNoIdentifiers()
        {
            WorldEnvironmentPresentationSnapshot snapshot = default;

            Assert.That(snapshot.IsActive, Is.False);
            Assert.That(snapshot.TokenId, Is.Zero);
            Assert.That(snapshot.ProfileId, Is.Empty);
            Assert.That(snapshot.BlendInProfileId, Is.Empty);
            Assert.That(snapshot.BlendOutProfileId, Is.Empty);
            Assert.That(snapshot.ScopeId, Is.Empty);
            Assert.That(snapshot.SourceId, Is.Empty);
            Assert.That(snapshot.Reason, Is.Empty);
            Assert.That(snapshot.Priority, Is.Zero);
        }

        [Test]
        public void Acquire_HighestPriorityThenEarliestSamePriority_SelectsFifoWinner()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            using var low = coordinator.Acquire(Request("low", 10));
            using var firstHigh = coordinator.Acquire(Request("first-high", 20));
            using var secondHigh = coordinator.Acquire(Request("second-high", 20));

            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(3));
            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(firstHigh.TokenId));
            Assert.That(coordinator.EffectiveSnapshot.ProfileId, Is.EqualTo("first-high"));
            Assert.That(secondHigh.TokenId, Is.GreaterThan(firstHigh.TokenId));
        }

        [Test]
        public void Dispose_WinningLease_RestoresQueuedWinnerThenBaseline()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            using var baseline = coordinator.Acquire(Request("baseline", 10));
            var firstHigh = coordinator.Acquire(Request("first-high", 20));
            using var secondHigh = coordinator.Acquire(Request("second-high", 20));

            firstHigh.Dispose();

            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(secondHigh.TokenId));
            Assert.That(coordinator.EffectiveSnapshot.ProfileId, Is.EqualTo("second-high"));

            secondHigh.Dispose();

            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(baseline.TokenId));
            Assert.That(coordinator.EffectiveSnapshot.ProfileId, Is.EqualTo("baseline"));
        }

        [Test]
        public void LowerPriorityAndQueuedSamePriority_DoNotRaiseEffectiveEvent()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            using var winner = coordinator.Acquire(Request("winner", 100));
            var changeCount = 0;
            coordinator.EffectiveSnapshotChanged += (_, _) => changeCount++;

            var lower = coordinator.Acquire(Request("lower", 10));
            var queued = coordinator.Acquire(Request("queued", 100));
            lower.Dispose();
            queued.Dispose();

            Assert.That(changeCount, Is.Zero);
            Assert.That(coordinator.EffectiveSnapshot.TokenId, Is.EqualTo(winner.TokenId));
        }

        [Test]
        public void CopyActiveSnapshots_ReturnsStableTokenOrderAndClearsDestination()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            using var first = coordinator.Acquire(Request("first", 30));
            using var second = coordinator.Acquire(Request("second", 10));
            using var third = coordinator.Acquire(Request("third", 20));
            var results = new List<WorldEnvironmentPresentationSnapshot> { default };

            coordinator.CopyActiveSnapshots(results);

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[0], Is.EqualTo(first.Snapshot));
            Assert.That(results[1], Is.EqualTo(second.Snapshot));
            Assert.That(results[2], Is.EqualTo(third.Snapshot));
            Assert.That(results[0].GetHashCode(), Is.EqualTo(first.Snapshot.GetHashCode()));
        }

        [Test]
        public void Dispose_CalledTwice_IsIdempotent()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            var lease = coordinator.Acquire(Request("winner", 10));
            var changeCount = 0;
            coordinator.EffectiveSnapshotChanged += (_, _) => changeCount++;

            lease.Dispose();
            lease.Dispose();

            Assert.That(lease.IsReleased, Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.EffectiveSnapshot.IsActive, Is.False);
            Assert.That(changeCount, Is.EqualTo(1));
        }

        [Test]
        public void Acquire_SubscriberThrows_RollsBackRequestAndRestoresWinner()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            using var baseline = coordinator.Acquire(Request("baseline", 10));
            Action<WorldEnvironmentPresentationSnapshot, WorldEnvironmentPresentationSnapshot> handler =
                (_, _) => throw new InvalidOperationException("subscriber failed");
            coordinator.EffectiveSnapshotChanged += handler;

            Assert.Throws<InvalidOperationException>(() =>
                coordinator.Acquire(Request("higher", 20)));
            coordinator.EffectiveSnapshotChanged -= handler;

            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(coordinator.EffectiveSnapshot, Is.EqualTo(baseline.Snapshot));
        }

        [Test]
        public void Release_SubscriberThrows_RollsBackReleaseAndKeepsLeaseUsable()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            using var baseline = coordinator.Acquire(Request("baseline", 10));
            var higher = coordinator.Acquire(Request("higher", 20));
            Action<WorldEnvironmentPresentationSnapshot, WorldEnvironmentPresentationSnapshot> handler =
                (_, _) => throw new InvalidOperationException("subscriber failed");
            coordinator.EffectiveSnapshotChanged += handler;

            Assert.Throws<InvalidOperationException>(() => higher.Dispose());
            coordinator.EffectiveSnapshotChanged -= handler;

            Assert.That(higher.IsReleased, Is.False);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(2));
            Assert.That(coordinator.EffectiveSnapshot, Is.EqualTo(higher.Snapshot));

            higher.Dispose();
            Assert.That(coordinator.EffectiveSnapshot, Is.EqualTo(baseline.Snapshot));
        }

        [Test]
        public void ChangeDispatch_ReentrantAcquireAndRelease_AreRejectedWithoutLosingLease()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            var baseline = coordinator.Acquire(Request("baseline", 10));
            Action<WorldEnvironmentPresentationSnapshot, WorldEnvironmentPresentationSnapshot> handler =
                (_, _) =>
                {
                    Assert.Throws<InvalidOperationException>(() =>
                        coordinator.Acquire(Request("nested", 30)));
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

        [Test]
        public void CrossThreadAccess_IsRejectedWithoutChangingState()
        {
            var coordinator = new WorldEnvironmentPresentationCoordinator();
            var baseline = coordinator.Acquire(Request("baseline", 10));
            var acquireError = Task.Run(() => CaptureException(() =>
                coordinator.Acquire(Request("background", 20)))).GetAwaiter().GetResult();
            var releaseError = Task.Run(() => CaptureException(baseline.Dispose))
                .GetAwaiter().GetResult();
            var readError = Task.Run(() => CaptureException(() =>
            {
                _ = coordinator.EffectiveSnapshot;
            })).GetAwaiter().GetResult();

            Assert.That(acquireError, Is.TypeOf<InvalidOperationException>());
            Assert.That(releaseError, Is.TypeOf<InvalidOperationException>());
            Assert.That(readError, Is.TypeOf<InvalidOperationException>());
            Assert.That(baseline.IsReleased, Is.False);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(coordinator.EffectiveSnapshot, Is.EqualTo(baseline.Snapshot));

            baseline.Dispose();
        }

        private static Exception CaptureException(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static WorldEnvironmentPresentationRequest Request(string profileId, int priority)
        {
            return new WorldEnvironmentPresentationRequest(
                profileId,
                "blend-in",
                "blend-out",
                "world-map",
                $"source-{profileId}",
                null,
                priority);
        }
    }
}
