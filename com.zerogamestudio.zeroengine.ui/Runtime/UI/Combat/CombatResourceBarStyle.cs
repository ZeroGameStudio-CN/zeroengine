using UnityEngine;

namespace ZeroEngine.UI.Combat
{
    public enum ResourceBarVisibilityMode
    {
        AlwaysVisible,
        HideWhenFull,
        ShowOnChangeThenHide,
        Manual
    }

    [CreateAssetMenu(menuName = "ZeroEngine/UI/Combat Resource Bar Style", fileName = "CombatResourceBarStyle")]
    public sealed class CombatResourceBarStyle : ScriptableObject
    {
        [SerializeField] private Color _frontColor = new Color(0.25f, 0.95f, 0.45f, 1f);
        [SerializeField] private Color _delayedColor = new Color(0.75f, 0.2f, 0.16f, 0.9f);
        [SerializeField] private Color _shieldColor = new Color(0.25f, 0.55f, 1f, 0.85f);
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color _lowValueColor = new Color(1f, 0.32f, 0.22f, 1f);
        [SerializeField, Range(0f, 1f)] private float _lowValueThreshold = 0.25f;
        [SerializeField] private float _frontFillSpeed = 2.4f;
        [SerializeField] private float _delayedFillSpeed = 1.6f;
        [SerializeField] private bool _showValueText = true;
        [SerializeField] private string _valueTextFormat = "{0}/{1}";
        [SerializeField] private ResourceBarVisibilityMode _visibilityMode = ResourceBarVisibilityMode.AlwaysVisible;
        [SerializeField] private float _fadeDuration = 0.12f;
        [SerializeField] private float _visibleAfterChangeDuration = 1.2f;
        [SerializeField] private bool _enableShieldSegment = true;
        [SerializeField] private bool _syncDelayedOnIncrease = true;

        public Color FrontColor { get => _frontColor; set => _frontColor = value; }
        public Color DelayedColor { get => _delayedColor; set => _delayedColor = value; }
        public Color ShieldColor { get => _shieldColor; set => _shieldColor = value; }
        public Color BackgroundColor { get => _backgroundColor; set => _backgroundColor = value; }
        public Color LowValueColor { get => _lowValueColor; set => _lowValueColor = value; }
        public float LowValueThreshold { get => _lowValueThreshold; set => _lowValueThreshold = Mathf.Clamp01(value); }
        public float FrontFillSpeed { get => _frontFillSpeed; set => _frontFillSpeed = Mathf.Max(0.01f, value); }
        public float DelayedFillSpeed { get => _delayedFillSpeed; set => _delayedFillSpeed = Mathf.Max(0.01f, value); }
        public bool ShowValueText { get => _showValueText; set => _showValueText = value; }
        public string ValueTextFormat { get => _valueTextFormat; set => _valueTextFormat = string.IsNullOrWhiteSpace(value) ? "{0}/{1}" : value; }
        public ResourceBarVisibilityMode VisibilityMode { get => _visibilityMode; set => _visibilityMode = value; }
        public float FadeDuration { get => _fadeDuration; set => _fadeDuration = Mathf.Max(0f, value); }
        public float VisibleAfterChangeDuration { get => _visibleAfterChangeDuration; set => _visibleAfterChangeDuration = Mathf.Max(0f, value); }
        public bool EnableShieldSegment { get => _enableShieldSegment; set => _enableShieldSegment = value; }
        public bool SyncDelayedOnIncrease { get => _syncDelayedOnIncrease; set => _syncDelayedOnIncrease = value; }
    }
}
