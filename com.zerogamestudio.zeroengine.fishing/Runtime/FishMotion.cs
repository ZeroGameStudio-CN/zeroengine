using System;
using System.Numerics;
namespace ZeroEngine.Fishing
{
    public sealed class FishMotion
    {
        private enum Motion { Swimming, ReservedSwimming, Probe, Approach, Bite, Returning, Reel, Hidden, Guided }
        private readonly Vector3 home;
        private readonly WaterBounds area;
        private readonly float speed, seed, depth, patrolDepth;
        private Motion motion;
        private Vector3 target, reelStart;
        private float reelAge, reelDuration, hiddenAge;
        private float guidedProgress;
        private Vector2 guidedVelocity;
        private FishSwimSettings ambient;
        private Vector2 ambientVelocity, ambientInfluence;
        private float ambientAnchor, ambientPhase, bodyFacing = 1, tailAge, ambientFloor;
        private bool solitary;
        private float interactionAge;
        private Vector3 interactionVelocity;
        public bool IsAmbient => motion == Motion.Swimming || motion == Motion.ReservedSwimming;
        public Vector2 AmbientVelocity => ambientVelocity;
        public float BodyFacing => ambient != null && (IsAmbient || motion == Motion.Probe || motion == Motion.Approach || motion == Motion.Returning) ? bodyFacing : Facing;
        public float SwimAngle => ambient == null ? FishMath.Sin(Age * 4 + seed) * 3
            : FishMath.Clamp(ambientVelocity.Y * 12, solitary ? -4 : -7, solitary ? 4 : 7) * Facing;
        public float TailBeat => FishMath.Sin((ambient == null ? Age * 9 : tailAge) + seed);
        public bool IsGuided => motion == Motion.Guided;
        public float GuidedAngle { get; private set; }
        public Vector3 Position { get; private set; }
        public float Age { get; private set; }
        public float Facing { get; private set; } = 1;
        public float Exposure { get; private set; } = 1;
        private float AmbientExposure => patrolDepth > 0
            ? FishMath.Lerp(.18f, 1, FishMath.SmoothStep(0, 1, .5f + FishMath.Sin(Age * (FishMath.PI / 9) + seed) * .5f)) : 1;
        private float SwimY => FishMath.Clamp(home.Y + FishMath.Sin(Age * 1.6f + seed) * .12f
            - (1 - AmbientExposure) * patrolDepth, area.yMin, area.yMax);
        private float SwimDepth => depth + (1 - AmbientExposure) * patrolDepth;
        public bool Available => motion == Motion.Swimming;
        public bool Reserved => motion == Motion.ReservedSwimming;
        public bool AtProbe => motion == Motion.Probe && Vector3.Distance(Position, target) < .2f;
        public bool Visible => motion != Motion.Hidden;
        public bool Biting => motion == Motion.Bite;
        public bool ReelReady => motion == Motion.Reel && reelAge >= reelDuration || IsGuided && guidedProgress >= 1;
        public float ReelProgress => IsGuided ? guidedProgress : motion == Motion.Reel ? reelAge / reelDuration : 0;
        public Vector2 ReelVelocity => IsGuided ? guidedVelocity : motion == Motion.Reel
            ? FishMath.XY(target - reelStart) / reelDuration + Vector2.UnitY * (2.8f * (1 - 2 * ReelProgress) / reelDuration)
            : Vector2.Zero;

        public FishMotion(Vector3 position, float speed, float seed, WaterBounds area, float depth, float patrolDepth = 0)
        {
            home = position;
            Position = home;
            this.speed = FishMath.Max(.1f, speed);
            this.seed = seed;
            this.area = area;
            this.depth = depth;
            this.patrolDepth = patrolDepth > 0 && !float.IsInfinity(patrolDepth) ? patrolDepth : 0;
            Exposure = AmbientExposure;
            if (this.patrolDepth > 0) Position = new Vector3(home.X, SwimY, SwimDepth);
            Facing = FishMath.FloorToInt(seed) % 2 == 0 ? 1 : -1;
        }

        public void ConfigureAmbient(FishSwimSettings settings, float anchor, float phase, bool solo)
        {
            ambient = settings != null && settings.Enabled ? settings : null;
            ambientAnchor = anchor; ambientPhase = phase; solitary = solo;
            ambientFloor = FishMath.Max(area.yMin, home.Y - .32f - patrolDepth);
            bodyFacing = Facing; ambientVelocity = new Vector2(Facing * speed * .55f, 0); ambientInfluence = Vector2.Zero;
        }
        public void SetAmbientInfluence(Vector2 influence) => ambientInfluence = influence;

