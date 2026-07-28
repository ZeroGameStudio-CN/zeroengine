# ZeroEngine 通用玩家设置平台 Spec

- 状态：Implemented（测试候选，待人工设备验收与正式发布）
- 最后更新：2026-07-28
- ZeroEngine 基线：`origin/main`，`1803bd36ff472a6cb407bec9e4ff95bcbce84384`
- 消费项目基线：GalleryKeeper、POB、LLS 本地工作区 2026-07-27 快照

## 推荐结论

新增 `com.zerogamestudio.zeroengine.settings` 1.0.0，作为全局玩家偏好的统一编排层；将现有 Input 兼容升级至 2.1.0，并通过适配器复用 Localization、Audio 和 Unity 显示能力。

运行时命名空间使用 `ZeroEngine.PlayerSettings`，避开 Persistence 旧版已经公开的 `ZeroEngine.Settings` 类型名；包 ID 不变。

通用层负责设置定义、校验、预览、应用/取消、恢复默认、持久化、迁移和事件。项目层只保留具体默认值、可选语言、AudioMixer/画质配置、InputActionAsset、文本与界面皮肤。

不把所有能力继续塞入 `zeroengine.persistence`，也不让 `zeroengine.input` 负责声音、语言或显示设置。

## 目标

- 为所有游戏提供一个全局、强类型、版本化且与存档槽无关的玩家设置服务。
- 通用支持语言、显示、声音、输入偏好与改键、震动以及常见辅助功能。
- 支持立即预览、确认应用、取消回滚、按分类/单项恢复默认。
- 支持开始界面和游戏内设置共用同一状态，不再各自保存一份。
- 支持项目自定义设置、存储适配器、应用器和动态选项。
- 以 GalleryKeeper 为首个接入项目验证设计；POB、LLS 的旧数据能够后续无损迁移。

## 非目标

- 不提供一套强制所有项目使用的 UGUI/UI Toolkit 美术或页面布局。
- 不规定项目必须开放哪些语言、画质档、音频总线、默认键位或辅助功能。
- 首版不替换 POB、LLS 的生产设置界面；只提供明确迁移映射和扩展能力。
- 不做 Steam Cloud、账号漫游、平台云同步或多人房主规则。
- 不把玩家偏好写入任意游戏存档槽。
- 不承诺支持任意 HID、方向盘、飞行摇杆或 XR；标准硬件范围为键盘鼠标、通用手柄和可选触屏适配。
- 首版不删除旧设置文件、旧键或兼容类。

## 已检查的现状

- `com.zerogamestudio.zeroengine.persistence/Runtime/Settings` 已有 `SettingsManager`，但它：
  - 实现 `ISaveable`，默认跟随存档槽，而 POB、LLS、GalleryKeeper 的玩家设置均为全局数据；
  - 使用字符串值和硬编码应用分支；
  - 改键仍基于旧 `KeyCode` 与 `Input.GetKey`，不能承担现代 Input System 改键。
- `com.zerogamestudio.zeroengine.input` 的说明声称支持设备检测和重绑定，实际 `InputManager` 只有 Action Map 启停与切换。
- `LocalizationManager` 已支持通过 locale code 切换语言，但没有统一持久化、初始化完成契约、失败结果或设置 UI 刷新模型。
- `AudioManager` 已支持 Master/Music/SFX 音量和关闭自身持久化，可由统一设置服务接管。
- POB 已验证每个 InputAction 保存覆盖、重复键交换、键鼠/手柄/触屏展示及 Steam Deck 输入意图判断，但实现与 ES3、具体 UI 和项目枚举强耦合。
- LLS 已验证全局音频、显示、语言数据模型和加载顺序，但使用语言索引且没有玩家改键。
- GalleryKeeper 已有强类型设置状态和 Input System 重绑定、冲突检测、JSON 持久化；现有相关 EditMode 测试 12/12 通过，但开始界面与游戏内设置能力不一致，改键范围也不完整。

## 架构与依赖

### 包边界

新增包：

