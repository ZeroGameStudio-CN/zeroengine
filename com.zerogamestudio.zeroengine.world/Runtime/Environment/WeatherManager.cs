using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.EnvironmentSystem
{
    /// <summary>
    /// Owns weather state, lookup, save data, and events. Visual and audio output
    /// is delegated to an explicitly authored presentation adapter.
    /// </summary>
    public class WeatherManager : MonoSingleton<WeatherManager>, ISaveable
    {
        [Header("Current Weather")]
        [SerializeField] private WeatherPresetSO _currentWeather;

        [Header("Available Presets")]
        [SerializeField] private List<WeatherPresetSO> _weatherPresets = new List<WeatherPresetSO>();

        [Header("Presentation (Optional)")]
        [Tooltip("Leave empty for state-only weather. Assign an IWeatherPresentationAdapter to opt into visuals and audio.")]
        [SerializeField] private MonoBehaviour _presentationAdapterBehaviour;
        [SerializeField] private Transform _followTarget;

        [Header("Debug")]
        [SerializeField] private bool _debugMode;

        public event Action<EnvironmentEventArgs> OnEnvironmentEvent;

        private readonly Dictionary<WeatherType, WeatherPresetSO> _presetLookup =
            new Dictionary<WeatherType, WeatherPresetSO>();
        private IWeatherPresentationAdapter _presentationAdapter;

        #region Properties

        public WeatherPresetSO CurrentWeather => _currentWeather;
        public WeatherType CurrentWeatherType =>
            _currentWeather != null ? _currentWeather.WeatherType : WeatherType.Clear;
        public bool HasPresentationAdapter => _presentationAdapter != null;

        #endregion

        #region ISaveable

        public string SaveKey => "WeatherManager";

        public void Register() => SaveSlotManager.Instance?.Register(this);
        public void Unregister() => SaveSlotManager.Instance?.Unregister(this);

        public object ExportSaveData()
        {
            return new WeatherSaveData
            {
                CurrentWeatherType = CurrentWeatherType
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is not WeatherSaveData saveData)
            {
                return;
            }

            SetWeather(saveData.CurrentWeatherType);
        }

        public void ResetToDefault()
        {
            ClearWeather();
            if (_weatherPresets.Count > 0)
            {
                SetWeather(_weatherPresets[0]);
            }
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            ResolvePresentationAdapter();
            BindFollowTarget();
            BuildPresetLookup();
        }

        private void Start()
        {
            Register();
            if (_currentWeather != null)
            {
                PresentWeather(_currentWeather, WeatherType.Clear, true);
            }
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Public API

        /// <summary>设置天气</summary>
        public void SetWeather(WeatherPresetSO preset)
        {
            if (preset == null || preset == _currentWeather)
            {
                return;
            }

            WeatherType previousType = CurrentWeatherType;
            _currentWeather = preset;
            PresentWeather(preset, previousType, false);

            OnEnvironmentEvent?.Invoke(
                EnvironmentEventArgs.WeatherChanged(preset.WeatherType, previousType));
            Log($"天气变更: {previousType} -> {preset.WeatherType}");
        }

        /// <summary>通过类型设置天气</summary>
        public void SetWeather(WeatherType type)
        {
            WeatherPresetSO preset = GetPreset(type);
            if (preset != null)
            {
                SetWeather(preset);
            }
        }

        /// <summary>获取预设</summary>
        public WeatherPresetSO GetPreset(WeatherType type)
        {
            _presetLookup.TryGetValue(type, out WeatherPresetSO preset);
            return preset;
        }

        /// <summary>清除天气状态，并通知显式演出适配器恢复其自有状态。</summary>
        public void ClearWeather()
        {
            WeatherType previousType = CurrentWeatherType;
            _currentWeather = null;
            _presentationAdapter?.ClearWeatherPresentation(previousType);
        }

        /// <summary>设置天气演出的跟随目标。</summary>
        public void SetFollowTarget(Transform target)
        {
            _followTarget = target;
            BindFollowTarget();
        }

        /// <summary>注册新的天气预设</summary>
        public void RegisterPreset(WeatherPresetSO preset)
        {
            if (preset == null)
            {
                return;
            }

            if (!_weatherPresets.Contains(preset))
            {
                _weatherPresets.Add(preset);
            }

            _presetLookup[preset.WeatherType] = preset;
        }

#if UNITY_INCLUDE_TESTS
        public void ConfigurePresentationAdapterForTests(MonoBehaviour adapterBehaviour)
        {
            _presentationAdapterBehaviour = adapterBehaviour;
            ResolvePresentationAdapter();
            BindFollowTarget();
        }
#endif

        #endregion

        #region Internal

        private void ResolvePresentationAdapter()
        {
            _presentationAdapter = null;
            if (_presentationAdapterBehaviour == null)
            {
                return;
            }

            if (_presentationAdapterBehaviour is not IWeatherPresentationAdapter adapter)
            {
                throw new InvalidOperationException(
                    $"Weather presentation component '{_presentationAdapterBehaviour.GetType().FullName}' "
                    + $"must implement {nameof(IWeatherPresentationAdapter)}.");
            }

            _presentationAdapter = adapter;
        }

        private void BindFollowTarget()
        {
            if (_presentationAdapter is IWeatherFollowTargetAdapter followTargetAdapter)
            {
                followTargetAdapter.SetFollowTarget(_followTarget);
            }
        }

        private void BuildPresetLookup()
        {
            _presetLookup.Clear();
            foreach (WeatherPresetSO preset in _weatherPresets)
            {
                if (preset != null)
                {
                    _presetLookup[preset.WeatherType] = preset;
                }
            }
        }

        private void PresentWeather(
            WeatherPresetSO preset,
            WeatherType previousType,
            bool immediate)
        {
            if (_presentationAdapter == null)
            {
                return;
            }

            _presentationAdapter.ApplyWeatherPresentation(
                new WeatherPresentationContext(
                    previousType,
                    preset.WeatherType,
                    preset,
                    immediate));
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[Weather] {message}");
            }
        }

        #endregion
    }

    #region Save Data

    [Serializable]
    public class WeatherSaveData
    {
        public WeatherType CurrentWeatherType;
    }

    #endregion
}
