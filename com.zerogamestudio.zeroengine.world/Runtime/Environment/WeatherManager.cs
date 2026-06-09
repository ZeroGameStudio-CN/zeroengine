using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.EnvironmentSystem
{
    /// <summary>
    /// 天气系统管理器。
    /// Owns weather state, presets, save data, and environment events. Presentation is delegated to optional adapters.
    /// </summary>
    public class WeatherManager : MonoSingleton<WeatherManager>, ISaveable
    {
        [Header("Current Weather")]
        [SerializeField] private WeatherPresetSO _currentWeather;

        [Header("Available Presets")]
        [SerializeField] private List<WeatherPresetSO> _weatherPresets = new List<WeatherPresetSO>();

        [Header("Debug")]
        [SerializeField] private bool _debugMode;

        public event Action<EnvironmentEventArgs> OnEnvironmentEvent;

        private readonly WeatherState _state = new WeatherState();
        private readonly List<IWeatherPresentationAdapter> _presentationAdapters = new List<IWeatherPresentationAdapter>();
        private readonly Dictionary<WeatherType, WeatherPresetSO> _presetLookup = new Dictionary<WeatherType, WeatherPresetSO>();
        private bool _presentationAdaptersOverriddenForTests;

        #region Properties

        public WeatherPresetSO CurrentWeather => _currentWeather;
        public WeatherType CurrentWeatherType => _state.CurrentWeatherType;

        #endregion

        #region ISaveable

        public string SaveKey => "WeatherManager";

        public void Register() => SaveSlotManager.Instance?.Register(this);
        public void Unregister() => SaveSlotManager.Instance?.Unregister(this);

        public object ExportSaveData()
        {
            return _state.ExportSaveData();
        }

        public void ImportSaveData(object data)
        {
            if (data is not WeatherSaveData saveData) return;
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
            _state.OnEnvironmentEvent += HandleStateEnvironmentEvent;
            BuildPresetLookup();
            RefreshPresentationAdapters();
        }

        private void Start()
        {
            Register();

            if (_currentWeather != null)
            {
                WeatherType previousType = CurrentWeatherType;
                _state.SetWeather(_currentWeather.WeatherType);
                NotifyApplyPresentation(previousType, _currentWeather, true);
            }
        }

        protected override void OnDestroy()
        {
            Unregister();
            _state.OnEnvironmentEvent -= HandleStateEnvironmentEvent;
            base.OnDestroy();
        }

        #endregion

        #region Public API

        /// <summary>设置天气</summary>
        public void SetWeather(WeatherPresetSO preset)
        {
            if (preset == null) return;
            if (preset == _currentWeather && CurrentWeatherType == preset.WeatherType) return;

            WeatherType previousType = CurrentWeatherType;
            _currentWeather = preset;
            _state.SetWeather(preset.WeatherType);
            NotifyApplyPresentation(previousType, preset, false);

            Log($"天气变更: {previousType} -> {preset.WeatherType}");
        }

        /// <summary>通过类型设置天气</summary>
        public void SetWeather(WeatherType type)
        {
            WeatherPresetSO preset = GetPreset(type);
            if (preset != null)
            {
                SetWeather(preset);
                return;
            }

            WeatherType previousType = CurrentWeatherType;
            _currentWeather = null;
            _state.SetWeather(type);
            NotifyClearPresentation(previousType);
            Log($"天气变更: {previousType} -> {type}");
        }

        /// <summary>获取预设</summary>
        public WeatherPresetSO GetPreset(WeatherType type)
        {
            _presetLookup.TryGetValue(type, out WeatherPresetSO preset);
            return preset;
        }

        /// <summary>清除天气状态和已绑定表现。</summary>
        public void ClearWeather()
        {
            WeatherType previousType = CurrentWeatherType;
            _currentWeather = null;
            _state.ClearWeather();
            NotifyClearPresentation(previousType);
        }

        /// <summary>兼容旧 API：将跟随目标转发给已绑定的天气表现适配器。</summary>
        public void SetFollowTarget(Transform target)
        {
            if (!_presentationAdaptersOverriddenForTests)
            {
                RefreshPresentationAdapters();
            }

            for (int i = 0; i < _presentationAdapters.Count; i++)
            {
                if (_presentationAdapters[i] is IWeatherFollowTargetAdapter adapter)
                {
                    adapter.SetFollowTarget(target);
                }
            }
        }

        /// <summary>注册新的天气预设</summary>
        public void RegisterPreset(WeatherPresetSO preset)
        {
            if (preset == null) return;
            if (!_weatherPresets.Contains(preset))
            {
                _weatherPresets.Add(preset);
            }

            _presetLookup[preset.WeatherType] = preset;
        }

        /// <summary>测试专用：覆盖自动发现的天气表现适配器。</summary>
        public void SetPresentationAdaptersForTests(params IWeatherPresentationAdapter[] adapters)
        {
            _presentationAdaptersOverriddenForTests = true;
            _presentationAdapters.Clear();
            if (adapters == null)
            {
                return;
            }

            for (int i = 0; i < adapters.Length; i++)
            {
                if (adapters[i] != null)
                {
                    _presentationAdapters.Add(adapters[i]);
                }
            }
        }

        #endregion

        #region Internal

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

        private void RefreshPresentationAdapters()
        {
            if (_presentationAdaptersOverriddenForTests)
            {
                return;
            }

            _presentationAdapters.Clear();
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || ReferenceEquals(behaviour, this))
                {
                    continue;
                }

                if (behaviour is IWeatherPresentationAdapter adapter)
                {
                    _presentationAdapters.Add(adapter);
                }
            }
        }

        private void NotifyApplyPresentation(WeatherType previousType, WeatherPresetSO preset, bool immediate)
        {
            if (!_presentationAdaptersOverriddenForTests)
            {
                RefreshPresentationAdapters();
            }

            var context = new WeatherPresentationContext(previousType, preset.WeatherType, preset, immediate);
            for (int i = 0; i < _presentationAdapters.Count; i++)
            {
                _presentationAdapters[i]?.ApplyWeatherPresentation(context);
            }
        }

        private void NotifyClearPresentation(WeatherType previousType)
        {
            if (!_presentationAdaptersOverriddenForTests)
            {
                RefreshPresentationAdapters();
            }

            for (int i = 0; i < _presentationAdapters.Count; i++)
            {
                _presentationAdapters[i]?.ClearWeatherPresentation(previousType);
            }
        }

        private void HandleStateEnvironmentEvent(EnvironmentEventArgs args)
        {
            OnEnvironmentEvent?.Invoke(args);
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
