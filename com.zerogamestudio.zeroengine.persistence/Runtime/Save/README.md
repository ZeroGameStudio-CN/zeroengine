# ZeroEngine.Save API 文档

> **用途**: 本文档面向AI助手，提供Save（存档）模块的快速参考。

---

## 目录结构

```
Save/
├── SaveManager.cs       # 底层存档管理器（单例）+ Provider 接口
├── SaveTypes.cs         # 存档类型定义 (v1.8.0+)
├── SaveSlotManager.cs   # 多槽位存档管理器 (v1.8.0+)
├── AutoSaveController.cs# 自动存档控制器 (v1.8.0+)
└── ScreenshotCapture.cs # 截图捕获组件 (v1.8.0+)
```

---

## 依赖

| 宏定义 | 依赖 | 说明 |
|--------|------|------|
| `ES3` | EasySave 3 插件 | 可选，提供更强大的序列化能力 |

> v1.1.0+: 不再强制依赖 ES3。无 ES3 时自动使用 JsonSaveProvider。

---

## 架构概览 (v1.8.0+)

```
SaveSlotManager (高层封装)
├── SlotManager         # 8 槽位 + 自动存档槽
├── AutoSaveController  # 自动存档调度
├── ScreenshotCapture   # 截图捕获
└── ES3Provider         # ES3 底层 (已有)

ISaveable 接口
├── InventoryManager.Export/Import()
├── QuestManager.Export/Import()
├── DialogSaveAdapter.Export/Import()
└── StatController.Export/Import()
```

---

## ISaveable 接口 (v1.8.0+)

**用途**: 可存档系统接口，实现此接口的系统可被 SaveSlotManager 自动管理

```csharp
public interface ISaveable
{
    string SaveKey { get; }           // 系统唯一标识符
    object ExportSaveData();          // 导出存档数据
    void ImportSaveData(object data); // 导入存档数据
    void ResetToDefault();            // 重置为初始状态
}
```

---

## SaveSlotMeta 类 (v1.8.0+)

**用途**: 存档槽位元信息

```csharp
public class SaveSlotMeta
{
    int SlotIndex;              // 槽位索引 (0-7 普通, -1 自动存档)
    DateTime Timestamp;         // 存档时间
    float PlayTimeSeconds;      // 游戏时长 (秒)
    string SceneName;           // 当前场景名
    string PlayerName;          // 玩家名称
    int PlayerLevel;            // 玩家等级
    int SaveVersion;            // 存档版本
    bool IsValid;               // 是否有效
    Dictionary<string, string> CustomMeta; // 自定义元数据

    // 便捷属性
    string FormattedPlayTime;   // 格式化游戏时长 (如 "1:23:45")
    string FormattedTimestamp;  // 格式化时间戳 (如 "2026-01-03 14:30")
}
```

---

## SaveSystemConfig 类 (v1.8.0+)

**用途**: 存档系统配置

```csharp
public class SaveSystemConfig
{
    int MaxSlots = 8;                              // 最大槽位数
    bool EnableAutoSave = true;                    // 启用自动存档
    float AutoSaveInterval = 300f;                 // 自动存档间隔 (秒)
    AutoSaveTrigger AutoSaveTriggers = Default;    // 触发条件
    int ScreenshotWidth = 320;                     // 截图宽度
    int ScreenshotHeight = 180;                    // 截图高度
    int ScreenshotQuality = 75;                    // 截图质量 (0-100)
}
```

---

## AutoSaveTrigger 枚举 (v1.8.0+)

```csharp
[Flags]
public enum AutoSaveTrigger
{
    None = 0,
    Interval = 1 << 0,        // 定时自动存档
    SceneChange = 1 << 1,     // 场景切换时
    QuestComplete = 1 << 2,   // 任务完成时
    ImportantEvent = 1 << 3,  // 重要事件时
    OnPause = 1 << 4,         // 游戏暂停/退出时
    Default = Interval | SceneChange | OnPause
}
```

---

## SaveSlotManager.cs (v1.8.0+)

**用途**: 多槽位存档管理器核心

