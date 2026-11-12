using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using ZeroEngine.FogOfWar.Unity;

namespace ZeroEngine.FogOfWar.URP
{
    // R is current geometric sight, G is solid terrain. Pixel density follows the actual camera.
    public class SightMaskRenderer2D : IDisposable
    {
        private readonly Mesh mesh = new() { name = "Fog visibility fan (runtime)", hideFlags = HideFlags.DontSave };
        private readonly List<Vector3> vertices = new();
        private readonly List<int> triangles = new();
        private int uploadedFanVertexCount = -1;
        [UnityEngine.Scripting.Preserve]
        public bool ReuseFanTopologyEnabled { get; set; } = true;
        [UnityEngine.Scripting.Preserve]
        public int FanTopologyUploadCount { get; private set; }
        [UnityEngine.Scripting.Preserve]
        public int FanTopologyReuseCount { get; private set; }
        private readonly CommandBuffer commands = new() { name = "Fog geometric sight" };
        private readonly Material material;
        private readonly GraphicsFormat rawFormat, softFormat;
        private RenderTexture blurScratch, softened;
        private bool useSoftened;
        private float lastSoftness = -1;
        private readonly Vector4[] blurKernel = new Vector4[24];
        public RenderTexture Texture { get; private set; }
        public RenderTexture PresentationTexture => useSoftened ? softened : Texture;

        // Only the map owner supplies published geometry/sight revisions to this path.
        private bool rendered, renderedTopologyReuse;
        private Camera renderedCamera;
        private SightPolygon2D renderedSight;
        private IReadOnlyList<Mesh> renderedTerrain;
        private Matrix4x4 renderedView, renderedProjection;
        private int renderedGeometryRevision, renderedTerrainCount, renderedWidth, renderedHeight;
        private uint renderedSightRevision;
        private float renderedZ, renderedSoftness;

