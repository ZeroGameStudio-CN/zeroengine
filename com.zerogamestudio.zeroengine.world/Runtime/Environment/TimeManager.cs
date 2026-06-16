using System;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;
using ZeroEngine.Timing;

namespace ZeroEngine.EnvironmentSystem
{
    #region Enums and Types

    /// <summary>天气类型</summary>
    public enum WeatherType
    {
        Clear,      // 晴天
        Cloudy,     // 多云
        Rain,       // 雨天
        Snow,       // 雪天
        Fog,        // 雾天
        Storm,      // 风暴
        Sandstorm,  // 沙尘暴
        Custom      // 自定义
    }

    /// <summary>时间段</summary>
    public enum TimeOfDay
    {
        Dawn,   // 黎明 (5-7)
        Day,    // 白天 (7-17)
        Dusk,   // 黄昏 (17-19)
        Night   // 夜晚 (19-5)
    }

    /// <summary>环境事件类型</summary>
    public enum EnvironmentEventType
    {
        TimeChanged,
        HourChanged,
        TimeOfDayChanged,
        WeatherChanged,
        WeatherTransitionStarted,
        WeatherTransitionCompleted
    }

    /// <summary>
    /// 环境系统事件参数 (struct 避免每帧 GC 分配)
    /// </summary>
    public readonly struct EnvironmentEventArgs
    {
        public readonly EnvironmentEventType Type;
        public readonly float CurrentHour;
        public readonly int HourInt;
        public readonly TimeOfDay TimeOfDay;
        public readonly TimeOfDay PreviousTimeOfDay;
        public readonly WeatherType Weather;
        public readonly WeatherType PreviousWeather;

        private EnvironmentEventArgs(
            EnvironmentEventType type,
            float currentHour = 0f,
            int hourInt = 0,
            TimeOfDay timeOfDay = default,
            TimeOfDay previousTimeOfDay = default,
            WeatherType weather = default,
            WeatherType previousWeather = default)
        {
            Type = type;
            CurrentHour = currentHour;
            HourInt = hourInt;
            TimeOfDay = timeOfDay;
            PreviousTimeOfDay = previousTimeOfDay;
            Weather = weather;
            PreviousWeather = previousWeather;
        }

        public static EnvironmentEventArgs TimeChanged(float hour)
            => new EnvironmentEventArgs(EnvironmentEventType.TimeChanged, currentHour: hour);

        public static EnvironmentEventArgs HourChanged(int hour)
            => new EnvironmentEventArgs(EnvironmentEventType.HourChanged, hourInt: hour);

        public static EnvironmentEventArgs TimeOfDayChanged(TimeOfDay current, TimeOfDay previous)
            => new EnvironmentEventArgs(EnvironmentEventType.TimeOfDayChanged, timeOfDay: current, previousTimeOfDay: previous);

        public static EnvironmentEventArgs WeatherChanged(WeatherType current, WeatherType previous)
            => new EnvironmentEventArgs(EnvironmentEventType.WeatherChanged, weather: current, previousWeather: previous);

        public static EnvironmentEventArgs WeatherTransitionStarted(WeatherType from, WeatherType to)
            => new EnvironmentEventArgs(EnvironmentEventType.WeatherTransitionStarted, weather: to, previousWeather: from);

        public static EnvironmentEventArgs WeatherTransitionCompleted(WeatherType weather)
            => new EnvironmentEventArgs(EnvironmentEventType.WeatherTransitionCompleted, weather: weather);
    }

    /// <summary>天气预设数据</summary>
    [Serializable]
    public class WeatherPresetData
    {
        public WeatherType WeatherType = WeatherType.Clear;
        [TextArea(2, 3)] public string Description;

        [Header("VFX")]
        public GameObject VfxPrefab;
        public Vector3 VfxOffset = new Vector3(0, 10, 0);

        [Header("Fog Override")]
        public bool OverrideFog;
        public bool EnableFog = true;
        public Color FogColor = Color.gray;
        [Range(0f, 0.1f)] public float FogDensity = 0.02f;

        [Header("Lighting")]
        [Range(0f, 2f)] public float LightIntensityMultiplier = 1f;

        [Header("Transition")]
        public float TransitionDuration = 2f;

        [Header("Audio")]
        public AudioClip AmbientSound;
        [Range(0f, 1f)] public float AmbientVolume = 0.5f;
    }

    /// <summary>昼夜光照预设数据</summary>
    [Serializable]
    public class DayNightPresetData
    {
        [Header("Sun")]
        public Gradient SunColorOverDay = new Gradient();
        public AnimationCurve SunIntensityOverDay = AnimationCurve.Linear(0, 0.2f, 1, 0.2f);
        public float MaxSunIntensity = 1.5f;

        [Header("Sun Rotation")]
        public float SunriseAngle = 90f;
        public float SunsetAngle = 270f;

