# ZeroEngine Dashboard：任务中心与工具库布局重设计

- 状态：Implemented
- 最后更新：2026-08-12
- ZeroEngine 基线：菜单收口 canonical merge `6745616347efdda59d3f5cc50fc531790091ce85`，tree `3122215c409fd7e82eee1fc8d45e5bfb296032e9`
- POB 实测快照：2026-08-11；14 份项目 descriptor、218 个 entry、107 个 effective surface；7 个 primary、199 个 advanced、12 个 maintenance entry；主 descriptor 声明 5 个 panel，provider 测试覆盖同 5 个，但工作台显示 0 个 panel、1 条 catalog diagnostic
- 设计批准：Approved；用户于 2026-08-11 回复“干”
- 执行授权：Authorized；范围为本文实现与本地验证
- 终端操作授权：Authorized；用户于 2026-08-12 回复“继续”，授权提交、PR、合并、POB Git pin 与 Plastic 精确提交
- 关系：本文只取代 `2026-08-11-zeroengine-editor-menu-consolidation.md` 的工作台信息架构、布局和视觉验收部分；唯一菜单、typed provider、schema v2、安全、稳定 ID、POB 所有权与 pin 合同继续有效

## 结论

当前实现已解决菜单散乱和文字重叠，但仍是“把菜单树换成长列表”：模块标题、section、surface、多个动作、帮助按钮和技术详情在同一滚动层级竞争注意力。文档元数据技术上位于帮助抽屉，实际目录中仍有文档窗口、文档生成命令和文档覆盖检查与编辑器工具并列，因此用户感知上仍是“文档和编辑器混着”。

重设计固定为三个一级页：

1. **首页**：项目工作区面板与少量 primary 工作流；默认入口。
2. **工具库**：完整、紧凑、可搜索的 surface/action 目录；面向专家和低频操作。
3. **系统**：诊断、包、adapter 和目录健康；不混业务工具。

用途、使用方法、相关资料和技术详情统一进入右侧上下文抽屉。首页和工具库不再逐项显示 `?` 与“详情”，模块名只作来源标签，不再作为主内容标题。

## 目标与非目标

### 目标

- 让用户首先看到“现在要完成什么”，而不是已安装多少模块或有多少命令。
- 将 218 个 entry 压缩为可理解的 surface；修复安全 visibility 后，首页展示 4 个有效 primary surface 与可用工作区面板。
- 将帮助、文档、技术 ID 与执行动作分层，减少首屏文字和重复控件。
- 所有用户可见固定文案和交互 tooltip 使用简体中文；POB descriptor 文案通过明确字段维护，不做运行时机器翻译。
- 在 760、960、1440 point 下保持可读、可达、无重叠，并让宽屏空间真正用于信息分栏。
- 保持 descriptor 自动发现、项目 scope、provider 所有权和安全语义，不硬编码 POB 或安装包集合。

### 非目标

- 不重写 218 个 action 的业务实现、provider ID、action ID、FullId、确认、Undo/dirty 或 availability。
- 不合并生命周期、数据源或安全等级不同的业务窗口。
- 不新增收藏、最近使用、使用频率追踪或云同步。
- 不在本轮引入多语言框架；英文稳定 ID 与 POB、TCE、Ability 等约定术语可保留在来源或开发信息中。
- 不把 POBDashboard 业务实现搬入 ZeroEngine；ZeroEngine 继续只提供通用宿主和 SPI。
- 不把维护动作放到首页，也不因搜索自动展开 destructive 内容。

## 已检查的当前行为