```text
com.zerogamestudio.zeroengine.settings/
  Runtime/
    Core/
    Persistence/
    Standard/
    Integrations/
    Unity/
  Editor/
  Tests/Editor/
  Tests/PlayMode/
  Samples~/
  README.md
  CHANGELOG.md
  package.json
```

`zeroengine.settings` 是可选的高层聚合包，依赖：

- `com.zerogamestudio.zeroengine.core`
- `com.zerogamestudio.zeroengine.persistence`
- `com.zerogamestudio.zeroengine.audio`
- `com.zerogamestudio.zeroengine.input`
- `com.zerogamestudio.zeroengine.localization`

消费项目继续按仓库约定，将所有 ZeroEngine 包固定到同一已验证 Git commit。Audio、Input、Localization 仍可被单独安装；它们不反向依赖 Settings。

### 分层

1. **Settings Core**
   - 设置 ID、值、定义、分类、目录、快照、会话、结果与事件。
   - 不引用项目 UI、项目类型、ES3、具体 InputAction 名或本地化表。
2. **Persistence**
   - 版本化文档、存储接口、备份恢复和迁移管线。
   - 默认提供全局 PlayerPrefs JSON 存储；项目可替换为 ES3、文件或现有全局 SaveManager。
3. **Standard**
   - 注册稳定的通用设置 ID 和默认约束，不决定项目是否显示某项。
4. **Integrations**
   - Localization、Audio、Input 的设置贡献器与应用器。
5. **Unity**
   - `SettingsBootstrap`、显示/画质应用器、显示变更确认计时器。
6. **Project**
   - 项目拥有一个 `SettingsCatalogSO`，选择启用的标准定义并配置默认值、约束、文本键和项目扩展。
   - 项目拥有启动 Prefab、旧数据导入器、界面和文本资源。

## 核心接口与数据

### 稳定标识与值

```csharp
public readonly struct SettingId
{
    public string Value { get; }
}

public enum SettingValueKind
{
    Bool,
    Int,
    Float,
    String
}

public readonly struct SettingValue
{
    public SettingValueKind Kind { get; }
    public bool AsBool();
    public int AsInt();
    public float AsFloat();
    public string AsString();
}
```

- ID 使用小写命名空间格式，例如 `display.windowMode`。
- 枚举保存稳定字符串，不保存 C# 枚举整数。
- 浮点、整数使用 `InvariantCulture` 编解码。
- 读取时必须验证值类型、有限数值、范围和动态选项；非法单项回退到该项目配置的默认值。

### 设置定义

每个 `SettingDefinition` 至少包含：

- `Id`
- `CategoryId`
- `ValueKind`
- `DefaultValue`
- 数值范围、步长或选项提供器
- `LabelKey`、`DescriptionKey`
- 排序值
- `ApplyPolicy`：`Preview`、`OnCommit` 或 `RestartRequired`
- 平台/能力可用性判定
- 是否在通用设置 UI 中可见

分类 ID 同样使用字符串，内置 `display`、`audio`、`controls`、`accessibility`、`language`，项目可新增分类。

目录构建时拒绝重复 ID、默认值类型错误、非法范围和缺少必需应用器。Editor 校验报告具体资产与 ID。

### 服务、应用器和会话

```csharp
public interface ISettingsService
{
    bool IsReady { get; }
    SettingsSnapshot Committed { get; }
    Task<SettingsInitializeResult> InitializeAsync(CancellationToken cancellationToken);
    SettingsSession OpenSession();
    Task<SettingsCommitResult> SetAndCommitAsync(
        SettingId id,
        SettingValue value,
        CancellationToken cancellationToken);
    event Action<SettingsChangedEvent> Changed;
    event Action MetadataChanged;
}

public interface ISettingApplier
{
    IReadOnlyCollection<SettingId> SettingIds { get; }
    Task<SettingApplyResult> ApplyAsync(
        SettingsSnapshot snapshot,
        CancellationToken cancellationToken);
}

public interface ISettingsStore
{
    SettingsStoreLoadResult Load();
    SettingsStoreSaveResult Save(SettingsDocument document);
}
```

