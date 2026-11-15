using System;

namespace ZeroEngine.Multiplayer.Motion
{
    public enum MotionCurveWrap : byte { Clamp, Loop, PingPong }
    [Flags] public enum MotionKeyWeights : byte { None, In = 1, Out = 2 }

    public struct MotionCurveKey
    {
        public float Time, Value, InTangent, OutTangent, InWeight, OutWeight;
        public MotionKeyWeights Weights;
        public MotionCurveKey(float time, float value, float inTangent = 0, float outTangent = 0)
        {
            Time = time; Value = value; InTangent = inTangent; OutTangent = outTangent;
            InWeight = OutWeight = 1f / 3; Weights = MotionKeyWeights.None;
        }
    }

    /// <summary>Bounded scalar curve recipe, including weighted handles and authored wrapping.</summary>
    public struct MotionCurve
    {
        public const int MaximumKeys = 64;
        public MotionCurveKey[] Keys;
        public MotionCurveWrap Before, After;
        public bool Active => Keys != null && Keys.Length != 0;
        public bool IsValid
        {
            get
            {
                if ((byte)Before > 2 || (byte)After > 2 || Keys != null && Keys.Length > MaximumKeys) return false;
                for (int i = 0; i < (Keys?.Length ?? 0); i++)
                {
                    var key = Keys[i];
                    if (!Bounded(key.Time, 86400) || !Bounded(key.Value, 10000) || !Tangent(key.InTangent) || !Tangent(key.OutTangent)
                        || !Bounded(key.InWeight, 1) || key.InWeight < 0 || !Bounded(key.OutWeight, 1) || key.OutWeight < 0
                        || (byte)key.Weights > 3 || i > 0 && key.Time <= Keys[i - 1].Time) return false;
                }
                return true;
            }
        }
        public float Evaluate(float time)
        {
            if (!Active) return 0;
            if (Keys.Length == 1) return Keys[0].Value;
            float first = Keys[0].Time, last = Keys[Keys.Length - 1].Time;
            if (time < first) time = Wrap(time, first, last, Before);
            if (time > last) time = Wrap(time, first, last, After);
            if (time <= first) return Keys[0].Value;
            if (time >= last) return Keys[Keys.Length - 1].Value;
            int low = 0, high = Keys.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (time < Keys[middle].Time) high = middle; else low = middle;
            }
            var a = Keys[low]; var b = Keys[high];
            // Infinite tangents describe an authored constant/step segment.
            if (float.IsInfinity(a.OutTangent) || float.IsInfinity(b.InTangent)) return a.Value;
            double duration = b.Time - a.Time, normalized = (time - a.Time) / duration;
            double wa = (a.Weights & MotionKeyWeights.Out) != 0 ? a.OutWeight : 1d / 3;
            double wb = (b.Weights & MotionKeyWeights.In) != 0 ? b.InWeight : 1d / 3;
            double u = normalized;
            if (a.Weights != MotionKeyWeights.None || b.Weights != MotionKeyWeights.None)
            {
                double lo = 0, hi = 1;
                for (int i = 0; i < 24; i++)
                {
                    double x = Bezier(0, wa, 1 - wb, 1, u);
                    if (Math.Abs(x - normalized) < 1e-8) break;
                    if (x < normalized) lo = u; else hi = u;
                    double slope = 3 * (1 - u) * (1 - u) * wa + 6 * (1 - u) * u * (1 - wb - wa) + 3 * u * u * wb;
                    double next = slope > 1e-10 ? u - (x - normalized) / slope : double.NaN;
                    u = next > lo && next < hi ? next : (lo + hi) * .5;
                }
            }
            return (float)Bezier(a.Value, a.Value + a.OutTangent * duration * wa, b.Value - b.InTangent * duration * wb, b.Value, u);
        }
        private static double Bezier(double a, double b, double c, double d, double t)
        { double r = 1 - t; return r * r * r * a + 3 * r * r * t * b + 3 * r * t * t * c + t * t * t * d; }
        private static float Wrap(float time, float first, float last, MotionCurveWrap mode)
        {
            if (mode == MotionCurveWrap.Clamp) return time < first ? first : last;
            double length = last - first, position = (time - first) / length;
            double turns = Math.Floor(position), fraction = position - turns;
            if (mode == MotionCurveWrap.PingPong && turns % 2 != 0) fraction = 1 - fraction;
            return (float)(first + fraction * length);
        }
        private static bool Bounded(float value, float limit) => float.IsFinite(value) && Math.Abs(value) <= limit;
        private static bool Tangent(float value) => float.IsInfinity(value) || Bounded(value, 1000000);
    }
}
