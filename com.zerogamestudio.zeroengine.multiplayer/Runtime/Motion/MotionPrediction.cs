using System;

namespace ZeroEngine.Multiplayer.Motion
{
    /// <summary>
    /// Render-only fixed-step continuation. Full steps are committed once; the fractional
    /// render remainder is evaluated on a copy, so frame rate never changes the history.
    /// </summary>
    public sealed class MotionPrediction
    {
        public const float MaximumContinuationSeconds = 3;
        private readonly MotionSnapshot source;
        private State committed;
        private int steps;
        private struct State
        {
            internal MotionVector2 Position, Velocity;
            internal float Rotation, CurveAge, TurnAge, GravityDelay;
            internal bool GravityActive;
        }
        public MotionPrediction(MotionSnapshot snapshot)
        {
            if (!snapshot.Active || !snapshot.IsValid) throw new ArgumentException("A valid active motion snapshot is required.", nameof(snapshot));
            source = snapshot; committed = Initial();
        }
        private State Initial() => new() { Position = source.Position, Velocity = source.Velocity, Rotation = source.Rotation,
            CurveAge = source.CurveAge, TurnAge = source.TurnAge, GravityDelay = source.GravityDelay, GravityActive = source.GravityActive };
        /// <summary>Suppress redundant pose revisions while retaining real authority corrections.</summary>
        public bool Matches(MotionSnapshot next, float positionTolerance = .05f, float angleTolerance = .5f, float velocityTolerance = .05f)
        {
            double age = next.SampleTime - source.SampleTime;
            if (!next.IsValid || !source.SameRecipe(next) || age < 0 || age > MaximumContinuationSeconds
                || !MotionSnapshot.Bounded(positionTolerance, 100) || positionTolerance < 0
                || !MotionSnapshot.Bounded(angleTolerance, 180) || angleTolerance < 0
                || !MotionSnapshot.Bounded(velocityTolerance, 10000) || velocityTolerance < 0) return false;
            var expected = Sample((float)age);
            if ((expected.Position - next.Position).LengthSquared > positionTolerance * positionTolerance
                || Math.Abs(MotionVector2.DeltaAngle(expected.Rotation, next.Rotation)) > angleTolerance) return false;
            if (source.Kind == MotionKind.Flight)
            {
                if ((expected.Velocity - next.Velocity).LengthSquared > velocityTolerance * velocityTolerance) return false;
                if ((source.Features & FlightFeatures.TurnTowardPoint) != 0
                    && ((source.Target + source.TargetVelocity * (float)age - next.Target).LengthSquared > positionTolerance * positionTolerance
                        || (source.TargetVelocity - next.TargetVelocity).LengthSquared > velocityTolerance * velocityTolerance)) return false;
                if (!source.GravityActive && (source.Features & FlightFeatures.Gravity) != 0
                    && Math.Abs(Math.Max(0, source.GravityDelay - (float)age * source.DomainScale) - Math.Max(0, next.GravityDelay)) > source.StepSeconds * source.DomainScale + .0001f) return false;
            }
            return true;
        }
        public MotionPose Sample(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            seconds = Math.Min(seconds, MaximumContinuationSeconds);
            if (source.Kind == MotionKind.Parabola) return Parabola(seconds);
            if (source.Kind == MotionKind.FollowAnchor)
                return Follow(source.AnchorPosition + source.AnchorVelocity * seconds, source.AnchorFacing);
            int whole = (int)Math.Floor((seconds + 1e-7f) / source.StepSeconds);
            if (whole < steps) { steps = 0; committed = Initial(); }
            while (steps < whole)
            {
                Step(ref committed, source.StepSeconds, steps * source.StepSeconds);
                steps++;
            }
            var render = committed;
            float remainder = Math.Max(0, seconds - whole * source.StepSeconds);
            if (remainder > 1e-7f) Step(ref render, remainder, whole * source.StepSeconds);
            return new() { Position = render.Position, Velocity = render.Velocity, Rotation = render.Rotation };
        }
        public MotionPose Follow(MotionVector2 anchor, int facing)
        {
            if (!anchor.IsFinite || facing != 1 && facing != -1) throw new ArgumentException("Invalid display anchor.");
            int mirroredFacing = source.MirrorWithFacing ? facing : source.InitialFacing;
            return new() { Position = anchor + new MotionVector2(source.FollowOffset.X * mirroredFacing, source.FollowOffset.Y),
                Rotation = mirroredFacing != source.InitialFacing ? 180 - source.FollowAngle : source.FollowAngle };
        }
        private MotionPose Parabola(float seconds)
        {
            float t = Math.Min(1, (source.ParabolaAge + seconds * source.DomainScale) / source.ParabolaDuration);
            MotionVector2 delta = source.ParabolaEnd - source.ParabolaStart;
            var p = source.ParabolaStart + delta * t;
            p.Y += 4 * source.ParabolaHeight * t * (1 - t);
            float rate = t >= 1 ? 0 : source.DomainScale / source.ParabolaDuration;
            return new() { Position = p, Velocity = (delta + new MotionVector2(0, 4 * source.ParabolaHeight * (1 - 2 * t))) * rate,
                Rotation = source.Rotation, AtEndpoint = t >= 1 };
        }
        private void Step(ref State state, float seconds, float elapsed)
        {
            float physics = seconds * source.PhysicsScale, domain = seconds * source.DomainScale;
            float rotation = state.Rotation;
            if ((source.Features & FlightFeatures.TurnTowardPoint) != 0 && domain > 0)
            {
                var target = source.Target + source.TargetVelocity * elapsed - state.Position;
                float rate = source.TurnCurve.Active ? source.TurnCurve.Evaluate(state.TurnAge) : source.TurnRate;
                state.TurnAge += domain;
                if (target.LengthSquared > 1e-12f && rate > 0)
                {
                    float delta = MotionVector2.DeltaAngle(rotation, MotionVector2.Angle(target));
                    state.Rotation = rotation + Math.Max(-rate * domain, Math.Min(rate * domain, delta));
                }
            }
            if ((source.Features & FlightFeatures.DriveVelocity) != 0)
            {
                float factor = 1;
                if ((source.Features & FlightFeatures.SpeedCurve) != 0)
                {
                    state.CurveAge += domain;
                    factor = source.SpeedCurve.Evaluate(Math.Min(1, state.CurveAge / source.CurveDuration));
                }
                state.Velocity = MotionVector2.Direction(rotation) * (source.BaseSpeed * source.VelocityScale * factor);
            }
            if ((source.Features & FlightFeatures.Gravity) != 0)
            {
                if (!state.GravityActive && (state.GravityDelay >= 0 && state.GravityDelay <= 1e-6f
                    || source.GravityDistance >= 0 && (state.Position - source.GravityOrigin).LengthSquared >= source.GravityDistance * source.GravityDistance))
                    state.GravityActive = true;
                if (state.GravityActive) state.Velocity += source.Acceleration * physics;
                if (state.GravityDelay > 0) state.GravityDelay = Math.Max(0, state.GravityDelay - domain);
            }
            state.Velocity /= 1 + source.LinearDamping * physics;
            state.Position += state.Velocity * physics;
        }
    }
}