`SettingsBootstrap` 显式存在于项目启动场景或持久化启动 Prefab 中，完成以下操作：

1. 收集目录、贡献器、应用器、存储和迁移器。
2. 构建并校验目录。
3. 加载、迁移、校验全局文档。
4. 在 Unity 主线程应用完整快照。
5. 向 `ZeroEngine.Core.ServiceRegistry` 注册 `ISettingsService`。
6. 所有必需应用器完成后才设置 `IsReady` 并发出 Ready 结果。

主界面创建前必须等待初始化完成；不依赖设置面板曾经被打开。

每次只允许一个可写 `SettingsSession`：

- `Set` 更新工作快照；`Preview` 项立即应用。
- `CommitAsync` 先应用受影响设置；全部成功后持久化并替换 committed 快照。
- 应用或持久化失败时重新应用旧 committed 快照，会话保持打开并返回结构化错误。
- `CancelAsync` 重新应用 committed 快照并丢弃工作值。
- `Reset` 支持单项、分类和全部，仍遵循预览/提交策略。
- 开始界面和游戏内界面读取同一服务，不持有独立数据副本。

## 持久化、迁移与恢复

### 文档格式

```text
SettingsDocument
  formatVersion: int
  entries:
    - id: string
      kind: SettingValueKind
      value: canonical string
```

- 初版 `formatVersion` 为 1。
- 未被当前目录识别的条目原样保留但不应用，避免暂时移除模块后丢失偏好。
- 重复 ID 或无法解析属于文档结构错误，整份文档不应用并进入 backup 恢复。
- 已知 ID 的类型不匹配、越界或无效选项只回退该单项默认值；其余合法项继续应用。
- 默认 PlayerPrefs 存储使用 `ZeroEngine.Settings.Primary` 与 `ZeroEngine.Settings.Backup`；项目可以通过存储命名空间覆盖前缀。
- 新文档成功序列化后，仅在旧 primary 能完整解析时才将其轮换为 backup；不得用损坏的 primary 覆盖有效 backup。之后写入新 primary 并调用 `PlayerPrefs.Save()`。
- 包同时提供基于 `SaveManager.SettingsFile` 的存储适配器；一个运行实例只能配置一个主存储，不进行双写。
- primary 无效时尝试 backup；二者均无效时使用默认值，报告恢复事件，不删除损坏数据。
- 设置值和改键 JSON不得写入普通日志；错误日志只记录设置 ID、阶段和错误类型。

### 迁移

`ISettingsMigration` 按连续版本执行；任一步失败都不覆盖旧文档。

项目旧数据导入遵循：

1. 仅当新 Settings 文档不存在时执行。
2. 读取旧全局数据并映射到稳定 ID。
3. 通过新目录校验和规范化。
4. 新文档保存成功后记录导入完成标记。
5. 不删除或改写旧数据。

首个接入项目必须提供 GalleryKeeper 导入器；POB、LLS 的映射记录在迁移文档中，但首版不切换其生产代码。

关键映射：

- GalleryKeeper `languageCode` → `localization.locale`
- LLS `LanguageIndex` → 迁移时解析出的 locale code，之后不再保存索引
- POB 的每 Action 覆盖 JSON → 应用到同一 `InputActionAsset` 后导出整资产覆盖 JSON
- 三项目的音量、窗口模式、分辨率、刷新率、VSync、帧率和画质 → 对应标准 ID
- 项目独有的 FOV、准星、伤害数字、特效强度等 → 项目命名空间 ID

## 标准设置

### 语言

| ID | 类型 | 行为 |
| --- | --- | --- |
| `localization.locale` | String | 保存 locale code，例如 `en`、`zh-Hans` |