- `ZeroEngineDashboard.DrawToolContent` 按 `module → section → surface` 嵌套绘制；每个 module 都重复标题、工具总数、帮助按钮和强调线。
- `DrawSurface` 同时绘制动作组、来源/安全 chip、帮助按钮、“详情”折叠、技术路由、availability 和 runtime diagnostic，导致一行承担过多职责。
- 左侧六分类统计的是当前可见 entry 数；原始 7 个 primary entry 覆盖 5 个 surface，其中两项 project-write 的 primary 声明非法。修复后为 5 个 primary entry、4 个 surface，其他分类仍不应显示 `0`。
- POB descriptor 当前有 218 个 entry：199 advanced、12 maintenance、7 primary；其中 103 个 project-write、12 个 destructive。
- 218 个 entry 均有 description 和 usage，但没有 entry 级 documentationPath/documentationUrl；三项名称与文档有关：
  - “组件文档”是 navigation window，属于参考资料入口；
  - “同步指令文档”是 project-write 生成动作，仍属于工具；
  - “TCE 组件说明覆盖率”是 read-only 检查，仍属于诊断工具。
- 真实窗口在 1170×709 内容区已无文字重叠，但首屏存在重复模块标题、大量横线、弱信息层级和长滚动。
- POB 主 descriptor 明确声明 `runtime-overview`、`resource-lifecycle`、`pickup-diagnostics`、`card-pool-diagnostics`、`project-settings` 五个 panel；`POBEditorUiRouteCoverageTests.PobWorkspaceProvider_CreatesFiveIndependentPanels_AndLegacyWindowIsFacade` 验证 `pob.dashboard` provider 可分别创建它们。
- 打开 `ZGS/工作台` 时实测顶部为 19 个模块、174 个已发现工具、0 个面板、1 个 catalog diagnostic，与上述五 panel 合同冲突。已通过现有系统页归因：POB 主 descriptor 的 `entries[20]`（关键测试）和 `entries[21]`（存档兼容测试）为 `project-write + primary`，违反既有安全合同，导致整份 descriptor 被隔离。
- 当前相关验证：核心 provider/route/source fixtures 18/18，通过；邻接 fixtures 91/91，通过；Unity Console 0 error。

## 信息架构

### 首页

首页替代现有“工作区”页，并成为 `ZGS/工作台` 的默认页。

- 左侧：**工作区导航**。显示当前 scope 下可用 panels；按 descriptor 顺序稳定排列。
- 中间：选中 panel 的正文；未选 panel 时显示 **常用工作流**，即当前 scope 下 `visibility=primary` 的 surface。
- 右侧：**上下文抽屉**。显示当前 panel/surface 的用途、状态、使用方法、相关资料和技术信息。
- 项目没有 panel 时不显示空左栏，常用工作流占满主区。
- 从 `ZGS/工作台` 普通入口打开时固定进入未选 panel 的首页总览；从既有 `ShowWorkspace(moduleId, panelId)` 公共入口打开时直接选中目标 panel。关闭窗口后不持久化选择。
- panel provider 缺失或失败时仅隔离该 panel，并在原位置显示中文原因与“前往系统诊断”；不能让整个首页退回空白。

首页不显示 advanced/maintenance，不显示 raw module 目录，不显示 raw action 总数。

### 工具库

工具库是完整目录，不再使用 module 大段分组。

- 左侧筛选：任务分类、范围、级别、安全、可用性；只显示有结果的分类，零结果分类不占固定行。
- 主区：每个 effective surface 一行或一张紧凑卡；标题是 surface 名称，模块/项目以次要 chip 标注。
- 默认只显示 primary 和 advanced；maintenance 需显式开启并保留警告条。
- 每个 surface 只显示一个主动作：优先使用既有 `surfaceDefault=true`，否则使用 Catalog 已稳定排序后的第一项；同一 surface 多个 default 继续触发既有 `surface-contract-conflict`，拆为独立项并进入系统诊断。其他动作进入“更多”菜单或展开区。多步骤 workflow 按既有 action 顺序展示，但不把每一步铺成并列大按钮。
- 搜索默认跨全部工具级别，但隐藏的 maintenance 只显示“有匹配的维护项”提示，不展示正文、不允许直接执行。
- 结果数以 surface 为单位；展开后再显示 action 数，避免“工具数”和实际可操作单元不一致。

