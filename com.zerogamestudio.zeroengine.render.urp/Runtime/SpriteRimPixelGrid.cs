using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Optional asset-specific grid metadata; never inferred from filenames or texture resolution alone.</summary>
    [MovedFrom(true, "POB", "POB.Rendering", "SpriteRimPixelGrid")]
    [DisallowMultipleComponent, AddComponentMenu("ZeroEngine/Rendering/边缘受光像素格")]
    public sealed class SpriteRimPixelGrid : MonoBehaviour
    {
        [Tooltip("For example, a 4x enlarged pixel-art texture uses (4,4). Native art uses (1,1).")]
        [InspectorName("每格美术像素对应贴图像素")] public Vector2Int TextureTexelsPerArtPixel = Vector2Int.one;
        [Tooltip("Local art pixels per unit. Leave zero for Sprite PPU / readable mesh UV measurement. Set for custom or unreadable meshes.")]
        [InspectorName("美术像素每本地单位（0自动）")] public Vector2 PixelsPerUnitOverride;
        [Tooltip("Optional local grid phase for custom mesh art. Sprite pivot alignment is automatic.")]
        [InspectorName("本地网格偏移")] public Vector2 GridOriginLocal;

        internal static bool TryMeasure(Renderer renderer, Texture texture, Vector4 textureST, out Vector2 step)
        {
            step = Vector2.zero;
            var metadata = renderer.GetComponent<SpriteRimPixelGrid>();
            if (!metadata) metadata = renderer.GetComponentInParent<SpriteRimPixelGrid>();
            if (metadata && metadata.PixelsPerUnitOverride.x > 0f && metadata.PixelsPerUnitOverride.y > 0f)
            {
                step = new Vector2(1f / metadata.PixelsPerUnitOverride.x, 1f / metadata.PixelsPerUnitOverride.y);
                return Valid(step);
            }
            Vector2 multiplier = metadata ? new Vector2(Mathf.Max(1, metadata.TextureTexelsPerArtPixel.x),
                Mathf.Max(1, metadata.TextureTexelsPerArtPixel.y)) : Vector2.one;
            if (renderer is SpriteRenderer sprite && sprite.sprite)
            {
                step = multiplier / sprite.sprite.pixelsPerUnit;
                return Valid(step);
            }
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter ? filter.sharedMesh : null;
            if (!mesh || !mesh.isReadable || !texture) return false;
            var positions = mesh.vertices;
            var uv = mesh.uv;
            if (uv.Length != positions.Length) return false;
            var horizontal = new List<float>();
            var vertical = new List<float>();
            // The primary material defines the actor grid; secondary atlas densities cannot distort it.
            for (int sub = 0; sub < Mathf.Min(mesh.subMeshCount, 1); sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < Mathf.Min(triangles.Length, 48); i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    var texelScale = new Vector2(texture.width * textureST.x, texture.height * textureST.y);
                    var u = Vector2.Scale(uv[b] - uv[a], texelScale);
                    var v = Vector2.Scale(uv[c] - uv[a], texelScale);
                    float determinant = u.x * v.y - u.y * v.x;
                    if (Mathf.Abs(determinant) < 0.000001f) continue;
                    var p = positions[b] - positions[a];
                    var q = positions[c] - positions[a];
                    float x = ((p * v.y - q * u.y) / determinant).magnitude;
                    float y = ((q * u.x - p * v.x) / determinant).magnitude;
                    if (x > 0.000001f) horizontal.Add(x);
                    if (y > 0.000001f) vertical.Add(y);
                }
            }
            if (horizontal.Count == 0 || vertical.Count == 0) return false;
            horizontal.Sort(); vertical.Sort();
            step = new Vector2(horizontal[horizontal.Count / 2] * multiplier.x,
                vertical[vertical.Count / 2] * multiplier.y);
            return Valid(step);
        }

        private static bool Valid(Vector2 step) => float.IsFinite(step.x) && float.IsFinite(step.y) &&
            step.x >= 0.000001f && step.y >= 0.000001f;
    }
}
