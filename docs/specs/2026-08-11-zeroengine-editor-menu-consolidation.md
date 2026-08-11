# ZeroEngine Dashboard V4：Unity Editor 菜单与工具入口收口

- 状态：Final
- 最后更新：2026-08-11
- ZeroEngine 基线：`main@aaf56643c42dc062d19f4d22be9021ec6b14a217`
- POB 基线：regular workspace `/main cs:16825 - head`；zeroengine 仓库的 18 个 `com.zerogamestudio.zeroengine.*` 包与 `com.zerogamestudio.analytics` 共 19 个业务包统一 pin `6f9ee5d3258a4eaf53fdffbd273a3e27e08482da`，`com.zerogamestudio.unity-mcp-control` 独立 pin `9bb7feedd010287ec35e35e6ce7f40f8563d4458`
- 设计批准：Approved；用户于 2026-08-11 要求“审一下修订到毕业”，授权本文通过毕业门后定稿
- 执行授权：Authorized；用户于 2026-08-11 要求“干”，覆盖本文范围内实施与本地验证
- 终端操作授权：Not requested

## 结论

第一方 Unity Editor 顶栏只保留一个入口：`ZGS/工作台`。它打开由 `com.zerogamestudio.zeroengine.dashboard` 提供的“ZGS 工作台”；`ZeroEngine/*`、项目内 `ZGS/*`、`POB/*` 和第一方 `Tools/*` 不再作为并列顶栏工具树。

所有持续有效的通用工具和 POB 工具改由显式 descriptor + action provider 自动进入工作台。ZeroEngine 只提供目录、执行、安全、诊断和 UI 合同；POB 的窗口、数据、项目写入和业务规则继续留在 POB provider。收进 ZeroEngine 指收进通用宿主，不是把 POB 业务代码搬进上游包。

自然依赖当前选择的 `Assets/*`、`GameObject/*` 或 `CONTEXT/*` 菜单继续留在 Unity 原生上下文；第三方包和 Unity 自带菜单不改。过期、一次性和 Run All 类入口直接退役，不把历史噪声原样搬进新工作台。

## 目标

1. 消除 `ZGS`、`ZeroEngine`、`POB`、第一方 `Tools` 多根并存和同类工具散落。
2. 让已安装 ZeroEngine 模块与项目适配包自动贡献工具，不在 Dashboard 中硬编码 POB 或可选模块。
3. 让全部保留工具可搜索、可分类、可理解，并在执行前显示真实可用性与安全级别。
4. 将同一工作面的窗口、步骤或 Dry Run / Apply / Validate / Build 动作合并展示，减少菜单叶子数量。
5. 对全部现有第一方菜单逐项得到“工作台 / 上下文保留 / 退役”之一，不能遗漏或双入口。
6. 保留业务行为、Profile、Undo、dirty、确认、快捷键和运行模式语义；只重构发现与入口。

## 非目标

- 不合并 Data Manager、Configurator、Formula Studio、Graph 编辑器等状态和职责不同的业务窗口。
- 不把 POB 私有类型、Addressables 分组规则、本地化批处理、测试路由或项目配置写入 ZeroEngine。
- 不修改玩家运行时 UI、Prefab、Scene、Addressables 内容、业务配置、序列化数据或存档。
- 不整理第三方 `Fullscreen/*`、Unity `Window/*`，也不接管不属于 ZGS/ZeroEngine/POB 的插件菜单。
- 不为了兼容保留可见的旧菜单别名；Unity `MenuItem` 没有可靠的隐藏别名，保留别名会直接违背收口目标。
- 不以反射字符串调用任意类型/方法，不扫描所有 `EditorWindow` 猜测工具，也不在 `OnGUI` 扫描程序集或文件。
- 本轮不新增收藏、最近使用、云同步或用户级持久偏好。

## 已检查的基线

### ZeroEngine

