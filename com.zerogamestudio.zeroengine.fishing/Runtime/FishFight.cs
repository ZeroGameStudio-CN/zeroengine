using System;
using System.Numerics;
namespace ZeroEngine.Fishing
{
    [Serializable]
    public sealed class FishFightSettings
    {
        public float CalmSeconds=1.2f,WarningSeconds=.4f,BurstSeconds=.65f;
        public float Strength=1,Stamina=1,ExhaustionPerSecond=.12f,RecoveryPerSecond=.05f;
        public float LineStrength=2.5f,ReelLoad=.35f,OverloadGrace=.25f;
    }
    public enum FishEffort { Calm, Warning, Burst }
    // Continuous resistance for a host choosing a force-based fishing mode. Does not grant or spawn catches.
    public sealed class FishFight
    {
        private FishFightSettings settings;
        private float cycleAge,overload,heading;
        private int burst;
        public float Age {get;private set;}
        public float Stamina {get;private set;}
        public float Tension {get;private set;}
        public bool Lost {get;private set;}
        public FishEffort Effort {get;private set;}
        public Vector2 Force {get;private set;}
        public void Begin(FishFightSettings data,int seed)
        {
            data=data??new FishFightSettings();
            var values=new[]{data.CalmSeconds,data.WarningSeconds,data.BurstSeconds,data.Strength,data.Stamina,data.ExhaustionPerSecond,data.RecoveryPerSecond,data.LineStrength,data.ReelLoad,data.OverloadGrace};
            foreach(float v in values)if(!FishMath.Finite(v)||v<=0)throw new ArgumentException("Use finite positive fish and tackle settings.");
            settings=new FishFightSettings{CalmSeconds=data.CalmSeconds,WarningSeconds=data.WarningSeconds,BurstSeconds=data.BurstSeconds,Strength=data.Strength,Stamina=data.Stamina,
                ExhaustionPerSecond=data.ExhaustionPerSecond,RecoveryPerSecond=data.RecoveryPerSecond,LineStrength=data.LineStrength,ReelLoad=data.ReelLoad,OverloadGrace=data.OverloadGrace};
            Age=cycleAge=overload=Tension=0;Stamina=1;Force=Vector2.Zero;Lost=false;Effort=FishEffort.Calm;burst=0;heading=FishMath.Repeat(seed*.618034f,1)*2-1;
        }
        public void Advance(float delta,bool reeling,float slack,Vector2 towardsRod)
        {
            if(settings==null||Lost||!FishMath.Finite(delta)||delta<=0)return;
            if(!FishMath.Finite(slack)||!FishMath.Finite(towardsRod.X)||!FishMath.Finite(towardsRod.Y))return;
            int count=Math.Min(512,Math.Max(1,(int)Math.Ceiling(delta/.02f)));float step=delta/count;
            for(int i=0;i<count&&!Lost;i++)
            {
                Age+=step;cycleAge+=step;
                float cycle=settings.CalmSeconds+settings.WarningSeconds+settings.BurstSeconds;
                if(cycleAge>=cycle){cycleAge%=cycle;burst++;}
                Effort=cycleAge<settings.CalmSeconds?FishEffort.Calm:cycleAge<settings.CalmSeconds+settings.WarningSeconds?FishEffort.Warning:FishEffort.Burst;
                Stamina=FishMath.Clamp01(Stamina+step*(reeling?-settings.ExhaustionPerSecond/settings.Stamina:settings.RecoveryPerSecond));
                var away=-FishMath.SafeDirection(towardsRod);var sideways=new Vector2(-away.Y,away.X);
                float phase=Effort==FishEffort.Burst?1.35f:Effort==FishEffort.Warning?.65f:.3f;
                float drive=settings.Strength*phase*FishMath.Lerp(.25f,1,Stamina);
                var desired=(away+sideways*(float)Math.Sin(Age*1.5f+heading+burst)*.4f)*drive*(1-FishMath.Clamp01(slack)*.65f);
                Force=Vector2.Lerp(Force,desired,1-(float)Math.Exp(-5*step));
                float load=(Force.Length()+(reeling?settings.ReelLoad:0))/settings.LineStrength;
                Tension=FishMath.Lerp(Tension,FishMath.Clamp01(load),1-(float)Math.Exp(-8*step));
                overload=Tension>=.98f?overload+step:0;Lost=overload>=settings.OverloadGrace;
            }
        }
        public void Reset(){settings=null;Age=Stamina=Tension=cycleAge=overload=0;Force=Vector2.Zero;Lost=false;Effort=FishEffort.Calm;}
    }
}
