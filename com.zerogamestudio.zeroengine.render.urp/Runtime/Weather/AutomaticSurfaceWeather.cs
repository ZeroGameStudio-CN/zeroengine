using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroEngine.Render.Weather;

namespace ZeroEngine.Render.Urp.Weather
{
    /// <summary>One scoped scene adapter. Object placement never requires receiver authoring.</summary>
    [DefaultExecutionOrder(7000)]
    [DisallowMultipleComponent]
    public sealed class AutomaticSurfaceWeather : MonoBehaviour, ISurfaceWeatherExposure
    {
        [SerializeField] private SurfaceWeatherSceneProfileSO _profile;
        private SurfaceWeatherMaterialCache _materials;
        private SurfaceWeatherSceneRegistry _registry;
        private SurfaceWeatherExposureCapture _exposure;
        private readonly Vector4[] _bounds = new Vector4[SurfaceWeatherField.Capacity], _states = new Vector4[SurfaceWeatherField.Capacity];
        private bool _presentationActive = true;
        public SurfaceWeatherField Field { get; } = new();
        public bool IsExposureReady => _presentationActive && _exposure != null && _exposure.Ready && LastFailure == null;
        public bool IsReady => IsExposureReady && _registry.Pending == 0;
        public int RegisteredObjects => _registry?.Count ?? 0;
        public int OwnedMaterials => _materials?.OwnedMaterials ?? 0;
        public int ExposureGeneration => _exposure?.PublishedGeneration ?? 0;
        public string LastFailure => _registry?.Failure ?? _exposure?.Failure;
        public IReadOnlyList<string> UnsupportedShaders => _materials?.Unsupported ?? Array.Empty<string>();

        public void Configure(SurfaceWeatherSceneProfileSO profile)
        {
            if (_registry != null || profile == null) throw new InvalidOperationException("Configure an idle scene weather owner once.");
            _profile = profile;
            if (isActiveAndEnabled) Initialize();
        }

        private void OnEnable() { if (_profile != null) Initialize(); }
        private void Initialize()
        {
            if (_profile.LitShader == null || _profile.TerrainShader == null || _profile.ExposureShader == null)
                throw new InvalidOperationException("The project weather profile must retain its three shader references.");
            SurfaceWeatherExposure.Register(this);
            try
            {
                _materials = new SurfaceWeatherMaterialCache(_profile);
                _registry = new SurfaceWeatherSceneRegistry(_profile, _materials);
                _exposure = new SurfaceWeatherExposureCapture(_profile);
                Shader.SetGlobalColor("_ZeroWeatherSnowColor", _profile.SnowColor);
            }
            catch { Shutdown(); throw; }
        }

        public void SetPresentationActive(bool active) => _presentationActive = active;
        public void DiscoverNow() => _registry?.DiscoverNow();
        public bool TryGetSceneBounds(Scene scene, out Bounds bounds)
        {
            bounds = default;
            return _registry != null && _registry.TryGetSceneBounds(scene, out bounds);
        }
        public float RainExposure(Vector3 point) => _exposure?.Sample(point, false) ?? 1f;
        public float SnowExposure(Vector3 point) => _exposure?.Sample(point, true) ?? 1f;
        public SurfaceWeatherValue Sample(Vector3 point)
        {
            SurfaceWeatherValue value = Field.Sample(point);
            return new SurfaceWeatherValue(value.Wetness * RainExposure(point), value.Snow * SnowExposure(point));
        }

        private void LateUpdate()
        {
            if (_registry == null) return;
            _registry.Tick();
            _exposure.Tick(_registry);
            IReadOnlyList<SurfaceWeatherRegion> regions = Field.Regions;
            for (int i = 0; i < regions.Count; i++)
            {
                SurfaceWeatherRegion r = regions[i]; Vector3 min = r.Bounds.min, max = r.Bounds.max;
                _bounds[i] = new Vector4(min.x, min.z, max.x, max.z);
                _states[i] = new Vector4(r.Value.Wetness, r.Value.Snow, r.Feather, r.Priority);
            }
            Shader.SetGlobalInt("_ZeroWeatherRegionCount", regions.Count);
            Shader.SetGlobalVectorArray("_ZeroWeatherRegionBounds", _bounds);
            Shader.SetGlobalVectorArray("_ZeroWeatherRegionStates", _states);
            Shader.SetGlobalVector("_ZeroWeatherDefault", new Vector4(Field.DefaultValue.Wetness, Field.DefaultValue.Snow, 0, 0));
            Shader.SetGlobalFloat("_ZeroSceneWeatherEnabled", _presentationActive && _exposure.Ready && LastFailure == null ? 1f : 0f);
        }

        private void OnDisable() => Shutdown();
        private void Shutdown()
        {
            if (!ReferenceEquals(SurfaceWeatherExposure.Current, this) && _registry == null) return;
            Shader.SetGlobalFloat("_ZeroSceneWeatherEnabled", 0f);
            Shader.SetGlobalInt("_ZeroWeatherRegionCount", 0);
            _exposure?.Dispose(); _exposure = null;
            _registry?.Dispose(); _registry = null;
            _materials?.Dispose(); _materials = null;
            SurfaceWeatherExposure.Unregister(this);
        }
    }
}