- 当前源码有 49 个 `MenuItem` attribute：39 个挂在 `ZeroEngine/*`、4 个挂在 `ZGS/*`、4 个是 `Assets/Export to Mod JSON` 的声明/校验，另有 2 个测试菜单。
- 生产菜单至少形成 `ZeroEngine/Tools`、`Mod System`、`Formula`、`Dialog`、`Tutorial`、`ZGS/ZeroEngine/TCE`、`ZGS/Analytics Dashboard` 和 `ZGS/Config Pipeline` 等并列层级。
- 8 份正式 descriptor 共声明 32 个 `menuPath`；`Buff Editor`、`Dialog/Export to CSV`、`Export Package` 和 `Mod System/Export to Mod...` 尚未进入 Dashboard。
- Dashboard schema v1 强制每个 entry 提供 `menuPath`，`DashboardEntryExecutor` 最终只调用 `EditorApplication.ExecuteMenuItem`。因此删除旧菜单前必须先替换执行总线。
- `ZeroEngine/Dashboard` 是当前宿主菜单；V3 已完成“工具 / 工作区 / 系统”和 POB 五面板，但刻意保留旧菜单。

### POB

- 当前工作树在 POB/第一方适配包范围内有 196 个 `MenuItem` attribute，约 195 条可见路径；其中约 189 条位于 `ZGS`，另有 3 条第一方 `Tools/*`、2 条 `POB/*` 和 1 条 `GameObject/*` 上下文菜单。
- 字面路径中 `ZGS/工具` 有 120 条；主要密度来自 `ZGS/工具/资源` 63 条、`ZGS/工具/本地化` 21 条、`ZGS/工具/POB` 13 条和 `ZGS/工具/POB Agent` 7 条。常量路径另包含字体、Addressables split、UI Panel lazy reference、发布与健康报告等动作。
- 其他并列根包括 `ZGS/工坊` 10 条、`ZGS/调试` 10 条、`ZGS/Demo` 7 条、`ZGS/数据` 6 条、`ZGS/Shader变体` 6 条、`ZGS/检查` 5 条，以及裸露的 Data Manager、无人机配置、Quest、Addressables、资源和 Tools 入口。
- POB 的两份 Dashboard descriptor 目前只接入 7 条路径：Data Manager、Configurator、Formula 两动作、关键测试、存档兼容测试和 Unity Test Runner。绝大多数现有工具仍绕过统一宿主。
- `ZGSToolMenuPathsTests` 仍以源码字符串硬编码并保护旧菜单层级；代码、测试、JSON 和文档中至少有 363 处相关路径引用，迁移不能只改 attribute。
- V3 的“保留旧菜单”是当时的兼容边界。本文获批后只在菜单入口层面取代该边界；V3 的布局、工作区、provider 隔离和安全合同继续有效。

实施开始前必须在获得 POB required task/path claim 后重跑同一受控范围 inventory；基线后的新增或删除入口按本文规则纳入，不以当前数量替代实际源码证据。

## 唯一顶栏菜单

第一方生产代码的全局顶栏菜单固定为：

```text
ZGS
└── 工作台
```

- `ZGS/工作台` 打开唯一 `ZeroEngineDashboard` 实例，默认定位“工具”页并聚焦搜索。
- 窗口标题显示“ZGS 工作台”；类型名、程序集名、package ID 和稳定技术 ID 继续使用 ZeroEngine 命名。
- 不保留 `ZeroEngine/Dashboard`、`ZGS/工具/POB 仪表盘` 或其他可见转发菜单。`ZeroEngineDashboard.ShowWindow/ShowWorkspace` 与 `POBDashboardWindow` 类型 facade 可继续保留公共调用兼容，但 facade 不带 `MenuItem`。
- 原先通过菜单快捷键打开的第一方窗口改用 Unity Shortcut Manager 的 `[Shortcut]`，快捷键调用同一 action provider；快捷键不重新生成顶栏叶子。
- `Assets/*`、`GameObject/*`、`CONTEXT/*` 只在动作确实依赖当前选择时保留。它们不复制到顶栏；可在帮助抽屉说明使用位置。

`ZGS` 作为工作室级共同所有者，能同时容纳通用 ZeroEngine 与项目 POB；把 POB 工具直接挂到 `ZeroEngine/*` 会产生错误归属，因此不采用。

## 工作台信息架构

Dashboard 保持“工具 / 工作区 / 系统”三个一级页；“工具”页由模块优先改为任务分类优先。

### 固定分类

1. **内容创作**：数据编辑器、Formula、Quest、Workshop、Demo 内容预览。
2. **数据与本地化**：Data Manager、配置管线、Schema、检索、导入导出、本地化检查与修复。
3. **资源与构建**：Addressables、纹理/字体/音频、Shader 变体、构建准备和资源迁移。
4. **检查与调试**：只读审计、运行诊断、可视化调试、健康报告和配置验证。
5. **测试与发布**：具名窄范围测试、发布检查、Workshop 发布、可审计打包流程。
6. **系统与安装**：通用包安装器、Dashboard/System 工具和框架级维护。