        [Header("Ambient")]
        public Gradient AmbientColorOverDay = new Gradient();
        public AnimationCurve AmbientIntensityOverDay = AnimationCurve.Linear(0, 0.5f, 1, 0.5f);

        [Header("Fog")]
        public Gradient FogColorOverDay = new Gradient();
        public bool EnableFog = true;

        [Header("Skybox")]
        public Material SkyboxMaterial;
    }

    /// <summary>天气预设 ScriptableObject</summary>
    [CreateAssetMenu(fileName = "WeatherPreset", menuName = "ZeroEngine/Environment/Weather Preset")]
    public class WeatherPresetSO : ScriptableObject
    {
        public WeatherPresetData Data = new WeatherPresetData();

        public WeatherType WeatherType => Data.WeatherType;
        public GameObject VfxPrefab => Data.VfxPrefab;
        public Vector3 VfxOffset => Data.VfxOffset;
        public bool OverrideFog => Data.OverrideFog;
        public bool EnableFog => Data.EnableFog;
        public Color FogColor => Data.FogColor;
        public float FogDensity => Data.FogDensity;
        public float LightIntensityMultiplier => Data.LightIntensityMultiplier;
        public float TransitionDuration => Data.TransitionDuration;
        public AudioClip AmbientSound => Data.AmbientSound;
        public float AmbientVolume => Data.AmbientVolume;
    }

    /// <summary>昼夜光照预设 ScriptableObject</summary>
    [CreateAssetMenu(fileName = "DayNightPreset", menuName = "ZeroEngine/Environment/Day Night Preset")]
    public class DayNightPresetSO : ScriptableObject
    {
        public DayNightPresetData Data = new DayNightPresetData();

        public Gradient SunColorOverDay => Data.SunColorOverDay;
        public AnimationCurve SunIntensityOverDay => Data.SunIntensityOverDay;
        public float MaxSunIntensity => Data.MaxSunIntensity;
        public float SunriseAngle => Data.SunriseAngle;
        public float SunsetAngle => Data.SunsetAngle;
        public Gradient AmbientColorOverDay => Data.AmbientColorOverDay;
        public AnimationCurve AmbientIntensityOverDay => Data.AmbientIntensityOverDay;
        public Gradient FogColorOverDay => Data.FogColorOverDay;
        public bool EnableFog => Data.EnableFog;
        public Material SkyboxMaterial => Data.SkyboxMaterial;
    }

    #endregion

    /// <summary>
    /// 游戏时间管理器
    /// 驱动昼夜循环，管理游戏内时间流逝
    /// </summary>
    public class TimeManager : MonoSingleton<TimeManager>, ISaveable
    {
        [Header("Time Settings")]
        [SerializeField, Range(0f, 24f)] private float _currentHour = 12f;
        [SerializeField, Range(0f, 3600f)] private float _timeScale = 60f;
        [SerializeField] private bool _isPaused;
        [SerializeField] private WorldClockDeltaPolicy _deltaPolicy = WorldClockDeltaPolicy.ScaledUnityDeltaTime;

        [Header("Time of Day Thresholds")]
        [SerializeField] private float _dawnStart = 5f;
        [SerializeField] private float _dayStart = 7f;
        [SerializeField] private float _duskStart = 17f;
        [SerializeField] private float _nightStart = 19f;

        [Header("Debug")]
        [SerializeField] private bool _debugMode;

        // 事件
        public event Action<EnvironmentEventArgs> OnEnvironmentEvent;
        public event Action<float> OnTimeChanged;
        public event Action<int> OnHourChanged;
        public event Action<TimeOfDay> OnTimeOfDayChanged;

        // 内部状态
        private int _lastHour = -1;
        private TimeOfDay _lastTimeOfDay = (TimeOfDay)(-1);

        #region Properties

        public float CurrentHour => _currentHour;
        public float NormalizedTime => _currentHour / 24f;
        public int CurrentHourInt => Mathf.FloorToInt(_currentHour);
        public bool IsPaused => _isPaused;
        public float TimeScale { get => _timeScale; set => _timeScale = Mathf.Max(0, value); }
        public WorldClockDeltaPolicy DeltaPolicy { get => _deltaPolicy; set => _deltaPolicy = value; }

        public TimeOfDay CurrentTimeOfDay
        {
            get
            {
                if (_currentHour >= _nightStart || _currentHour < _dawnStart) return TimeOfDay.Night;
                if (_currentHour >= _duskStart) return TimeOfDay.Dusk;
                if (_currentHour >= _dayStart) return TimeOfDay.Day;
                return TimeOfDay.Dawn;
            }
        }

        #endregion

        #region ISaveable

        public string SaveKey => "TimeManager";

        public void Register() => SaveSlotManager.Instance?.Register(this);
        public void Unregister() => SaveSlotManager.Instance?.Unregister(this);