```csharp
public class SaveSlotManager : Singleton<SaveSlotManager>
{
    // 配置
    SaveSystemConfig Config { get; }

    // 属性
    int CurrentSlotIndex { get; }     // 当前槽位 (-2 未加载)
    bool HasLoadedSave { get; }       // 是否已加载
    float CurrentPlayTime { get; }    // 当前会话游戏时长
    int MaxSlots { get; }             // 最大槽位数

    // 系统注册
    void Register(ISaveable saveable);
    void Unregister(ISaveable saveable);
    bool IsRegistered(ISaveable saveable);

    // 槽位查询
    IReadOnlyList<SaveSlotMeta> GetAllSlotMetas();
    SaveSlotMeta GetSlotMeta(int slotIndex);
    bool HasSave(int slotIndex);
    Texture2D GetSlotScreenshot(int slotIndex);

    // 保存
    void Save(int slotIndex, Action<bool> callback = null);
    void SaveCurrent(Action<bool> callback = null);
    void AutoSave(Action<bool> callback = null);

    // 加载
    void Load(int slotIndex, Action<bool> callback = null);
    void LoadAutoSave(Action<bool> callback = null);

    // 删除
    void Delete(int slotIndex);
    void DeleteAll();

    // 新游戏
    void NewGame(int targetSlot = 0);

    // 快捷操作
    void QuickSave();
    void QuickLoad();

    // 元信息设置
    void SetPlayerName(string name);
    void SetPlayerLevel(int level);
    void SetCustomMeta(string key, string value);

    // 事件
    event Action<SaveEventArgs> OnSaveCompleted;
    event Action<SaveEventArgs> OnLoadCompleted;
    event Action<int> OnSlotDeleted;
}
```

---

## AutoSaveController.cs (v1.8.0+)

**用途**: 自动存档控制器

```csharp
public class AutoSaveController : MonoBehaviour
{
    void Initialize(SaveSlotManager slotManager, SaveSystemConfig config);
    void Enable();
    void Disable();
    void Pause();   // 暂停 (加载中、战斗中等)
    void Resume();  // 恢复

    // 外部触发
    void TriggerAutoSave(AutoSaveTrigger trigger = ImportantEvent);
    void OnQuestCompleted(string questId);  // 任务完成
    void OnImportantEvent(string eventName); // 重要事件

    event Action<AutoSaveTrigger> OnAutoSaveTriggered;
}
```

---

## ScreenshotCapture.cs (v1.8.0+)

`CaptureAsync` captures the completed screen, including overlay UI. On supported
devices it crops and scales on the GPU before an asynchronous thumbnail readback;
it does not synchronously read the full screen. GPU UV orientation is normalized
to the existing upright screenshot convention before readback. Requests are serialized so a
render target remains valid until native completion. Unsupported devices retain
the synchronous compatibility path. A failed readback returns null once, allowing
`SaveSlotManager` to keep the previous preview while completing the saved data.
Batch Editors do not resume `WaitForEndOfFrame`; they complete the callback
with null immediately, preserving the stored preview and completing the save.
Interactive Editors and Players retain the rendered frame-end capture path.
Disabling or destroying the component settles outstanding callbacks; in-flight
render targets are released by the native completion callback. `CaptureSync`
remains available and restores the caller's render target and camera target.

**用途**: 截图捕获组件

```csharp
public class ScreenshotCapture : MonoBehaviour
{
    void Initialize(int width, int height, int quality);
    void CaptureAsync(Action<byte[]> callback);  // 异步捕获
    byte[] CaptureSync();  // 同步捕获 (阻塞一帧)
}
```

---

## ISaveProvider 接口 (v1.1.0+)

**用途**: 存档后端抽象接口，允许切换不同的存储实现

```csharp
public interface ISaveProvider
{
    void Save<T>(string key, T data, string fileName);
    T Load<T>(string key, T defaultValue, string fileName);
    bool Exists(string key, string fileName);
    void DeleteKey(string key, string fileName);
    void DeleteFile(string fileName);
    byte[] LoadBytes(string key, string fileName);
    void SaveBytes(string key, byte[] bytes, string fileName);
}
```

### 内置实现

| 类名 | 说明 | 条件 |
|------|------|------|
| `JsonSaveProvider` | 纯 JSON 实现，无外部依赖 | 始终可用 |
| `ES3SaveProvider` | Easy Save 3 包装 | 需要 `ES3` 宏定义 |

---

## SaveManager.cs

**用途**: 底层存档管理器，自动选择最佳后端

```csharp
public class SaveManager : Singleton<SaveManager>
{
    public const string DefaultSaveFile = "SaveData.es3";
    public const string SettingsFile = "Settings.es3";
    public const string GlobalDataFile = "GlobalData.es3";

    // Provider (v1.1.0+)
    ISaveProvider Provider { get; set; }  // 自动选择: ES3 > JSON
    void UseJsonProvider();               // 强制使用 JSON 后端

    // 基础操作
    void Save<T>(string key, T data, string fileName = DefaultSaveFile);
    T Load<T>(string key, T defaultValue = default, string fileName = DefaultSaveFile);
    bool Exists(string key, string fileName = DefaultSaveFile);

    // 删除
    void DeleteKey(string key, string fileName = DefaultSaveFile);
    void DeleteFile(string fileName = DefaultSaveFile);

    // 图片/字节
    void SaveImage(string key, byte[] bytes, string fileName = DefaultSaveFile);
    byte[] LoadImage(string key, string fileName = DefaultSaveFile);
}
```

