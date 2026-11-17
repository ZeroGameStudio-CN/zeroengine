using System;
using NUnit.Framework;
using ZeroEngine.Multiplayer.Motion;

namespace ZeroEngine.Multiplayer.Tests.Motion
{
    public sealed class MotionPredictionTests
    {
        private static MotionSnapshot Flight() => new() { Kind = MotionKind.Flight, StepSeconds = .02f,
            PhysicsScale = 1, DomainScale = 1, VelocityScale = 1, Velocity = new(10, 0), GravityDelay = -1, GravityDistance = -1 };
        private static MotionCurve Linear(float from, float to) => new() { Keys = new[] {
            new MotionCurveKey(0, from, to - from, to - from), new MotionCurveKey(1, to, to - from, to - from) } };

        [Test] public void Steering_UsesResolvedTargetAndPriorPhysicsHeading()
        {
            var s = Flight(); s.Features = FlightFeatures.DriveVelocity | FlightFeatures.TurnTowardPoint;
            s.BaseSpeed = 10; s.Target = new(0, 100); s.TurnRate = 100;
            var p = new MotionPrediction(s); var first = p.Sample(.02f);
            Assert.That(first.Position.X, Is.EqualTo(.2f).Within(1e-6));
            Assert.That(first.Position.Y, Is.EqualTo(0).Within(1e-6));
            Assert.That(first.Rotation, Is.EqualTo(2).Within(1e-6));
            var later = p.Sample(.5f);
            Assert.That(later.Rotation, Is.EqualTo(50).Within(.001));
            Assert.That(later.Position.Y, Is.GreaterThan(1));
        }
        [Test] public void SpeedCurve_PreservesDiscreteOriginalVelocitySteps()
        {
            var s = Flight(); s.Features = FlightFeatures.DriveVelocity | FlightFeatures.SpeedCurve;
            s.BaseSpeed = 20; s.SpeedCurve = Linear(1, 0); s.CurveDuration = 2;
            var p = new MotionPrediction(s).Sample(.5f);
            Assert.That(p.Position.X, Is.EqualTo(8.7f).Within(.0001));
            Assert.That(p.Velocity.X, Is.EqualTo(15).Within(.0001));
        }
        [Test] public void DelayedGravity_ChangesAfterTheDeclaredDomainBoundary()
        {
            var s = Flight(); s.Features = FlightFeatures.Gravity;
            s.Acceleration = new(0, -10); s.GravityDelay = 1;
            var p = new MotionPrediction(s);
            Assert.That(p.Sample(1).Position.Y, Is.EqualTo(0).Within(.0001));
            var fallen = p.Sample(1.2f);
            Assert.That(fallen.Position.Y, Is.EqualTo(-.22f).Within(.0001));
            Assert.That(fallen.Velocity.Y, Is.EqualTo(-2).Within(.0001));
        }
        [Test] public void DelayedGravity_DistanceUsesDisplacementFromOriginalAnchor()
        {
            var s = Flight(); s.Features = FlightFeatures.Gravity; s.Position = new(1, 0);
            s.Acceleration = new(0, -10); s.GravityDistance = 1; s.GravityOrigin = default;
            Assert.That(new MotionPrediction(s).Sample(.02f).Position.Y, Is.EqualTo(-.004f).Within(.00001));
        }
        [Test] public void Parabola_ClampsToEndpointWithoutOwningDespawn()
        {
            var s = Flight(); s.Kind = MotionKind.Parabola; s.ParabolaStart = default;
            s.ParabolaEnd = new(10, 0); s.ParabolaHeight = 5; s.ParabolaDuration = 1;
            var p = new MotionPrediction(s); var peak = p.Sample(.5f);
            Assert.That(peak.Position.X, Is.EqualTo(5)); Assert.That(peak.Position.Y, Is.EqualTo(5));
            var end = p.Sample(2); Assert.That(end.Position.X, Is.EqualTo(10)); Assert.That(end.Position.Y, Is.EqualTo(0));
            Assert.That(end.AtEndpoint, Is.True); Assert.That(end.Velocity.LengthSquared, Is.Zero);
        }
        [TestCase(true, 8, 150)] [TestCase(false, 12, 30)]
        public void FollowAnchor_UsesDisplayFacingAndOriginalMirrorPolicy(bool mirror, float x, float angle)
        {
            var s = Flight(); s.Kind = MotionKind.FollowAnchor; s.FollowOffset = new(2, 3); s.FollowAngle = 30;
            s.AnchorFacing = 1; s.InitialFacing = 1; s.MirrorWithFacing = mirror;
            var pose = new MotionPrediction(s).Follow(new(10, 20), -1);
            Assert.That(pose.Position.X, Is.EqualTo(x)); Assert.That(pose.Position.Y, Is.EqualTo(23));
            Assert.That(pose.Rotation, Is.EqualTo(angle));
        }
        [Test] public void FollowAnchor_ContinuesFromResolvedVelocityWithoutNewPackets()
        {
            var s = Flight(); s.Kind = MotionKind.FollowAnchor; s.AnchorFacing = s.InitialFacing = 1;
            s.AnchorPosition = new(5, 6); s.AnchorVelocity = new(2, 3);
            var pose = new MotionPrediction(s).Sample(.5f);
            Assert.That(pose.Position.X, Is.EqualTo(6)); Assert.That(pose.Position.Y, Is.EqualTo(7.5f));
        }
        [Test] public void Freeze_SeparatesLocalDomainFromPhysicsClock()
        {
            var s = Flight(); s.Features = FlightFeatures.DriveVelocity | FlightFeatures.SpeedCurve | FlightFeatures.Gravity;
            s.BaseSpeed = 20; s.SpeedCurve = Linear(1, 2); s.CurveDuration = 1;
            s.Position = new(3, 4); s.DomainScale = s.VelocityScale = 0; s.Acceleration = default; s.GravityActive = true;
            var frozen = new MotionPrediction(s).Sample(2);
            Assert.That(frozen.Position.X, Is.EqualTo(3)); Assert.That(frozen.Position.Y, Is.EqualTo(4));
        }
        [Test] public void SlowMotion_DoesNotApplyPhysicsScaleTwice()
        {
            var s = Flight(); s.PhysicsScale = .5f;
            Assert.That(new MotionPrediction(s).Sample(1).Position.X, Is.EqualTo(5).Within(.0001));
        }
        [TestCase(0, 30)] [TestCase(0, 60)] [TestCase(0, 144)]
        [TestCase(1, 30)] [TestCase(1, 60)] [TestCase(1, 144)]
        [TestCase(2, 30)] [TestCase(2, 60)] [TestCase(2, 144)]
        [TestCase(3, 30)] [TestCase(3, 60)] [TestCase(3, 144)]
        public void RenderRate_DoesNotChangeCommittedIntegration(int shape, int fps)
        {
            var s = Flight(); s.BaseSpeed = 10; s.Target = new(10, 20); s.TurnRate = 90;
            s.SpeedCurve = Linear(.2f, 1); s.CurveDuration = 1; s.Acceleration = new(0, -10); s.GravityDelay = .4f;
            s.Features = shape switch { 0 => FlightFeatures.TurnTowardPoint | FlightFeatures.DriveVelocity,
                1 => FlightFeatures.SpeedCurve | FlightFeatures.DriveVelocity, 2 => FlightFeatures.Gravity,
                _ => FlightFeatures.TurnTowardPoint | FlightFeatures.DriveVelocity | FlightFeatures.SpeedCurve | FlightFeatures.Gravity };
            var p = new MotionPrediction(s); for (int i = 1; i < fps; i++) p.Sample((float)i / fps);
            var actual = p.Sample(1.3f); var expected = new MotionPrediction(s).Sample(1.3f);
            Assert.That((actual.Position - expected.Position).LengthSquared, Is.LessThan(1e-9));
            Assert.That(actual.Rotation, Is.EqualTo(expected.Rotation).Within(1e-5));
        }
        [Test] public void WeightedCurve_InvertsHandleTimeBeforeEvaluatingValue()
        {
            var a = new MotionCurveKey(0, 0, 0, 4) { OutWeight = .5f, Weights = MotionKeyWeights.Out };
            var b = new MotionCurveKey(1, 1, 0, 0) { InWeight = .1f, Weights = MotionKeyWeights.In };
            var c = new MotionCurve { Keys = new[] { a, b } };
            Assert.That(c.Evaluate(.65f), Is.EqualTo(1.25f).Within(.00001));
        }
        [TestCase(MotionCurveWrap.Clamp, 0, 1)] [TestCase(MotionCurveWrap.Loop, .75f, .25f)] [TestCase(MotionCurveWrap.PingPong, .25f, .75f)]
        public void CurveWraps_RespectNegativeAndPostRangeTime(MotionCurveWrap wrap, float before, float after)
        {
            var c = Linear(0, 1); c.Before = c.After = wrap;
            Assert.That(c.Evaluate(-.25f), Is.EqualTo(before).Within(1e-6));
            Assert.That(c.Evaluate(1.25f), Is.EqualTo(after).Within(1e-6));
        }
        [Test] public void StepCurve_HoldsLeftValueUntilBoundary()
        {
            var c = Linear(1, 3); c.Keys[0].OutTangent = float.PositiveInfinity;
            Assert.That(c.IsValid, Is.True); Assert.That(c.Evaluate(.9f), Is.EqualTo(1)); Assert.That(c.Evaluate(1), Is.EqualTo(3));
        }
        [Test] public void RewindingAnObservation_DoesNotChangeLaterPrediction()
        {
            var s = Flight(); s.Features = FlightFeatures.Gravity; s.Acceleration = new(0, -10); s.GravityActive = true;
            var p = new MotionPrediction(s); p.Sample(2); p.Sample(.2f);
            Assert.That(p.Sample(1).Position.Y, Is.EqualTo(new MotionPrediction(s).Sample(1).Position.Y).Within(1e-6));
        }
        [Test] public void Continuation_HasABoundedHorizon()
        {
            var p = new MotionPrediction(Flight());
            Assert.That(p.Sample(100).Position.X, Is.EqualTo(30).Within(.0001));
        }
        [Test] public void InvalidSnapshots_AreRejectedBeforeIntegration()
        {
            var s = Flight(); s.StepSeconds = 0;
            Assert.That(s.IsValid, Is.False); Assert.Throws<ArgumentException>(() => new MotionPrediction(s));
            s = Flight(); s.Position.X = float.NaN; Assert.That(s.IsValid, Is.False);
            s = Flight(); s.Features = (FlightFeatures)128; Assert.That(s.IsValid, Is.False);
            s = Flight(); s.Features = FlightFeatures.SpeedCurve; Assert.That(s.IsValid, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionPrediction(Flight()).Sample(float.NaN));
        }
        [Test] public void InvalidCurveKeys_AreBoundedAndMonotonic()
        {
            var c = Linear(1, 2); c.Keys[1].Time = 0; Assert.That(c.IsValid, Is.False);
            c = Linear(1, 2); c.Keys[0].InWeight = float.NaN; Assert.That(c.IsValid, Is.False);
            c = new() { Keys = new MotionCurveKey[MotionCurve.MaximumKeys + 1] }; Assert.That(c.IsValid, Is.False);
        }
    }
}
