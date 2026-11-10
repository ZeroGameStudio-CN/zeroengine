using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class FeedbackCoordinatorTests
    {
        private static readonly FeedbackKey FirstKey = new FeedbackKey(1, 1);
        private static readonly FeedbackKey SecondKey = new FeedbackKey(2, 1);

        [Test]
        public void EvaluationDoesNotReserveOrConsumeARejectedOutput()
        {
            var coordinator = new FeedbackCoordinator(1);
            var rejectedByOutput = coordinator.Evaluate(FirstKey, 0.5, 10);

            Assert.That(rejectedByOutput.CanPlay, Is.True);
            Assert.That(coordinator.Count, Is.Zero);
            Assert.That(coordinator.Evaluate(FirstKey, 0.5, 10).Decision, Is.EqualTo(FeedbackDecision.New));
            Assert.That(coordinator.Evaluate(SecondKey, 0.2, 10).Decision, Is.EqualTo(FeedbackDecision.New));
        }

        [Test]
        public void CommitSuppressesEqualAndWeakerRequestsWithoutRefreshingRetention()
        {
            var coordinator = new FeedbackCoordinator(2);
            Accept(coordinator, FirstKey, 0.05, 10, 11);

            Assert.That(coordinator.Evaluate(FirstKey, 0.05, 10.4).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
            Assert.That(coordinator.Evaluate(FirstKey, 0.01, 10.9).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
            Assert.That(coordinator.Evaluate(FirstKey, 0.05, 11).Decision, Is.EqualTo(FeedbackDecision.New));
        }

        [Test]
        public void StrongerRequestKeepsFirstEventTimeForHitStopDeadline()
        {
            var coordinator = new FeedbackCoordinator(2);
            Accept(coordinator, FirstKey, 0.02, 10, 11);

            var upgrade = coordinator.Evaluate(FirstKey, 0.08, 10.04);
            Assert.That(upgrade.Decision, Is.EqualTo(FeedbackDecision.Stronger));
            Assert.That(upgrade.PreviousStrength, Is.EqualTo(0.02));
            Assert.That(upgrade.FirstAcceptedAt + upgrade.Strength, Is.EqualTo(10.08).Within(1e-10));
            Assert.That(coordinator.Commit(upgrade, 11, out _), Is.EqualTo(FeedbackCommitResult.Committed));

            var laterUpgrade = coordinator.Evaluate(FirstKey, 0.1, 10.07);
            Assert.That(laterUpgrade.FirstAcceptedAt, Is.EqualTo(10));
            Assert.That(laterUpgrade.PreviousStrength, Is.EqualTo(0.08));
        }

        [Test]
        public void StrongerRetentionCanExtendButCannotShortenTheCommittedWindow()
        {
            var coordinator = new FeedbackCoordinator(1);
            Accept(coordinator, FirstKey, 0.1, 0, 5);
            Accept(coordinator, FirstKey, 0.2, 1, 2);
            Assert.That(coordinator.Evaluate(FirstKey, 0.2, 4).Decision, Is.EqualTo(FeedbackDecision.Duplicate));

            Accept(coordinator, FirstKey, 0.3, 4, 6);
            Assert.That(coordinator.Evaluate(FirstKey, 0.3, 5).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
            Assert.That(coordinator.Evaluate(FirstKey, 0.3, 6).Decision, Is.EqualTo(FeedbackDecision.New));
        }

        [Test]
        public void DifferentAttacksAndPulsesRemainDistinct()
        {
            var coordinator = new FeedbackCoordinator(3);
            Accept(coordinator, FirstKey, 0.1, 0, 1);

            Assert.That(coordinator.Evaluate(new FeedbackKey(1, 2), 0.1, 0).Decision, Is.EqualTo(FeedbackDecision.New));
            Assert.That(coordinator.Evaluate(new FeedbackKey(2, 1), 0.1, 0).Decision, Is.EqualTo(FeedbackDecision.New));
        }

        [Test]
        public void SameKeyInDifferentChannelsDoesNotShareAdmission()
        {
            var hitStop = new FeedbackCoordinator(1);
            var camera = new FeedbackCoordinator(1);
            Accept(hitStop, FirstKey, 0.08, 0, 1);

            Assert.That(camera.Evaluate(FirstKey, 0.5, 0).CanPlay, Is.True);
            Assert.That(hitStop.Evaluate(FirstKey, 0.08, 0).CanPlay, Is.False);
        }

        [Test]
        public void FullCapacityRejectsNewKeyWithoutEvictingAnActiveKey()
        {
            var coordinator = new FeedbackCoordinator(1);
            Accept(coordinator, FirstKey, 0.05, 0, 1);

            Assert.That(coordinator.Evaluate(SecondKey, 100, 0.1).Decision, Is.EqualTo(FeedbackDecision.CapacityExceeded));
            Assert.That(coordinator.Evaluate(FirstKey, 0.05, 0.1).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
            Assert.That(coordinator.Evaluate(FirstKey, 0.08, 0.1).Decision, Is.EqualTo(FeedbackDecision.Stronger));
            Assert.That(coordinator.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExpirationAtDeadlineMakesCapacityAvailable()
        {
            var coordinator = new FeedbackCoordinator(1);
            Accept(coordinator, FirstKey, 0.1, 0, 1);

            Assert.That(coordinator.Evaluate(SecondKey, 0.1, 1).Decision, Is.EqualTo(FeedbackDecision.New));
            Assert.That(coordinator.Count, Is.Zero);
            Accept(coordinator, SecondKey, 0.1, 1, 2);
            Assert.That(coordinator.Count, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedRequestsAcrossManyWindowsStayWithinCapacity()
        {
            var coordinator = new FeedbackCoordinator(4);
            for (int i = 1; i <= 2000; i++)
            {
                double now = i;
                Accept(coordinator, new FeedbackKey(i, 1), i, now, now + 4);
                Assert.That(coordinator.Count, Is.LessThanOrEqualTo(4));
            }
        }

        [Test]
        public void StaleEvaluationCannotCommitAfterACompetingCommit()
        {
            var coordinator = new FeedbackCoordinator(2);
            var delayed = coordinator.Evaluate(FirstKey, 0.1, 0);
            Accept(coordinator, SecondKey, 0.2, 0, 1);

            Assert.That(coordinator.Commit(delayed, 1, out _), Is.EqualTo(FeedbackCommitResult.StaleEvaluation));
            Assert.That(coordinator.Evaluate(FirstKey, 0.1, 0).Decision, Is.EqualTo(FeedbackDecision.New));
        }

        [Test]
        public void EvaluationCannotBeCommittedTwice()
        {
            var coordinator = new FeedbackCoordinator(1);
            var evaluation = coordinator.Evaluate(FirstKey, 0.1, 0);
            Assert.That(coordinator.Commit(evaluation, 1, out _), Is.EqualTo(FeedbackCommitResult.Committed));
            Assert.That(coordinator.Commit(evaluation, 1, out _), Is.EqualTo(FeedbackCommitResult.StaleEvaluation));
            Assert.That(coordinator.Count, Is.EqualTo(1));
        }

        [Test]
        public void ClockAdvanceInvalidatesAPendingEvaluationEvenWithoutMutation()
        {
            var coordinator = new FeedbackCoordinator(2);
            var delayed = coordinator.Evaluate(FirstKey, 0.1, 0);
            coordinator.Evaluate(SecondKey, 0.1, 0.1);

            Assert.That(coordinator.Commit(delayed, 1, out _), Is.EqualTo(FeedbackCommitResult.StaleEvaluation));
            Assert.That(coordinator.Count, Is.Zero);
        }

        [Test]
        public void PendingUpgradeCannotCommitAfterAnotherUpgrade()
        {
            var coordinator = new FeedbackCoordinator(1);
            Accept(coordinator, FirstKey, 0.1, 0, 1);
            var weaker = coordinator.Evaluate(FirstKey, 0.2, 0);
            Accept(coordinator, FirstKey, 0.3, 0, 1);

            Assert.That(coordinator.Commit(weaker, 1, out _), Is.EqualTo(FeedbackCommitResult.StaleEvaluation));
            Assert.That(coordinator.Evaluate(FirstKey, 0.25, 0).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
        }

        [Test]
        public void CrossCoordinatorEvaluationAndHandleCannotMutateAnotherScope()
        {
            var first = new FeedbackCoordinator(1);
            var second = new FeedbackCoordinator(1);
            var evaluation = first.Evaluate(FirstKey, 0.1, 0);
            Assert.That(second.Commit(evaluation, 1, out _), Is.EqualTo(FeedbackCommitResult.WrongCoordinator));
            var handle = Accept(first, FirstKey, 0.1, 0, 1);
            Accept(second, FirstKey, 0.1, 0, 1);

            Assert.That(second.Cancel(handle), Is.False);
            Assert.That(second.Count, Is.EqualTo(1));
        }

        [Test]
        public void OldHandleCannotCancelAStrongerReplacement()
        {
            var coordinator = new FeedbackCoordinator(1);
            var oldHandle = Accept(coordinator, FirstKey, 0.1, 0, 1);
            var newHandle = Accept(coordinator, FirstKey, 0.2, 0, 1);

            Assert.That(coordinator.Cancel(oldHandle), Is.False);
            Assert.That(coordinator.Cancel(newHandle), Is.True);
            Assert.That(coordinator.Cancel(newHandle), Is.False);
            Assert.That(coordinator.Count, Is.Zero);
        }

        [Test]
        public void ExpiredHandleCannotCancelAReusedSlot()
        {
            var coordinator = new FeedbackCoordinator(1);
            var oldHandle = Accept(coordinator, FirstKey, 0.1, 0, 1);
            Accept(coordinator, SecondKey, 0.2, 1, 2);

            Assert.That(coordinator.Cancel(oldHandle), Is.False);
            Assert.That(coordinator.Count, Is.EqualTo(1));
        }

        [Test]
        public void CancelInvalidatesPendingEvaluations()
        {
            var coordinator = new FeedbackCoordinator(2);
            var handle = Accept(coordinator, FirstKey, 0.1, 0, 1);
            var delayed = coordinator.Evaluate(SecondKey, 0.1, 0);
            coordinator.Cancel(handle);

            Assert.That(coordinator.Commit(delayed, 1, out _), Is.EqualTo(FeedbackCommitResult.StaleEvaluation));
        }

        [Test]
        public void ResetInvalidatesOldHandlesAndEvaluationsEvenWithReusedKeyAndClock()
        {
            var coordinator = new FeedbackCoordinator(1);
            var oldHandle = Accept(coordinator, FirstKey, 0.1, 5, 6);
            var oldEvaluation = coordinator.Evaluate(FirstKey, 0.2, 5);
            coordinator.Reset();
            Accept(coordinator, FirstKey, 0.1, 0, 1);

            Assert.That(coordinator.Cancel(oldHandle), Is.False);
            Assert.That(coordinator.Commit(oldEvaluation, 6, out _), Is.EqualTo(FeedbackCommitResult.StaleEvaluation));
            Assert.That(coordinator.Count, Is.EqualTo(1));
        }

        [Test]
        public void BackwardsClockDoesNotClearOrChangeCommittedState()
        {
            var coordinator = new FeedbackCoordinator(1);
            Accept(coordinator, FirstKey, 0.1, 10, 11);

            Assert.That(coordinator.Evaluate(FirstKey, 0.2, 9).Decision, Is.EqualTo(FeedbackDecision.ClockMovedBackwards));
            Assert.That(coordinator.Evaluate(FirstKey, 0.1, 10).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
            Assert.That(coordinator.Count, Is.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidStrengthDoesNotConsumeCapacity(double strength)
        {
            var coordinator = new FeedbackCoordinator(1);
            Assert.That(coordinator.Evaluate(FirstKey, strength, 0).Decision, Is.EqualTo(FeedbackDecision.InvalidStrength));
            Assert.That(coordinator.Count, Is.Zero);
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidClockDoesNotChangeTimeOrigin(double now)
        {
            var coordinator = new FeedbackCoordinator(1);
            Assert.That(coordinator.Evaluate(FirstKey, 0.1, now).Decision, Is.EqualTo(FeedbackDecision.InvalidTime));
            Assert.That(coordinator.Evaluate(FirstKey, 0.1, 0).CanPlay, Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidRetentionDoesNotConsumeTheProposal(double retainUntil)
        {
            var coordinator = new FeedbackCoordinator(1);
            var evaluation = coordinator.Evaluate(FirstKey, 0.1, 0);
            Assert.That(coordinator.Commit(evaluation, retainUntil, out _), Is.EqualTo(FeedbackCommitResult.InvalidRetention));
            Assert.That(coordinator.Count, Is.Zero);
            Assert.That(coordinator.Commit(evaluation, 1, out _), Is.EqualTo(FeedbackCommitResult.Committed));
        }

        [Test]
        public void DefaultAndRejectedProposalsCannotBeCommitted()
        {
            var coordinator = new FeedbackCoordinator(1);
            Assert.That(default(FeedbackEvaluation).CanPlay, Is.False);
            Assert.That(coordinator.Commit(default, 1, out _), Is.EqualTo(FeedbackCommitResult.NotAccepted));
            var invalid = coordinator.Evaluate(default, 1, 0);
            Assert.That(invalid.Decision, Is.EqualTo(FeedbackDecision.InvalidKey));
            Assert.That(coordinator.Commit(invalid, 1, out _), Is.EqualTo(FeedbackCommitResult.NotAccepted));
            Assert.That(coordinator.Cancel(default), Is.False);
        }

        [Test]
        public void KeyUsesFullWidthIdsAndRejectsOnlyInvalidIdentityValues()
        {
            var key = new FeedbackKey(long.MaxValue, long.MaxValue);
            Assert.That(key.IsValid, Is.True);
            Assert.That(key, Is.EqualTo(new FeedbackKey(long.MaxValue, long.MaxValue)));
            Assert.That(key.GetHashCode(), Is.EqualTo(new FeedbackKey(long.MaxValue, long.MaxValue).GetHashCode()));
            Assert.That(key == new FeedbackKey(long.MaxValue, long.MaxValue), Is.True);
            Assert.That(key != new FeedbackKey(long.MaxValue, 0), Is.True);
            Assert.That(new FeedbackKey(1, 0).IsValid, Is.True);
            Assert.That(new FeedbackKey(0, 1).IsValid, Is.False);
            Assert.That(new FeedbackKey(-1, 1).IsValid, Is.False);
            Assert.That(new FeedbackKey(1, -1).IsValid, Is.False);
        }

        [Test]
        public void ZeroStrengthIsLeftForTheConsumerToInterpret()
        {
            var coordinator = new FeedbackCoordinator(1);
            Accept(coordinator, FirstKey, 0, 0, 1);
            Assert.That(coordinator.Evaluate(FirstKey, 0, 0).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
            Assert.That(coordinator.Evaluate(FirstKey, 0.1, 0).Decision, Is.EqualTo(FeedbackDecision.Stronger));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveCapacityIsRejected(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FeedbackCoordinator(capacity));
        }

        private static FeedbackHandle Accept(FeedbackCoordinator coordinator, FeedbackKey key,
            double strength, double now, double retainUntil)
        {
            var evaluation = coordinator.Evaluate(key, strength, now);
            Assert.That(evaluation.CanPlay, Is.True);
            Assert.That(coordinator.Commit(evaluation, retainUntil, out var handle),
                Is.EqualTo(FeedbackCommitResult.Committed));
            return handle;
        }
    }
}