分类 ID 固定为 `authoring`、`data-localization`、`assets-build`、`diagnostics`、`test-release`、`system-setup`；用户文案为简体中文。任意 module 可贡献多个分类，但不得自造新的一级分类。

### 浏览与密度

- 左侧或紧凑选择器显示固定分类；内容区按 surface 分组，不再用 30 多个 package/module 作为主要导航。
- 顶部提供范围筛选：`全部 / 通用 / <项目名>`。项目项按 descriptor 的 `projectId/projectDisplayName` 动态生成、按 `projectId` 稳定排序；POB 中显示 `全部 / 通用 / POB`，Dashboard 不硬编码 POB 或项目集合。
- 默认只显示 `primary`。`advanced` 需主动打开“高级工具”，`maintenance` 需再主动打开“维护工具”并常驻风险提示；两个开关只在窗口内瞬态保存。
- `primary` 只用于日常重复窗口和低门槛安全动作；`advanced` 用于有持续职责的专业检查、构建和项目写入；`maintenance` 用于恢复、迁移、destructive 或高影响动作。所有 destructive 动作固定为 maintenance，不能降级。
- 搜索同时匹配简体中文名称、说明、使用方法、稳定 ID、旧菜单关键词和来源模块；旧路径只作为迁移关键词，不作为执行入口或常驻详情。隐藏层级存在匹配项时只提示数量和所需筛选，不自动展开 maintenance。
- 模块名、POB/通用范围、安全和可用性用必要 chip 表示；技术 ID、provider、来源和旧路径进入帮助/详情。

## 合并与退役规则

### 合并为一个 surface

- 同一窗口的不同页签或 Profile：一个 surface，多动作切页，不生成多个窗口实例。
- 同一数据目标的 `Dry Run / Apply / Validate / Build`：一个 surface，动作顺序固定；每个动作保留自己的 safety、confirmation 和 availability。
- 同一流程的编号步骤：一个 surface，按步骤展示；不把步骤继续铺成顶栏菜单。
- 同一功能的通用实现与 POB adapter：保持各自 `FullId` 和 provider 所有权，通过 `mountModuleId/replaces` 在同一 surface 展示。
- 文档入口：归入对应 surface 的帮助抽屉；只有生成、导出或发布等真实动作才占工具行。

### 必须独立

- 数据源、持久状态、Undo/dirty、运行模式或安全等级不同的业务窗口不合并生命周期。
- 项目写入和 destructive 动作可与同一 surface 的只读动作并列，但必须使用独立按钮、独立确认和独立禁用原因；不能以一个确认覆盖整组。
- Play Mode 运行工具与 Edit Mode 配置工具不得因名称相近而共享实例。

### 退役

- 明确标记“旧布局/已退役”的入口删除 attribute 和 descriptor，不迁入维护区。
- `Run All`、完整 PlayMode/EditMode 全跑和 Demo 全量测试不进入工作台；保留关键测试、存档兼容和经过项目规则定义的具名窄范围路由。
- 已完成且没有恢复职责的一次性迁移入口删除菜单；若仍承担可审计恢复，只能进入 `maintenance`，必须有中文用途、前置条件、项目写入/破坏性标记和确认。
- 仅供测试 fixture/gallery 的菜单改为测试直接调用或 provider test double，不能出现在生产顶栏。

## 通用 action provider 合同

### 依赖方向

- `com.zerogamestudio.zeroengine.editor-ui` 新增 Editor-only action SPI；它只定义 provider、action state/result 和执行上下文，不发现 descriptor、不读取项目、不引用 Dashboard 或业务包。
- `com.zerogamestudio.zeroengine.dashboard` 依赖 editor-ui，负责 TypeCache 发现、descriptor 绑定、安全确认、执行、诊断与 UI。
- ZeroEngine 模块、POB 包和项目代码的 provider 只依赖 editor-ui；Dashboard 不直接引用任何可选模块或 POB 类型。
- 未安装 Dashboard 时业务程序集和公开 Open/Run API 仍可编译；统一菜单发现需要安装 Dashboard，不再承诺每个模块有独立顶栏菜单。

### SPI 与发现

