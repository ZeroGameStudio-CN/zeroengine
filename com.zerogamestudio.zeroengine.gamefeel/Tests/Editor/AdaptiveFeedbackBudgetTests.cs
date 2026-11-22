using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class AdaptiveFeedbackBudgetTests
    {
        private sealed class Run
        {
            internal readonly AdaptiveFeedbackBudget Budget;
            internal double Now;
            internal float Minimum = 1f;
            internal Run(float ceiling = 1, bool automatic = true)
            {
                Budget = new AdaptiveFeedbackBudget(); Budget.Configure(ceiling, automatic);
            }
            internal void Frames(int count, double elapsed = 1d / 60d, double target = 1d / 60d, bool eligible = true)
            {
                for (int i = 0; i < count; i++)
                {
                    Now += elapsed; Budget.ObserveFrame(Now, elapsed, target, eligible);
                    Minimum = Math.Min(Minimum, Budget.Scale);
                }
            }
            internal void Roots(int count) { for (int i = 0; i < count; i++) Budget.EffectStarted(); }
            internal void Return(int count) { for (int i = 0; i < count; i++) Budget.EffectStopped(); }
        }

        [Test]
        public void ManualModeAndZeroCeiling_BypassObservationAndKeepThePlayerChoice()
        {
            var manual = new Run(.5f, false); manual.Roots(200); manual.Frames(600, .05);
            Assert.That(manual.Budget.Scale, Is.EqualTo(1)); Assert.That(manual.Budget.Samples, Is.Zero);
            Assert.That(manual.Budget.ActiveEffects, Is.Zero);
            Assert.That(manual.Budget.Effects.ActiveLimit, Is.EqualTo(new CombatPresentationSettings(.5f).Effects.ActiveLimit));
            var off = new Run(0); off.Roots(200); off.Frames(600, .05);
            Assert.That(off.Budget.Enabled, Is.False); Assert.That(off.Budget.Player.Disabled, Is.True);
        }

        [Test]
        public void CrowdingOnlyTightensFutureBudgets_AndNeverRaisesThePlayerCeiling()
        {
            var run = new Run(.5f); var original = run.Budget.Player.Effects;
            run.Roots(120); run.Frames(1200);
            Assert.That(run.Budget.Scale, Is.LessThan(1)); Assert.That(run.Budget.Scale, Is.GreaterThanOrEqualTo(.35f));
            Assert.That(run.Budget.Player.Density, Is.EqualTo(.5f));
            Assert.That(run.Budget.Effects.ActiveLimit, Is.LessThanOrEqualTo(original.ActiveLimit));
            Assert.That(run.Budget.Effects.WindowLimit, Is.LessThanOrEqualTo(original.WindowLimit));
            Assert.That(run.Budget.Effects.FrameLimit, Is.LessThanOrEqualTo(original.FrameLimit));
            Assert.That(run.Budget.Effects.MergeSeconds, Is.GreaterThanOrEqualTo(original.MergeSeconds));
        }

        [Test]
        public void FullCeilingStaysUnlimitedUntilSustainedPressureExists()
        {
            var run = new Run(); run.Roots(3); run.Frames(600);
            Assert.That(run.Budget.Effects.Unlimited, Is.True);
            run.Roots(100); run.Frames(360);
            Assert.That(run.Budget.Effects.Unlimited, Is.False);
            Assert.That(run.Budget.Player.Full, Is.True);
        }

        [Test]
        public void AShortHitchAndLoadingOrPauseDoNotEraseEffects()
        {
            var run = new Run(); run.Roots(8); run.Frames(200);
            run.Frames(1, .18); run.Frames(180);
            Assert.That(run.Budget.Scale, Is.EqualTo(1));
            run.Frames(200, .05, eligible: false); run.Frames(180);
            Assert.That(run.Budget.Scale, Is.EqualTo(1));
        }

        [Test]
        public void ASelectedThirtyFpsCapIsNotMisclassifiedAsSlowdown()
        {
            var run = new Run(); run.Roots(12); run.Frames(900, 1d / 30d, 1d / 30d);
            Assert.That(run.Budget.Scale, Is.EqualTo(1));
        }

        [Test]
        public void SlowFramesWithNoOptionalEffectsDoNotTriggerAVisualReduction()
        {
            var run = new Run(); run.Frames(400, .05);
            Assert.That(run.Budget.Scale, Is.EqualTo(1));
        }

        [Test]
        public void AReductionWithoutFrameTimeBenefitStopsFurtherPerformanceCuts()
        {
            var run = new Run(); run.Roots(8); run.Frames(600, .05);
            Assert.That(run.Minimum, Is.GreaterThanOrEqualTo(.899f));
            Assert.That(run.Minimum, Is.LessThan(1));
        }

        [Test]
        public void MeasurableFrameTimeBenefitAllowsTheNextControlledReduction()
        {
            var run = new Run(); run.Roots(8);
            for (int i = 0; i < 300 && run.Budget.Scale >= 1f; i++) run.Frames(1, .05);
            Assert.That(run.Budget.Scale, Is.LessThan(1));
            run.Frames(120, .032);
            Assert.That(run.Minimum, Is.LessThan(.9f));
        }

        [Test]
        public void RecoveryWaitsForStabilityAndReturnsGraduallyToTheOriginalChoice()
        {
            var run = new Run(.5f); run.Roots(120); run.Frames(900);
            float low = run.Budget.Scale; run.Return(120); run.Frames(120);
            Assert.That(run.Budget.Scale, Is.EqualTo(low));
            run.Frames(3600);
            Assert.That(run.Budget.Scale, Is.EqualTo(1));
            Assert.That(run.Budget.Player.Density, Is.EqualTo(.5f));
        }

        [Test]
        public void ConfigurationAndRoomResetInvalidateObservationEpochsAndKeepCountsNonnegative()
        {
            var run = new Run(); run.Roots(50); int epoch = run.Budget.Epoch;
            run.Budget.Configure(.5f, true);
            Assert.That(run.Budget.Epoch, Is.Not.EqualTo(epoch)); Assert.That(run.Budget.ActiveEffects, Is.Zero);
            run.Return(100); Assert.That(run.Budget.ActiveEffects, Is.Zero);
            epoch = run.Budget.Epoch; run.Budget.Reset();
            Assert.That(run.Budget.Epoch, Is.Not.EqualTo(epoch)); Assert.That(run.Budget.Player.Density, Is.EqualTo(.5f));
            run.Budget.Configure(.5f, false); Assert.That(run.Budget.Scale, Is.EqualTo(1));
        }

        [Test]
        public void InvalidTimingAndFrameTargetChangesStartFreshObservationWindows()
        {
            var run = new Run(); run.Roots(8);
            run.Budget.ObserveFrame(double.NaN, .05, .016, true);
            run.Frames(200, .04, .04); run.Frames(100, .02, .02);
            Assert.That(run.Budget.Scale, Is.EqualTo(1));
        }

        [Test]
        public void AdaptiveFeedbackUsesTheSameDensityCurve_ForVisualsNumbersAndFlashes()
        {
            var run = new Run(); run.Roots(140); run.Frames(1200);
            var resolved = new CombatPresentationSettings(run.Budget.Scale);
            Assert.That(run.Budget.Feedback.Density, Is.EqualTo(resolved.Density));
            Assert.That(run.Budget.Effects.ActiveLimit, Is.EqualTo(resolved.Effects.ActiveLimit));
            Assert.That(run.Budget.Feedback.DamageMergeSeconds(.15f), Is.GreaterThan(.4f));
            Assert.That(run.Budget.Feedback.RepeatedFlashSeconds, Is.GreaterThan(.25));
            Assert.That(run.Budget.Player.Density, Is.EqualTo(1));
            run.Budget.Configure(.5f, false);
            Assert.That(run.Budget.Feedback.Density, Is.EqualTo(.5f));
        }

        [Test]
        public void WarmFrameSamplingAndRootCounters_DoNotAllocate()
        {
            var run = new Run(); run.Frames(300);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
            {
                run.Budget.EffectStarted(); run.Budget.EffectStopped();
                run.Now += 1d / 60d; run.Budget.ObserveFrame(run.Now, 1d / 60d, 1d / 60d, true);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }
    }
}