### 系统

- 首屏只显示 catalog 健康、错误/警告及可修复原因。
- 已安装包、descriptor、provider、project adapter 放在次级折叠区。
- raw module/tool/panel 数属于系统统计，不再放在所有页面的主标题旁。
- 系统页不得承载 POB 业务动作；修复动作若会写项目，必须仍通过正常 provider、安全和确认合同执行。

## 文档、帮助与参考资料

schema v2 增加可选 `contentType`：

- `action`：默认值；可执行的 window/command，进入工具库。
- `reference`：只打开说明、指南、示例或参考窗口；不进入工具库主列表，进入上下文抽屉的“相关资料”，也可被全局搜索命中并标记为“资料”。

约束：

- `reference` 只允许 `navigation` 或 `read-only`，禁止 project-write/destructive。
- module/entry 的 documentationPath/documentationUrl 继续是资料元数据，不生成独立工具卡。
- reference 默认归属自身 module：选择该 module 提供的 surface 或 panel 时，它出现在“相关资料”；没有可关联 surface/panel 时仍可由全局搜索打开。资料搜索结果位于独立“相关资料”分组，不与可执行工具混排。
- “组件文档”迁为 `reference`。
- “同步指令文档”仍为 `action`，可见名称改为“生成/同步控制台指令文档”，明确它会写项目。
- “TCE 组件说明覆盖率”仍为 diagnostics action，不因名称含“说明”而归为资料。
- 帮助抽屉按固定顺序显示：用途 → 当前状态/禁用原因 → 使用方法 → 相关资料 → 安全与影响 → 折叠的开发信息。
- provider/action ID、来源路径、旧菜单关键词只在“开发信息”内显示；普通用户首屏不见。

## 视觉与交互布局

```text
┌ ZGS 工作台 ─────────────── [全局搜索] ─────────── [问题] [刷新] ┐
│ [首页] [工具库] [系统]                                        │
├──────────────┬──────────────────────────────┬──────────────────┤
│ 工作区/筛选  │ 主面板或 surface 列表         │ 上下文抽屉       │
│              │                              │ 用途             │
│ 仅有效项     │ 标题 + 一行结果说明           │ 当前状态         │
│ 不显示 0 项  │ 来源/安全为次要 chip          │ 使用方法         │
│              │ [主动作] [更多…]              │ 相关资料         │
│              │                              │ 开发信息 ▸       │
└──────────────┴──────────────────────────────┴──────────────────┘
```

- 宽屏使用三栏；标准宽度收起右侧抽屉为按需侧滑；紧凑宽度使用单列，筛选改为弹出面板。断点沿用已验证的 editor-ui 合同：`<900` 为 Compact、`900–1279` 为 Standard、`>=1280` 为 Wide；descriptor 不携带布局尺寸。
- 标题、结果说明、元数据形成三级字重；同一 surface 的说明最多显示两行，完整内容在抽屉。
- 去除每个 module 的蓝色强调线、重复工具数和 `?`；列表只在选中/hover 时出现必要控制。
- 安全等级必须有文字/图标，不只依赖颜色。project-write/destructive 在主动作旁显示，不埋入 tooltip。
- 禁用原因显示在抽屉和动作附近的短句中；不为同一 surface 的每个 action 堆叠 HelpBox。
- 键盘顺序固定为：一级页 → 搜索 → 左侧导航/筛选 → surface → 主动作/更多 → 上下文抽屉。

上述布局是可逆的宿主层调整；不改变 descriptor 身份或业务窗口状态。

## 数据与状态流

```text
已安装 package / 项目 descriptor
        ↓ 既有 Catalog + provider 校验
surface + action + panel + reference
        ↓ presentation projection
首页(primary surface + panels) / 工具库(action surfaces) / 系统(diagnostics)
        ↓ selection context
右侧抽屉(用途、状态、资料、安全、开发信息)
        ↓ 既有 DashboardEntryExecutor
typed provider action
```

