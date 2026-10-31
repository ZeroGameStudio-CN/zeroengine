using System;
using System.Numerics;
namespace ZeroEngine.Fishing
{
    internal static class FishMath
    {
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Sign(float x) => Math.Sign(x);
        public static float Abs(float x) => Math.Abs(x);
        public static float Min(float x,float y) => Math.Min(x,y);
        public static float Max(float x,float y) => Math.Max(x,y);
        public static int FloorToInt(float x) => (int)Math.Floor(x);
        public const float PI = (float)Math.PI;
        public static float Exp(float x) => (float)Math.Exp(x);
        public static float Clamp(float x,float lo,float hi) => Math.Max(lo,Math.Min(hi,x));
        public static float Clamp01(float x) => Clamp(x,0,1);
        public static float Lerp(float a,float b,float t) => a+(b-a)*Clamp01(t);
        public static float Repeat(float x,float length) => x-(float)Math.Floor(x/length)*length;
        public static float InverseLerp(float a,float b,float x) => a==b?0:Clamp01((x-a)/(b-a));
        public static float SmoothStep(float a,float b,float t) {t=Clamp01(t);return Lerp(a,b,t*t*(3-2*t));}
        public static float MoveTowards(float x,float target,float step) => Abs(target-x)<=step?target:x+Sign(target-x)*step;
        public static Vector2 SafeDirection(Vector2 v) => v.LengthSquared()>.000001f?Vector2.Normalize(v):Vector2.Zero;
        public static Vector2 ClampMagnitude(Vector2 v,float max) => v.LengthSquared()>max*max?SafeDirection(v)*max:v;
        public static Vector3 MoveTowards(Vector3 a,Vector3 b,float step) {var v=b-a;float d=v.Length();return d<=step||d<.000001f?b:a+v/d*step;}
        public static Vector2 MoveTowards(Vector2 a,Vector2 b,float step) {var v=b-a;float d=v.Length();return d<=step||d<.000001f?b:a+v/d*step;}
        public static bool Finite(float x) => !float.IsNaN(x)&&!float.IsInfinity(x);
        public static Vector2 XY(Vector3 v) => new Vector2(v.X,v.Y);
    }
    public readonly struct WaterBounds
    {
        public readonly float xMin,yMin,xMax,yMax;
        public float width => xMax-xMin;
        public WaterBounds(float left,float bottom,float right,float top)
        {
            if(!FishMath.Finite(left)||!FishMath.Finite(right)||!FishMath.Finite(bottom)||!FishMath.Finite(top)||right<=left||top<=bottom)
                throw new ArgumentException("Use a finite, positive water rectangle.");
            xMin=left;yMin=bottom;xMax=right;yMax=top;
        }
    }
}