- editor-ui 新增公开、Editor-only 的 `[EditorToolActionProvider("provider-id")]`、`IEditorToolActionProvider`、`IEditorToolAction`、不可变 state/result/context 类型；命名与现有 `EditorWorkspacePanelProviderAttribute` 模式一致。
- provider ID 使用 `^[a-z0-9]+(?:[.-][a-z0-9]+)*$` 且在当前 Catalog 全局唯一；action ID 使用 `^[a-z0-9]+(?:-[a-z0-9]+)*$` 且在 provider 内唯一。descriptor 的 `providerId/actionId` 必须唯一绑定一个可构造 action，也不得被多个正式 entry 重复绑定。
- `IEditorToolActionProvider.CreateAction(actionId)` 按 ID 返回 action；provider/action 构造与创建必须无副作用、无订阅、无后台任务。Dashboard 通过 TypeCache + attribute 建索引，不为读取 ProviderId 构造 provider。
- provider 与 action 只在当前窗口、当前 Catalog 生命周期内为可见 entry 延迟创建并缓存；descriptor 刷新或窗口关闭时丢弃缓存。action 不拥有需释放的外部资源，执行所需资源必须在单次 Execute 内成对管理。
- Dashboard 只为当前分类、scope 与 visibility 下实际绘制的 action 调用 `GetState()`；隐藏分类不创建 action、不轮询。`GetState()` 返回 enabled、checked 和中文禁用原因，只允许读取廉价即时 Editor 状态，不得写盘、刷新资产、打开窗口、执行菜单或启动长任务。

### 执行协议

- `IEditorToolAction.Execute(context)` 是统一入口，返回 `succeeded/cancelled/failed` 与中文用户摘要；context 只提供宿主窗口、entry 身份和刷新请求，不提供任意反射或菜单执行能力。
- 每次点击依次执行：解析唯一 provider/action → 校验 descriptor availability → 读取 state → 对 `project-write/destructive` 显示 descriptor 中文确认 → 确认后再次读取 state → Execute。任一步不可用、取消或异常均不得进入下一步。
- 现有业务前置检查和领域确认继续由所有者保留；与 host 完全重复的通用确认可迁到 descriptor，带参数、预览或不可逆细节的领域确认不得删除。provider 不能降低或吞掉确认，也不能把尚未发生的确认视为已接受。
- 仅有私有 `MenuItem` handler 的动作必须把原 handler body 提取到同程序集 typed internal 方法；跨 asmdef/package 调用使用所有者的公开 Editor-only facade。禁止复制业务逻辑、调用私有反射或把旧菜单当中转。
- provider 构造、状态读取或执行异常只隔离相关 action；其他工具、工作区与系统页继续可用，系统页显示 provider/action/source 诊断。
- Dashboard 只在 Catalog 刷新时通过 TypeCache 建立 provider 索引；`OnGUI` 不反射扫描。禁止 descriptor 指定任意类型名/方法名，禁止 `MethodInfo.Invoke`，禁止以旧 `menuPath` 作为 provider fallback。

## Descriptor schema v2

正式第一方 descriptor 升级到 `schemaVersion: 2`。v2 module 新增：

- `scope`：`universal` 或 `project`。
- `projectId/projectDisplayName`：`project` 时必填；POB 固定为 `pob/POB`。

v2 entry 保留 `id/displayName/description/usage/mountModuleId/section/surface*/order/safety/confirmation/availability/replaces`，并改为：

- `category`：使用六个固定 ID。
- `visibility`：`primary`、`advanced` 或 `maintenance`。
- `executionKind`：正式入口固定为 `provider`。
- `providerId/actionId`：均为稳定小写 ASCII ID，必须唯一命中。
- `legacyKeywords`：可选，仅用于搜索迁移前名称；不得显示为菜单或执行路径。
- `documentationPath/documentationUrl`：可选 entry 级帮助。v2 package descriptor 的相对路径必须解析在安装包根内，project descriptor 的相对路径必须解析在当前 Unity 项目根内；拒绝绝对路径、规范化后的目录穿越和根外目标，URL 仅允许 HTTPS。entry 未提供时回退 module 级文档；同 surface 的不同 action 可在帮助抽屉列各自链接。v1 继续使用当前解析语义。

