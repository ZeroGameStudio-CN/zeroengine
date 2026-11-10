using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class VisualEffectBudgetTests
    {
        [Test]
        public void ManyDifferentVariants_ShareFrameAndWindowBudgets()
        {
            var budget = new VisualEffectBudget<int>();
            var settings = new VisualEffectSettings(3, 5, 64, .1, .01);
            int accepted = 0;
            for (int i = 0; i < 100; i++) if (budget.TryAcquire(i, settings, .02, 1, false, out _)) accepted++;
            Assert.That(accepted, Is.EqualTo(3));
            for (int i = 100; i < 200; i++) if (budget.TryAcquire(i, settings, .04, 2, false, out _)) accepted++;
            Assert.That(accepted, Is.EqualTo(5));
            Assert.That(budget.TryAcquire(201, settings, .06, 3, false, out _), Is.False);
            Assert.That(budget.TryAcquire(201, settings, .13, 4, false, out _), Is.True);
        }

        [Test]
        public void RepeatedVariant_IsMergedWithinTheSameTimeBucket()
        {
            var budget = new VisualEffectBudget<int>();
            var settings = VisualEffectSettings.For(VisualDetailLevel.Simplified);
            Assert.That(budget.TryAcquire(1, settings, .01, 1, false, out var lease), Is.True);
            Assert.That(budget.TryAcquire(1, settings, .02, 2, false, out _), Is.False);
            budget.Release(lease);
            Assert.That(budget.TryAcquire(1, settings, .17, 3, false, out _), Is.True);
        }

        [Test]
        public void ActiveLifetimeBudget_RecoversOnlyWhenItsOwnLeaseEnds()
        {
            var budget = new VisualEffectBudget<int>();
            var settings = new VisualEffectSettings(8, 16, 1, .1, .01);
            Assert.That(budget.TryAcquire(1, settings, .01, 1, false, out var lease), Is.True);
            Assert.That(budget.TryAcquire(2, settings, .2, 2, false, out _), Is.False);
            Assert.That(budget.Release(lease), Is.True);
            Assert.That(budget.Release(lease), Is.False);
            Assert.That(budget.TryAcquire(2, settings, .21, 3, false, out _), Is.True);
        }

        [Test]
        public void FullAndImportantEffects_RemainAvailableAtBudgetCapacity()
        {
            var budget = new VisualEffectBudget<int>(1);
            var minimal = VisualEffectSettings.For(VisualDetailLevel.Minimal);
            Assert.That(budget.TryAcquire(1, minimal, .01, 1, false, out _), Is.True);
            Assert.That(budget.TryAcquire(2, minimal, .02, 2, true, out var important), Is.True);
            Assert.That(important.IsTracked, Is.False);
            Assert.That(budget.TryAcquire(3, VisualEffectSettings.For(VisualDetailLevel.Full), .03, 3, false, out _), Is.True);
            Assert.That(budget.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetAndForeignLease_CannotReleaseANewerLifetime()
        {
            var first = new VisualEffectBudget<int>(); var second = new VisualEffectBudget<int>();
            var settings = VisualEffectSettings.For(VisualDetailLevel.Simplified);
            first.TryAcquire(1, settings, .01, 1, false, out var stale);
            first.Reset();
            first.TryAcquire(1, settings, .01, 1, false, out var current);
            Assert.That(first.Release(stale), Is.False);
            Assert.That(second.Release(current), Is.False);
            Assert.That(first.ActiveCount, Is.EqualTo(1));
            Assert.That(first.Release(current), Is.True);
        }

        [Test]
        public void DensityAndOpacity_AreIndependentAndProtectedEffectsKeepTheirStrength()
        {
            Assert.That(VisualEffectSettings.Opacity(.2f, true), Is.EqualTo(.2f));
            Assert.That(VisualEffectSettings.Opacity(0f, false), Is.EqualTo(1f));
            Assert.That(VisualEffectSettings.Opacity(0f, true, true), Is.EqualTo(1f));
            Assert.That(VisualEffectSettings.Opacity(float.NaN, true), Is.EqualTo(1f));
        }

        [Test]
        public void BackwardsTime_DoesNotResetOrEvadeTheBudget()
        {
            var budget = new VisualEffectBudget<int>();
            var settings = VisualEffectSettings.For(VisualDetailLevel.Simplified);
            budget.TryAcquire(1, settings, 1, 1, false, out _);
            Assert.That(budget.TryAcquire(2, settings, .5, 2, false, out _), Is.False);
            Assert.That(budget.ActiveCount, Is.EqualTo(1));
        }
    }
}