- 选项由 Unity Localization 的实际可用 Locale 动态生成。
- 配置必须提供默认 locale code；运行时缺失时回退到第一个可用 Locale，并报告配置错误。
- 没有任何可用 Locale 时禁用语言单项并返回非致命初始化错误，其余设置仍可使用。
- 首次运行可由项目配置选择系统语言；已有保存值始终优先。
- 切换语言为即时预览；设置页面通过 `MetadataChanged` 刷新标题、说明和选项文本。
- 语言选项默认显示 Locale 原生名称；项目可替换文本解析器。
- Localization 初始化和切换失败必须返回结果，不能只打印警告后假装成功。

### 显示与画质

| ID | 类型 | 说明 |
| --- | --- | --- |
| `display.windowMode` | String | `FullScreenMode` 稳定名称 |
| `display.width` | Int | 窗口/独占分辨率宽度 |
| `display.height` | Int | 窗口/独占分辨率高度 |
| `display.refreshRate` | Int | 0 表示自动 |
| `display.vSyncCount` | Int | 由项目配置允许范围 |
| `display.frameRateLimit` | Int | -1 表示不限制 |
| `display.quality` | String | Unity Quality 名称，不保存数组索引 |

- 支持项由当前平台和项目配置决定；移动平台可隐藏窗口模式和分辨率，但仍保留数据。
- `FullScreenWindow` 使用桌面原生分辨率。
- 独占全屏找不到完全匹配刷新率时，选择同尺寸最接近的可用刷新率；尺寸不存在则回退当前分辨率。
- 画质名称失效时回退项目默认画质名称，再回退当前有效画质。
- VSync 开启时有效 `Application.targetFrameRate` 为 -1，但保留玩家选择的帧率上限；关闭 VSync 后恢复。
- 窗口模式、分辨率和刷新率属于高风险预览。包提供自动恢复令牌，项目 UI 必须确认或取消；默认确认时间为 15 秒。这是可配置、可逆的通用 UX 默认值。

### 声音

| ID | 类型 | 说明 |
| --- | --- | --- |
| `audio.master` | Float | 0..1 |
| `audio.music` | Float | 0..1 |
| `audio.sfx` | Float | 0..1 |

- 音量为即时预览。
- ZeroEngine Audio 适配器调用现有 `SetMasterVolume`、`SetBGMVolume`、`SetSFXVolume`。
- Settings 接管时必须调用 `SetVolumePersistenceEnabled(false)`，防止 AudioManager 与 Settings 双写。
- UI 音效、语音等额外总线通过项目自定义 ID 和应用器扩展。

### 控制与改键

| ID | 类型 | 说明 |
| --- | --- | --- |
| `input.pointerSensitivity` | Float | 项目配置范围 |
| `input.gamepadSensitivity` | Float | 项目配置范围 |
| `input.gamepadDeadzone` | Float | 0..0.95 |
| `input.invertY` | Bool | 镜头 Y 轴 |
| `input.vibration` | Float | 0..1 |
| `input.glyphStyle` | String | Auto/Xbox/PlayStation/Nintendo 或项目扩展 |
| `input.bindingOverrides` | String | 隐藏字段，InputActionAsset 覆盖 JSON |

现有 `zeroengine.input` 升级至兼容性的 2.1：

- 保留旧 `InputManager`，标记为兼容入口，不删除现有 API。
- 新服务通过 Action GUID、Binding GUID 和 binding group 工作，不依赖易变的 binding index。
- 支持键盘鼠标、通用 Gamepad 及可选 Touch 展示族。
- 通用 Gamepad 覆盖 Xbox、PlayStation、Switch Pro 和 Steam Deck；设备品牌只影响提示图标。
- 设备展示采用最后一次明确输入意图；被动鼠标移动不得把 Steam Deck 从手柄展示切成键鼠。
- 重绑定支持普通按钮和组合绑定的各 part；鼠标 delta、指针位置和触屏坐标默认不可绑定。
- 冲突策略支持 Swap、Reject、Allow。默认同一上下文可交换；保留的取消/确认绑定及不兼容控件拒绝交换。
- 取消、超时、恢复单项默认、恢复全部默认、冲突结果均返回结构化结果。
- 设置服务保存整份 `InputActionAsset.SaveBindingOverridesAsJson()`；启动时在游戏输入启用前加载。
- Touch 虚拟摇杆布局由项目适配，不作为键鼠/手柄的交互式改键。

