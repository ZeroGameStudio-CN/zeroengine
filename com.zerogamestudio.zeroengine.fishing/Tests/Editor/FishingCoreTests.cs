using System;
using System.Numerics;
using NUnit.Framework;
namespace ZeroEngine.Fishing.Tests
{
    public sealed class FishingCoreTests
    {
        private static FishMotion Fish(float seed=.7f) => new FishMotion(new Vector3(-2,-1,-11),1,seed,new WaterBounds(-5,-3,5,0),-11);
        [Test] public void Cruise_ReplaysTheSameSeedAndNeverLeavesWater()
        {
            var a=Fish();var b=Fish();a.ConfigureAmbient(new FishSwimSettings(),0,.7f,false);b.ConfigureAmbient(new FishSwimSettings(),0,.7f,false);
            for(int i=0;i<1200;i++){a.Advance(.02f,7);b.Advance(.02f,7);Assert.That(a.Position,Is.EqualTo(b.Position));Assert.That(a.Position.X,Is.InRange(-5,5));Assert.That(a.Position.Y,Is.InRange(-3,0));}
            a.Reset();b=Fish();b.ConfigureAmbient(new FishSwimSettings(),0,.7f,false);
            for(int i=0;i<100;i++){a.Advance(.02f,7);b.Advance(.02f,7);}Assert.That(a.Position,Is.EqualTo(b.Position));
        }
        [Test] public void Reservation_DoesNotAttractBeforeTheLandedBait()
        {
            var a=Fish();var b=Fish();a.ConfigureAmbient(new FishSwimSettings(),0,.7f,false);b.ConfigureAmbient(new FishSwimSettings(),0,.7f,false);a.Reserve();
            for(int i=0;i<150;i++){a.Advance(.02f,7);b.Advance(.02f,7);}Assert.That(a.Position,Is.EqualTo(b.Position));
        }
        [Test] public void Probe_ReactsWithDelayAndBrakesWithoutTeleporting()
        {
            var a=Fish();a.ConfigureAmbient(new FishSwimSettings {ReactionDelay=.2f,ReactionVariation=.2f},0,.7f,false);a.Reserve();
            var target=new Vector3(1,-.6f,-11);float maxStep=0;float face=a.BodyFacing;
            for(int i=0;i<400;i++){a.Probe(target);var before=a.Position;a.Advance(.02f,7);maxStep=Math.Max(maxStep,Vector3.Distance(before,a.Position));Assert.That(Math.Abs(a.BodyFacing-face),Is.LessThanOrEqualTo(.091f));face=a.BodyFacing;}
            Assert.That(maxStep,Is.LessThan(.05f));Assert.That(a.AtProbe,Is.True);
        }
        [Test] public void LongMouth_CanReachAStableProbeWithoutAlternatingItsFacing()
        {
            var a=Fish();a.Reserve();for(int i=0;i<300;i++){a.Probe(new Vector3(-2-a.Facing*1.1f,-.5f,-11));a.Advance(.02f,7);}
            Assert.That(a.AtProbe,Is.True);Assert.That(a.Position.X+a.Facing*1.1f,Is.EqualTo(-2).Within(.02f));
        }
        [Test] public void Pause_FreezesEveryMotorValueAndCancellationReleasesTheReservation()
        {
            var a=Fish();a.ConfigureAmbient(new FishSwimSettings(),0,.7f,false);a.Advance(.3f,7);var pos=a.Position;float age=a.Age;
            foreach(var dt in new[]{0,-1,float.NaN,float.PositiveInfinity})a.Advance(dt,7);
            Assert.That(a.Position,Is.EqualTo(pos));Assert.That(a.Age,Is.EqualTo(age));a.Reserve();a.Release();Assert.That(a.Available,Is.True);
        }
        [Test] public void CandidateSelection_KeepsTheWinnerAndAllowsOnlyOneLargeNeighbour()
        {
            var candidates=new[]{new FishCandidate(7,Vector3.Zero,1.5f,true),new FishCandidate(8,Vector3.One,2,true),new FishCandidate(9,new Vector3(2,0,0),.6f,true),new FishCandidate(10,new Vector3(3,0,0),.5f,true)};
            var ids=SchoolSteering.SelectCandidates(7,Vector3.Zero,candidates,3,1);Assert.That(ids,Is.EqualTo(new[]{7,9,10}));
        }
        [Test] public void Separation_UsesOneSnapshotAndPushesCloseNeighboursApart()
        {
            var p=new[]{Vector3.Zero,new Vector3(.1f,0,0)};var v=new[]{Vector2.Zero,Vector2.Zero};var g=new[]{-1,-1};var active=new[]{true,true};var s=new FishSwimSettings();
            Assert.That(SchoolSteering.Calculate(0,p,v,g,active,s).X,Is.LessThan(0));Assert.That(SchoolSteering.Calculate(1,p,v,g,active,s).X,Is.GreaterThan(0));
        }
        [Test] public void Encounter_RequiresWaterThenInspectionThenRealMouthContact()
        {
            var value=new BaitEncounter();value.Begin(17,.5f,.3f);value.Advance(10,true,true,true);Assert.That(value.Phase,Is.EqualTo(BaitPhase.Airborne));
            value.Land();value.Advance(.01f,true,false,false);value.Advance(.6f,true,true,false);Assert.That(value.TargetsHook,Is.True);Assert.That(value.ReadyToHook,Is.False);
            value.Advance(.4f,true,true,false);Assert.That(value.ReadyToHook,Is.False);value.Advance(.15f,true,true,true);Assert.That(value.Nibble,Is.GreaterThan(.9f));
            value.Advance(.16f,true,true,true);Assert.That(value.ReadyToHook,Is.True);Assert.That(value.FishId,Is.EqualTo(17));
        }
        [Test] public void UnreachableHook_TimesOutAndCancellationDropsItsIdentity()
        {
            var value=new BaitEncounter {Timeout=2};value.Begin(4,.5f,.3f);value.Land();value.Advance(3,false,false,false);Assert.That(value.Phase,Is.EqualTo(BaitPhase.Cancelled));Assert.That(value.FishId,Is.EqualTo(-1));
        }
        [Test] public void Fight_WarnsBeforeEachBurstAndRecoversStaminaWhenReleased()
        {
            var fight=new FishFight();fight.Begin(new FishFightSettings {CalmSeconds=.2f,WarningSeconds=.15f,BurstSeconds=.2f,LineStrength=10},9);bool warning=false,burst=false;
            for(int i=0;i<30;i++){fight.Advance(.02f,true,0,Vector2.UnitX);warning|=fight.Effort==FishEffort.Warning;if(fight.Effort==FishEffort.Burst){Assert.That(warning,Is.True);burst=true;}}
            Assert.That(burst,Is.True);float stamina=fight.Stamina;fight.Advance(.5f,false,.5f,Vector2.UnitX);Assert.That(fight.Stamina,Is.GreaterThan(stamina));
        }
        [Test] public void Fight_FishMassAndTackleStrengthAffectLoadAndOverloadCanLoseFish()
        {
            var a=new FishFight();var b=new FishFight();a.Begin(new FishFightSettings {Strength=1,LineStrength=10},7);b.Begin(new FishFightSettings {Strength=2,LineStrength=10},7);
            for(int i=0;i<80;i++){a.Advance(.02f,true,0,Vector2.UnitY);b.Advance(.02f,true,0,Vector2.UnitY);}Assert.That(b.Tension,Is.GreaterThan(a.Tension));
            var weak=new FishFight();weak.Begin(new FishFightSettings {Strength=10,LineStrength=.3f,OverloadGrace=.1f},8);weak.Advance(2,true,0,Vector2.UnitY);Assert.That(weak.Lost,Is.True);
        }
        [Test] public void Fight_PauseAndResetHaveNoRetainedInputOrForce()
        {
            var f=new FishFight();f.Begin(new FishFightSettings(),1);f.Advance(.2f,true,0,Vector2.UnitY);var force=f.Force;float age=f.Age;
            foreach(var dt in new[]{0,-1,float.NaN,float.PositiveInfinity})f.Advance(dt,true,0,Vector2.UnitY);Assert.That(f.Force,Is.EqualTo(force));Assert.That(f.Age,Is.EqualTo(age));f.Reset();Assert.That(f.Force,Is.EqualTo(Vector2.Zero));Assert.That(f.Lost,Is.False);
        }

