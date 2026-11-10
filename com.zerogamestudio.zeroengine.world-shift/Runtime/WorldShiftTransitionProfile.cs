using UnityEngine;

namespace ZeroEngine.WorldShift
{
    [CreateAssetMenu(menuName = "ZeroEngine/World Shift/Transition Profile")]
    public sealed class WorldShiftTransitionProfile : ScriptableObject
    {
        [SerializeField, Min(0.05f)] private float duration = 1.35f;
        [SerializeField, Min(0f)] private float scanStartRadius;
        [SerializeField, Min(0.1f)] private float scanEndRadius = 10.8f;
        [SerializeField, Min(0.01f)] private float edgeWidth = 0.34f;
        [SerializeField, Range(0.1f, 8f)] private float noiseScale = 1.2f;
        [SerializeField, Range(0f, 1f)] private float noiseStrength = 0.04f;
        [SerializeField] private Color edgeColor = new(0.42f, 0.96f, 1f, 1f);
        [SerializeField] private float scanFrontYOffset;
        [SerializeField] private GameObject scanFrontPrefab;
        [SerializeField] private WorldShiftRenderBackend renderBackend = WorldShiftRenderBackend.MaterialReveal;
        [SerializeField] private LayerMask overworldLayerMask;
        [SerializeField] private LayerMask underworldLayerMask;

        public float Duration
        {
            get => duration;
            set => duration = Mathf.Max(0.05f, value);
        }

        public float ScanStartRadius
        {
            get => scanStartRadius;
            set => scanStartRadius = Mathf.Max(0f, value);
        }

        public float ScanEndRadius
        {
            get => scanEndRadius;
            set => scanEndRadius = Mathf.Max(0.1f, value);
        }

        public float EdgeWidth
        {
            get => edgeWidth;
            set => edgeWidth = Mathf.Max(0.01f, value);
        }

        public float NoiseScale
        {
            get => noiseScale;
            set => noiseScale = Mathf.Clamp(value, 0.1f, 8f);
        }

        public float NoiseStrength
        {
            get => noiseStrength;
            set => noiseStrength = Mathf.Clamp01(value);
        }

        public Color EdgeColor
        {
            get => edgeColor;
            set => edgeColor = value;
        }

        public float ScanFrontYOffset
        {
            get => scanFrontYOffset;
            set => scanFrontYOffset = value;
        }

        public GameObject ScanFrontPrefab
        {
            get => scanFrontPrefab;
            set => scanFrontPrefab = value;
        }

        public WorldShiftRenderBackend RenderBackend
        {
            get => renderBackend;
            set => renderBackend = value;
        }

        public LayerMask OverworldLayerMask
        {
            get => overworldLayerMask;
            set => overworldLayerMask = value;
        }

        public LayerMask UnderworldLayerMask
        {
            get => underworldLayerMask;
            set => underworldLayerMask = value;
        }
    }
}
