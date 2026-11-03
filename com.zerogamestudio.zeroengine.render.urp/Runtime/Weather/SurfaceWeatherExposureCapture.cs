using System;
using UnityEngine;
using UnityEngine.Rendering;
using ZeroEngine.Render.Weather;

namespace ZeroEngine.Render.Urp.Weather
{
    internal sealed class SurfaceWeatherExposureCapture : IDisposable
    {
        private sealed class Map
        {
            public RenderTexture Published, Staging;
            public float[] PublishedPixels, StagingPixels;
            public SurfaceWeatherProjection Projection, PendingProjection;
            public SurfaceWeatherExposureFrame Frame;
            public AsyncGPUReadbackRequest Request;
            public bool HasRequest;
            public bool Copied;
        }
        private readonly Map[] _maps = { new(), new() };
        private readonly SurfaceWeatherSceneProfileSO _profile;
        private readonly Material _capture;
        private readonly MaterialPropertyBlock _sourceProperties = new();
        private readonly Vector4[] _u = new Vector4[2], _v = new Vector4[2], _axes = new Vector4[2];
        private bool _pending;
        private bool _disposed;
        private int _capturedVersion = -1;
        private float _nextCapture;
        public bool Ready { get; private set; }
        public int PublishedGeneration { get; private set; }
        public string Failure { get; private set; }

        public SurfaceWeatherExposureCapture(SurfaceWeatherSceneProfileSO profile)
        {
            if (!SystemInfo.supportsAsyncGPUReadback || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat))
                throw new NotSupportedException("Scene exposure requires RFloat rendering and async GPU readback.");
            _profile = profile;
            _capture = new Material(profile.ExposureShader) { hideFlags = HideFlags.DontSave };
            // ShaderLab translates its logical depth comparison for a reversed-Z device.
            // Reversing the comparison here a second time selects the bottom of the scene.
            _capture.SetFloat("_ZeroDepthTest", (float)CompareFunction.LessEqual);
            int size = profile.ExposureResolution;
            try
            {
                for (int i = 0; i < _maps.Length; i++)
                {
                    _maps[i].Published = Allocate(size, "Published", i);
                    _maps[i].Staging = Allocate(size, "Staging", i);
                    _maps[i].PublishedPixels = new float[size * size];
                    _maps[i].StagingPixels = new float[size * size];
                }
            }
            catch { Dispose(); throw; }
        }

        private static RenderTexture Allocate(int size, string role, int index)
        {
            var texture = new RenderTexture(size, size, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = $"ZeroSurfaceWeather_{role}_{index}", hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false
            };
            if (!texture.Create())
            {
                SurfaceWeatherMaterialCache.DestroyOwned(texture);
                throw new InvalidOperationException("Could not create the bounded scene exposure target.");
            }
            return texture;
        }

        public void Tick(SurfaceWeatherSceneRegistry registry)
        {
            if (Failure != null) return;
            if (_pending)
            {
                if (!_maps[0].Copied || !_maps[1].Copied) return;
                for (int i = 0; i < _maps.Length; i++)
                {
                    Map map = _maps[i];
                    (map.Published, map.Staging) = (map.Staging, map.Published);
                    (map.PublishedPixels, map.StagingPixels) = (map.StagingPixels, map.PublishedPixels);
                    map.Projection = map.PendingProjection;
                    map.Frame = new SurfaceWeatherExposureFrame(map.Projection, map.PublishedPixels);
                    _u[i] = map.Projection.U; _v[i] = map.Projection.V;
                    _axes[i] = new Vector4(map.Projection.Axis.x, map.Projection.Axis.y, map.Projection.Axis.z, map.Projection.Bias);
                }
                Shader.SetGlobalTexture("_ZeroWeatherRainExposure", _maps[0].Published);
                Shader.SetGlobalTexture("_ZeroWeatherSnowExposure", _maps[1].Published);
                Shader.SetGlobalVectorArray("_ZeroWeatherExposureU", _u);
                Shader.SetGlobalVectorArray("_ZeroWeatherExposureV", _v);
                Shader.SetGlobalVectorArray("_ZeroWeatherExposureAxis", _axes);
                Shader.SetGlobalVector("_ZeroWeatherExposureSize", new Vector4(1f / _profile.ExposureResolution, _profile.ExposureResolution, 0, 0));
                Shader.SetGlobalFloat("_ZeroWeatherExposureReady", 1f);
                Ready = true; PublishedGeneration++; _pending = false;
            }
            if (registry.Pending != 0 || registry.Version == _capturedVersion || Time.unscaledTime < _nextCapture) return;
            if (!registry.TryGetCasterBounds(out Bounds bounds)) bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
            try { Capture(registry, bounds); }
            catch (Exception error) { Failure = "Scene exposure capture failed: " + error.Message; return; }
            _capturedVersion = registry.Version;
            _nextCapture = Time.unscaledTime + _profile.ExposureInterval;
        }