`displayName/surfaceActionLabel` 是可见中文 label，`description` 是简短 tooltip，`usage` 和文档链接进入帮助抽屉。v2 entry 不允许 `menuPath`。Dashboard 4.x 继续解析外部 schema v1、标记“旧版入口”并保持原 `ExecuteMenuItem` 行为；本仓全部正式 descriptor 与 POB adapter 在发布前必须是 v2，静态门禁止新增第一方 v1。v1 兼容合同在首个 Dashboard 5.x 发布时到期，实际删除不属于本次变更。

entry 的 `FullId`、mount、replaces 和 surface identity 保持稳定；当前已存在的 32 个上游与 7 个 POB、共 39 个 descriptor entry 只替换执行绑定，不改 ID。新接入 POB 工具获得明确 POB 所有者 ID，不伪装成通用 ZeroEngine 实现。

## 迁移矩阵

| 当前范围 | 处理 | 工作台归属 |
| --- | --- | --- |
| `ZeroEngine/Dashboard` | 替换为唯一 `ZGS/工作台` | 宿主入口 |
| 其余 `ZeroEngine/*` 38 个 attribute | 删除顶栏 attribute，补齐/迁移 v2 provider | 按六类归入“通用” |
| ZeroEngine 的 4 个 `ZGS/*` | 删除旧路径，迁移各自所有者 provider | Analytics/Config/TCE 通用模块 |
| ZeroEngine 测试菜单 2 个 | 取消生产菜单，测试直接调用/provider double | 不显示 |
| `Assets/Export to Mod JSON` | 保留选择上下文与 validate 语义 | 原生 Assets 上下文 |
| POB `ZGS/工具/资源` 及常量组 | 同目标动作合并为 surface | POB · 资源与构建 |
| POB `ZGS/工具/本地化` 与字体动作 | 通用导入导出为 advanced，目标批修/恢复为 maintenance | POB · 数据与本地化 |
| POB `ZGS/工坊` | 创作窗口归内容；校验/发布归测试与发布；文档进帮助 | POB · 内容创作/测试与发布 |
| POB `ZGS/调试`、`ZGS/检查` | 窗口、审计、toggle 改 provider；toggle 保留 checked 状态 | POB · 检查与调试 |
| POB `ZGS/Shader变体` 6 步 | 合为一个顺序 surface，逐动作保留安全语义 | POB · 资源与构建 |
| POB `ZGS/Demo` | 预览/模拟进入 advanced；Run All 退役 | POB · 内容创作/检查与调试 |
| POB Data Manager、无人机、Quest、数据入口 | 去除裸叶并注册 v2 | POB · 内容创作/数据与本地化 |
| POB Agent、`POB/Audit`、`POB/Maintenance` | 可重复报告归 diagnostics；恢复/迁移归 maintenance | POB · 检查/测试与发布 |
| POB 第一方 `Tools/*` 3 条 | 迁移 provider；原快捷键改 `[Shortcut]` | 对应 POB 分类 |
| `GameObject/Remove All Non-2D Colliders` | 保留选择上下文 | 原生 GameObject 上下文 |
| POB 仪表盘与测试菜单 | 仪表盘菜单删除；关键/存档兼容改 provider；全量门仅保留自动化 API | POB · 工作区/测试与发布 |
| 第三方/Unity 菜单 | 不改 | 外部所有者 |

POB 迁移必须先生成一次受控 inventory，将每个实际 `MenuItem` 归入上述唯一 disposition；实现终审按“基线集合 = provider + context + retired”核对，任何未分类或多重分类均阻断发布。descriptor/provider 按现有程序集、包和业务职责拆分，不把约 190 个可见动作塞进一个 descriptor 或一个 provider。该 inventory 是临时验证产物，最终事实与计数写回本 Spec，不新增长期手工维护的第二份菜单清单。

## 兼容、失败、回滚

- 窗口类型、公开 Open/Run 方法、descriptor `FullId`、Profile、数据结果、Undo/dirty、EditorPrefs、测试分类和快捷键行为保持；只删除旧公开菜单路径。
- `EditorApplication.ExecuteMenuItem(oldPath)` 明确不兼容。实现前必须搜索 ZeroEngine、POB、自动化脚本、测试和活跃文档；第一方调用全部改稳定 provider/API，旧历史文档只保留为归档证据并标明已失效。
- provider 缺失、重复、actionId 不存在、构造/状态/执行异常全部 fail closed；不得回退旧菜单或任选实现。
- v1 外部 descriptor 失败只隔离自身，不影响 v2；系统页分别显示 legacy 与 provider 诊断。
- ZeroEngine 合并前回归通过撤销任务 PR 改动修复；合并后、POB 提交前回归不推进 pin；POB 提交后回归通过新的精确 Plastic changeset 反向本任务 POB provider/menu/docs 变更，并将 manifest/lock 成对 pin 回 `6f9ee5d…`。不重写 Git/Plastic 历史，不重新添加零散菜单别名作为热修。
- 回滚不改生产配置、不删除 Unity layout、不重写 Git/Plastic 历史。