        [Test]
        public void Perception_DoesNotInspectAirborneOrIncompatibleLures()
        {
            var sense=new FishSenseSettings {SightRange=4,HearingRange=4,AvoidedLureMask=2};
            Assert.That(FishPerception.Inspect(Vector2.Zero,Vector2.UnitX,sense,new LureStimulus(Vector2.UnitX,false,1,1,1)).Interested,Is.False);
            Assert.That(FishPerception.Inspect(Vector2.Zero,Vector2.UnitX,sense,new LureStimulus(Vector2.UnitX,true,1,1,2)).Interested,Is.False);
        }
        [Test]
        public void Perception_SightHearingAndSpeciesPreferenceHaveDistinctEffects()
        {
            var sense=new FishSenseSettings {SightRange=4,HearingRange=4,PreferredLureMask=1};
            var plain=FishPerception.Inspect(Vector2.Zero,Vector2.UnitX,sense,new LureStimulus(Vector2.UnitX,true,1,0,0));
            var preferred=FishPerception.Inspect(Vector2.Zero,Vector2.UnitX,sense,new LureStimulus(Vector2.UnitX,true,1,0,1));
            Assert.That(plain.Seen,Is.True);Assert.That(plain.Heard,Is.False);Assert.That(preferred.Strength,Is.GreaterThan(plain.Strength));
            var heard=FishPerception.Inspect(Vector2.Zero,Vector2.UnitX,sense,new LureStimulus(Vector2.UnitX,true,1,1,0),true);
            Assert.That(heard.Seen,Is.False);Assert.That(heard.Heard,Is.True);
        }
        [Test]
        public void ThreatAvoidance_IsLocalAndCannotPullFishTowardThePredator()
        {
            var away=FishPerception.AvoidThreat(Vector2.Zero,Vector2.UnitX,2,1);
            Assert.That(away.X,Is.LessThan(0));Assert.That(away.Length(),Is.LessThanOrEqualTo(1));
            Assert.That(FishPerception.AvoidThreat(Vector2.Zero,new Vector2(3,0),2),Is.EqualTo(Vector2.Zero));
        }
    }
}