### 辅助功能

| ID | 类型 | 说明 |
| --- | --- | --- |
| `accessibility.uiScale` | Float | 项目配置范围 |
| `accessibility.highContrast` | Bool | 高对比展示信号 |
| `accessibility.reduceMotion` | Bool | 减少非必要动态效果信号 |

- 包负责状态、事件和目录元数据。
- 项目注册 Canvas、动画、材质等具体应用目标；没有目标时该定义不进入可见目录。

项目可以使用同一机制增加 FOV、准星、字幕、色觉模式、游戏玩法偏好等，不修改 Settings Core。

## UI 与本地化边界

- 包不依赖 `zeroengine.ui`，不提供最终页面 Prefab。
- 包提供可观察的目录、当前值、默认值、动态选项、dirty、可用性和失败结果。
- 玩家界面负责布局、焦点导航、视觉皮肤和确认弹窗。
- `LabelKey`/`DescriptionKey` 通过 `ISettingsTextResolver` 解析；包运行时代码不硬编码玩家可见语言。
- 示例提供 UGUI 绑定和中英文本，但示例资产不是生产 UI 依赖。
- 键鼠、手柄均须能完成打开设置、修改、确认、取消、恢复默认和改键；触屏导航由项目适配。

## 失败、恢复和回滚

- 目录错误：Editor 校验失败；运行时禁用有问题的单项并返回初始化错误，不影响其余有效设置。
- 加载损坏：尝试 backup，失败后使用默认值，不覆盖原数据。
- 应用失败：恢复旧 committed 快照，不持久化失败值。
- 保存失败：恢复旧 committed 快照和运行时效果，返回可显示错误。
- 语言资源尚未就绪：等待 Localization 初始化；取消或失败时保留旧语言。
- 显示确认超时：自动恢复旧窗口/分辨率/刷新率。
- 改键 JSON 无效：清除运行时覆盖并使用默认绑定，保留恢复事件供 UI 提示。
- 回滚发布：消费项目固定回上一 ZeroEngine commit 并重新启用旧设置入口；旧数据未被删除，新文档被旧版本忽略。

旧 `ZeroEngine.Settings.SettingsManager` 与 `KeyBindingData` 首版仅标记 `[Obsolete]` 并更新文档，不移动、不删除，避免破坏现有项目。

## 生产约束

- Settings Core 不进行每帧轮询；只在初始化、用户变更和确认计时期间工作。
- 所有 Unity API 应用在主线程执行。
- Input 设备追踪使用 Input System 事件或动作回调，不扫描所有设备每帧。
- 设置文档不包含账号凭据、设备序列号或个人信息。
- 日志不得输出完整改键 JSON或自定义字符串设置内容。
- 所有公共事件在 Domain Reload、退出和对象销毁时正确解绑。
- 禁用 Domain Reload 时，静态服务、设备状态和测试覆盖必须通过 `SubsystemRegistration` 重置。
- 包最低 Unity 版本保持 2022.3；在 GalleryKeeper 的 Unity 6000.3.10f1 上进行消费兼容验证。

## 首版影响范围

ZeroEngine：

- 新增 `com.zerogamestudio.zeroengine.settings/**`
- 扩展 `com.zerogamestudio.zeroengine.input/**`
- 小幅扩展 `com.zerogamestudio.zeroengine.localization/**` 的异步就绪/失败结果
- 更新 `com.zerogamestudio.zeroengine.persistence/Runtime/Settings/**` 的弃用说明
- 更新根 README、消费项目文档和 `.github/workflows/tests.yml`

GalleryKeeper 首个接入：

- `Assets/Scripts/Runtime/GalleryUserSettings*.cs`
- `Assets/Scripts/Runtime/GalleryInput*.cs`
- 开始界面、暂停界面及相关测试
- `Packages/manifest.json` 中同一 ZeroEngine commit 的包固定
- 项目旧设置导入器和配置资产