        private void AdvanceAmbient(float delta)
        {
            float cycle = FishMath.Repeat(Age * (.075f + FishMath.Repeat(seed * .39f, 1) * .035f) + seed * .13f, 1);
            float pace = 1 + FishMath.Sin(Age * .85f + seed) * ambient.PaceVariation;
            if (cycle > .72f && cycle < .82f) pace = .16f;
            else if (cycle >= .88f) pace = ambient.BurstMultiplier;
            if (solitary) pace = FishMath.Min(.9f, pace) * .72f;
            float clock = Age * (solitary ? .13f : .25f) + ambientPhase;
            var goal = new Vector2(FishMath.Clamp(ambientAnchor + FishMath.Sin(clock) * area.width * .27f + FishMath.Sin(clock * .73f + seed) * .48f, area.xMin + .2f, area.xMax - .2f),
                FishMath.Clamp(SwimY + FishMath.Sin(clock * 1.17f + seed * .43f) * .18f, ambientFloor, area.yMax));
            var desired = FishMath.SafeDirection(goal - FishMath.XY(Position)) * speed * pace + ambientInfluence;
            float response = FishMath.Max(.1f, ambient.SteeringResponse);
            ambientVelocity = Vector2.Lerp(ambientVelocity, FishMath.ClampMagnitude(desired, speed * FishMath.Max(1, ambient.BurstMultiplier)), 1 - FishMath.Exp(-response * delta));
            var next = FishMath.XY(Position) + ambientVelocity * delta;
            Position = new Vector3(FishMath.Clamp(next.X, area.xMin, area.xMax), FishMath.Clamp(next.Y, ambientFloor, area.yMax), SwimDepth + FishMath.Sin(clock * .7f) * .04f);
            if (FishMath.Abs(ambientVelocity.X) > .035f) Facing = FishMath.Sign(ambientVelocity.X);
        }

        public void Reserve() { if (Available) motion = Motion.ReservedSwimming; }
        public void Probe(Vector3 center)
        {
            target = Clamp(center);
            if (motion != Motion.Probe)
            {
                interactionAge = 0; interactionVelocity = new Vector3(ambientVelocity.X, ambientVelocity.Y, 0);
                if (FishMath.Abs(target.X - Position.X) > .35f) Facing = FishMath.Sign(target.X - Position.X);
            }
            motion = Motion.Probe;
        }
        public void Approach(Vector3 bait) { target = Clamp(bait); motion = Motion.Approach; }
        public void Follow(Vector3 bait, bool biting)
        {
            if (motion != Motion.ReservedSwimming && motion != Motion.Probe && motion != Motion.Approach && motion != Motion.Bite) return;
            target = Clamp(bait);
            motion = biting ? Motion.Bite : Motion.Approach;
        }
        public void FollowMouth(Vector3 center, bool biting)
        {
            if (motion != Motion.ReservedSwimming && motion != Motion.Probe && motion != Motion.Approach && motion != Motion.Bite) return;
            // Keep one approach orientation: moving the pivot to align a long mouth must not flip it back.
            if (motion != Motion.Approach && motion != Motion.Bite && FishMath.Abs(center.X - Position.X) > .35f)
                Facing = FishMath.Sign(center.X - Position.X);
            // Only a reserved surface fish may rise to the float and share its water depth.
            target = new Vector3(FishMath.Clamp(center.X, area.xMin, area.xMax),
                FishMath.Max(area.yMin, center.Y), center.Z);
            motion = biting ? Motion.Bite : Motion.Approach;
        }
        public void BeginReel(Vector3 outlet, float duration)
        {
            if (motion == Motion.Reel || motion == Motion.Hidden) return;
            reelStart = Position;
            target = outlet;
            reelDuration = FishMath.Max(.1f, duration);
            reelAge = 0;
            motion = Motion.Reel;
        }
        public void Caught() { motion = Motion.Hidden; hiddenAge = 0; }
        public void Guide(Vector3 position, Vector2 velocity, float progress, float angle)
        {
            Position = position;
            guidedVelocity = velocity;
            guidedProgress = FishMath.Clamp01(progress);
            GuidedAngle = angle;
            if (FishMath.Abs(velocity.X) > .001f) Facing = FishMath.Sign(velocity.X);
            motion = Motion.Guided;
        }
        public void Release(float direction = 0)
        {
            if (motion == Motion.Hidden || motion == Motion.Swimming || motion == Motion.Returning) return;
            if (motion == Motion.ReservedSwimming) { motion = Motion.Swimming; return; }
            float side = direction == 0 ? -Facing : FishMath.Sign(direction);
            target = new Vector3(FishMath.Clamp(Position.X + side * 1.5f, area.xMin, area.xMax), home.Y, depth);
            interactionVelocity = new Vector3(ambientVelocity.X, ambientVelocity.Y, 0);
            motion = Motion.Returning;
        }
        public void Reset()
        {
            Age = 0; tailAge = 0; interactionAge = 0; interactionVelocity = Vector3.Zero; motion = Motion.Swimming; reelAge = hiddenAge = 0;
            Exposure = AmbientExposure;
            Position = patrolDepth > 0 ? new Vector3(home.X, SwimY, SwimDepth) : home;
            Facing = FishMath.FloorToInt(seed) % 2 == 0 ? 1 : -1;
            bodyFacing = Facing; ambientVelocity = new Vector2(Facing * speed * .55f, 0); ambientInfluence = Vector2.Zero;
        }