        public SightMaskRenderer2D(Shader shader, bool compactTextures = true)
        {
            material = new Material(shader) { hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            // The fog consumer reads raw R/G and softened R only. Preserve both
            // resolution and UNorm8 precision, with the existing format as fallback.
            rawFormat = SelectFormat(GraphicsFormat.R8G8_UNorm, compactTextures);
            softFormat = SelectFormat(GraphicsFormat.R8_UNorm, compactTextures);
        }

        private static GraphicsFormat SelectFormat(GraphicsFormat preferred, bool compact) =>
            compact && SystemInfo.IsFormatSupported(preferred,
                GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample | GraphicsFormatUsage.Linear)
                ? preferred : GraphicsFormat.None;

        public bool RenderIfChanged(Camera camera, SightPolygon2D sight, IReadOnlyList<Mesh> terrain,
            int geometryRevision, float z, float softnessPixels = 0)
        {
            int width = Mathf.Max(1, camera.pixelWidth), height = Mathf.Max(1, camera.pixelHeight);
            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            bool targetsReady = Texture && Texture.IsCreated() && Texture.width == width && Texture.height == height
                && (!(softnessPixels > 0) || blurScratch && softened && blurScratch.IsCreated() && softened.IsCreated()
                    && blurScratch.width == width && blurScratch.height == height
                    && softened.width == width && softened.height == height);
            if (rendered && targetsReady && renderedTopologyReuse == ReuseFanTopologyEnabled
                && ReferenceEquals(camera, renderedCamera)
                && ReferenceEquals(sight, renderedSight) && ReferenceEquals(terrain, renderedTerrain)
                && sight.Revision == renderedSightRevision && geometryRevision == renderedGeometryRevision
                && terrain.Count == renderedTerrainCount && width == renderedWidth && height == renderedHeight
                && view.Equals(renderedView) && projection.Equals(renderedProjection)
                && z.Equals(renderedZ) && softnessPixels.Equals(renderedSoftness)) return false;

            Render(camera, sight, terrain, z, softnessPixels);
            // Publish the reuse key only after command construction and execution succeeded.
            renderedCamera = camera; renderedSight = sight; renderedTerrain = terrain;
            renderedSightRevision = sight.Revision; renderedGeometryRevision = geometryRevision;
            renderedTerrainCount = terrain.Count; renderedWidth = width; renderedHeight = height;
            renderedView = view; renderedProjection = projection;
            renderedZ = z; renderedSoftness = softnessPixels;
            renderedTopologyReuse = ReuseFanTopologyEnabled; rendered = true;
            return true;
        }

        public void Render(Camera camera, SightPolygon2D sight, IReadOnlyList<Mesh> terrain, float z, float softnessPixels = 0)
        {
            rendered = false;
            int width = Mathf.Max(1, camera.pixelWidth), height = Mathf.Max(1, camera.pixelHeight);
            if (!Texture || !Texture.IsCreated() || Texture.width != width || Texture.height != height)
            {
                if (Texture) { Texture.Release(); DestroyOwned(Texture); }
                ReleaseSoftTargets();
                Texture = NewMaskTarget(width, height, rawFormat, "Fog sight and terrain (runtime)");
            }
            vertices.Clear();
            vertices.Add(new Vector3(sight.Origin.x, sight.Origin.y, z));
            foreach (var point in sight.Points) vertices.Add(new Vector3(point.x, point.y, z));
            // A fan's indices depend only on vertex count. Positions and all bounds stay current.
            bool uploadTopology = !ReuseFanTopologyEnabled || vertices.Count < 4
                || uploadedFanVertexCount != vertices.Count;
            if (uploadTopology)
            {
                triangles.Clear();
                for (int i = 1; i < vertices.Count; i++)
                { triangles.Add(0); triangles.Add(i); triangles.Add(i + 1 < vertices.Count ? i + 1 : 1); }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
                uploadedFanVertexCount = vertices.Count;
                unchecked { FanTopologyUploadCount++; }
            }
            else
            {
                mesh.SetVertices(vertices);
                mesh.RecalculateBounds();
                unchecked { FanTopologyReuseCount++; }
            }
            commands.Clear();
            commands.SetRenderTarget(Texture);
            commands.ClearRenderTarget(false, true, Color.clear);
            // RawImage samples bottom-left UVs directly, without a camera blit's render-target Y flip.
            commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            foreach (var solid in terrain) if (solid) commands.DrawMesh(solid, Matrix4x4.identity, material, 0, 1);
            useSoftened = softnessPixels > 0;
            if (useSoftened)
            {
                if (!blurScratch || !softened || !blurScratch.IsCreated() || !softened.IsCreated())
                {
                    ReleaseSoftTargets();
                    blurScratch = NewMaskTarget(width, height, softFormat, "Fog soft sight horizontal (runtime)");
                    softened = NewMaskTarget(width, height, softFormat, "Fog soft sight (runtime)");
                }
                UpdateKernel(Mathf.Clamp(softnessPixels, 0, 48));
                commands.Blit(Texture, blurScratch, material, 2);
                commands.Blit(blurScratch, softened, material, 3);
            }
            else ReleaseSoftTargets();
            Graphics.ExecuteCommandBuffer(commands);
        }

        private void UpdateKernel(float radius)
        {
            if (radius == lastSoftness) return;
            lastSoftness = radius;
            int extent = Mathf.CeilToInt(radius);
            int pairs = (extent + 1) / 2;
            float sigma = Mathf.Max(.01f, radius * .4f), total = 1;
            for (int pair = 0; pair < pairs; pair++)
            {
                int first = pair * 2 + 1, second = first + 1;
                float a = Mathf.Exp(-.5f * first * first / (sigma * sigma));
                float b = second <= extent ? Mathf.Exp(-.5f * second * second / (sigma * sigma)) : 0;
                float weight = a + b;
                // Adjacent source texels share one bilinear fetch; never spread sparse taps over a wide edge.
                blurKernel[pair] = new Vector4((first * a + second * b) / Mathf.Max(weight, 1e-20f), weight, 0, 0);
                total += 2 * weight;
            }
            for (int pair = 0; pair < pairs; pair++) blurKernel[pair].y /= total;
            material.SetInt("_BlurPairCount", pairs);
            material.SetFloat("_BlurCenterWeight", 1 / total);
            material.SetVectorArray("_BlurKernel", blurKernel);
        }

        private static RenderTexture NewMaskTarget(int width, int height, GraphicsFormat format, string name)
        {
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            { sRGB = false, msaaSamples = 1, useMipMap = false, autoGenerateMips = false };
            if (format != GraphicsFormat.None) descriptor.graphicsFormat = format;
            var result = new RenderTexture(descriptor)
            { name = name, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, antiAliasing = 1 };
            result.Create();
            return result;
        }

        private void ReleaseSoftTargets()
        {
            if (blurScratch) { blurScratch.Release(); DestroyOwned(blurScratch); blurScratch = null; }
            if (softened) { softened.Release(); DestroyOwned(softened); softened = null; }
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        public void Dispose()
        {
            rendered = false; renderedCamera = null; renderedSight = null; renderedTerrain = null;
            commands.Dispose();
            ReleaseSoftTargets();
            if (Texture) { Texture.Release(); DestroyOwned(Texture); Texture = null; }
            DestroyOwned(mesh); DestroyOwned(material);
        }
    }
}