POB、LLS 首版只新增迁移映射文档，不改生产代码或资产。

## 实施顺序

1. 新建 Settings Core、文档格式、存储、会话、迁移接口和纯 EditMode 测试。
2. 实现显示/画质应用器、风险预览确认及测试。
3. 补强 ZeroEngine Input 的设备追踪、GUID 重绑定、冲突与覆盖存储测试。
4. 实现 Localization、Audio、Input 集成适配器。
5. 增加 Editor 目录校验、Sample 和消费文档。
6. 在 GalleryKeeper 编写旧数据导入器，替换设置状态和输入重绑定，开始界面与暂停界面接入同一服务。
7. 运行 ZeroEngine 包测试及 GalleryKeeper EditMode/PlayMode 验证。
8. Spec 根据实际实现更新为 as-built；未经另行授权不提交、推送或发布。

## 验证

ZeroEngine：

- `package.json`、asmdef、`.meta` 和目录结构校验通过。
- Settings 与 Input 的专用 EditMode 测试程序集运行数大于 0，失败/错误均为 0。
- 显示确认计时、生命周期或 Input System 事件需要 Unity 生命周期时，使用最小 PlayMode 测试。
- CI 的 modular lane 明确将新 Settings 与 Input 测试程序集列入 testables；不得仅依赖当前会覆盖 testables 列表的旧逻辑。
- `git diff --check` 通过，无项目专用类型或路径进入通用包。

GalleryKeeper：

- 设置迁移、非法数据、语言、显示、音量、设备识别、改键往返、冲突和恢复默认的 EditMode 测试通过。
- 开始界面与暂停界面的键鼠和手柄 PlayMode 路径通过。
- 重启测试 Player 后，语言、音量、显示、输入偏好和改键仍生效。
- Unity Console 在编译、测试和退出后无新增 Error/Exception。

人工验收：

- Windows 键鼠、Xbox 类手柄、PlayStation 类手柄各完成一次完整设置流程。
- 切换语言后当前设置页立即刷新，重启后保持。
- 高风险显示设置不确认时自动恢复。
- 改键冲突、取消和恢复默认均给出明确结果，不丢失原绑定。

## 验收标准

1. 新包可在 Unity 2022.3 和 6000.3.10f1 编译，且不引用 GalleryKeeper、POB、LLS 类型或资产。
2. 所有标准设置拥有稳定 ID、明确类型、项目默认值、校验规则、应用策略和可用性规则。
3. 全局设置独立于存档槽；同一玩家偏好在不同存档间一致。
4. 初始化在主 UI 前完成；设置无需先打开面板即可生效。
5. 会话的预览、提交、取消、分类重置和全部重置均有自动化测试；失败会恢复旧 committed 状态。
6. 设置文档支持版本迁移、unknown entry 保留、primary/backup 恢复和非法单项回退。
7. 语言以 locale code 保存；可用语言动态生成；切换、失败回退、即时 UI 元数据刷新和重启保持均通过测试。
8. 窗口模式、分辨率、刷新率、VSync、帧率上限和画质按本 Spec 规则应用；高风险显示预览能确认或超时恢复。
9. Master/Music/SFX 音量可即时预览、取消恢复、提交保持，且不存在 AudioManager 与 Settings 双写。
10. 键盘鼠标及通用手柄均可识别和改键；组合绑定、GUID 定位、冲突策略、取消、超时、单项/全部重置和 JSON 往返通过测试。
11. PlayStation、Nintendo、Xbox/Steam Deck 的提示展示与通用 Gamepad 绑定逻辑分离，设备品牌不产生重复绑定方案。
12. 项目可注册自定义分类、设置、动态选项、文本解析器、存储和应用器，无需修改 Settings Core。
13. 包不强制生产 UI；GalleryKeeper 的开始和暂停设置使用同一服务并支持键鼠、手柄完整操作。
14. GalleryKeeper 旧设置仅在新文档缺失时导入；成功后旧数据仍保留，回退旧版本不会丢失设置。
15. POB、LLS 的标准字段及项目独有字段均有明确迁移映射，不要求首版修改其生产代码。
16. 新增测试在本地运行数大于 0 且零失败，CI modular lane 确实执行 Settings 与 Input 测试。
17. 文档、Sample、弃用信息和消费项目固定同一 ZeroEngine commit 的升级步骤与实际实现一致。
18. GalleryKeeper 中 `ISettingsService`/`SettingsSession` 是设置的唯一可变状态源；
    `GalleryUserSettingsState` 只负责旧数据迁移和向现有玩法代码提供只读兼容投影，新设置
    初始化后不得再写旧 `gallery-user-settings` 键。