## 影响范围

### ZeroEngine

- `com.zerogamestudio.zeroengine.editor-ui/Editor/Workspace/**` 或同层新 `Actions/**` SPI、asmdef、README、CHANGELOG 与测试。
- `com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/**`、`Execution/**`、`ZeroEngineDashboard.cs`、`DashboardText.cs`、README、package version、descriptor/tests/静态门。
- 8 份正式 `ZeroEngineDashboardModule.json` 及其所有者窗口/命令入口；旧单体包与 modular 包保持互斥 lane。
- 所有第一方 ZeroEngine `MenuItem` 声明；`Assets` 上下文例外按矩阵保留。
- `docs/specs/2026-08-11-zeroengine-editor-menu-consolidation.md` 持续更新为 as-built。

版本默认：Dashboard `4.0.0`，editor-ui `1.3.0`。Dashboard major 表示公开菜单路径收口；editor-ui SPI 为 Editor-only 加法版本。

### POB

- `Assets/Assets/_Scripts/_POB/**/Editor/**` 与 `Packages/com.zerogamestudio.pob.*/Editor/**` 中全部第一方 `MenuItem` 所有者及配对测试/descriptor/meta。
- 按现有 Editor asmdef/package 与职责拆分 POB descriptor/action provider；不形成单个约 190-action 巨型类，不移动 runtime 业务代码。
- `ZGSToolMenuPathsTests` 改为唯一顶栏、provider/descriptor 覆盖和上下文 allowlist 合同；现有硬编码旧路径断言与受影响 source tests 同步更新。
- `AGENTS.md` quick reference、`docs/rules/third_party.md` 菜单归属规则、活跃 runbook/README 中的入口更新为 `ZGS/工作台` + 稳定工具名称。
- 最终成对更新 `Packages/manifest.json` 与 `Packages/packages-lock.json`，19 个业务 pin 同一 canonical；unity-mcp-control pin 不随业务包改变。

其他 POB pending、ProjectSettings、业务资产、Prefab、Scene、公式 Profile、玩家内容和第三方 packages 不在范围内。

## 实施顺序

1. 在 editor-ui 实现 action SPI、状态/result 与无副作用合同测试。
2. Dashboard 增加 schema v2、provider registry、分类优先 UI、scope/visibility 筛选和按 action 安全执行；v1 回归继续通过。
3. 迁移 ZeroEngine 正式 descriptor 与所有者入口，补齐未入 Dashboard 的 4 个生产工具；静态门先保证只有 `ZGS/工作台` 与上下文 allowlist。
4. 运行 ZeroEngine package、descriptor、静态门和临时消费工程验证；此阶段不修改 POB manifest/lock，不发布提交。
5. 获得终端操作授权后，提交并推送 ZeroEngine 任务分支、建立 PR，但在真实 POB 联调通过前不合并。
6. 在 POB required task/path claim 下重跑 inventory，将 manifest/lock 成对 pin 到 PR 的不可变 Git commit；任何阶段都不使用业务 `file:` pin。
7. 按迁移矩阵分批建立资源、本地化、内容、诊断、发布 descriptor/provider；每批同时移除对应 `MenuItem`、合并 surface、退役入口并更新测试和活跃文档。
8. 运行 POB provider、route、源码合同和可见工作台路线；若上游修订产生新 PR commit，manifest/lock 成对重 pin 后重跑受影响验证。
9. ZeroEngine 五 CI lane、独立审查和 POB 联调均通过后按仓库流程合并 PR；不得自批、绕过或先合并后补真实联调。
10. POB manifest/lock 成对切到 canonical merge commit，复跑最终门后用 Plastic 精确提交本任务路径，并释放全部 task/claims/Unity ownership。

## 验证

### 静态与纯逻辑门

