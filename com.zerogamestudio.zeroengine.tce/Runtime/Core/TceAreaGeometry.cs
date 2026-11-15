using UnityEngine;

namespace ZeroEngine.TCE
{
    public enum TceAreaShape { Circle = 0, Box = 1 }

    public static class TceAreaGeometry
    {
        public static bool Contains(TceAreaShape shape, Vector2 point, float radius, Vector2 size) =>
            shape == TceAreaShape.Circle ? point.sqrMagnitude <= radius * radius :
                Mathf.Abs(point.x) <= size.x * .5f && Mathf.Abs(point.y) <= size.y * .5f;

        public static bool Intersect(TceAreaShape shape, Vector2 from, Vector2 to, float radius, Vector2 size,
            out float enter, out float exit)
        {
            enter = 0f; exit = 1f;
            Vector2 direction = to - from;
            if (shape == TceAreaShape.Circle)
            {
                float a = direction.sqrMagnitude;
                if (a < 1e-12f) return Contains(shape, from, radius, size);
                float b = Vector2.Dot(from, direction);
                float c = from.sqrMagnitude - radius * radius;
                float discriminant = b * b - a * c;
                if (discriminant < 0f) return false;
                float root = Mathf.Sqrt(discriminant);
                enter = Mathf.Max(0f, (-b - root) / a);
                exit = Mathf.Min(1f, (-b + root) / a);
                return enter <= exit;
            }
            Vector2 half = size * .5f;
            return Clip(from.x, direction.x, half.x, ref enter, ref exit) &&
                Clip(from.y, direction.y, half.y, ref enter, ref exit);
        }

        private static bool Clip(float origin, float direction, float half, ref float enter, ref float exit)
        {
            if (Mathf.Abs(direction) < 1e-6f) return Mathf.Abs(origin) <= half;
            float first = (-half - origin) / direction, last = (half - origin) / direction;
            if (first > last) (first, last) = (last, first);
            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, last);
            return enter <= exit;
        }
    }
}