- Catalog 继续是唯一事实源；布局层只生成 projection，不复制 action 列表。
- 选择状态只在当前窗口内保存；不新增 EditorPrefs。
- 搜索索引加入 `contentType` 与 reference 标题，但继续匹配中文、usage、legacyKeywords 和稳定 ID。
- action state 仍只为当前可见 surface 延迟读取；右侧抽屉选中项可读取一次状态，不轮询隐藏目录。

## 中文与可访问性合同

- Dashboard 宿主固定文案集中在 `DashboardText.cs`；布局迁移不得在 `ZeroEngineDashboard.cs` 新增散落的用户可见字符串。
- POB 的 `displayName`、`surfaceDisplayName`、`surfaceActionLabel`、`description`、`usage`、`confirmation` 使用简体中文；稳定 ID、provider/action ID 不翻译。
- 每个可点击图标、仅图标按钮、主动作、“更多”、筛选和状态控件都有中文 tooltip。tooltip 说明结果、条件或影响，不原样重复 label。
- 禁用原因在动作附近和上下文抽屉以可见中文短句呈现；tooltip 只能补充，不能成为唯一说明。
- POB、Mod、TCE、Ability、Addressables 等项目词汇保持现有写法；其余宿主标签不得混用未解释的英文。

## 兼容、失败、回滚

- `contentType` 缺失按 `action` 处理；现有 schema v2 descriptor 无需一次性重写。
- schema v1 保持原行为并在工具库标记旧版入口；不进入首页。
- 稳定 FullId、provider/action、surface identity、panel provider 与执行协议不变。
- `ShowWorkspace(moduleId, panelId)`、现有 Open/Run API 与菜单转发行为不变；只改变 Dashboard 内的默认落点和 presentation。
- 新版布局出现回归时可回滚 Dashboard UI/projection 与可选字段解析，不回滚菜单收口、typed SPI 或 POB providers。
- catalog diagnostic、panel provider 错误或 reference 路径错误都 fail closed，并在系统页给出源路径和中文原因。
- 已确认当前 1 条 catalog diagnostic 是两项 project-write action 的 visibility 错误；必须将它们改为 `advanced` 并清零诊断，不得用布局隐藏问题。

## 影响范围

### ZeroEngine

- `com.zerogamestudio.zeroengine.dashboard/Editor/ZeroEngineDashboard.cs`
- `com.zerogamestudio.zeroengine.dashboard/Editor/DashboardText.cs`
- `com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/DashboardCatalog.cs`
- Dashboard UI/catalog/projection tests、README、CHANGELOG、package version
- 必要时在 `com.zerogamestudio.zeroengine.editor-ui` 增加通用三栏、抽屉或紧凑 surface row primitive；不得加入 POB 类型

### POB

- `Assets/Assets/_Scripts/_POB/Editor/ZeroEngineDashboardModule.json`：将“关键测试”“存档兼容测试”改为 advanced，修订“同步指令文档”的可见名称；“TCE 组件说明覆盖率”保留 action
- `Assets/Assets/_Scripts/_POB/Editor/Tools/WorkshopTools/ZeroEngineDashboardModule.json`：仅将“组件文档”声明为 reference
- 其余 12 份 descriptor 默认只做只读 reconciliation；除非当前 catalog diagnostic 的证据明确要求，否则不改
- POB route/source tests 与可见验收，不改业务 action 实现

## 实施顺序

