using UnityEngine;

namespace ZeroEngine.Render.Urp.Weather
{
    /// <summary>One project-level weather adaptation profile; no per-instance receiver authoring.</summary>
    public sealed class SurfaceWeatherSceneProfileSO : ScriptableObject
    {
        [SerializeField] private Shader _litShader;
        [SerializeField] private Shader _terrainShader;
        [SerializeField] private Shader _exposureShader;
        [SerializeField] private LayerMask _environmentLayers = ~0;
        [SerializeField, Range(64, 1024)] private int _exposureResolution = 512;
        [SerializeField, Range(.05f, 2f)] private float _exposureInterval = .25f;
        [SerializeField, Range(.1f, 5f)] private float _discoveryInterval = .5f;
        [SerializeField, Range(32, 2048)] private int _discoveryBudget = 256;
        [SerializeField, Min(1)] private int _maximumRegisteredObjects = 8192;
        [SerializeField, Min(32)] private int _maximumPendingTransforms = 65536;
        [SerializeField, Min(.001f)] private float _surfaceBias = .04f;
        [SerializeField] private Vector3 _rainSkyDirection = new(.3f, 1f, .12f);
        [SerializeField] private Color _snowColor = new(.87f, .93f, .98f, 1f);
        [SerializeField] private Material[] _nonOccludingMaterials = System.Array.Empty<Material>();
        [SerializeField] private Shader[] _nonReceiverShaders = System.Array.Empty<Shader>();
        [SerializeField] private Material[] _nonReceiverMaterials = System.Array.Empty<Material>();
        public Shader LitShader => _litShader;
        public Shader TerrainShader => _terrainShader;
        public Shader ExposureShader => _exposureShader;
        public int EnvironmentLayers => _environmentLayers;
        public int ExposureResolution => Mathf.Clamp(Mathf.NextPowerOfTwo(_exposureResolution), 64, 1024);
        public float ExposureInterval => Mathf.Clamp(_exposureInterval, .05f, 2f);
        public float DiscoveryInterval => Mathf.Clamp(_discoveryInterval, .1f, 5f);
        public int DiscoveryBudget => Mathf.Clamp(_discoveryBudget, 32, 2048);
        public int MaximumRegisteredObjects => Mathf.Clamp(_maximumRegisteredObjects, 1, 32768);
        public int MaximumPendingTransforms => Mathf.Clamp(_maximumPendingTransforms, 32, 131072);
        public float SurfaceBias => Mathf.Max(.001f, _surfaceBias);
        public Vector3 RainSkyDirection => _rainSkyDirection.sqrMagnitude > .01f ? _rainSkyDirection.normalized : Vector3.up;
        public Color SnowColor => _snowColor;
        public bool IsNonReceiver(Shader shader) => System.Array.IndexOf(_nonReceiverShaders, shader) >= 0;
        public bool IsNonReceiver(Material material) => material != null && (IsNonReceiver(material.shader)
            || System.Array.IndexOf(_nonReceiverMaterials, material) >= 0);
        public bool CanOcclude(Material material)
        {
            if (material == null || IsNonReceiver(material)
                || System.Array.IndexOf(_nonOccludingMaterials, material) >= 0) return false;
            // Optical transparency does not make a glass roof permeable to rain.
            return material.renderQueue <= 2500 || material.shader.name == "Universal Render Pipeline/Lit"
                || material.HasProperty("_ZeroSceneWeatherReceiver");
        }

        public void Configure(Shader lit, Shader terrain, Shader exposure, int layers = ~0, Vector3? rainSkyDirection = null,
            Material[] nonOccludingMaterials = null, Shader[] nonReceiverShaders = null, Material[] nonReceiverMaterials = null)
        {
            _litShader = lit;
            _terrainShader = terrain;
            _exposureShader = exposure;
            _environmentLayers = layers;
            if (rainSkyDirection.HasValue && rainSkyDirection.Value.sqrMagnitude > .01f)
                _rainSkyDirection = rainSkyDirection.Value.normalized;
            _nonOccludingMaterials = nonOccludingMaterials ?? System.Array.Empty<Material>();
            _nonReceiverShaders = nonReceiverShaders ?? System.Array.Empty<Shader>();
            _nonReceiverMaterials = nonReceiverMaterials ?? System.Array.Empty<Material>();
        }
    }
}