- ZeroEngine 第一方生产源码只允许全局 `[MenuItem("ZGS/工作台")]`；禁止其他 `ZGS/*`、`ZeroEngine/*`、`POB/*` 和第一方 `Tools/*`。`Assets/*` allowlist 与 validate 配对必须精确。
- POB 第一方源码禁止全局 `ZGS/*`、`ZeroEngine/*`、`POB/*`、`Tools/*`；只允许矩阵批准的 `GameObject/*`/其他真实上下文及其 validate 配对。
- schema v2 覆盖合法 provider、scope、固定 category、visibility、legacyKeywords、每 action safety；拒绝 v2 `menuPath`、任意反射目标和缺失确认。
- v1 descriptor 保持解析/执行兼容；正式上游与 POB descriptor 全部为 v2，第一方静态门不再以 `MenuItem` 存在证明入口有效。
- provider registry 覆盖缺失、重复、抽象类型、构造失败、action 缺失、状态异常、执行异常、排序与失败隔离。
- 安全门断言 host confirmation 发生在 provider Execute 前；取消、窗口关闭和 Esc 不执行。每个 surface 的动作独立安全，不继承默认动作的较低等级。
- inventory reconciliation 证明每个实施基线旧路径恰好属于 provider、context 或 retired；provider action 与 descriptor 一一对应，无孤儿与重复。
- 第一方运行时代码、自动化、测试与活跃文档不再调用或指导旧全局菜单路径；旧路径仅可出现在 `legacyKeywords`、迁移测试输入或明确标记失效的历史归档。
- 搜索与分类覆盖旧关键词、中文名称、scope、六类、advanced/maintenance；隐藏维护工具不影响搜索诊断。
- 固定菜单文案、labels、tooltips、usage、禁用原因与确认均为简体中文；技术 ID、路径、品牌缩写和业务对象名保持原值。
- entry/module 文档路径分别覆盖合法 package/project 根与绝对路径、`..`、规范化根外目标等拒绝用例；外链覆盖 HTTPS 通过和其他 scheme 拒绝。

### Unity 与可见路线

- ZeroEngine 五个 CI lane 继续通过；新增 dashboard/editor-ui 最窄 EditMode tests 不要求本机 Run All。
- POB 运行最窄 provider/descriptor/source contract tests，并抽样每类至少一个窗口、只读命令、project-write 取消/执行、destructive 取消和 toggle checked 状态。
- Unity 顶栏实际只出现 `ZGS > 工作台` 这一条第一方全局入口；不再出现 `ZeroEngine`、`POB` 或第一方 `Tools` 根。第三方与原生上下文不计入失败。
- 在 760、960、1440 point 验证六类、全部/通用/POB、advanced/maintenance、长中文、四动作 surface 与帮助抽屉，无重叠、遮挡或不可达按钮。
- 搜索、分类、scope、visibility、surface action 与帮助抽屉可用键盘到达和执行；安全/禁用/选中状态同时有文字或图形，不只依赖颜色。
- 从 `ZGS/工作台` 搜索并打开 Formula Studio、Data Manager、POB 配置器、资源工具、检查工具、关键测试和 POB 运行概览；窗口/Profile/工作区定位正确且不产生重复实例。
- 验证用既有菜单可视 route、provider tests 和窗口状态；不使用 `execute_code`。任何 `outcome_unknown` 立即停止且不重放。
- 验证前后 POB `EditorSettings.asset`、`LevelConfig.asset`、`EndlessBuildSettings.asset` 哈希与任务外 pending 不变；最终 Console 0 error。
- 最终 manifest/lock 无业务 `file:`，19 个业务包统一 pin canonical merge commit，独立 unity-mcp-control pin 不变。

## 验收标准

