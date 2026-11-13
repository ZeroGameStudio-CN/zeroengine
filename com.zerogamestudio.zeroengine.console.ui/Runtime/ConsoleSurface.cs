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
            mesh.AddVert(rect.center, color, Vector2.zero);
            const int steps = 5;
            for (int corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner < 2 ? rect.xMax - radius : rect.xMin + radius,
                    corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius);
                float first = -90 + corner * 90;
                for (int step = 0; step <= steps; step++)
                {
                    float angle = (first + step * 90f / steps) * Mathf.Deg2Rad;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                }
            }
            int count = 4 * (steps + 1);
            for (int i = 0; i < count; i++) mesh.AddTriangle(0, 1 + i, 1 + (i + 1) % count);
        }
    }
}
