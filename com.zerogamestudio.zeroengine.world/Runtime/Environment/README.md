# ZeroEngine.EnvironmentSystem API 文档

> **用途**: 本文档面向 AI 助手，提供 Environment 模块的快速参考。

> **注意**: v1.13.0 命名空间已从 `ZeroEngine.Environment` 变更为 `ZeroEngine.EnvironmentSystem`

---

## 目录结构

| 文件 | 描述 |
|------|------|
| `TimeManager.cs` | 游戏时间管理器 + 所有枚举/类型/ScriptableObject 定义 |
| `WeatherState.cs` | 纯天气状态与存档数据 |
| `WeatherManager.cs` | 天气状态、预设、事件和存档管理器 |
| `WeatherPresentationContext.cs` | 天气表现适配器上下文 |
| `IWeatherPresentationAdapter.cs` | 天气表现适配器接口 |
| `IWeatherFollowTargetAdapter.cs` | 可选 VFX 跟随目标接口 |
| `DefaultWeatherPresentationAdapter.cs` | 默认雾效、VFX、环境音适配器 |
| `LightingManager.cs` | 光照控制器 |

**类型定义 (均在 TimeManager.cs 中)**:
- `WeatherType` - 天气类型枚举
- `TimeOfDay` - 时间段枚举
- `EnvironmentEventType` - 事件类型枚举
- `EnvironmentEventArgs` - 事件参数类
- `WeatherPresetData` / `DayNightPresetData` - 数据结构
- `WeatherPresetSO` / `DayNightPresetSO` - ScriptableObject

---

## WeatherManager.cs

**用途**: 管理天气状态、预设查询、存档数据和天气事件。雾效、VFX、环境音效等 Unity 表现副作用由显式绑定的 `IWeatherPresentationAdapter` 处理。

### Public API

```csharp
public class WeatherManager : MonoSingleton<WeatherManager>, ISaveable
{
    // Properties
    WeatherPresetSO CurrentWeather { get; }
    WeatherType CurrentWeatherType { get; }

    // Events
    event Action<EnvironmentEventArgs> OnEnvironmentEvent;

    // Methods
    void SetWeather(WeatherPresetSO preset);
    void SetWeather(WeatherType type);
    WeatherPresetSO GetPreset(WeatherType type);
    void ClearWeather();
    void SetFollowTarget(Transform target);  // 兼容 API: 转发给 IWeatherFollowTargetAdapter
    void RegisterPreset(WeatherPresetSO preset);

    // ISaveable
    string SaveKey { get; }  // "WeatherManager"
    object ExportSaveData();
    void ImportSaveData(object data);
    void ResetToDefault();
}
```

**使用示例**:

```csharp
using ZeroEngine.EnvironmentSystem;

// 设置天气
WeatherManager.Instance.SetWeather(WeatherType.Rain);
WeatherManager.Instance.SetWeather(myRainPreset);

// 获取当前天气
var weather = WeatherManager.Instance.CurrentWeatherType;

// 清除天气状态，并通知已绑定的表现适配器清理表现
WeatherManager.Instance.ClearWeather();

// 设置 VFX 跟随目标；仅当同对象绑定了 IWeatherFollowTargetAdapter 时生效
WeatherManager.Instance.SetFollowTarget(Camera.main.transform);

// 监听天气变化
WeatherManager.Instance.OnEnvironmentEvent += args =>
{
    if (args.Type == EnvironmentEventType.WeatherChanged)
        Debug.Log($"Weather: {args.PreviousWeather} -> {args.Weather}");
};
```

---

## DefaultWeatherPresentationAdapter.cs

**用途**: 提供默认 Unity 表现层，把 `WeatherPresetSO` 中的 VFX、雾效和环境音应用到场景。需要这些表现时，显式把该组件挂在 `WeatherManager` 同一个 GameObject 上；只需要天气状态和事件时不要绑定它。

### Public API

```csharp
public sealed class DefaultWeatherPresentationAdapter : MonoBehaviour,
    IWeatherPresentationAdapter,
    IWeatherFollowTargetAdapter
{
    void SetFollowTarget(Transform target);
    void ApplyWeatherPresentation(WeatherPresentationContext context);
    void ClearWeatherPresentation(WeatherType previousWeatherType);
}
```

**使用示例**:

```csharp
using ZeroEngine.EnvironmentSystem;

// 组件应在 Inspector 中和 WeatherManager 挂在同一个 GameObject 上。
var adapter = WeatherManager.Instance.GetComponent<DefaultWeatherPresentationAdapter>();
adapter.SetFollowTarget(Camera.main.transform);

WeatherManager.Instance.SetWeather(WeatherType.Rain);
```

---

## TimeManager.cs

**用途**: 驱动昼夜循环，管理游戏内时间流逝

### Public API