        public object ExportSaveData()
        {
            return new TimeSaveData
            {
                CurrentHour = _currentHour,
                TimeScale = _timeScale,
                IsPaused = _isPaused
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is not TimeSaveData saveData) return;

            _currentHour = saveData.CurrentHour;
            _timeScale = saveData.TimeScale;
            _isPaused = saveData.IsPaused;
            _lastHour = -1;
            _lastTimeOfDay = (TimeOfDay)(-1);
        }

        public void ResetToDefault()
        {
            _currentHour = 12f;
            _timeScale = 60f;
            _isPaused = false;
            _lastHour = -1;
            _lastTimeOfDay = (TimeOfDay)(-1);
        }

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            Register();
        }

        private void Update()
        {
            if (_isPaused) return;

            float deltaSeconds = _deltaPolicy == WorldClockDeltaPolicy.ScaledUnityDeltaTime
                ? Time.deltaTime
                : Time.unscaledDeltaTime;

            AdvanceSeconds(deltaSeconds, _deltaPolicy == WorldClockDeltaPolicy.UnscaledWorldClockDomain);
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Public API

        /// <summary>设置当前时间</summary>
        public void SetTime(float hour)
        {
            _currentHour = Mathf.Repeat(hour, 24f);
            _lastHour = -1;
            _lastTimeOfDay = (TimeOfDay)(-1);
            Log($"时间设置为: {hour:F2}");
        }

        /// <summary>设置时间为指定时间段</summary>
        public void SetTimeOfDay(TimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case TimeOfDay.Dawn: SetTime(_dawnStart); break;
                case TimeOfDay.Day: SetTime(12f); break;
                case TimeOfDay.Dusk: SetTime(_duskStart); break;
                case TimeOfDay.Night: SetTime(_nightStart); break;
            }
        }

        /// <summary>暂停时间</summary>
        public void Pause() => _isPaused = true;

        /// <summary>恢复时间</summary>
        public void Resume() => _isPaused = false;

        /// <summary>设置暂停状态</summary>
        public void SetPaused(bool paused) => _isPaused = paused;

        /// <summary>推进指定小时数</summary>
        public void AdvanceHours(float hours)
        {
            AdvanceHoursInternal(hours);
        }

        public void AdvanceUnscaledSecondsForTests(float unscaledSeconds)
        {
            if (_isPaused) return;
            AdvanceSeconds(unscaledSeconds, _deltaPolicy == WorldClockDeltaPolicy.UnscaledWorldClockDomain);
        }

        /// <summary>获取格式化时间字符串</summary>
        public string GetFormattedTime()
        {
            int hour = CurrentHourInt;
            int minute = Mathf.FloorToInt((_currentHour - hour) * 60);
            return $"{hour:D2}:{minute:D2}";
        }

        #endregion

        #region Internal

        private void AdvanceSeconds(float deltaSeconds, bool applyWorldClockScale)
        {
            float seconds = Mathf.Max(0f, deltaSeconds);
            if (applyWorldClockScale)
            {
                seconds *= TimeControlLocator.Service.GetScale(TimeDomainIds.WorldClock);
            }

            float deltaHours = (seconds / 3600f) * _timeScale;
            AdvanceHoursInternal(deltaHours);
        }

        private void AdvanceHoursInternal(float hours)
        {
            _currentHour = Mathf.Repeat(_currentHour + hours, 24f);
            PublishTimeEvents();
        }

        private void PublishTimeEvents()
        {
            // 每帧时间更新 - 使用简单 float 事件避免 boxing
            // 注意: OnEnvironmentEvent(TimeChanged) 不在每帧触发以避免 GC
            // 如需每帧时间，请使用 OnTimeChanged 事件
            OnTimeChanged?.Invoke(_currentHour);

            int currentHourInt = CurrentHourInt;
            if (currentHourInt != _lastHour)
            {
                _lastHour = currentHourInt;
                OnHourChanged?.Invoke(currentHourInt);
                OnEnvironmentEvent?.Invoke(EnvironmentEventArgs.HourChanged(currentHourInt));
                Log($"小时变更: {currentHourInt}:00");
            }

            TimeOfDay currentToD = CurrentTimeOfDay;
            if (currentToD != _lastTimeOfDay)
            {
                TimeOfDay previousToD = _lastTimeOfDay;
                _lastTimeOfDay = currentToD;
                OnTimeOfDayChanged?.Invoke(currentToD);
                OnEnvironmentEvent?.Invoke(EnvironmentEventArgs.TimeOfDayChanged(currentToD, previousToD));
                Log($"时间段变更: {previousToD} -> {currentToD}");
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode) Debug.Log($"[Time] {message}");
        }

        #endregion
    }

    #region Save Data

    [Serializable]
    public class TimeSaveData
    {
        public float CurrentHour;
        public float TimeScale;
        public bool IsPaused;
    }

    #endregion
}
