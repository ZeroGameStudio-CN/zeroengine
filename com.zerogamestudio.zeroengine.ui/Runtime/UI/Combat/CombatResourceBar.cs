using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Combat
{
    public class CombatResourceBar : MonoBehaviour
    {
        private static CombatResourceBarStyle _fallbackStyle;

        [SerializeField] private Image _frontFill;
        [SerializeField] private Image _delayedFill;
        [SerializeField] private Image _shieldFill;
        [SerializeField] private TextMeshProUGUI _valueText;
        [SerializeField] private Image _background;
        [SerializeField] private CombatResourceBarStyle _style;
        [SerializeField] private GameObject _visibilityRoot;

        private float _current;
        private float _min;
        private float _max = 1f;
        private float _shield;
        private float _visibleUntil;
        private bool _manualVisible = true;

        public float ValueNormalized { get; private set; }
        public float FrontNormalized => _frontFill != null ? _frontFill.fillAmount : ValueNormalized;
        public float DelayedNormalized => _delayedFill != null ? _delayedFill.fillAmount : ValueNormalized;
        public float ShieldNormalized { get; private set; }

        private CombatResourceBarStyle EffectiveStyle => _style != null ? _style : FallbackStyle;

        private static CombatResourceBarStyle FallbackStyle
        {
            get
            {
                if (_fallbackStyle == null)
                {
                    _fallbackStyle = ScriptableObject.CreateInstance<CombatResourceBarStyle>();
                    _fallbackStyle.hideFlags = HideFlags.HideAndDontSave;
                }

                return _fallbackStyle;
            }
        }

        private void Update()
        {
            AdvanceFront(Time.unscaledDeltaTime);
            AdvanceDelayed(Time.unscaledDeltaTime);

            if (EffectiveStyle.VisibilityMode == ResourceBarVisibilityMode.ShowOnChangeThenHide)
            {
                ApplyVisibility();
            }
        }

        public void ConfigureForRuntime(Image frontFill, Image delayedFill, Image shieldFill)
        {
            ConfigureForRuntime(frontFill, delayedFill, shieldFill, null, null);
        }

        public void ConfigureForRuntime(Image frontFill, Image delayedFill, Image shieldFill, TextMeshProUGUI valueText, Image background)
        {
            _frontFill = frontFill;
            _delayedFill = delayedFill;
            _shieldFill = shieldFill;
            _valueText = valueText;
            _background = background;

            ConfigureFill(_frontFill);
            ConfigureFill(_delayedFill);
            ConfigureFill(_shieldFill);
            ApplyStyle(_style);
            RefreshVisuals(instant: true);
        }

        public void ApplyStyle(CombatResourceBarStyle style)
        {
            _style = style;
            var effectiveStyle = EffectiveStyle;

            if (_frontFill != null)
            {
                _frontFill.color = ValueNormalized <= effectiveStyle.LowValueThreshold
                    ? effectiveStyle.LowValueColor
                    : effectiveStyle.FrontColor;
            }

            if (_delayedFill != null)
            {
                _delayedFill.color = effectiveStyle.DelayedColor;
            }

            if (_shieldFill != null)
            {
                _shieldFill.color = effectiveStyle.ShieldColor;
            }

            if (_background != null)
            {
                _background.color = effectiveStyle.BackgroundColor;
            }

            RefreshText();
            ApplyShieldSegment();
            ApplyVisibility();
        }

        public void SetValue(float current, float max, bool instant = false)
        {
            SetValue(current, 0f, max, instant);
        }

        public void SetValue(float current, float min, float max, bool instant = false)
        {
            var previous = _current;
            _min = min;
            _max = Mathf.Max(min + 0.0001f, max);
            _current = Mathf.Clamp(current, _min, _max);
            ValueNormalized = Mathf.Clamp01((_current - _min) / (_max - _min));

            var increased = _current > previous;
            RefreshVisuals(instant || (increased && EffectiveStyle.SyncDelayedOnIncrease));
            MarkChanged(previous != _current);
        }

        public void SetShield(float current, float max)
        {
            var previous = _shield;
            var safeMax = Mathf.Max(1f, max);
            _shield = Mathf.Max(0f, current);
            ShieldNormalized = Mathf.Clamp01(_shield / safeMax);

            ApplyShieldSegment();
            MarkChanged(previous != _shield);
        }

        public void SyncDelayed()
        {
            if (_delayedFill != null)
            {
                _delayedFill.fillAmount = ValueNormalized;
            }
        }

        public void AdvanceFront(float deltaTime)
        {
            if (_frontFill == null)
            {
                return;
            }

            _frontFill.fillAmount = Mathf.MoveTowards(
                _frontFill.fillAmount,
                ValueNormalized,
                Mathf.Max(0f, deltaTime) * Mathf.Max(0.01f, EffectiveStyle.FrontFillSpeed));
        }

        public void AdvanceDelayed(float deltaTime)
        {
            if (_delayedFill == null)
            {
                return;
            }

            _delayedFill.fillAmount = Mathf.MoveTowards(
                _delayedFill.fillAmount,
                ValueNormalized,
                Mathf.Max(0f, deltaTime) * Mathf.Max(0.01f, EffectiveStyle.DelayedFillSpeed));
        }

        public void SetVisible(bool visible)
        {
            _manualVisible = visible;
            ApplyVisibility();
        }

        public void SetVisibilityRoot(GameObject visibilityRoot)
        {
            _visibilityRoot = visibilityRoot;
            ApplyVisibility();
        }

        private void RefreshVisuals(bool instant)
        {
            if (_frontFill != null)
            {
                if (instant || _frontFill.fillAmount < ValueNormalized)
                {
                    _frontFill.fillAmount = ValueNormalized;
                }

                _frontFill.color = ValueNormalized <= EffectiveStyle.LowValueThreshold
                    ? EffectiveStyle.LowValueColor
                    : EffectiveStyle.FrontColor;
            }

            if (_delayedFill != null && (instant || _delayedFill.fillAmount < ValueNormalized))
            {
                _delayedFill.fillAmount = ValueNormalized;
            }

            RefreshText();
            ApplyShieldSegment();
            ApplyVisibility();
        }

        private void RefreshText()
        {
            if (_valueText == null)
            {
                return;
            }

            _valueText.gameObject.SetActive(EffectiveStyle.ShowValueText);
            if (!EffectiveStyle.ShowValueText)
            {
                return;
            }

            var current = Mathf.RoundToInt(_current);
            var max = Mathf.RoundToInt(_max);
            try
            {
                _valueText.text = string.Format(EffectiveStyle.ValueTextFormat, current, max);
            }
            catch (FormatException)
            {
                _valueText.text = $"{current}/{max}";
            }
        }

        private static void ConfigureFill(Image fill)
        {
            if (fill == null)
            {
                return;
            }

            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        private void ApplyShieldSegment()
        {
            if (_shieldFill == null)
            {
                return;
            }

            if (!EffectiveStyle.EnableShieldSegment || ShieldNormalized <= 0f)
            {
                _shieldFill.gameObject.SetActive(false);
                return;
            }

            var start = ValueNormalized;
            var end = Mathf.Clamp01(ValueNormalized + ShieldNormalized);
            if (end <= start)
            {
                _shieldFill.gameObject.SetActive(false);
                return;
            }

            var shieldRect = _shieldFill.rectTransform;
            shieldRect.anchorMin = new Vector2(start, 0f);
            shieldRect.anchorMax = new Vector2(end, 1f);
            shieldRect.offsetMin = Vector2.zero;
            shieldRect.offsetMax = Vector2.zero;
            shieldRect.pivot = new Vector2(0f, 0.5f);

            _shieldFill.gameObject.SetActive(true);
            _shieldFill.fillAmount = 1f;
        }

        private void MarkChanged(bool changed)
        {
            if (!changed)
            {
                return;
            }

            _visibleUntil = Time.unscaledTime + EffectiveStyle.VisibleAfterChangeDuration;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            var mode = EffectiveStyle.VisibilityMode;
            if (mode == ResourceBarVisibilityMode.Manual)
            {
                SetActive(_manualVisible);
                return;
            }

            if (mode == ResourceBarVisibilityMode.HideWhenFull)
            {
                SetActive(ValueNormalized < 1f || ShieldNormalized > 0f);
                return;
            }

            if (mode == ResourceBarVisibilityMode.ShowOnChangeThenHide)
            {
                SetActive(Time.unscaledTime <= _visibleUntil || ValueNormalized < 1f || ShieldNormalized > 0f);
                return;
            }

            SetActive(true);
        }

        private void SetActive(bool visible)
        {
            var target = _visibilityRoot != null ? _visibilityRoot : gameObject;
            if (target.activeSelf != visible)
            {
                target.SetActive(visible);
            }
        }
    }
}