```csharp
public class TimeManager : MonoSingleton<TimeManager>, ISaveable
{
    // Properties
    float CurrentHour { get; }           // 0-24
    float NormalizedTime { get; }        // 0-1
    int CurrentHourInt { get; }          // 0-23
    bool IsPaused { get; }
    float TimeScale { get; set; }        // 秒转小时倍率
    TimeOfDay CurrentTimeOfDay { get; }  // Dawn/Day/Dusk/Night

    // Events
    event Action<EnvironmentEventArgs> OnEnvironmentEvent;
    event Action<float> OnTimeChanged;
    event Action<int> OnHourChanged;
    event Action<TimeOfDay> OnTimeOfDayChanged;

    // Methods
    void SetTime(float hour);
    void SetTimeOfDay(TimeOfDay timeOfDay);
    void Pause();
    void Resume();
    void SetPaused(bool paused);
    void AdvanceHours(float hours);
    string GetFormattedTime();  // "HH:MM"

    // ISaveable
    string SaveKey { get; }  // "TimeManager"
    object ExportSaveData();
    void ImportSaveData(object data);
    void ResetToDefault();
}
```

**使用示例**:

```csharp
using ZeroEngine.EnvironmentSystem;

// 获取时间
float hour = TimeManager.Instance.CurrentHour;
TimeOfDay period = TimeManager.Instance.CurrentTimeOfDay;
Debug.Log($"Time: {TimeManager.Instance.GetFormattedTime()} ({period})");

// 设置时间
TimeManager.Instance.SetTime(12f);  // 正午
TimeManager.Instance.SetTimeOfDay(TimeOfDay.Night);

// 时间控制
TimeManager.Instance.TimeScale = 120f;  // 1秒现实 = 2分钟游戏
TimeManager.Instance.Pause();
TimeManager.Instance.Resume();

// 推进时间
TimeManager.Instance.AdvanceHours(6f);  // 前进 6 小时

// 监听时间变化
TimeManager.Instance.OnTimeOfDayChanged += tod =>
{
    Debug.Log($"Time of day: {tod}");
    // 切换 BGM、NPC 行为等
};

TimeManager.Instance.OnHourChanged += hour =>
{
    Debug.Log($"Hour: {hour}:00");
};
```

---

## LightingManager.cs

**用途**: 根据 TimeManager 时间控制场景光照、太阳方向、环境光、雾效

### Public API

```csharp
public class LightingManager : MonoSingleton<LightingManager>
{
    // Properties
    DayNightPresetSO Preset { get; set; }
    Light SunLight { get; set; }

    // Methods
    void ForceRefresh();
    void SetSmoothTransition(bool enabled, float smoothTime = 0.5f);
}
```

**使用示例**:

```csharp
using ZeroEngine.EnvironmentSystem;

// 设置预设
LightingManager.Instance.Preset = myDayNightPreset;
LightingManager.Instance.SunLight = directionalLight;

// 控制过渡模式
LightingManager.Instance.SetSmoothTransition(true, 0.5f);

// 强制刷新光照
LightingManager.Instance.ForceRefresh();
```

---

## WeatherPresetSO.cs

**用途**: 定义单个天气类型的配置

### 创建方式

```
菜单: Create > ZeroEngine > Environment > Weather Preset
```

### 配置项

| 字段 | 类型 | 描述 |
|------|------|------|
| `WeatherType` | enum | 天气类型 |
| `VfxPrefab` | GameObject | VFX 粒子预制体 |
| `VfxOffset` | Vector3 | VFX 相对相机偏移 |
| `OverrideFog` | bool | 是否覆盖雾效 |
| `EnableFog` | bool | 启用雾效 |
| `FogColor` | Color | 雾颜色 |
| `FogDensity` | float | 雾密度 |
| `LightIntensityMultiplier` | float | 光照强度系数 |
| `TransitionDuration` | float | 过渡时长 |
| `AmbientSound` | AudioClip | 环境音效 |
| `AmbientVolume` | float | 音量 |

---

## DayNightPresetSO.cs

**用途**: 定义一天内光照、颜色、雾效的变化曲线

### 创建方式

```
菜单: Create > ZeroEngine > Environment > DayNight Preset
```

### 配置项

| 字段 | 类型 | 描述 |
|------|------|------|
| `SunColorOverDay` | Gradient | 太阳颜色 (0=午夜, 0.5=正午) |
| `SunIntensityOverDay` | AnimationCurve | 太阳强度 |
| `MaxSunIntensity` | float | 最大太阳强度 |
| `SunriseAngle` | float | 日出角度 |
| `SunsetAngle` | float | 日落角度 |
| `AmbientColorOverDay` | Gradient | 环境光颜色 |
| `FogColorOverDay` | Gradient | 雾颜色 |
| `EnableFog` | bool | 启用雾效 |

### 静态工厂

```csharp
// 创建默认预设
var preset = DayNightPresetSO.CreateDefault();
```

### 评估方法

```csharp
// 手动评估指定时间的值
Color sunColor = preset.EvaluateSunColor(0.5f);  // 正午
float intensity = preset.EvaluateSunIntensity(0.25f);  // 日出
```

---

## EnvironmentEnums.cs

### WeatherType

```csharp
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
```