1. 将 POB 主 descriptor 的“关键测试”“存档兼容测试”改为 advanced，清除已归因 diagnostic，恢复五个已声明 panel 的有效发现。
2. 为 Catalog 增加 `contentType` 与 presentation projection 纯逻辑测试。
3. 将一级页改为“首页 / 工具库 / 系统”，把现有 Workspace panel host 合并入首页。
4. 实现上下文抽屉，移除逐 module/逐 surface 重复的帮助与技术详情控件。
5. 将工具库改为 surface-first 紧凑列表和“主动作 + 更多”；保留现有安全执行器。
6. 迁移 POB 两份目标 descriptor，复核全部 14 份 descriptor 的 primary/advanced/maintenance 分布但不顺带改写。
7. 跑静态、Catalog、provider、POB route/source 与三档宽度可见验收；更新本文为 as-built。

## 验证

- 纯逻辑：projection 对 primary/panel/action/reference/system 的唯一归属；reference 安全限制；surface 计数；零结果分类隐藏；maintenance 搜索不泄露执行入口。
- 回归：现有 typed provider、稳定 ID、schema v1/v2、确认、availability、异常隔离测试继续通过。
- ZeroEngine 静态门：运行 `Tools/Tests/Test-ZeroEngineDashboardDescriptors.ps1` 与 `Tools/Tests/Test-ZeroEngineEditorUiContract.ps1`，退出码均为 0。
- ZeroEngine 最窄 Unity 矩阵：运行 `Tools/Tests/run-dashboard-editmode-tests.ps1` 的 `dashboard-only`、`dashboard-with-modules` lanes，输出 `PASS Dashboard EditMode matrix` 且失败数为 0。
- POB：14 descriptor、218 entry 全部仍被 Catalog 接受；107 个 effective surface 无孤儿、重复或错误 action 绑定。
- POB 最窄 EditMode：`POBEditorUiRouteCoverageTests` 与 `ZGSToolMenuPathsTests` 全部通过，特别是五 panel 创建、descriptor typed provider、旧菜单移除和稳定身份断言。
- 可见路线：从 `ZGS/工作台` 验证首页 panel、4 个 primary surface、工具库过滤/搜索/更多、reference 帮助、系统诊断。
- 宽度：760、960、1440 point 下无文字重叠、横向裁切、不可达按钮、空白固定栏或被遮挡抽屉。
- 文案：所有固定 label、tooltip、禁用原因、确认、帮助标题为简体中文；技术名仅在来源 chip 或开发信息中出现。
- 文案负向检查：宿主新增可见文本未散落在 `ZeroEngineDashboard.cs`；仅图标控件不存在空 tooltip；禁用原因不只存在于 tooltip。
- 安全：project-write/destructive 标识始终可见；maintenance 默认隐藏且不能从搜索直接执行。
- 最终：catalog diagnostic=0、五个 POB panel 均可在首页选择、Unity Console 0 error；EditorSettings 与其他生产配置哈希不变。

## 验收标准

