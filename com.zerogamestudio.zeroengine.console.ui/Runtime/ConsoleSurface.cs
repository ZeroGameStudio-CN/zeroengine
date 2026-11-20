using UnityEngine;

namespace ZeroEngine.Console.UI
{
    // Rounded surfaces use a small UI mesh, without sprites or extra materials.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class ConsoleSurface : UnityEngine.UI.MaskableGraphic
    {
        public float Radius { get; set; } = 6;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            float radius = Mathf.Clamp(Radius, 0, Mathf.Min(rect.width, rect.height) * .5f);
            Color32 vertexColor = color;
            mesh.AddVert(rect.center, vertexColor, Vector2.zero);
            const int steps = 5;
            var start = Vector2.down;
            for (int corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner < 2 ? rect.xMax - radius : rect.xMin + radius,
                    corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius);
                var direction = start;
                for (int step = 0; step <= steps; step++)
                {
                    mesh.AddVert(center + direction * radius, vertexColor, Vector2.zero);
                    direction = Rotate(direction, .9510565f, .309017f);
                }
                start = new Vector2(-start.y, start.x);
            }
            int count = 4 * (steps + 1);
            for (int i = 0; i < count; i++) mesh.AddTriangle(0, 1 + i, 1 + (i + 1) % count);
        }

        internal static Vector2 Rotate(Vector2 direction, float cosine, float sine) =>
            new Vector2(direction.x * cosine - direction.y * sine, direction.x * sine + direction.y * cosine);
    }
}