        private void Capture(SurfaceWeatherSceneRegistry registry, Bounds bounds)
        {
            RenderTexture previous = RenderTexture.active;
            using var command = new CommandBuffer { name = "Zero scene weather geometry exposure" };
            for (int i = 0; i < _maps.Length; i++)
            {
                Map map = _maps[i];
                map.PendingProjection = new SurfaceWeatherProjection(bounds, i == 0 ? _profile.RainSkyDirection : Vector3.up,
                    _profile.ExposureResolution, _profile.SurfaceBias);
                SurfaceWeatherProjection p = map.PendingProjection;
                command.SetRenderTarget(map.Staging);
                command.SetViewport(new Rect(0, 0, _profile.ExposureResolution, _profile.ExposureResolution));
                command.ClearRenderTarget(true, true, new Color(-1000000f, 0, 0, 0), 1f);
                command.SetGlobalVector("_ZeroCaptureU", p.U); command.SetGlobalVector("_ZeroCaptureV", p.V);
                command.SetGlobalVector("_ZeroCaptureAxis", p.Depth);
                command.SetGlobalVector("_ZeroCaptureParams", new Vector4(p.DepthSpan, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f, 0, 0));
                foreach (SurfaceWeatherSceneRegistry.Entry entry in registry.Entries)
                {
                    if (!entry.Active || !entry.Casts || entry.Mesh == null) continue;
                    // A static-batched MeshFilter refers to the whole combined mesh, while
                    // its Renderer still owns only its material slots and batch slice.
                    int slots = entry.Renderer != null ? entry.Before.Length : entry.Mesh.subMeshCount;
                    for (int sub = 0; sub < slots; sub++)
                    {
                        Material source = entry.Before != null && entry.Before.Length > 0 ? entry.Before[Mathf.Min(sub, entry.Before.Length - 1)] : null;
                        if (entry.Terrain == null && !_profile.CanOcclude(source)) continue;
                        _sourceProperties.Clear();
                        if (entry.Renderer != null)
                        {
                            entry.Renderer.GetPropertyBlock(_sourceProperties, sub);
                            if (_sourceProperties.isEmpty) entry.Renderer.GetPropertyBlock(_sourceProperties);
                        }
                        Texture texture = _sourceProperties.HasTexture(Shader.PropertyToID("_BaseMap"))
                            ? _sourceProperties.GetTexture("_BaseMap")
                            : source != null && source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
                        bool clip = source != null && (source.IsKeywordEnabled("_ALPHATEST_ON")
                            || source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > .5f);
                        bool softCutout = source != null && source.renderQueue > 2500 && texture != null;
                        command.SetGlobalTexture("_ZeroCaptureBaseMap", texture != null ? texture : Texture2D.whiteTexture);
                        Vector2 scale = source != null && source.HasProperty("_BaseMap") ? source.GetTextureScale("_BaseMap") : Vector2.one;
                        Vector2 offset = source != null && source.HasProperty("_BaseMap") ? source.GetTextureOffset("_BaseMap") : Vector2.zero;
                        Vector4 st = _sourceProperties.HasVector(Shader.PropertyToID("_BaseMap_ST"))
                            ? _sourceProperties.GetVector("_BaseMap_ST") : new Vector4(scale.x, scale.y, offset.x, offset.y);
                        command.SetGlobalVector("_ZeroCaptureBaseST", st);
                        command.SetGlobalFloat("_ZeroCaptureAlphaClip", clip || softCutout ? 1f : 0f);
                        command.SetGlobalFloat("_ZeroCaptureCutoff", softCutout && !clip ? .05f
                            : _sourceProperties.HasFloat(Shader.PropertyToID("_Cutoff")) ? _sourceProperties.GetFloat("_Cutoff")
                            : source != null && source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .5f);
                        if (entry.Renderer != null) command.DrawRenderer(entry.Renderer, _capture, sub, 0);
                        else command.DrawMesh(entry.Mesh, entry.Matrix, _capture, sub, 0);
                    }
                }
            }
            if (previous != null) command.SetRenderTarget(previous);
            else command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            command.SetViewport(new Rect(0, 0, Screen.width, Screen.height));
            Graphics.ExecuteCommandBuffer(command);
            RenderTexture.active = previous;
            for (int i = 0; i < _maps.Length; i++)
            {
                Map map = _maps[i];
                map.Copied = false;
                map.Request = AsyncGPUReadback.Request(map.Staging, 0, TextureFormat.RFloat, request => Receive(map, request));
                map.HasRequest = true;
            }
            _pending = true;
        }

        private void Receive(Map map, AsyncGPUReadbackRequest request)
        {
            map.HasRequest = false;
            if (_disposed) return;
            if (request.hasError) { Failure = "Scene exposure readback failed; no CPU/GPU generation was published."; return; }
            // A completed readback is only valid for its serviced frame. The two maps
            // may finish on different frames, so copy each in its own completion callback.
            try { request.GetData<float>().CopyTo(map.StagingPixels); map.Copied = true; }
            catch (Exception error) { Failure = "Scene exposure data reception failed: " + error.Message; }
        }

        public float Sample(Vector3 point, bool snow) => _maps[snow ? 1 : 0].Frame?.Sample(point) ?? 1f;

        public void Dispose()
        {
            _disposed = true;
            foreach (Map map in _maps)
            {
                if (map.HasRequest && !map.Request.done) map.Request.WaitForCompletion();
                if (map.Published != null) { map.Published.Release(); SurfaceWeatherMaterialCache.DestroyOwned(map.Published); }
                if (map.Staging != null) { map.Staging.Release(); SurfaceWeatherMaterialCache.DestroyOwned(map.Staging); }
            }
            SurfaceWeatherMaterialCache.DestroyOwned(_capture);
            Shader.SetGlobalFloat("_ZeroWeatherExposureReady", 0f);
            Shader.SetGlobalTexture("_ZeroWeatherRainExposure", null);
            Shader.SetGlobalTexture("_ZeroWeatherSnowExposure", null);
        }
    }
}