1. 一级页只有“首页 / 工具库 / 系统”；旧“工作区”能力已合并到首页，没有第二套 panel 导航。
2. 普通入口打开首页总览，显示五个 POB panel 和 4 个有效 primary surface；`ShowWorkspace` 仍直接打开目标 panel；首页不显示 advanced/maintenance、raw module 清单或 raw tool 总数。
3. 工具库以 effective surface 为主单元，模块只作来源 chip；主动作严格遵循 `surfaceDefault`/稳定首项规则，其他动作经“更多”可达；冲突 default fail closed 并产生诊断。
4. 零结果分类不占固定导航行；计数统一为 surface 数，展开后才显示 action 数。
5. 文档路径/网址和 `contentType=reference` 不生成普通工具卡；reference 按 module 关联到上下文抽屉，搜索时进入独立“相关资料”分组，不与动作混排。
6. “组件文档”归 reference；“同步指令文档”作为明确的 project-write 生成工具；“TCE 组件说明覆盖率”作为 diagnostics action。
7. 用途、状态、使用方法、资料、安全与开发信息只在单一上下文抽屉展示；主列表不再重复 `?`、“详情”、技术路由和成组 HelpBox。
8. 760、960、1440 point 分别命中 Compact、Standard、Wide；三档均无重叠、截断、空白固定栏或不可达动作，Compact 能以单列完成同一操作。
9. 安全、禁用和 checked 状态有中文文字/图形表达，不只依赖颜色；maintenance 搜索不能绕过显式开启。
10. descriptor/provider 自动发现、POB 所有权、稳定 ID、surface identity、公共 Open/Run API 与 action 行为不变。
11. 当前 14 descriptor、218 entry、107 surface 全部通过 reconciliation；两个 ZeroEngine 静态门、两个最窄 Dashboard Unity lanes、POB `POBEditorUiRouteCoverageTests` 与 `ZGSToolMenuPathsTests` 均为 0 失败。
12. 当前 1 条 catalog diagnostic 被归因并清零；五个 POB panel 均能从首页选择且 `ShowWorkspace` 可直达，最终 Console 0 error，生产配置和任务外 pending 不变。
13. Dashboard 固定文案与所有交互 tooltip 为简体中文；新增宿主文案集中在 `DashboardText.cs`，技术 ID 不进入普通首屏，禁用原因不只依赖 tooltip。

## 实施结果（2026-08-12）

- 已完成首页、工具库、系统三个一级页；工作区 panel 合并进首页，工具库改为 surface-first 的主动作与“更多”，资料与动作通过 `contentType` 分流。
- 已完成统一中文宿主文案与 tooltip；`ZeroEngineDashboard.cs` 中仅保留菜单路径 `ZGS/工作台` 这一处中文常量，其余固定文案集中在 `DashboardText.cs`。
- 宽屏上下文栏仅在存在当前选择时出现，首页总览与工具库总览不再保留空白固定右栏。
- POB 两项 project-write 测试入口已改为 advanced；“组件文档”已标记为 reference；控制台文档生成动作已使用明确的写入型名称。
- 静态门通过：Dashboard descriptors=8；Editor UI contract descriptors=31、coverage=33、modules=31。
- Unity Dashboard 矩阵通过：dashboard-only 92 项中 91 通过、1 跳过、0 失败；dashboard-with-modules 189/189 通过。
- POB 最窄回归已通过：`POBEditorUiRouteCoverageTests` 7/7，`ZGSToolMenuPathsTests` 11/11，最终 Console 0 error。
- 760 point 真实窗口截图确认单列可读、无重叠且动作可达。Windows 200% 缩放下桌面取屏无法可靠隔离 960/1440 窗口，因此未把污染截图计作可见证据；Standard/Wide 由既有断点合同、Unity 矩阵和源码逐项核对覆盖。该验收回退已获用户授权。
- 实现与本地验证已完成；不可变的 Git/PR/Plastic 发布标识由终端 closeout report 记录，避免在同一提交中自引用尚未生成的标识。

## 自审

- 根因不是配色，而是 module-first 渲染和“一行承载所有信息”；本文改为 task/presentation-first，不靠继续增加折叠层缓解。
- 首页使用现有 primary 与 panels 自动生成，不引入收藏、使用频率或 POB 硬编码。
- 完整目录仍可达，advanced/maintenance 没有被删除或伪装成常用工具。
- reference 与 action 通过明确字段和安全约束区分，不按中文关键词猜测。
- reference 的 module 级关联、搜索分组和无 surface 时的入口均已定义，不再把“相关资料如何出现”留给实现决定。
- 主动作沿用现有 `surfaceDefault` 和稳定排序，未新增第二套优先级字段。
- POB panel 数已用主 descriptor 与 provider 测试交叉确认五个；实测 0 个的根因已由系统页精确归因，不再存在实现关键的未知基线。
- 旧执行、安全和兼容合同保持，回滚只涉及宿主 presentation。
- 本文经封闭式自审且用户已批准设计与实现；提交、发布、合并仍未授权。
