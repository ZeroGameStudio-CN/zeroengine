using UnityEngine;

namespace ZeroEngine.Timing
{
    public sealed class UnityGlobalTimeDriver : MonoBehaviour
    {
        [SerializeField]
        private string[] _drivenDomainValues =
        {
            TimeDomainIds.GlobalValue,
            TimeDomainIds.GameplayValue,
            TimeDomainIds.PresentationValue,
            TimeDomainIds.CinematicValue
        };

        private float _originalFixedDeltaTime;
        private bool _hasOriginalFixedDeltaTime;

        private void Awake()
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            _hasOriginalFixedDeltaTime = true;
        }

        private void Update()
        {
            TimeControlLocator.Service.Tick(Time.unscaledDeltaTime);
            ApplyUnityTimeScale();
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            if (_hasOriginalFixedDeltaTime)
            {
                Time.fixedDeltaTime = _originalFixedDeltaTime;
            }
        }

        private void ApplyUnityTimeScale()
        {
            float scale = 1f;
            if (_drivenDomainValues != null)
            {
                foreach (string value in _drivenDomainValues)
                {
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    scale = Mathf.Min(scale, TimeControlLocator.Service.GetScale(new TimeDomainId(value)));
                }
            }

            scale = TimeScaleMath.Clamp(scale);
            Time.timeScale = scale;

            if (_hasOriginalFixedDeltaTime)
            {
                Time.fixedDeltaTime = _originalFixedDeltaTime * Mathf.Max(scale, 0.0001f);
            }
        }
    }
}
