using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    // Core contracts: single/multiple contact semantics, channel isolation and bounded ownership.
    public sealed class FeedbackChannelPoliciesTests
    {
        private static FeedbackKey Key(long attack = 1, long pulse = 1, ulong target = 0) => new FeedbackKey(attack, pulse, target);

        [Test]
        public void ContactScopeRetainsFullWidthAndSeparatesTargetsWithoutSplittingGlobalOutput()
        {
            var batch = new FeedbackBatchCounter(8);
            Assert.That(batch.Observe(Key(target: 1), .5, 1, 2), Is.True);
            Assert.That(batch.Observe(Key(target: 0x100000001UL), .5, 1, 2), Is.True);
            Assert.That(batch.Observe(Key(target: 1), .7, 1, 2), Is.False);
            Assert.That(batch.Count, Is.EqualTo(2));
            var global = new FeedbackBatchCounter(8);
            Assert.That(global.Observe(Key(), .5, 1, 2), Is.True);
            Assert.That(global.Observe(Key(), .5, 1, 2), Is.False);
            Assert.That(global.Count, Is.EqualTo(1));
        }

        [Test]
        public void DisabledAudioWindowDoesNotCreateAnotherAggregationWindow()
        {
            var batch = new FeedbackBatchCounter(8);
            Assert.That(batch.Observe(Key(), .5, 1, 1), Is.True);
            Assert.That(batch.Observe(Key(), .5, 1, 1), Is.True);
            Assert.That(batch.Count, Is.EqualTo(2));
        }

        [Test]
        public void TargetPoolReleaseAndWorldResetNeverReuseOldContactIds()
        {
            var scopes = new FeedbackScopeIds<object>();
            object first = new object(), second = new object();
            ulong a = scopes.GetId(first), b = scopes.GetId(second);
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(scopes.GetId(first), Is.EqualTo(a));
            scopes.Release(first);
            ulong reused = scopes.GetId(first);
            Assert.That(reused, Is.Not.EqualTo(a));
            scopes.Reset();
            Assert.That(scopes.GetId(first), Is.Not.EqualTo(reused));
            Assert.That(scopes.GetId(null), Is.Zero);
            Assert.That(new FeedbackScopeIds<string>().GetId("different-target-kind"), Is.Not.EqualTo(scopes.GetId(first)));
        }

        [TestCase(.005)]
        [TestCase(.02)]
        [TestCase(.5)]
        public void SingleStopPreservesAuthoredDurationAndDoesNotAddAMinimum(double duration)
        {
            var state = new FeedbackDeadlineCoordinator();
            Assert.That(state.TryRequest(Key(), duration, false, 0, 1, out double deadline), Is.True);
            Assert.That(deadline, Is.EqualTo(1 + duration).Within(1e-9));
        }

        [Test]
        public void NextStopUsesActualRecoveryInsteadOfAnUnrelatedFixedCadence()
        {
            var state = new FeedbackDeadlineCoordinator(recoverySeconds: .01, upgradeBurstSeconds: .1);
            Assert.That(state.TryRequest(Key(), .005, false, 0, 1, out _), Is.True);
            Assert.That(state.TryRequest(Key(2), .005, false, 0, 1.016, out double deadline), Is.True);
            Assert.That(deadline, Is.EqualTo(1.021).Within(1e-9));
        }

        [Test]
        public void StopUpgradeKeepsFirstDeadlineAndDenseIndependentHitsLeaveRecovery()
        {
            var state = new FeedbackDeadlineCoordinator(recoverySeconds: .04, upgradeBurstSeconds: .12);
            state.TryRequest(Key(), .02, false, 0, 1, out _);
            Assert.That(state.TryRequest(Key(), .08, true, 0, 1.03, out double deadline), Is.True);
            Assert.That(deadline, Is.EqualTo(1.08).Within(1e-9));
            for (int i = 0; i < 8; i++)
            {
                state.TryRequest(Key(2 + i), .09 + i * .01, false, 0, 1.04 + i * .01, out deadline);
                Assert.That(deadline, Is.LessThanOrEqualTo(1.12 + 1e-9));
            }
            Assert.That(state.TryRequest(Key(99), .03, false, 0, 1.161, out _), Is.True);
        }

        [Test]
        public void RejectedOutputDoesNotSpendHapticIdentityOrAnotherChannelsAdmission()
        {
            var haptic = new FeedbackGroupGate();
            var camera = new FeedbackGroupGate();
            var first = haptic.Evaluate(Key(), 1, 1); // Sink rejects: no Commit.
            Assert.That(first.CanPlay, Is.True);
            var retry = haptic.Evaluate(Key(), 1, 1);
            Assert.That(retry.CanPlay, Is.True);
            Assert.That(haptic.Commit(retry, 1), Is.True);
            Assert.That(haptic.Evaluate(Key(), 1, 1).CanPlay, Is.False);
            Assert.That(camera.Evaluate(Key(), 1, 1).CanPlay, Is.True);
        }

        [Test]
        public void AudioLayersDistinguishSingleAndMultipleContactsWithinVoiceAndVolumeBounds()
        {
            var settings = new FeedbackAudioSettings(.4f, .8f, .75f, .7f, 8);
            var single = FeedbackAudioLayers.Evaluate(1, .9f, 1.1f, .05, settings, true, true);
            var multiple = FeedbackAudioLayers.Evaluate(8, .9f, 1.1f, .05, settings, true, true);
            Assert.That(single.ExtraPulses, Is.Zero);
            Assert.That(single.HasCluster, Is.False);
            Assert.That(single.HasWeight, Is.False);
            Assert.That(single.Pitch, Is.EqualTo(1.1f));
            Assert.That(multiple.ExtraPulses, Is.InRange(1, 2));
            Assert.That(multiple.HasCluster && multiple.HasWeight, Is.True);
            Assert.That(multiple.Volume, Is.GreaterThanOrEqualTo(single.Volume).And.LessThanOrEqualTo(1));
            Assert.That(multiple.Pitch, Is.LessThanOrEqualTo(single.Pitch));
        }

        [Test]
        public void PooledScaleCompositionIsIdempotentAndHdrAttenuationKeepsLowComponents()
        {
            Assert.That(default(FeedbackScaleState).Multiplier, Is.EqualTo(1));
            var state = FeedbackScaleState.Neutral;
            state.SetCoordinated(.6f); state.SetProfile(1.5f); state.SetCoordinated(.6f);
            Assert.That(state.Multiplier, Is.EqualTo(.9f).Within(1e-6));
            state.Reset(); Assert.That(state.Multiplier, Is.EqualTo(1));
            Assert.That(FeedbackPresentation.AttenuateExcess(5, .25f), Is.EqualTo(2));
            Assert.That(FeedbackPresentation.AttenuateExcess(.3f, .25f), Is.EqualTo(.3f));
            Assert.That(FeedbackPresentation.AttenuateExcess(-.3f, .25f), Is.EqualTo(-.3f));
        }

        [Test]
        public void OverlapModesRespectOccupancyAndAmplitudeBudgetWithoutATemporalGate()
        {
            Assert.That(FeedbackMixing.CanAccept(FeedbackMixingMode.Additive, .2f, .4f), Is.True);
            Assert.That(FeedbackMixing.ResolveStrength(.3f, .4f, .5f), Is.EqualTo(.1f).Within(1e-6));
            Assert.That(FeedbackMixing.ShouldReplace(FeedbackMixingMode.Replace, .2f, .4f), Is.True);
            Assert.That(FeedbackMixing.CanAccept(FeedbackMixingMode.KeepStrongest, .2f, .4f), Is.False);
            Assert.That(FeedbackMixing.CanAccept(FeedbackMixingMode.KeepStrongest, .6f, .4f), Is.True);
            Assert.That(FeedbackMixing.ResolveStrength(.3f, .4f, 0), Is.EqualTo(.3f));
        }
    }

    public sealed class FeedbackSignalCoordinatorTests
    {
        private sealed class Output { public bool Alive = true; public int Cancels; }
        private readonly struct Signal : IFeedbackSignal, IEquatable<Signal>
        {
            internal readonly Output Output;
            internal Signal(Output output) => Output = output;
            public bool IsPlaying => Output != null && Output.Alive;
            public void Cancel() { if (Output != null) { Output.Alive = false; Output.Cancels++; } }
            public bool Equals(Signal other) => ReferenceEquals(Output, other.Output);
        }

        [Test]
        public void DifferentGroupsCanStartTogetherAndSameGroupUpgradesKeepOutputIdentity()
        {
            var state = new FeedbackSignalCoordinator<Signal>(8);
            var key = new FeedbackKey(1, 1);
            var output = new Output();
            Assert.That(state.TryEvaluate(key, .3, false, 1, out var first), Is.True);
            Assert.That(state.Commit(first, new Signal(output), .3), Is.True);
            Assert.That(state.TryEvaluate(new FeedbackKey(2, 1), .3, false, 1, out var second), Is.True);
            Assert.That(state.Commit(second, new Signal(new Output()), .3), Is.True);
            Assert.That(state.TryEvaluate(key, .6, false, 1.01, out var upgrade), Is.True);
            Assert.That(upgrade.Upgrade, Is.True);
            Assert.That(state.PreviousSignal(upgrade).Output, Is.SameAs(output));
            Assert.That(state.Commit(upgrade, new Signal(output), .6), Is.True);
            Assert.That(output.Cancels, Is.Zero);
            Assert.That(state.TryEvaluate(key, .5, false, 1.02, out _), Is.False);
        }

        [Test]
        public void ExplicitPromotionCancelsFallbackOnlyAfterItsReplacementIsAccepted()
        {
            var state = new FeedbackSignalCoordinator<Signal>();
            var key = new FeedbackKey(1,1); var fallback = new Output();
            state.TryEvaluate(key, .5, true, 1, out var first);
            state.Commit(first, new Signal(fallback), .5);
            Assert.That(state.TryEvaluate(key, .5, false, 1.01, out var promotion), Is.True);
            Assert.That(promotion.Promotion, Is.True);
            Assert.That(state.Commit(promotion, default, .5), Is.False);
            Assert.That(fallback.Cancels, Is.Zero);
            Assert.That(state.TryEvaluate(key, .5, false, 1.01, out promotion), Is.True);
            Assert.That(state.Commit(promotion, new Signal(new Output()), .5), Is.True);
            Assert.That(fallback.Cancels, Is.EqualTo(1));
            Assert.That(state.TryEvaluate(key, .8, true, 1.02, out _), Is.False);
        }

        [Test]
        public void CompletedSignalCannotBeRevivedAndForeignOrStaleRequestsCannotCommit()
        {
            var state = new FeedbackSignalCoordinator<Signal>();
            var other = new FeedbackSignalCoordinator<Signal>();
            var output = new Output(); var key = new FeedbackKey(1,1);
            state.TryEvaluate(key, 1, false, 1, out var first);
            Assert.That(other.Commit(first, new Signal(output), 1), Is.False);
            Assert.That(state.Commit(first, new Signal(output), 1), Is.True);
            Assert.That(state.Commit(first, new Signal(new Output()), 1), Is.False);
            output.Alive = false;
            Assert.That(state.TryEvaluate(key, 2, false, 1.1, out _), Is.False);
            Assert.That(state.TryEvaluate(new FeedbackKey(2,1), 1, false, 1.1, out var next), Is.True);
            state.Reset();
            Assert.That(state.Commit(next, new Signal(new Output()), 1), Is.False);
        }
    }
}