19. GalleryKeeper 开始页和暂停页复用同一个设置控制器与会话；语言、显示、声音、操作和
    辅助分类均由相同稳定 ID 读写，不存在第二套 UI 状态或保存路径。
20. GalleryKeeper 改键 UI 只通过 `ZeroEngine.InputSystem.InputBindingService` 的
    Action/Binding GUID API 完成显示、交互改键、冲突处理、单项/全部恢复和 JSON 往返，
    不再自行按 binding index 实现冲突与持久化。
21. 自动化测试证明旧数据首次迁移、新文档优先、设置提交后重启保持且不回写旧键，并分别
    覆盖开始页与暂停页入口使用同一服务。

## As-built 记录

- 新增 `com.zerogamestudio.zeroengine.settings` 1.0.0；程序集与运行时命名空间为
  `ZeroEngine.PlayerSettings`，避免和 Persistence 旧 `ZeroEngine.Settings` 类型冲突。
- 已实现强类型值、目录校验、单写会话、预览/提交/取消/分类及全部重置、版本文档、
  unknown entry 保留、主备恢复、PlayerPrefs/SaveManager 存储、迁移接口和服务注册。
- 已实现语言、显示/画质、15 秒显示恢复、声音、输入偏好、绑定 JSON、辅助功能适配器。
- Input 2.1 保留旧 `InputManager`，新增 Action/Binding GUID 改键、Swap/Reject/Allow、
  组合 part 支持、取消/超时、单项/全部恢复、全资产 JSON，以及键鼠/手柄/Touch
  展示族和手柄 glyph 品牌分离。
- GalleryKeeper 首接仅在新文档缺失时导入旧 v5 全局数据；初始化后
  `GalleryUserSettingsState` 只作为现有玩法代码的兼容投影，所有公开 setter、开始页和
  暂停页均委托同一个 `SettingsSession`，旧键不再写入。
- GalleryKeeper 的改键显示、GUID 定位、交互改键、Reject 冲突、单项/全部恢复和 JSON
  往返均直接使用 `InputBindingService`；项目 UI 不再按 binding index 自行处理冲突。
- POB、LLS 未改生产代码，映射见 `docs/settings-migration-guide.md`。
- CI modular lane 的 testables 覆盖逻辑已修正，不再被 umbrella-only 列表覆盖。
- 自动化结果：Settings/Input EditMode 17/17，Gallery 设置/输入 EditMode 12/12，
  Gallery 开始、暂停、首次迁移/重启保持/旧键不回写路径 PlayMode 3/3；最终 Unity
  Console 无 Error/Exception。
- 本地验证使用 Unity 6000.3.10f1。Unity 2022.3 CI、Windows 实机键鼠、Xbox、
  PlayStation 与显示超时的人工验收仍待发布流程执行。
- 首版采用代码构建 `SettingsCatalog` 和项目 `SettingsBootstrap` 子类，未提供生产
  UI Prefab；这是保持 UI 项目所有权的既定边界。`SettingsCatalogSO` 示例和通用
  UGUI Sample 延后，不阻塞运行时契约。
- 未提交、推送或发布；GalleryKeeper 暂时使用本地 `file:` 包依赖，发布前必须替换
  为同一 ZeroEngine commit 的 Git URL。
