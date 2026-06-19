# ZeroEngine.Persistence

持久化系统包，包含存档系统和设置系统。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core
- **可选集成**: Easy Save 3 code is disabled in the default runtime assembly until it is moved to an optional adapter assembly or supplied by project code.

## Designer Config Validation

`ZeroEngine.Persistence.Editor` provides `PersistenceConfigValidator` for
settings definitions. It reports duplicate keys, missing display names,
invalid numeric ranges, unparseable defaults, missing enum options, and broken
setting dependencies.

## 包含模块

### Save (存档系统)
- `SaveSlotManager` - 多槽位存档管理器
- `ISaveable` - 可存档接口
- `SaveSlotMeta` - 存档元信息
- `ISaveProvider` - 存档后端接口
- `JsonSaveProvider` - JSON 存档实现
- `ScreenshotCapture` - 存档截图

### Settings (设置系统)
- `SettingsManager` - 设置管理器
- 音频/图形/语言设置

## 快速使用

### Save
```csharp
using ZeroEngine.Save;

// 保存
SaveSlotManager.Instance.Save(slotIndex: 0, success =>
    Debug.Log($"Save: {success}"));

// 加载
SaveSlotManager.Instance.Load(slotIndex: 0);

// 快速存档
SaveSlotManager.Instance.QuickSave();
SaveSlotManager.Instance.QuickLoad();

// 获取槽位信息
var metas = SaveSlotManager.Instance.GetAllSlotMetas();
```

### ISaveable 接口
```csharp
using ZeroEngine.Save;

public class MyManager : MonoBehaviour, ISaveable
{
    public string SaveKey => "MyManager";

    public object CaptureState() => new SaveData { value = 100 };

    public void RestoreState(object state)
    {
        var data = (SaveData)state;
        // 恢复状态
    }
}
```

### Settings
```csharp
using ZeroEngine.Settings;

SettingsManager.Instance.MasterVolume = 0.8f;
SettingsManager.Instance.IsFullscreen = true;
SettingsManager.Instance.ApplySettings();
```

## 条件编译

| 宏 | 说明 |
|----|------|
| `ES3` | 启用 Easy Save 3 后端 |

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
