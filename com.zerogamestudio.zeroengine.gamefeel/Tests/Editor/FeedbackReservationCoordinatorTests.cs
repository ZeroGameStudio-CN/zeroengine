using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class FeedbackReservationCoordinatorTests
    {
        private readonly struct Variant : IEquatable<Variant>
        {
            internal readonly int Id;
            internal Variant(int id) => Id = id;
            public bool Equals(Variant other) => Id == other.Id;
            public override bool Equals(object obj) => obj is Variant other && Equals(other);
            public override int GetHashCode() => Id;
        }

        private static FeedbackKey Key(long attack = 1, long pulse = 1) => new(attack, pulse);
        private static Variant Effect(int id = 1) => new(id);

        private static FeedbackReservationCoordinator<Variant> Create(
            int variants = 4, int identities = 8, int reservations = 8) =>
            new(variants, identities, reservations, 8);

        private static FeedbackReservation<Variant> Begin(
            FeedbackReservationCoordinator<Variant> coordinator,
            Variant variant,
            FeedbackKey key,
            double now = 1,
            int frame = 1,
            int budget = 0,
            bool important = false)
        {
            Assert.That(coordinator.TryBegin(variant, key, important ? 2 : 1, now, frame, budget,
                important, out var reservation), Is.True);
            return reservation;
        }

        private static void Complete(
            FeedbackReservationCoordinator<Variant> coordinator,
            FeedbackReservation<Variant> reservation,
            bool output = true,
            double now = 1) =>
            Assert.That(coordinator.Complete(reservation, output, now),
                Is.EqualTo(FeedbackReservationCommitResult.Committed));

        [Test]
        public void SameVariantAndKeySuppressesDuplicateAfterOutputCommit()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, Effect(), Key()));
            Assert.That(coordinator.TryBegin(Effect(), Key(), 1, 1.1, 1, 0, false, out _), Is.False);
            Assert.That(coordinator.LastDecision, Is.EqualTo(FeedbackReservationDecision.Duplicate));
        }

        [Test]
        public void StrongerRequestIsAdmittedAndKeepsFirstTimestamp()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, Effect(), Key(), 1));
            Assert.That(coordinator.TryBegin(Effect(), Key(), 2, 1.1, 1, 0, true, out var stronger), Is.True);
            Assert.That(stronger.Decision, Is.EqualTo(FeedbackReservationDecision.Stronger));
            Assert.That(stronger.FirstAcceptedAt, Is.EqualTo(1));
            Complete(coordinator, stronger, true, 1.1);
        }

        [Test]
        public void FailedOutputCanRetryWithoutConsumingIdentity()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, Effect(), Key()), false);
            Complete(coordinator, Begin(coordinator, Effect(), Key(), 1.1), true, 1.1);
        }

        [Test]
        public void DifferentVariantsAndKeysRemainIndependent()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, Effect(1), Key()));
            Complete(coordinator, Begin(coordinator, Effect(2), Key()));
            Complete(coordinator, Begin(coordinator, Effect(1), Key(2)));
            Assert.That(coordinator.ActiveVariantCount, Is.EqualTo(2));
        }

        [Test]
        public void FrameBudgetBlocksDecorativeOutputButImportantOutputPasses()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, Effect(), Key(), budget: 1));
            Assert.That(coordinator.TryBegin(Effect(), Key(2), 1, 1, 1, 1, false, out _), Is.False);
            Assert.That(coordinator.LastDecision, Is.EqualTo(FeedbackReservationDecision.FrameBudgetExceeded));
            Complete(coordinator, Begin(coordinator, Effect(), Key(3), 1, 1, 1, true));
        }

        [Test]
        public void PendingDuplicateIsBlockedAndReservationReleases()
        {
            var coordinator = Create();
            var pending = Begin(coordinator, Effect(), Key());
            Assert.That(coordinator.TryBegin(Effect(), Key(), 1, 1, 1, 0, false, out _), Is.False);
            Complete(coordinator, pending, false);
            Complete(coordinator, Begin(coordinator, Effect(), Key(), 1.1), true, 1.1);
        }

        [Test]
        public void ReservationCapacityRejectsRecursiveReentry()
        {
            var coordinator = Create(reservations: 2);
            var first = Begin(coordinator, Effect(), Key());
            var second = Begin(coordinator, Effect(), Key(2));
            Assert.That(coordinator.TryBegin(Effect(), Key(3), 1, 1, 1, 0, false, out _), Is.False);
            Assert.That(coordinator.LastDecision, Is.EqualTo(FeedbackReservationDecision.ReservationCapacityExceeded));
            Complete(coordinator, first);
            Complete(coordinator, second);
        }

        [Test]
        public void FullVariantCacheFallsBackWithoutForgettingKnownVariant()
        {
            var coordinator = Create(variants: 1);
            Complete(coordinator, Begin(coordinator, Effect(1), Key()));
            Complete(coordinator, Begin(coordinator, Effect(2), Key(2)));
            Assert.That(coordinator.LastDecision, Is.EqualTo(FeedbackReservationDecision.CapacityFallback));
            Assert.That(coordinator.TryBegin(Effect(1), Key(), 1, 1.1, 1, 0, false, out _), Is.False);
        }

        [Test]
        public void FullIdentityLedgerFallsBackWithoutEvictingKnownIdentity()
        {
            var coordinator = Create(identities: 1);
            Complete(coordinator, Begin(coordinator, Effect(), Key(1)));
            Complete(coordinator, Begin(coordinator, Effect(), Key(2)));
            Assert.That(coordinator.LastDecision, Is.EqualTo(FeedbackReservationDecision.CapacityFallback));
            Assert.That(coordinator.TryBegin(Effect(), Key(1), 1, 1.1, 1, 0, false, out _), Is.False);
        }

        [Test]
        public void ZeroBudgetDisablesOnlyFrameBudget()
        {
            var coordinator = Create();
            for (int i = 0; i < 12; i++)
                Complete(coordinator, Begin(coordinator, Effect(), Key(i + 1), budget: 0));
            Assert.That(coordinator.FrameAcceptedCount, Is.EqualTo(12));
        }

        [Test]
        public void ResetInvalidatesOldReservationsAndStartsNewLifecycle()
        {
            var coordinator = Create();
            var old = Begin(coordinator, Effect(), Key());
            coordinator.Reset();
            Complete(coordinator, Begin(coordinator, Effect(), Key(), 0));
            Assert.That(coordinator.Complete(old, true, 0), Is.EqualTo(FeedbackReservationCommitResult.StaleReservation));
        }

        [Test]
        public void BackwardsClockResetsOldStateBeforeAcceptingNewOrigin()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, Effect(), Key(), 10), true, 10);
            Assert.That(coordinator.TryBegin(Effect(), Key(), 1, 1, 2, 0, false, out var reservation), Is.True);
            Assert.That(reservation.Decision, Is.EqualTo(FeedbackReservationDecision.New));
            Complete(coordinator, reservation, true, 1);
        }

        [Test]
        public void FullWidthVariantAndIdentityValuesStayDistinct()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, new Variant(int.MaxValue), new FeedbackKey(long.MaxValue, long.MaxValue)));
            Assert.That(coordinator.TryBegin(new Variant(int.MinValue), new FeedbackKey(long.MaxValue - 1, long.MaxValue), 1, 1.1, 1, 0, false, out _), Is.True);
        }

        [Test]
        public void WarmDuplicatePathMakesNoManagedAllocation()
        {
            var coordinator = Create();
            Complete(coordinator, Begin(coordinator, Effect(), Key()));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
                coordinator.TryBegin(Effect(), Key(), 1, 1, 1, 0, false, out _);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }
    }
}
