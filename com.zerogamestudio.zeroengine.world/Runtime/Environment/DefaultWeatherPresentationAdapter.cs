using System.Collections;
using UnityEngine;

namespace ZeroEngine.EnvironmentSystem
{
    /// <summary>
    /// Legacy Unity presentation for ZE weather presets. Attach explicitly when fog, VFX, and ambient audio are wanted.
    /// </summary>
    public sealed class DefaultWeatherPresentationAdapter : MonoBehaviour, IWeatherPresentationAdapter, IWeatherFollowTargetAdapter
    {
        [SerializeField] private Transform _followTarget;
        [SerializeField] private bool _debugMode;

        private GameObject _activeVfx;
        private AudioSource _currentAmbientSource;
        private WeatherPresetSO _currentPreset;
        private float _originalFogDensity;
        private Color _originalFogColor;
        private bool _originalFogEnabled;

        private void Awake()
        {
            _originalFogEnabled = RenderSettings.fog;
            _originalFogColor = RenderSettings.fogColor;
            _originalFogDensity = RenderSettings.fogDensity;
        }

        private void LateUpdate()
        {
            if (_activeVfx != null && _followTarget != null && _currentPreset != null)
            {
                _activeVfx.transform.position = _followTarget.position + _currentPreset.VfxOffset;
            }
        }

        private void OnDestroy()
        {
            ClearActiveVfx();
            if (_currentAmbientSource != null)
            {
                Destroy(_currentAmbientSource.gameObject);
                _currentAmbientSource = null;
            }

            RestoreFogImmediate();
        }

        public void SetFollowTarget(Transform target)
        {
            _followTarget = target;
        }

        public void ApplyWeatherPresentation(WeatherPresentationContext context)
        {
            if (!context.HasPreset)
            {
                ClearWeatherPresentation(context.PreviousWeatherType);
                return;
            }

            ResolveFollowTarget();
            _currentPreset = context.CurrentPreset;
            ApplyWeather(context.CurrentPreset, context.Immediate);
        }

        public void ClearWeatherPresentation(WeatherType previousWeatherType)
        {
            _currentPreset = null;
            ClearActiveVfx();
            StopAmbientSound();
            RestoreFog();
        }

        private void ResolveFollowTarget()
        {
            if (_followTarget != null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _followTarget = mainCamera.transform;
            }
        }

        private void ClearActiveVfx()
        {
            if (_activeVfx == null)
            {
                return;
            }

            Destroy(_activeVfx);
            _activeVfx = null;
        }

        private void ApplyWeather(WeatherPresetSO preset, bool immediate)
        {
            float duration = immediate ? 0f : preset.TransitionDuration;

            ClearActiveVfx();
            if (preset.VfxPrefab != null && _followTarget != null)
            {
                _activeVfx = Instantiate(
                    preset.VfxPrefab,
                    _followTarget.position + preset.VfxOffset,
                    Quaternion.identity);
            }

            if (preset.OverrideFog)
            {
                ApplyFog(preset, immediate, duration);
            }

            PlayAmbientSound(preset, duration);
            Log($"Applied weather presentation: {preset.WeatherType}");
        }

        private void ApplyFog(WeatherPresetSO preset, bool immediate, float duration)
        {
            RenderSettings.fog = preset.EnableFog;
            if (!preset.EnableFog)
            {
                return;
            }

            if (immediate || duration <= 0f)
            {
                RenderSettings.fogColor = preset.FogColor;
                RenderSettings.fogDensity = preset.FogDensity;
                return;
            }

            StartCoroutine(TransitionFog(preset.FogColor, preset.FogDensity, duration));
        }

        private IEnumerator TransitionFog(Color targetColor, float targetDensity, float duration)
        {
            Color startColor = RenderSettings.fogColor;
            float startDensity = RenderSettings.fogDensity;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                RenderSettings.fogColor = Color.Lerp(startColor, targetColor, t);
                RenderSettings.fogDensity = Mathf.Lerp(startDensity, targetDensity, t);
                yield return null;
            }

            RenderSettings.fogColor = targetColor;
            RenderSettings.fogDensity = targetDensity;
        }

        private void RestoreFog()
        {
            if (!isActiveAndEnabled)
            {
                RestoreFogImmediate();
                return;
            }

            StartCoroutine(TransitionFog(_originalFogColor, _originalFogDensity, 1f));
            RenderSettings.fog = _originalFogEnabled;
        }

        private void RestoreFogImmediate()
        {
            RenderSettings.fog = _originalFogEnabled;
            RenderSettings.fogColor = _originalFogColor;
            RenderSettings.fogDensity = _originalFogDensity;
        }

        private void PlayAmbientSound(WeatherPresetSO preset, float fadeDuration)
        {
            StopAmbientSound();
            if (preset.AmbientSound == null)
            {
                return;
            }

            var go = new GameObject("WeatherAmbient");
            go.transform.SetParent(transform);
            _currentAmbientSource = go.AddComponent<AudioSource>();
            _currentAmbientSource.clip = preset.AmbientSound;
            _currentAmbientSource.loop = true;
            _currentAmbientSource.volume = 0f;
            _currentAmbientSource.Play();

            StartCoroutine(FadeAudioVolume(_currentAmbientSource, preset.AmbientVolume, fadeDuration));
        }

        private void StopAmbientSound()
        {
            if (_currentAmbientSource == null)
            {
                return;
            }

            AudioSource source = _currentAmbientSource;
            _currentAmbientSource = null;
            StartCoroutine(FadeOutAndDestroy(source, 1f));
        }

        private IEnumerator FadeAudioVolume(AudioSource source, float targetVolume, float duration)
        {
            if (source == null)
            {
                yield break;
            }

            if (duration <= 0f)
            {
                source.volume = targetVolume;
                yield break;
            }

            float startVolume = source.volume;
            float elapsed = 0f;

            while (elapsed < duration && source != null)
            {
                elapsed += Time.deltaTime;
                source.volume = Mathf.Lerp(startVolume, targetVolume, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (source != null)
            {
                source.volume = targetVolume;
            }
        }

        private IEnumerator FadeOutAndDestroy(AudioSource source, float duration)
        {
            if (source == null)
            {
                yield break;
            }

            yield return FadeAudioVolume(source, 0f, duration);

            if (source != null)
            {
                Destroy(source.gameObject);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[WeatherPresentation] {message}");
            }
        }
    }
}