        public void Advance(float delta, float approachSpeed)
        {
            if (!(delta > 0) || float.IsInfinity(delta)) return;
            Age += delta;
            if (ambient != null) tailAge += delta * (5 + ambientVelocity.Length() * 4);
            bool patrolling = motion == Motion.Swimming || motion == Motion.ReservedSwimming || motion == Motion.Returning;
            Exposure = FishMath.MoveTowards(Exposure, patrolling ? AmbientExposure : 1, delta * .8f);
            if (IsGuided) return;
            Vector3 previous = Position;
            if (motion == Motion.Hidden)
            {
                hiddenAge += delta;
                if (hiddenAge < 1.2f) return;
                Position = new Vector3(area.xMin, SwimY, SwimDepth);
                Facing = 1; Exposure = 0;
                bodyFacing = 1; ambientVelocity = new Vector2(speed * .55f, 0);
                motion = Motion.Swimming;
            }
            if (motion == Motion.Reel)
            {
                reelAge = FishMath.Min(reelDuration, reelAge + delta);
                float t = reelAge / reelDuration;
                // One parabolic lift; its final velocity continues into the food's ordinary physics.
                Position = Vector3.Lerp(reelStart, target, t) + Vector3.UnitY * (2.8f * t * (1 - t));
                var position = Position;
                position.Z = FishMath.Lerp(reelStart.Z, target.Z, FishMath.SmoothStep(0, 1, FishMath.Min(1, t * 2)));
                Position = position;
            }
            else if (motion == Motion.Returning)
            {
                target.Y = SwimY;
                target.Z = SwimDepth;
                AdvanceInteraction(delta, approachSpeed * .4f);
                if (Vector3.Distance(Position, target) < .01f) motion = Motion.Swimming;
            }
            else if (motion == Motion.Probe)
            {
                interactionAge += delta;
                float delay = ambient == null ? 0 : ambient.ReactionDelay + FishMath.Repeat(seed * .371f,1) * ambient.ReactionVariation;
                if (ambient != null && interactionAge < delay) AdvanceAmbient(delta);
                else AdvanceInteraction(delta, approachSpeed * .4f);
            }
            else if (motion == Motion.Approach) AdvanceInteraction(delta, approachSpeed);
            else if (motion == Motion.Bite) Position = FishMath.MoveTowards(Position, target, FishMath.Max(.1f, approachSpeed) * delta);
            else if (ambient != null && ambient.Enabled) AdvanceAmbient(delta);
            else
            {
                float edge = Facing > 0 ? area.xMax : area.xMin;
                float x = FishMath.MoveTowards(Position.X, edge, speed * delta);
                Position = new Vector3(x, SwimY, SwimDepth);
                if (FishMath.Abs(x - edge) < .001f) Facing = -Facing;
            }
            if (ambient != null) bodyFacing = FishMath.MoveTowards(bodyFacing, Facing, delta * FishMath.Max(.1f,ambient.TurnSpeed));
            float direction = Position.X - previous.X;
            if (motion == Motion.Reel && FishMath.Abs(direction) > .001f ||
                motion == Motion.Returning && FishMath.Abs(target.X - previous.X) > .35f)
                Facing = direction > 0 ? 1 : -1;
        }


        private void AdvanceInteraction(float delta,float limit)
        {
            if (ambient == null) { Position=FishMath.MoveTowards(Position,target,FishMath.Max(.1f,limit)*delta); return; }
            int count=Math.Min(512,Math.Max(1,(int)Math.Ceiling(delta/.02f)));
            float step=delta/count;
            for(int i=0;i<count;i++)
            {
                var gap=target-Position; float distance=gap.Length();
                float acceleration=FishMath.Max(.1f,ambient.InteractionAcceleration);
                float maxSpeed=FishMath.Min(FishMath.Max(.1f,limit),speed*FishMath.Max(1,ambient.InteractionSpeedMultiplier));
                float braking=(float)Math.Sqrt(2*acceleration*distance);
                var desired=distance>.00001f?gap/distance*FishMath.Min(maxSpeed,braking):Vector3.Zero;
                interactionVelocity=FishMath.MoveTowards(interactionVelocity,desired,acceleration*step);
                var next=Position+interactionVelocity*step;
                if(Vector3.Dot(gap,target-next)<=0) {next=target;interactionVelocity=Vector3.Zero;}
                Position=new Vector3(FishMath.Clamp(next.X,area.xMin,area.xMax),FishMath.Clamp(next.Y,area.yMin,Math.Max(area.yMax,target.Y)),next.Z);
            }
        }

        private Vector3 Clamp(Vector3 point) => new(FishMath.Clamp(point.X, area.xMin, area.xMax), FishMath.Clamp(point.Y, area.yMin, area.yMax), depth);
    }
}
