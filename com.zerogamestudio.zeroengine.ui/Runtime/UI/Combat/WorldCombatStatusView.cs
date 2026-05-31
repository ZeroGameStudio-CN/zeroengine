using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Combat
{
    public sealed class WorldCombatStatusView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _factionText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _valueText;
        [SerializeField] private Image _factionStrip;
        [SerializeField] private Image _healthFill;
        [SerializeField] private Image _delayedHealthFill;
        [SerializeField] private Image _shieldFill;
        [SerializeField] private WorldCombatResourceBar _healthBar;
        [SerializeField] private CombatResourceBarStyle _healthBarStyle;
        [SerializeField] private GameObject _selectionMarker;
        [SerializeField] private GameObject _turnMarker;
        [SerializeField] private float _delayedHealthSpeed = 1.6f;

        private WorldCombatResourceBar _configuredHealthBar;
        private Image _configuredHealthFill;
        private Image _configuredDelayedHealthFill;
        private Image _configuredShieldFill;
        private GameObject _configuredVisibilityRoot;
        private CombatResourceBarStyle _configuredHealthBarStyle;

        public float HealthNormalized { get; private set; }
        public float ShieldNormalized { get; private set; }

        private void Update()
        {
            if (_healthBar == null)
            {
                AdvanceDelayedHealth(Time.unscaledDeltaTime);
            }
        }

        public void ConfigureForRuntime(
            TextMeshProUGUI factionText,
            TextMeshProUGUI nameText,
            TextMeshProUGUI valueText,
            Image factionStrip,
            Image healthFill,
            Image delayedHealthFill,
            Image shieldFill,
            GameObject selectionMarker,
            GameObject turnMarker)
        {
            _factionText = factionText;
            _nameText = nameText;
            _valueText = valueText;
            _factionStrip = factionStrip;
            _healthFill = healthFill;
            _delayedHealthFill = delayedHealthFill;
            _shieldFill = shieldFill;
            _selectionMarker = selectionMarker;
            _turnMarker = turnMarker;
            ConfigureHealthBar();
        }

        public void SetName(string displayName)
        {
            if (_nameText != null)
            {
                _nameText.text = displayName ?? string.Empty;
            }
        }

        public void SetFaction(string label, Color color)
        {
            if (_factionText != null)
            {
                _factionText.text = label ?? string.Empty;
                _factionText.color = color;
            }

            if (_factionStrip != null)
            {
                _factionStrip.color = color;
            }
        }

        public void SetHealth(float current, float max, bool instant = false)
        {
            var safeMax = Mathf.Max(1f, max);
            HealthNormalized = Mathf.Clamp01(current / safeMax);
            ConfigureHealthBar();

            if (_healthBar != null)
            {
                _healthBar.SetValue(current, safeMax, instant);
            }
            else if (_healthFill != null)
            {
                _healthFill.fillAmount = HealthNormalized;
            }

            if (_healthBar == null && _delayedHealthFill != null && (instant || _delayedHealthFill.fillAmount < HealthNormalized))
            {
                _delayedHealthFill.fillAmount = HealthNormalized;
            }

            if (_valueText != null)
            {
                _valueText.text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(safeMax)}";
            }
        }

        public void SetShield(float current, float max)
        {
            var safeMax = Mathf.Max(1f, max);
            ShieldNormalized = Mathf.Clamp01(current / safeMax);
            ConfigureHealthBar();

            if (_healthBar != null)
            {
                _healthBar.SetShield(current, safeMax);
            }
            else if (_shieldFill != null)
            {
                _shieldFill.gameObject.SetActive(current > 0f);
                _shieldFill.fillAmount = ShieldNormalized;
            }
        }

        public void SyncDelayedHealth()
        {
            if (_healthBar != null)
            {
                _healthBar.SyncDelayed();
            }
            else if (_delayedHealthFill != null)
            {
                _delayedHealthFill.fillAmount = HealthNormalized;
            }
        }

        public void AdvanceDelayedHealth(float deltaTime)
        {
            if (_healthBar != null)
            {
                _healthBar.AdvanceDelayed(deltaTime);
                return;
            }

            if (_delayedHealthFill == null)
            {
                return;
            }

            _delayedHealthFill.fillAmount = Mathf.MoveTowards(
                _delayedHealthFill.fillAmount,
                HealthNormalized,
                Mathf.Max(0f, deltaTime) * Mathf.Max(0.01f, _delayedHealthSpeed));
        }

        public void ApplyHealthBarStyle(CombatResourceBarStyle style)
        {
            _healthBarStyle = style;
            ConfigureHealthBar();
        }

        public void SetSelected(bool selected)
        {
            if (_selectionMarker != null)
            {
                _selectionMarker.SetActive(selected);
            }
        }

        public void SetTurnActive(bool active)
        {
            if (_turnMarker != null)
            {
                _turnMarker.SetActive(active);
            }
        }

        private void ConfigureHealthBar()
        {
            if (_healthBar == null)
            {
                _healthBar = GetComponent<WorldCombatResourceBar>();
            }

            if (_healthBar == null && (_healthFill != null || _delayedHealthFill != null || _shieldFill != null))
            {
                _healthBar = gameObject.AddComponent<WorldCombatResourceBar>();
            }

            if (_healthBar != null)
            {
                var visibilityRoot = ResolveHealthBarRoot();
                var barReferencesChanged = _configuredHealthBar != _healthBar
                    || _configuredHealthFill != _healthFill
                    || _configuredDelayedHealthFill != _delayedHealthFill
                    || _configuredShieldFill != _shieldFill;

                if (barReferencesChanged)
                {
                    _healthBar.ConfigureForRuntime(_healthFill, _delayedHealthFill, _shieldFill);
                    _configuredHealthBar = _healthBar;
                    _configuredHealthFill = _healthFill;
                    _configuredDelayedHealthFill = _delayedHealthFill;
                    _configuredShieldFill = _shieldFill;
                }

                if (_configuredVisibilityRoot != visibilityRoot)
                {
                    _healthBar.SetVisibilityRoot(visibilityRoot);
                    _configuredVisibilityRoot = visibilityRoot;
                }

                if (barReferencesChanged || _configuredHealthBarStyle != _healthBarStyle)
                {
                    _healthBar.ApplyStyle(_healthBarStyle);
                    _configuredHealthBarStyle = _healthBarStyle;
                }
            }
        }

        private GameObject ResolveHealthBarRoot()
        {
            if (_healthFill != null)
            {
                return _healthFill.transform.parent != null ? _healthFill.transform.parent.gameObject : _healthFill.gameObject;
            }

            if (_delayedHealthFill != null)
            {
                return _delayedHealthFill.transform.parent != null ? _delayedHealthFill.transform.parent.gameObject : _delayedHealthFill.gameObject;
            }

            return _shieldFill != null ? _shieldFill.gameObject : gameObject;
        }
    }
}
