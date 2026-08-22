# ZeroEngine.Persistence

持久化系统包，包含后端无关的存档槽位编排、稳定参与者合同、原子文件提升和安全截图策略；旧 Save/Settings API 仍保留用于兼容。

## 版本
- **当前版本**: 2.1.0
- **依赖**: ZeroEngine.Core
- **可选依赖**: Easy Save 3 (ES3)

## 包含模块

### Save (存档系统)
- `ZeroEngine.Persistence.SaveParticipantRegistry` - 按注册顺序捕获和恢复稳定 key 的参与者
- `ZeroEngine.Persistence.SaveSlotPipeline<TMetadata>` - 注入 gate、metadata/screenshot provider 和 slot backend 的编排管线
- `ZeroEngine.Persistence.AtomicFilePromotion` - 多文件临时文件提升、失败回滚和成功清理
- `ZeroEngine.Persistence.ScreenshotFilePolicy` - 规范文件名、根目录、reparse/symlink 和 PNG/JPEG 尺寸/像素/长度校验
- `SaveSlotManager` - 多槽位存档管理器
- `ISaveable` - 可存档接口
- `SaveSlotMeta` - 存档元信息
- `ISaveProvider` - 存档后端接口
- `JsonSaveProvider` - JSON 存档实现
- `ScreenshotCapture` - 存档截图

### Settings (设置系统)
- `SettingsManager` - 设置管理器（已标记 Obsolete，保留 API）
- 音频/图形/语言设置

新设置项目应使用 `com.zerogamestudio.zeroengine.settings` 的
`ZeroEngine.PlayerSettings` 服务和会话合同；本包中的 `ZeroEngine.Settings`
只用于旧项目兼容，不再承载新的设置功能。

### Persistence pipeline 示例

```csharp
var participants = new SaveParticipantRegistry();
participants.Register(new DelegateSaveParticipantAdapter(
    "p5.party",
    capture: () => partySnapshot,
    restore: state => party.Restore(state)));

var pipeline = new SaveSlotPipeline<SlotMetadata>(backend, participants,
    new SaveSlotPipelineOptions<SlotMetadata>
    {
        Gate = new DelegateSaveSlotOperationGate((slot, operation) =>
            SaveSlotGateDecision.Allow())
    });

SaveSlotResult result = await pipeline.SaveAsync("slot-0", cancellationToken);
```

`ISaveSlotBackend<TMetadata>` 负责序列化、临时文件可读回验和实际磁盘格式；
ZE 管线只负责 capture、prepare-before-restore、稳定顺序 restore 以及结构化
`Saved/Loaded/Deleted/Blocked/Failed/Cancelled` 结果。

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
