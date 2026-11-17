using System;

namespace ZeroEngine.Multiplayer.Motion
{
    /// <summary>Transport-neutral world-space vector. No engine or physics object is retained.</summary>
    public struct MotionVector2
    {
        public float X, Y;
        public MotionVector2(float x, float y) { X = x; Y = y; }
        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);
        public static MotionVector2 operator +(MotionVector2 a, MotionVector2 b) => new(a.X + b.X, a.Y + b.Y);
        public static MotionVector2 operator -(MotionVector2 a, MotionVector2 b) => new(a.X - b.X, a.Y - b.Y);
        public static MotionVector2 operator *(MotionVector2 a, float b) => new(a.X * b, a.Y * b);
        public static MotionVector2 operator /(MotionVector2 a, float b) => new(a.X / b, a.Y / b);
        public static MotionVector2 Direction(float degrees)
        {
            double angle = degrees * (Math.PI / 180);
            return new((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
        public static float Angle(MotionVector2 direction) => (float)(Math.Atan2(direction.Y, direction.X) * (180 / Math.PI));
        public static MotionVector2 Rotate(MotionVector2 value, float degrees)
        {
            var direction = Direction(degrees);
            return new(value.X * direction.X - value.Y * direction.Y, value.X * direction.Y + value.Y * direction.X);
        }
        public static float DeltaAngle(float from, float to)
        {
            float angle = (to - from) % 360;
            if (angle > 180) angle -= 360;
            if (angle < -180) angle += 360;
            return angle;
        }
    }
}