### TimeOfDay

```csharp
public enum TimeOfDay
{
    Dawn,   // 黎明 (5-7)
    Day,    // 白天 (7-17)
    Dusk,   // 黄昏 (17-19)
    Night   // 夜晚 (19-5)
}
```

### EnvironmentEventType

```csharp
public enum EnvironmentEventType
{
    TimeChanged,
    HourChanged,
    TimeOfDayChanged,
    WeatherChanged,
    WeatherTransitionStarted,
    WeatherTransitionCompleted
}
```

### EnvironmentEventArgs

```csharp
public class EnvironmentEventArgs
{
    EnvironmentEventType Type { get; }
    float CurrentHour { get; }
    int HourInt { get; }
    TimeOfDay TimeOfDay { get; }
    TimeOfDay PreviousTimeOfDay { get; }
    WeatherType Weather { get; }
    WeatherType PreviousWeather { get; }

    // Factory methods
    static EnvironmentEventArgs TimeChanged(float hour);
    static EnvironmentEventArgs HourChanged(int hour);
    static EnvironmentEventArgs TimeOfDayChanged(TimeOfDay current, TimeOfDay previous);
    static EnvironmentEventArgs WeatherChanged(WeatherType current, WeatherType previous);
}
```

---

## 架构图

```
Environment System (v1.13.0)
├── WeatherType / TimeOfDay            # 枚举
├── EnvironmentEventArgs               # 事件参数
├── WeatherPresetData / DayNightData   # 数据结构
├── WeatherPresetSO                    # 天气预设 SO
├── DayNightPresetSO                   # 昼夜预设 SO
├── TimeManager                        # 时间管理器 (ISaveable)
│   ├── 24h Cycle                      # 24 小时循环
│   ├── Time of Day Events             # 时间段事件
│   └── Time Control                   # 时间控制
├── WeatherManager                     # 天气管理器 (ISaveable)
│   ├── Weather State                  # 天气状态
│   ├── Preset Lookup                  # 预设查询
│   └── Save/Event Bridge              # 存档与事件桥接
├── IWeatherPresentationAdapter        # 可选天气表现边界
├── DefaultWeatherPresentationAdapter  # 默认 VFX/雾效/环境音表现
└── LightingManager                    # 光照管理器
    ├── Sun Color/Intensity            # 太阳颜色/强度
    ├── Sun Rotation                   # 太阳旋转
    ├── Ambient Light                  # 环境光
    └── Fog Sync                       # 雾效同步
```

---

## 完整使用示例

```csharp
using UnityEngine;
using ZeroEngine.EnvironmentSystem;

public class EnvironmentController : MonoBehaviour
{
    [SerializeField] private DayNightPresetSO _dayNightPreset;
    [SerializeField] private WeatherPresetSO[] _weatherPresets;
    [SerializeField] private Light _sunLight;

    private void Start()
    {
        // 设置光照
        LightingManager.Instance.Preset = _dayNightPreset;
        LightingManager.Instance.SunLight = _sunLight;

        // 注册天气预设
        foreach (var preset in _weatherPresets)
            WeatherManager.Instance.RegisterPreset(preset);

        // 设置时间
        TimeManager.Instance.TimeScale = 60f;  // 1秒 = 1分钟游戏

        // 监听事件
        TimeManager.Instance.OnTimeOfDayChanged += OnTimeOfDayChanged;
        WeatherManager.Instance.OnEnvironmentEvent += OnWeatherChanged;
    }

    private void OnTimeOfDayChanged(TimeOfDay timeOfDay)
    {
        switch (timeOfDay)
        {
            case TimeOfDay.Dawn:
                // 播放黎明音效
                break;
            case TimeOfDay.Night:
                // 切换夜间 NPC 行为
                break;
        }
    }

    private void OnWeatherChanged(EnvironmentEventArgs args)
    {
        if (args.Type == EnvironmentEventType.WeatherChanged)
        {
            // 更新 UI 天气图标
            // 调整角色移动速度等
        }
    }

    // 外部调用
    public void SetRainyWeather()
    {
        WeatherManager.Instance.SetWeather(WeatherType.Rain);
    }

    public void SkipToNight()
    {
        TimeManager.Instance.SetTimeOfDay(TimeOfDay.Night);
    }
}
```

---

## 跨系统集成

### Environment + Save

```csharp
// WeatherManager 和 TimeManager 实现 ISaveable
// 自动在 Start 时注册到 SaveSlotManager
// 存档自动包含当前时间和天气状态
```

### Environment + Calendar

```csharp
// Environment.TimeManager 管理一天内的时间 (小时)
// Calendar.CalendarManager 管理日期 (天/月/年)
// 可以联动: 日期变化时重置时间
```

---

## 版本历史

| 版本 | 变更 |
|------|------|
| v1.13.0 | 初始版本，从 ZGSProject_5 迁移 |
| v1.13.0 (patch) | 命名空间变更为 `ZeroEngine.EnvironmentSystem`；类型定义合并到 `TimeManager.cs` |
