using System;

namespace ZeroEngine.Multiplayer.Motion
{
    public enum MotionKind : byte { None, Flight, Parabola, FollowAnchor }
    [Flags] public enum FlightFeatures : byte { None, DriveVelocity = 1, SpeedCurve = 2, TurnTowardPoint = 4, Gravity = 8 }

    public struct MotionPose
    {
        public MotionVector2 Position, Velocity;
        public float Rotation;
        public bool AtEndpoint;
    }

    /// <summary>
    /// One authoritative resolved motion epoch. Targets and anchors are supplied data;
    /// evaluating a snapshot cannot select a target, query physics, cause damage, or end a lease.
    /// PhysicsScale maps wall seconds to the original physics clock; DomainScale independently
    /// maps wall seconds to local gameplay time. VelocityScale preserves resolved speed scaling.
    /// </summary>
    public struct MotionSnapshot
    {
        public MotionKind Kind;
        public double SampleTime;
        public FlightFeatures Features;
        public MotionVector2 Position, Velocity;
        public float Rotation, StepSeconds, PhaseSeconds, PhysicsScale, DomainScale, VelocityScale;
        public float BaseSpeed, CurveAge, CurveDuration, TurnAge, TurnRate, LinearDamping;
        public MotionCurve SpeedCurve, TurnCurve;
        public MotionVector2 Target, TargetVelocity, Acceleration, GravityOrigin;
        public bool GravityActive;
        public float GravityDelay, GravityDistance;
        public MotionVector2 ParabolaStart, ParabolaEnd;
        public float ParabolaAge, ParabolaDuration, ParabolaHeight;
        public MotionVector2 AnchorPosition, AnchorVelocity, FollowOffset;
        public int AnchorFacing, InitialFacing;
        public float FollowAngle;
        public bool MirrorWithFacing;
        public bool Active => Kind != MotionKind.None;
        public bool IsValid
        {
            get
            {
                if (Kind == MotionKind.None) return Features == 0 && SpeedCurve.Keys == null && TurnCurve.Keys == null;
                if ((byte)Kind > 3 || (byte)Features > 15 || double.IsNaN(SampleTime) || double.IsInfinity(SampleTime) || SampleTime < 0
                    || !Vector(Position, 100000) || !Vector(Velocity, 10000)
                    || !Bounded(Rotation, 100000) || !float.IsFinite(StepSeconds) || StepSeconds < .001f || StepSeconds > 1
                    || !Nonnegative(PhaseSeconds, StepSeconds) || !Nonnegative(PhysicsScale, 100)
                    || !Nonnegative(DomainScale, 100) || !Nonnegative(VelocityScale, 100)
                    || !Bounded(BaseSpeed, 10000) || !Nonnegative(CurveAge, 86400) || !Nonnegative(TurnAge, 86400)
                    || !Nonnegative(TurnRate, 100000) || !Nonnegative(LinearDamping, 100)
                    || !SpeedCurve.IsValid || !TurnCurve.IsValid || !Vector(Target, 100000) || !Vector(TargetVelocity, 10000)
                    || !Vector(Acceleration, 100000) || !Vector(GravityOrigin, 100000)
                    || !Bounded(GravityDelay, 86400) || GravityDelay < -1 || !Bounded(GravityDistance, 100000) || GravityDistance < -1) return false;
                if (Kind == MotionKind.Flight)
                    return (Features & FlightFeatures.SpeedCurve) == 0 || (Features & FlightFeatures.DriveVelocity) != 0
                        && SpeedCurve.Active && Nonnegative(CurveDuration, 86400) && CurveDuration > 0;
                if (Features != 0) return false;
                if (Kind == MotionKind.Parabola)
                    return Vector(ParabolaStart, 100000) && Vector(ParabolaEnd, 100000) && Nonnegative(ParabolaAge, 86400)
                        && Nonnegative(ParabolaDuration, 86400) && ParabolaDuration > 0 && Nonnegative(ParabolaHeight, 10000);
                return Vector(AnchorPosition, 100000) && Vector(AnchorVelocity, 10000) && Vector(FollowOffset, 10000)
                    && (AnchorFacing == 1 || AnchorFacing == -1) && (InitialFacing == 1 || InitialFacing == -1) && Bounded(FollowAngle, 100000);
            }
        }
        public bool SameRecipe(MotionSnapshot other)
        {
            if (Kind != other.Kind || Features != other.Features || StepSeconds != other.StepSeconds
                || PhysicsScale != other.PhysicsScale || DomainScale != other.DomainScale || VelocityScale != other.VelocityScale
                || BaseSpeed != other.BaseSpeed || CurveDuration != other.CurveDuration || TurnRate != other.TurnRate
                || LinearDamping != other.LinearDamping || GravityActive != other.GravityActive || GravityDistance != other.GravityDistance
                || (Acceleration - other.Acceleration).LengthSquared > 1e-8f || (GravityOrigin - other.GravityOrigin).LengthSquared > 1e-8f
                || ParabolaHeight != other.ParabolaHeight || ParabolaDuration != other.ParabolaDuration
                || (ParabolaStart - other.ParabolaStart).LengthSquared > 1e-8f || (ParabolaEnd - other.ParabolaEnd).LengthSquared > 1e-8f
                || (FollowOffset - other.FollowOffset).LengthSquared > 1e-8f || FollowAngle != other.FollowAngle
                || InitialFacing != other.InitialFacing || MirrorWithFacing != other.MirrorWithFacing) return false;
            return SameCurve(SpeedCurve, other.SpeedCurve) && SameCurve(TurnCurve, other.TurnCurve);
        }
        private static bool SameCurve(MotionCurve a, MotionCurve b)
        {
            if (a.Before != b.Before || a.After != b.After || (a.Keys?.Length ?? 0) != (b.Keys?.Length ?? 0)) return false;
            for (int i = 0; i < (a.Keys?.Length ?? 0); i++)
            {
                var x = a.Keys[i]; var y = b.Keys[i];
                if (x.Time != y.Time || x.Value != y.Value || x.InTangent != y.InTangent || x.OutTangent != y.OutTangent
                    || x.InWeight != y.InWeight || x.OutWeight != y.OutWeight || x.Weights != y.Weights) return false;
            }
            return true;
        }
        internal static bool Bounded(float x, float bound) => float.IsFinite(x) && Math.Abs(x) <= bound;
        private static bool Nonnegative(float x, float bound) => Bounded(x, bound) && x >= 0;
        private static bool Vector(MotionVector2 x, float bound) => x.IsFinite && Math.Abs(x.X) <= bound && Math.Abs(x.Y) <= bound;
    }
}
