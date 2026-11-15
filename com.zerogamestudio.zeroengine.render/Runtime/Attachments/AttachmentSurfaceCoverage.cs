using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render.Attachments
{
    internal static class AttachmentSurfaceCoverage
    {
        private const int TextureBudget = 16 * 1024 * 1024;
        private const int PerTextureLimit = 4 * 1024 * 1024;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly List<Material> Materials = new();
        private static readonly HashSet<Texture2D> Pending = new();
        private static readonly Dictionary<Texture2D, byte[]> Alpha = new();
        private static int retainedBytes;
        private static int generation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            unchecked { generation++; }
            Pending.Clear();
            Alpha.Clear();
            Materials.Clear();
            retainedBytes = 0;
        }

        internal static Texture2D MeshTexture(Renderer renderer)
        {
            renderer.GetSharedMaterials(Materials);
            // A single atlas provides an unambiguous UV coverage source. Multi-material
            // renderers require a host-specific source rather than guessed UV mapping.
            if (Materials.Count != 1 || !Materials[0]) return null;
            var material = Materials[0];
            return material.GetTexture(MainTex) as Texture2D ?? material.GetTexture(BaseMap) as Texture2D;
        }

        internal static byte[] GetAlpha(Texture2D texture)
        {
            if (!texture) return null;
            if (Alpha.TryGetValue(texture, out var values)) return values;
            int count = texture.width * texture.height;
            if (count <= 0 || count > PerTextureLimit || count > TextureBudget - retainedBytes || Pending.Count >= 12)
                return null;
            if (texture.isReadable)
            {
                var colors = texture.GetPixels32();
                if (colors.Length != count) return null;
                var result = new byte[count];
                for (int i = 0; i < count; i++) result[i] = colors[i].a;
                Alpha.Add(texture, result);
                retainedBytes += count;
                return result;
            }
            if (!SystemInfo.supportsAsyncGPUReadback || !Pending.Add(texture)) return null;

            int requestGeneration = generation;
            AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, request =>
            {
                if (requestGeneration != generation) return;
                Pending.Remove(texture);
                if (!texture || request.hasError || count > TextureBudget - retainedBytes) return;
                var colors = request.GetData<Color32>();
                if (colors.Length != count) return;
                var result = new byte[count];
                for (int i = 0; i < count; i++) result[i] = colors[i].a;
                Alpha[texture] = result;
                retainedBytes += count;
            });
            return null;
        }

        internal static bool IsOpaque(byte[] alpha, int width, int height, Vector2 uv)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * width), 0, width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * height), 0, height - 1);
            return alpha[y * width + x] >= 192;
        }
    }
}