1. Unity 顶栏中第一方全局菜单只有 `ZGS/工作台`；源码与实机均无 `ZeroEngine/*`、`POB/*`、第一方 `Tools/*` 或其他 `ZGS/*`。
2. 真实依赖选择上下文的 `Assets/GameObject/CONTEXT` 入口按 allowlist 保留，第三方与 Unity 自带菜单完全不受影响。
3. 实施基线全部第一方菜单恰好归入 provider、context、retired 之一；没有遗漏、双入口或未解释的路径。
4. 已安装通用模块和项目 adapter 通过 descriptor v2 + 唯一 provider 自动出现；scope 筛选由 descriptor 动态生成并在 POB 中显示 POB，Dashboard 不硬编码 POB、包集合或业务类型。
5. v2 正式入口不依赖 `MenuItem`、`ExecuteMenuItem` 或任意反射；provider 缺失/重复/异常只隔离相关 action。
6. 工作台按六个固定任务分类浏览，支持全部/通用/POB、advanced/maintenance 和中文/旧关键词搜索；默认只显示 primary，不自动展开 maintenance。
7. 同窗口多页签、同目标 Dry Run/Apply/Validate/Build 和顺序步骤按规则合并；不同状态、安全或生命周期的业务窗口不被错误合并。
8. 每个 action 保留原 availability、Profile、数据结果、Undo/dirty、EditorPrefs 与快捷键；项目写入和 destructive 始终有独立中文标识与确认。
9. 明确退役旧布局、Run All、测试 fixture 菜单和无恢复职责的一次性入口，不以 maintenance 作为垃圾桶。
10. 当前 32 个上游与 7 个 POB、共 39 个 descriptor entry 的 `FullId`、mount、replaces 与 surface identity 不变；公开 Open/Run API 和 `POBDashboardWindow` 类型兼容保留，第一方活跃调用/文档不再依赖明确失效的旧菜单路径。
11. ZeroEngine 只拥有通用 SPI/host；所有 POB 数据、业务动作、测试路由和项目配置继续由 POB provider 所有。
12. schema v1 外部 descriptor 在 Dashboard 4.x 继续工作并显示旧版诊断；全部第一方正式 descriptor 发布时为 v2 且无 menu fallback，兼容合同明确在 5.x 到期。
13. 所有固定 label、tooltip、usage、禁用原因、安全提示和确认是简体中文；entry 文档受 package/project 根、目录穿越与 HTTPS 校验，帮助抽屉是用途、使用方法、文档和技术详情的单一位置。
14. ZeroEngine 静态门、provider/catalog/action tests 与五 CI lane 通过；POB 最窄 provider/route/source tests 和每类可见抽样通过，最终 Console 0 error。
15. 760、960、1440 point 下六类、范围筛选、维护筛选和四动作 surface 无文字重叠、遮挡、不可达动作或无提示截断；关键导航与动作可用键盘完成，状态不只依赖颜色表达。
16. 验证不修改 POB 生产配置或任务外 pending；任何 unknown 不重放，最终 workspace task/claims/Unity ownership 全部释放。
17. Dashboard `4.0.0` 与 editor-ui `1.3.0` 同一 canonical 发布；POB manifest/lock 成对 pin zeroengine 仓库的 19 个业务包到该 commit，无业务 `file:`，`com.zerogamestudio.unity-mcp-control` 独立 pin 不变。

## 需求到验收映射

- 菜单根收口、全量处置和上下文例外：验收 1–3、9。
- 安装模块自动加载、通用宿主与 POB 所有权：验收 4、5、11、12。
- 同类合并、密度分层和保留行为：验收 6–10。
- 中文 label/tooltip/帮助、可读性与视觉无重叠：验收 13、15。
- 安全、验证、配置不变和可恢复发布：验收 14、16、17。

## 自审记录

- 架构：菜单碎片的根因是 `menuPath` 执行总线，不只是不统一的名称；provider SPI 先于菜单删除，避免中央硬编码和反射。
- 归属：`ZGS` 是唯一人类顶栏品牌，ZeroEngine 是宿主实现，POB 是显式项目 scope；三者不再竞争顶栏根。
- 可读性：分类优先、范围筛选与 maintenance 隐藏解决约 200 项直接平铺，未引入收藏或第二套导航状态。
- 安全：确认由 host 在 Execute 前执行；surface 内动作不共享较低 safety；失败 fail closed，不回退旧菜单。
- 兼容：保留稳定 ID、公共 API、类型、Profile 和快捷键，明确放弃不可隐藏的旧菜单路径；v1 外部 descriptor 在 4.x 保留、到 5.x 到期。
- 范围：上下文与第三方菜单保留，POB 业务不进上游，生产配置/资产不变；所有现有第一方路径有确定迁移规则。
- 终审修订：按实测将 descriptor 数修正为 32+7=39、`ZeroEngine/*` 迁移数修正为 38；provider 改为既有 attribute 发现模式，补齐延迟生命周期、执行顺序、typed handler、entry 文档安全根、动态项目筛选与键盘可达合同。
- 可执行性：实现顺序、两仓范围、版本、回滚、静态门、Unity 路线、需求映射和 17 项验收已冻结；无未决占位项，用户设计批准已记录，毕业门通过后状态为 Final。
