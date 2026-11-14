using UnityEngine;

namespace ZeroEngine.Console.UI
{
    internal enum ConsoleGlyph { Terminal, Clear, Copy, FontSmaller, FontLarger, Follow, FollowPaused, Run, Stop, Close, Up, Down, Ready, Running, Search }

    [DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
    internal sealed class ConsoleIcon : UnityEngine.UI.MaskableGraphic
    {
        private ConsoleGlyph glyph;
        private Rect bounds;
        private float scale;
        internal ConsoleGlyph Glyph
        {
            get => glyph;
            set { if (glyph == value) return; glyph = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear(); bounds = GetPixelAdjustedRect(); scale = Mathf.Min(bounds.width, bounds.height) / 24;
            if (scale <= 0) return;
            switch (glyph)
            {
                case ConsoleGlyph.Terminal:
                    Line(mesh, 5, 17, 10, 12); Line(mesh, 10, 12, 5, 7); Line(mesh, 12, 7, 19, 7); break;
                case ConsoleGlyph.Clear:
                    Line(mesh, 6, 17, 18, 17); Line(mesh, 10, 20, 14, 20);
                    Line(mesh, 8, 15, 9, 5); Line(mesh, 9, 5, 15, 5); Line(mesh, 15, 5, 16, 15);
                    Line(mesh, 11, 13, 11, 8); Line(mesh, 13, 13, 13, 8); break;
                case ConsoleGlyph.Copy:
                    Line(mesh, 14, 19, 6, 19); Line(mesh, 6, 19, 6, 8); Line(mesh, 6, 8, 8, 8);
                    Box(mesh, 10, 4, 19, 15); break;
                case ConsoleGlyph.FontSmaller:
                case ConsoleGlyph.FontLarger:
                    Line(mesh, 4, 6, 9, 18); Line(mesh, 9, 18, 14, 6); Line(mesh, 6, 10, 12, 10);
                    Line(mesh, 17, 12, 23, 12);
                    if (glyph == ConsoleGlyph.FontLarger) Line(mesh, 20, 9, 20, 15);
                    break;
                case ConsoleGlyph.Follow:
                    Line(mesh, 12, 20, 12, 8); Line(mesh, 8, 12, 12, 8); Line(mesh, 12, 8, 16, 12);
                    Line(mesh, 6, 4, 18, 4); break;
                case ConsoleGlyph.FollowPaused:
                    Line(mesh, 9, 6, 9, 18); Line(mesh, 15, 6, 15, 18); break;
                case ConsoleGlyph.Run:
                    Triangle(mesh, new Vector2(8, 5), new Vector2(19, 12), new Vector2(8, 19)); break;
                case ConsoleGlyph.Stop: Box(mesh, 6, 6, 18, 18); break;
                case ConsoleGlyph.Close:
                    Line(mesh, 7, 7, 17, 17); Line(mesh, 7, 17, 17, 7); break;
                case ConsoleGlyph.Up:
                    Line(mesh, 7, 9, 12, 14); Line(mesh, 12, 14, 17, 9); break;
                case ConsoleGlyph.Down:
                    Line(mesh, 7, 15, 12, 10); Line(mesh, 12, 10, 17, 15); break;
                case ConsoleGlyph.Ready: Disc(mesh, new Vector2(12, 12), 4); break;
                case ConsoleGlyph.Running:
                    Ring(mesh, new Vector2(12, 12), 8); Line(mesh, 12, 12, 12, 17); Line(mesh, 12, 12, 16, 10); break;
                case ConsoleGlyph.Search:
                    Ring(mesh, new Vector2(10, 14), 6); Line(mesh, 14, 10, 20, 4); break;
            }
        }

        private Vector2 Point(Vector2 p) => bounds.center + (p - new Vector2(12, 12)) * scale;
        private void Triangle(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(Point(a), color, Vector2.zero); mesh.AddVert(Point(b), color, Vector2.zero); mesh.AddVert(Point(c), color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
        }
        private void Line(UnityEngine.UI.VertexHelper mesh, float x1, float y1, float x2, float y2)
        {
            var a = new Vector2(x1, y1); var b = new Vector2(x2, y2);
            var offset = new Vector2(-(b - a).y, (b - a).x).normalized * .85f;
            Triangle(mesh, a + offset, b + offset, b - offset); Triangle(mesh, a + offset, b - offset, a - offset);
            Disc(mesh, a, .85f); Disc(mesh, b, .85f);
        }
        private void Disc(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius)
        {
            const int segments = 12;
            for (int i = 0; i < segments; i++)
                Triangle(mesh, center, center + Direction(i, segments) * radius, center + Direction(i + 1, segments) * radius);
        }
        private void Ring(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius)
        {
            const int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = Direction(i, segments), b = Direction(i + 1, segments);
                Triangle(mesh, center + a * (radius - .85f), center + a * (radius + .85f), center + b * (radius + .85f));
                Triangle(mesh, center + a * (radius - .85f), center + b * (radius + .85f), center + b * (radius - .85f));
            }
        }
        private static Vector2 Direction(int step, int segments)
        { float angle = step * Mathf.PI * 2 / segments; return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); }
        private void Box(UnityEngine.UI.VertexHelper mesh, float left, float bottom, float right, float top)
        { Line(mesh, left, bottom, right, bottom); Line(mesh, right, bottom, right, top); Line(mesh, right, top, left, top); Line(mesh, left, top, left, bottom); }
    }
}