---

## 使用示例

### v1.8.0+ 多槽位存档

```csharp
using ZeroEngine.Save;

// 1. 实现 ISaveable 接口 (系统自动注册)
public class MyGameSystem : MonoBehaviour, ISaveable
{
    public string SaveKey => "MySystem";

    public object ExportSaveData()
    {
        return new MySystemData { ... };
    }

    public void ImportSaveData(object data)
    {
        if (data is MySystemData myData)
        {
            // 恢复状态
        }
    }

    public void ResetToDefault()
    {
        // 重置为初始状态
    }

    void Start()
    {
        SaveSlotManager.Instance.Register(this);
    }

    void OnDestroy()
    {
        SaveSlotManager.Instance.Unregister(this);
    }
}

// 2. 保存到槽位
SaveSlotManager.Instance.Save(0, success => Debug.Log($"Saved: {success}"));

// 3. 加载槽位
SaveSlotManager.Instance.Load(0, success => Debug.Log($"Loaded: {success}"));

// 4. 获取槽位信息
var metas = SaveSlotManager.Instance.GetAllSlotMetas();
foreach (var meta in metas)
{
    if (meta.IsValid)
    {
        Debug.Log($"Slot {meta.SlotIndex}: {meta.PlayerName} Lv.{meta.PlayerLevel}");
        Debug.Log($"  Time: {meta.FormattedPlayTime} | {meta.FormattedTimestamp}");
    }
}

// 5. 获取截图
var screenshot = SaveSlotManager.Instance.GetSlotScreenshot(0);
if (screenshot != null)
{
    slotPreviewImage.texture = screenshot;
}

// 6. 新游戏
SaveSlotManager.Instance.NewGame(0);

// 7. 设置玩家信息
SaveSlotManager.Instance.SetPlayerName("Hero");
SaveSlotManager.Instance.SetPlayerLevel(10);

// 8. 快捷操作
SaveSlotManager.Instance.QuickSave();  // F5
SaveSlotManager.Instance.QuickLoad();  // F9

// 9. 自动存档控制
// 自动存档由 AutoSaveController 管理，无需手动调用
// 可通过以下方式触发特定事件的自动存档:
var autoSave = SaveSlotManager.Instance.GetComponent<AutoSaveController>();
autoSave?.OnQuestCompleted("quest_main_01");
autoSave?.OnImportantEvent("boss_defeated");
```

### 传统存档方式 (v1.1.0)

```csharp
// 保存数据
SaveManager.Instance.Save("PlayerLevel", 10);
SaveManager.Instance.Save("Inventory", inventoryData);

// 加载数据
int level = SaveManager.Instance.Load("PlayerLevel", 1);  // 默认值1
var inventory = SaveManager.Instance.Load<InventoryData>("Inventory");

// 检查存在
if (SaveManager.Instance.Exists("PlayerLevel"))
{
    // ...
}

// 删除
SaveManager.Instance.DeleteKey("TempData");
SaveManager.Instance.DeleteFile("OldSave.es3");

// 保存/加载图片
SaveManager.Instance.SaveImage("Screenshot", texture.EncodeToPNG());
byte[] imageData = SaveManager.Instance.LoadImage("Screenshot");
```

---

## 已集成系统

以下系统已实现 `ISaveable` 接口，自动接入 `SaveSlotManager`:

| 系统 | SaveKey | 说明 |
|------|---------|------|
| InventoryManager | `"Inventory"` | 背包数据 |
| QuestManager | `"Quest"` | 任务进度 |
| DialogSaveAdapter | `"Dialog"` | 对话全局变量 |

### StatController 存档支持

`StatController` 提供存档 API，但不自动注册 (因为可能有多个实例):

```csharp
// 导出
StatControllerSaveData data = statController.ExportSaveData();

// 导入
statController.ImportSaveData(data);

// 重置
statController.ResetStats();
```

---

## 文件位置

默认保存位置 (`Application.persistentDataPath`):
- **Windows**: `%userprofile%/AppData/LocalLow/<CompanyName>/<ProductName>/`
- **macOS**: `~/Library/Application Support/<CompanyName>/<ProductName>/`

槽位文件命名:
- 普通槽位: `SaveSlot_0.es3`, `SaveSlot_1.es3`, ...
- 自动存档: `AutoSave.es3`
- 元信息索引: `SaveMetas.es3`

---

## 版本历史

| 版本 | 变更 |
|------|------|
| v1.8.0 | 完整重构: SaveSlotManager, ISaveable, AutoSaveController, ScreenshotCapture |
| v1.1.0 | ISaveProvider 接口, JsonSaveProvider 后端 |
| v1.0.0 | 初始版本，ES3 包装 |
