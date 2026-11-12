using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class VisualEffectCoalescerTests
    {
        [Test]
        public void DuplicatesKeepOneRepresentative_AndPromoteTheNextLiveOutput()
        {
            var groups = new VisualEffectCoalescer<int, object>();
            var first = new object(); var second = new object(); var third = new object();
            groups.TryAcquire(1, first, out var a, out bool visible); Assert.That(visible, Is.True);
            groups.TryAcquire(1, second, out var b, out visible); Assert.That(visible, Is.False);
            groups.TryAcquire(1, third, out var c, out visible); Assert.That(visible, Is.False);
            groups.Release(a, out var next); Assert.That(next, Is.SameAs(second));
            Assert.That(groups.IsRepresentative(b), Is.True);
            groups.Release(b, out next); Assert.That(next, Is.SameAs(third));
            Assert.That(groups.IsRepresentative(c), Is.True);
            groups.Release(c, out next); Assert.That(next, Is.Null); Assert.That(groups.GroupCount, Is.Zero);
        }

        [Test]
        public void DifferentKeysKeepIndependentCues_AndRemovingADuplicateDoesNotReplaceTheRepresentative()
        {
            var groups = new VisualEffectCoalescer<int, object>(); var item = new object();
            groups.TryAcquire(1, item, out var first, out _);
            groups.TryAcquire(1, item, out var duplicate, out _);
            groups.TryAcquire(2, item, out var separate, out bool visible); Assert.That(visible, Is.True);
            groups.Release(duplicate, out var next); Assert.That(next, Is.Null);
            Assert.That(groups.IsRepresentative(first) && groups.IsRepresentative(separate), Is.True);
        }

        [Test]
        public void ResetAndForeignOrReleasedLeasesCannotAffectANewerLifetime()
        {
            var groups = new VisualEffectCoalescer<int, object>(2); var foreign = new VisualEffectCoalescer<int, object>(2);
            var item = new object(); groups.TryAcquire(1, item, out var old, out _); groups.Reset();
            groups.TryAcquire(1, item, out var current, out _); foreign.TryAcquire(1, item, out var other, out _);
            Assert.That(groups.Release(old, out _), Is.False); Assert.That(groups.Release(other, out _), Is.False);
            Assert.That(groups.IsRepresentative(current), Is.True); groups.Release(current, out _);
            Assert.That(groups.Release(current, out _), Is.False); Assert.That(groups.Count, Is.Zero);
        }

        [Test]
        public void CapacityOverflowPreservesTheUntrackedCue_AndFreedSlotsAreReused()
        {
            var groups = new VisualEffectCoalescer<int, object>(1); var item = new object();
            groups.TryAcquire(1, item, out var a, out _);
            Assert.That(groups.TryAcquire(1, item, out var overflow, out bool visible), Is.False);
            Assert.That(visible, Is.True); Assert.That(overflow.IsTracked, Is.False);
            groups.Release(a, out _); Assert.That(groups.TryAcquire(2, item, out _, out visible), Is.True);
            Assert.That(visible, Is.True);
        }

        [Test]
        public void LimitedGroupsKeepTheirOwnRepresentatives_AndPromoteTheFirstWaitingOutput()
        {
            var groups = new VisualEffectCoalescer<int, object>(8, 2); var item = new object(); var next = new object();
            groups.TryAcquire(1, item, out var a, out _); groups.TryAcquire(1, item, out var b, out _);
            groups.TryAcquire(1, next, out var c, out bool visible); Assert.That(visible, Is.False);
            groups.Release(b, out var promoted); Assert.That(promoted, Is.SameAs(next));
            Assert.That(groups.IsRepresentative(a) && groups.IsRepresentative(c), Is.True);
            Assert.That(groups.RepresentativeCount, Is.EqualTo(2));
        }

        [Test]
        public void LimitChangesAndOutputReplacementKeepLeasesAndPromoteTheCurrentOutput()
        {
            var groups = new VisualEffectCoalescer<int, object>(8); var item = new object(); var replacement = new object();
            groups.TryAcquire(1, item, out var a, out _); groups.TryAcquire(1, item, out var b, out _);
            groups.TryAcquire(1, item, out var c, out _);
            groups.ConfigureMaximum(2); Assert.That(groups.IsRepresentative(b), Is.True);
            Assert.That(groups.IsRepresentative(c), Is.False);
            groups.ConfigureMaximum(1); Assert.That(groups.IsRepresentative(b), Is.False);
            groups.ReplaceOutput(b, replacement); groups.Release(a, out var promoted);
            Assert.That(promoted, Is.SameAs(replacement)); Assert.That(groups.IsRepresentative(b), Is.True);
            groups.Release(b, out _); groups.Release(c, out _); Assert.That(groups.RepresentativeCount, Is.Zero);
        }

        [Test]
        public void WarmRepresentativeChurnDoesNotAllocate()
        {
            var groups = new VisualEffectCoalescer<int, object>(4); var item = new object();
            for (int i = 0; i < 32; i++) { groups.TryAcquire(1, item, out var a, out _); groups.Release(a, out _); }
            groups.ConfigureMaximum(2); groups.ConfigureMaximum(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
            {
                groups.TryAcquire(1, item, out var a, out _); groups.TryAcquire(1, item, out var b, out _);
                groups.ConfigureMaximum(2); groups.ConfigureMaximum(1);
                groups.Release(a, out _); groups.Release(b, out _);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }
    }
}
