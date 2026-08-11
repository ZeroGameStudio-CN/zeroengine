# ZeroEngine Dashboard V3：可读工作区与 POB 面板归并

- 状态：Closed
- 最后更新：2026-08-11
- ZeroEngine 基线：`main@bbc45849a934f63e0068cf32022c8dd33b79cf9a`；POB 当前消费实现 `7cc164764c55b70792a4625fe0bb795001e851be`
- POB 基线：Dashboard rollout `/main cs:16813`；检查时工作区 `/main cs:16819`，远端 `cs:16820-16821` 与 Dashboard 无路径重叠
- 设计批准：Approved；用户于 2026-08-11 要求“审一下修订好后开干”
- 执行授权：Authorized；范围为本 Spec 的本地实现与验证，来源同上
- 终端操作授权：Authorized；用户于 2026-08-11 回复“授权继续”，范围限定为提交、推送、创建并完成 ZeroEngine PR，随后将 POB 19 个业务包统一 pin 到该 canonical merge commit 并精确 Plastic checkin；不得纳入其他 pending
- 关闭授权：Authorized；用户于 2026-08-11 再次回复“授权继续”，范围限定为通过独立 docs-only PR 持久化本 Spec 的最终关闭证据并清理本任务 Git 资源

## 结论

Dashboard V3 不再让“统一入口”和旧 `POBDashboardWindow` 两套界面并存。统一 Dashboard 保留“工具 / 系统”，新增“工作区”页；已安装模块或项目适配器可通过显式描述符贡献内嵌面板，由 Dashboard 自动加载、统一导航、帮助、安全提示和响应式布局。

POB 的旧 Odin Dashboard 拆成通用宿主可承载的 POB provider：运行概览、资源生命周期、拾取物诊断、卡池诊断和项目配置。旧菜单 `ZGS/工具/POB 仪表盘` 保留为兼容入口，但只打开同一个 ZeroEngine Dashboard 并定位到 POB 工作区，不再生成第二个业务窗口。

视觉整改先解决信息密度和布局合同，再调整装饰：默认界面只常驻名称、状态和主操作；说明、用法、技术路径和文档进入 tooltip、帮助抽屉或详情区。任何宽度下，标题、标签和按钮都不得占用同一不可收缩区域。

## 目标

1. 消除 Dashboard 与 POB 面板中的文字重叠、按钮挤压、截断后无完整信息和重复说明。
2. 建立“常驻信息 / hover 提示 / 帮助抽屉 / 技术详情”四层文案规则。
3. 让模块按安装状态自动贡献内嵌工作区面板，同时保持包边界和项目适配器归属。
4. 将旧 POBDashboard 的有效能力归入统一 Dashboard，并删除重复窗口、重复轮询和不符合当前测试规则的 Run All 操作。
5. 保留现有菜单、业务数据、Profile、Undo、dirty、安全确认和诊断语义。

## 非目标

- 不把 29 个领域编辑器全部嵌入 Dashboard；“工具”页仍负责打开最合适的独立业务窗口。
- 不把 POB 私有类型、缓存反射、卡池规则或运行时管理器放入 ZeroEngine 包。
- 不改玩家运行时 UI、Prefab、Scene、Addressables 内容、业务配置或序列化格式。
- 不引入 Odin、网页框架、USS 资源、字体、位图主题、运行时 Localization 包或新的 EditorPrefs 偏好。
- 不在本轮合并 Data Manager 与 Configurator、GraphView 编辑器与 Inspector 等跨状态/安全边界窗口。
- 不保留“运行所有 EditMode / PlayMode / 全部测试”三个旧按钮；项目验证继续走既有具名、窄范围入口。

## 已检查的当前行为

- `ZeroEngineDashboard.DrawSurface()` 把同一 `description` 同时作为标题 tooltip 和常驻副标题；模块标题又常驻模块说明，形成重复阅读层级。
- `EditorUiGUILayout.ActionRow()` 始终把文本列和任意数量按钮放进同一水平布局，没有动作区宽度协商或窄宽堆叠，因此长中文、多个动作和侧栏同时存在时会互相挤压。
- Dashboard 当前只有 `Compact/Standard` 两种宽度模式；宽度达到 900 point 后固定启用 196-point 侧栏，但内容行仍按单一横排绘制。
- 旧 `POBDashboardWindow` 是 1192 行 Odin 窗口，包含 `Managers / Caches / Pools / Pickups / Settings / Card Pool / Tests` 七个 Tab，大量固定比例 `HorizontalGroup` 在窄浮窗中会重叠。
- 旧窗口每 0.5 秒同时刷新卡池预览、Pickup 数据和 Play Mode 运行态；隐藏 Tab 也持续工作。
- 旧 Settings Tab 含项目写入与运行时开关，Tests Tab 可直接 Run All；它们与只读诊断混在同一层级，安全语义不清。
- 当前 Unity 中统一 Dashboard、公式中心和旧 POBDashboard 同时存在；用户主要看到的是置顶的旧窗口，造成“已统一但仍像两套系统”的结果。

## 信息架构

Dashboard 固定为三个一级页：

1. **工具**：已安装模块声明的窗口和命令；负责搜索、分组与启动，不承载业务编辑器内容。
2. **工作区**：已安装模块或项目适配器显式声明的内嵌面板；用于运行监控、诊断、轻量配置和连续操作。
3. **系统**：Dashboard 自身健康、描述符诊断、安装包和项目适配器状态。

“工作区”按贡献模块分组。标准宽度使用左侧面板导航，窄宽度使用顶部选择器；同一时刻只激活一个面板。未安装或未声明 provider 的模块不产生空分组。

“工具”与“工作区”共享搜索字符串，但分别过滤各自内容。“系统”继续搜索诊断、包和适配器。切页不得触发业务扫描、写盘或菜单执行。

## 文案与帮助层级

### 常驻信息

- 顶部：窗口名、当前页、错误/警告数量和刷新。
- 模块：模块显示名；说明只在首次空态或帮助抽屉显示，不在每组重复。
- 工具行：工作面名称、必要的 POB/安全/可用性 chip、动作按钮。
- 面板：面板名称、当前关键状态、主要控件和真实错误/警告。

### Hover tooltip

- 工具名称和动作：使用入口 `description`；不再把同一句话常驻绘制在行内。
- 截断文本：tooltip 必须提供完整原文。
- 图标、页签、搜索、清空、刷新、帮助、详情、安全 chip 和禁用控件：全部使用简体中文 tooltip。
- tooltip 解释“是什么/点击后做什么”，不承载必须阅读的危险确认。

### 帮助抽屉

Dashboard 右上角提供统一“帮助”入口；工具行和工作区面板也可用 `?` 打开同一抽屉。抽屉按当前选择显示：

- 用途：descriptor/provider 的短说明。
- 使用方法：可选 `usage` 文本，支持分行但不解析 Markdown。
- 可用条件与安全等级：来自现有 availability/safety，不复制另一套事实。
- 文档：模块现有 `documentationPath` / `documentationUrl`；不存在时不显示空按钮。
- 技术详情：`moduleId`、`FullId`、provider ID、菜单路径和来源路径，可选择复制。

帮助抽屉是 Dashboard 内可滚动区域，不创建独立 EditorWindow。没有 `usage` 时显示用途和文档即可，不生成“暂无说明”占位噪声。

### 描述符增量

保持 `schemaVersion: 1`，新增可选字段：

- entry `usage`：只在帮助抽屉显示的简体中文使用方法。
- module `panels[]`：显式声明工作区面板；字段为 `id`、`displayName`、`description`、可选 `usage`、`section`、`providerId`、`order`、`safety`、`availability`。

旧描述符无新字段时行为不变。面板显示文案只由描述符所有；provider 不重复提供标题或说明，避免两份来源漂移。

只含 `panels`、不含可见工具 entry 的模块可以出现在“工作区”，但不得因此进入“工具”模块列表；Catalog 分别构造 `VisibleToolModules` 与 `VisibleWorkspaceModules`，不复用“有 entry 才可见”的旧过滤结果。

## 响应式与可读性合同

### 页面布局

- `< 900` point：Compact；隐藏侧栏，使用顶部选择器；header 指标收敛为问题数；工具动作区换到下一行。
- `>= 900` point：Standard；保留 196-point 侧栏；工具行仅在实际可用宽度足够时横排，否则局部堆叠，不能仅依据整个窗口宽度判断。
- `>= 1280` point：Wide；工作区可将彼此独立的只读摘要卡排成两列，表格、长列表、配置和所有工具行仍保持单列。

900 延续现有 Dashboard 行为；1280 是可逆实施默认值，只影响只读摘要卡排版，不持久化。

### 行布局

- 公共 Editor UI 新增纯布局判定：输入内容可用宽度、动作数量和通过当前 `GUIStyle.CalcSize` 得到的动作文字宽度，输出 `Inline` 或 `Stacked`。
- Inline 模式先为动作区保留测量宽度，文本区获得剩余宽度；Stacked 模式动作位于下一行并右对齐，Compact 下主动作可扩展为整行。
- 不允许通过固定中文字符数、字符串裁剪或负间距“修复”重叠。
- 标题允许一行；空间不足时视觉截断但 tooltip 保留全文。说明默认不常驻，因此不与动作争夺高度。
- 状态和上下文 chip 可换行；项目写入和 destructive 标签不得因宽度不足被隐藏。
- 长表格在 Compact 下切换为主字段行 + 可展开详情，不继续使用四至六列固定比例。

### 视觉密度

- 一个工作面只使用一层容器；去掉“模块标题卡 → section 卡 → action 卡”的重复嵌套。
- 同组工具用分隔线，不为每行重复背景、说明和文档按钮。
- 空态、真实警告、错误和禁用原因可常驻；教程性文字进入帮助层。
- 颜色继续只来自 editor-ui 语义 palette；不新增渐变、阴影、大色块或非 Unity 字体。

## 通用工作区面板合同

### 依赖方向

- `com.zerogamestudio.zeroengine.editor-ui` 新增 Editor-only 的通用面板 SPI；它只定义生命周期、绘制上下文、动作安全和布局组件，不发现模块、不读取文件、不调用业务代码。
- `com.zerogamestudio.zeroengine.dashboard` 依赖 editor-ui，负责 descriptor、provider 绑定、导航、帮助、错误隔离和激活生命周期。
- 模块/项目 provider 只需引用 editor-ui；只有需要主动跳转 Dashboard 的兼容入口才引用 Dashboard Editor assembly。
- Dashboard 仍不得直接引用 POB 或任一可选业务模块。

### 发现与绑定

- provider 实现公共 `IEditorWorkspacePanelProvider`，并用稳定 provider ID 标记；一个 provider 可服务多个 descriptor panel，由 `CreatePanel(panelId)` 返回彼此独立的 panel 实例。descriptor 的 panel ID 传入 factory，不由 provider 再声明显示文案。
- Dashboard 只在 `RefreshCatalog()` 使用 Unity `TypeCache` 枚举该接口实现并构建 provider ID 索引；`OnGUI` 不做类型扫描。
- descriptor `panels[].providerId` 必须命中唯一 provider。缺失、重复、抽象类型、构造失败或 ID 不一致均产生可定位 diagnostic，并隐藏该面板；不得回退到猜测类型或任意反射构造。
- provider 只在用户选择对应面板时实例化。切换面板按顺序执行 deactivate/dispose，再 activate 新面板。

### 生命周期与副作用

- host 只绘制和 tick 当前激活面板；隐藏面板无轮询、Repaint、AssetDatabase、文件或业务刷新。
- provider 可声明刷新间隔；`0` 表示仅用户操作刷新。Play Mode 专用面板在 Edit Mode 显示可读禁用态，不实例化运行态数据源。
- Activate、Tick 和 Draw 不得写项目、执行菜单或改变业务状态。所有状态改变必须来自显式用户控件，并通过 host 的 action/toggle 合同声明 safety、确认文案和可用条件。
- 单个 provider 的 Activate/Tick/Draw 异常只隔离该面板，显示错误、复制详情和“重试加载”；不清空其他面板或破坏 Tools/System。
- 面板不得保存业务数据到 Dashboard；只允许窗口内瞬态选择、搜索和滚动状态。既有业务 EditorPrefs 继续由原所有者管理。

## POBDashboard 迁移

### 面板映射

| 旧 Tab | 新工作区 | 决定 |
| --- | --- | --- |
| Managers | 运行概览 | 保留场景、房间、战斗和实体摘要；仅 Play Mode 自动刷新 |
| Caches + Pools | 资源生命周期 | 合并缓存与对象池，因为数据源和排障流程连续；长列表使用主字段 + 详情 |
| Pickups | 拾取物诊断 | 保留账本、潜在来源、输出与清空；清空继续使用 destructive 确认 |
| Card Pool | 卡池诊断 | 保留筛选、规则和候选预览；改为手动刷新，不再后台每 0.5 秒重算 |
| Settings | 项目配置 | 保留无尽构建、Define、本地覆盖和关卡分支；每个项目写入控件保留 Undo/dirty 并显示中文安全确认 |
| Tests | 不迁移为面板 | 删除三个 Run All；在“工具”页声明既有“关键测试”和“存档兼容测试”窄范围命令，另提供打开 Test Runner 的导航入口 |

缓存私有字段反射、POB manager、Pickup/CardPool 数据模型、Endless 配置和 LevelConfig 写入全部留在 POB provider；ZeroEngine 只看通用 panel/action/status 模型。

### 兼容入口

- `ZGS/工具/POB 仪表盘` 保持不变，调用 Dashboard 公共导航 API，打开唯一 `ZeroEngineDashboard` 实例并选择“工作区 / POB / 运行概览”。
- `POBDashboardWindow` 公共类型保留一个发布周期作为 `[Obsolete]` 转发 facade；被旧 Unity layout 或外部 `GetWindow<POBDashboardWindow>` 恢复时通过 `EditorApplication.delayCall` 导航统一 Dashboard 并关闭自身，避免在 `OnEnable` 重入窗口生命周期。facade 不再持有业务字段、Odin attribute、update 订阅或 TestRunnerApi。
- 下一发布周期是否删除 facade 另行决策；本轮不破坏编译期类型。

### POB 源码拆分

- `Assets/Assets/_Scripts/_POB/Editor/Tools/Dashboard/AddressableDebugWindow.cs`：缩减为旧类型/菜单兼容 facade。
- 新增 `Assets/Assets/_Scripts/_POB/Editor/Tools/Dashboard/POBDashboardPanelProvider.cs`：provider 与五个面板组合。
- 按复杂度拆出只读数据源/行模型，但不移动 runtime 业务类型、不形成新的通用抽象。
- `Assets/Assets/_Scripts/_POB/Editor/POB.Editor.asmdef`：增加 `ZeroEngine.EditorUI.Editor` 与 `ZeroEngine.Dashboard.Editor` 引用。
- `Assets/Assets/_Scripts/_POB/Editor/ZeroEngineDashboardModule.json`：增加 POB panels 和三个验证工具入口；不产生 POB 专属 Tools 模块 Tab。
- 三个验证入口精确为既有“关键测试”“存档兼容测试”和 Unity Test Runner 导航；前两项 `availability=edit-mode`、`safety=project-write` 并显示中文确认，不能伪装成只读导航。

### 未发布上游的本地联调

- `POB.Editor.asmdef` 增加 Dashboard package `>=3.2.0` 的 version define `ZEROENGINE_DASHBOARD_WORKSPACE_V3`。provider 与新 facade 只在该 define 下编译；旧 pin 下继续编译现有 Dashboard，避免未发布期间把 POB 留在不可编译状态。
- 本地联调可在受控任务中把 manifest/lock 成对切到 ZeroEngine Spec 工作树的精确 `file:` 包；验证结束必须成对恢复原 Git pin，并确认 lock 无 local source/path。
- 最终 canonical 可用并准备 POB Plastic checkin 时，先 pin 到新 commit，再移除仅用于过渡的旧实现分支；最终 changeset 不保留旧 Odin 业务窗、条件回退或 `file:`。

## 影响范围

### ZeroEngine

- `com.zerogamestudio.zeroengine.editor-ui/Editor/EditorUiGUILayout.cs`
- `com.zerogamestudio.zeroengine.editor-ui/Editor/EditorUiStyles.cs`
- `com.zerogamestudio.zeroengine.editor-ui/Editor/EditorUiTokens.cs`
- 新增 `com.zerogamestudio.zeroengine.editor-ui/Editor/Workspace/**` 及配对 `.meta`
- `com.zerogamestudio.zeroengine.dashboard/Editor/ZeroEngineDashboard.cs`
- `com.zerogamestudio.zeroengine.dashboard/Editor/DashboardText.cs`
- `com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/DashboardCatalog.cs`
- 新增 `com.zerogamestudio.zeroengine.dashboard/Editor/Workspace/**` 及配对 `.meta`
- Dashboard/editor-ui package、README、CHANGELOG、coverage、静态门和最窄 Editor tests

版本默认：editor-ui `1.2.0`、Dashboard `3.2.0`。两者仍为 Editor-only；Dashboard 只依赖同提交 editor-ui，不新增可选业务包依赖。

### POB

- 上述 POB Dashboard facade/provider、`POB.Editor.asmdef`、项目 descriptor
- `Assets/Assets/_Scripts/_POB/Tests/Editor/ZeroEngineDashboard/**` 的 descriptor/provider/兼容入口覆盖
- 最终成对更新 `Packages/manifest.json` 与 `Packages/packages-lock.json`

其他 POB pending、ProjectSettings、生产资产、Prefab、Scene、公式 Profile、Data Manager 和 Configurator 业务逻辑不在范围内。

## 兼容、失败与回滚

- 旧 descriptor、旧工具 entry、`FullId`、mount、replaces、菜单、安全和执行器完全兼容；`usage`/`panels` 缺失时无行为变化。
- Workspace provider 失败只使对应面板不可用；Tools/System 仍可使用，System 显示 provider ID、描述符来源和异常摘要。
- POB 项目配置写入继续使用原 Undo/dirty 和确认；迁移不得改变值、默认值、文件路径或写入时机。
- 若工作区 host 发生严重回归，POB manifest/lock 成对回到 `7cc1647…`，同时恢复旧 facade/provider/asmdef/descriptor；无业务数据迁移。
- 不重写 Git/Plastic 历史，不自动恢复用户配置，不以删除用户 layout 作为回滚手段。

## 实施顺序

1. 在 editor-ui 实现可测的 ActionRow 布局判定、帮助/详情组件和通用 workspace SPI。
2. 扩展 Dashboard catalog 的 `usage/panels` 解析、provider 绑定、失败诊断和纯逻辑测试。
3. 将 Dashboard shell 改为“工具 / 工作区 / 系统”，移除工具行常驻重复说明并接入帮助抽屉。
4. 在 ZeroEngine visual gallery 验证长中文、长英文、1-3 个动作、chip 换行和 provider 错误态。
5. 在 POB 新增 provider，逐块迁移五个工作区；删除隐藏 Tab 轮询和 Run All 路线，保留兼容 facade。
6. 跑 ZeroEngine 静态门/最窄测试，再按 required Unity 路由验证 POB 菜单跳转、面板内容、Console 和配置哈希。
7. 只有获得终端操作授权后，才进入 ZeroEngine PR/canonical 发布和 POB 同 commit pin/Plastic 精确提交。

## 验证

### 自动门

- editor-ui：布局纯函数覆盖 420/760/960/1440 point、长中文/英文、1/2/3 个动作；断言文本区与动作区矩形不相交、动作可达、Stacked/Inline 选择稳定。
- editor-ui gallery：Compact/Standard/Wide、chip 换行、帮助抽屉、详情、空/警告/错误/禁用和长路径可复制。
- Dashboard catalog：旧 descriptor、`usage`、合法 panel、缺 provider、重复 provider、构造失败、ID 冲突、排序、搜索和 diagnostic 隔离。
- Dashboard lifecycle：只有选中 provider 被实例化/tick；切换/关闭必定 deactivate/dispose；异常不影响其他页面。
- 静态副作用门：editor-ui SPI 无 AssetDatabase/PackageManager/文件/网络/菜单；Dashboard `OnGUI` 无 TypeCache/包/Assets 扫描；provider Activate/Tick 不含项目写入入口。
- POB：五面板映射完整；旧七 Tab attribute 与三条 Run All 方法消失；facade 不再继承 `OdinEditorWindow`，不订阅 `EditorApplication.update`；项目写入仍有 Undo/dirty/确认。
- POB route coverage：旧菜单指向唯一 Dashboard host；三个验证入口只绑定既有具名菜单，不通过 TestRunnerApi 构造 Run All。
- 过渡编译：旧 `3.1.1` pin 下 version define 不存在且 POB 仍可编译；本地 `3.2.0` 包下 define 生效并只编译新 provider/facade。恢复 manifest/lock 后无 `file:` 残留。

### 可见路线

- 760、960、1440 point 各检查工具、工作区、系统；使用最长当前中文标签、三个动作、POB chip、项目写入 chip 和长来源路径，无重叠、遮挡、不可达按钮或无提示截断。
- 打开 `ZeroEngine/Dashboard` 后可进入 POB 工作区；打开 `ZGS/工具/POB 仪表盘` 后仍是同一个 Dashboard 实例并定位到运行概览，不出现第二个 `POBDashboard Window`。
- Play Mode 下只有当前 POB 运行面板按 0.5 秒刷新；切到其他模块/页面后停止刷新。卡池只在显式刷新后重建。
- POB 项目配置值、ProjectSettings/生产资产哈希与任务外 Plastic pending 在验证前后不变；任何新 `outcome_unknown` 立即停止且不重放。
- 最终 Console 0 error；不使用 `execute_code` 验证 catalog 或窗口。

## 验收标准

1. 760、960、1440 point 和最长现有中文/英文文案下，所有 Dashboard 工具行、面板标题、chip、表格和按钮无重叠、遮挡或不可达操作。
2. 工具行不再常驻重复 `description`；默认只显示名称、必要状态和动作，完整说明可从 hover 或帮助抽屉获得。
3. 当前模块/工具/面板的用途、使用方法、安全、文档和技术详情均有单一可发现位置；不存在无内容的文档/帮助占位。
4. 已安装模块可通过 descriptor `panels` + 唯一 provider ID 自动出现在“工作区”，Dashboard 不硬编码 POB 或可选模块类型。
5. provider 缺失、重复或异常时只隔离对应面板，并在“系统”显示可定位诊断；Tools/System 不失效。
6. 同一时刻只实例化、绘制和 tick 当前面板；隐藏 POB 面板无 0.5 秒后台刷新，卡池不自动重算。
7. POB 的 Managers、Caches/Pools、Pickups、Card Pool、Settings 分别归入五个新工作区，Caches/Pools 合并且业务结果不变。
8. 旧 Tests Tab 与三条 Run All 路线删除；统一 Dashboard 只提供既有关键测试、存档兼容测试和 Test Runner 导航入口。
9. `ZGS/工具/POB 仪表盘` 与旧 `POBDashboardWindow` 类型兼容入口都定位到唯一 ZeroEngine Dashboard；正常使用不再出现第二个 POB 业务窗口。
10. POB provider 保留原 manager/cache/pool/pickup/card/config 数据来源、Undo、dirty、确认、EditorPrefs 和 Play/Edit Mode 语义；ZeroEngine 不包含 POB 业务代码。
11. 项目写入和 destructive 操作始终有中文文字安全标识与确认，不因紧凑布局或帮助收纳被隐藏或弱化。
12. Dashboard 固定文案、POB descriptor/provider label、tooltip、帮助与确认均为简体中文；技术 ID、路径、品牌缩写和业务对象名保持原值。
13. 旧 schema v1 描述符及现有 28 条上游 route/POB Formula surface 保持兼容；Formula Studio、Data Manager 与 Configurator 不因 V3 合并或改入口。
14. ZeroEngine 静态门、Dashboard/editor-ui 最窄测试、POB provider/route 测试和可见窗口路线通过，最终 Console 0 error，生产配置与任务外 pending 不变。
15. 发布时 editor-ui `1.2.0` 与 Dashboard `3.2.0` 同一 canonical 落地；POB 19 个业务 pin 同 commit、manifest/lock 无业务 `file:`，独立 unity-mcp-control pin 不变。

## As-Built（2026-08-11）

- editor-ui 已提供 Compact/Standard/Wide 响应式合同、动作行折叠策略和无业务依赖的工作区 SPI；Dashboard 已支持 descriptor `panels`、唯一 provider 注册、延迟构造、单面板生命周期、失败隔离、工作区导航和中文帮助抽屉。
- POB 已迁移为运行概览、资源生命周期、拾取物诊断、卡池诊断、项目配置五个独立面板；缓存与对象池归并到资源生命周期，旧 Odin 窗口仅保留延迟转发 facade，旧菜单复用唯一 Dashboard。
- 视觉验收实际覆盖约 760 point 紧凑布局和约 1169 point 宽布局；自动布局/画廊回归覆盖 420、760、960、1440 point、最长中英文文案及 1–3 个动作。紧凑布局使用下拉选择，宽布局使用左侧导航，未见文字重叠、遮挡或不可达按钮。
- ZeroEngine 静态门通过：Dashboard descriptors=8；Editor UI descriptors=28、coverage=30、modules=28。editor-ui 六个隔离 lane 全部通过；Dashboard `dashboard-only` 68 个测试中 67 passed、1 ignored，`dashboard-with-modules` 165/165 passed。
- Dashboard 默认矩阵的非必需 `modules-only` 广域 lane 另有 1 个既有 Data Toolkit 测试在 `-nographics` 下因无图形设备失败（719 passed、1 failed、2 skipped）；失败不经过 Dashboard/editor-ui 改动路径，本次未扩 scope 修改该模块。
- POB 本地 3.2.0 过渡态的 provider/route 两条精确 EditMode 测试各 1/1 passed；恢复旧 Git pin 后 19 包同 commit 合同测试 1/1 passed，最终 Console 0 error。旧菜单实测将同一 Dashboard 定位到运行概览，不产生第二业务窗口。
- ZeroEngine 实现已通过 PR #39 合并到 canonical main：merge commit `6f9ee5d3258a4eaf53fdffbd273a3e27e08482da`，tree `a81014fc5aead5fa6fd6d68384b6e803e2d925df`。五条必需 Unity CI 全绿；同提交上一条与本功能无关的 Toast 时序 lane 重跑后通过，未为其改代码。
- POB 19 个业务包已统一 pin 到上述 canonical merge commit，`manifest.json` 与 `packages-lock.json` 成对落地且无业务 `file:`，独立 unity-mcp-control pin 未改变。最终三条精确 EditMode 验证 3/3 passed，Console 0 error。
- POB 生产配置保持不变：`EditorSettings.asset` SHA-256 `ee88f9bf514d391131f32928955607a1f1f3b7b7f5d0d36c477bdb2bda184b33`，`LevelConfig.asset` `5f7647450579e8724deea0a0c616e15be09295a4f3d601052145a2c91fc7f68c`，`EndlessBuildSettings.asset` `d5921ae3c2ebd88c11a62948d13e069e2612f820bd5212abb6930abda49e4326`。
- POB 精确提交为 Plastic `cs:16824`；changeset 实际仅含本任务 10 个目标文件，其他 pending 未纳入。结束时本任务 workspace task/claims 均为 0，Unity ownership 已释放，10 个目标路径 pending 为 0。

## 自审记录

- 架构：通用 host/SPI 留在 ZeroEngine，POB 数据与动作留在项目 provider；Dashboard 不获得可选模块编译依赖。
- 可读性：根因按布局和信息层处理，不以缩小字体、裁字或增加窗口最小宽度掩盖。
- 安全：工作区只激活一个 provider；所有写入来自显式用户动作，旧 Run All 删除，provider 失败按面板隔离。
- 兼容：旧菜单、旧类型、旧 descriptor 和现有工具 route 保留；新增字段全部可选。
- 迁移：旧 POB 大窗只保留一期 facade，业务能力逐项映射，无数据迁移和隐式配置恢复。
- 验证：自动几何/生命周期门覆盖结构，可见路线覆盖真实 Unity 排版；截图不替代行为测试。

终审修订补齐四个实现风险：多 panel provider factory、仅 panel 模块的独立可见性、旧窗口延迟转发，以及 canonical 未发布前的 version-define 双基线编译。修订后源码对照自审 Critical=0、Important=0。用户已批准设计、本地实现与验证，并于 2026-08-11 授权按既定精确范围完成 PR、push、canonical 发布和 POB Plastic checkin。

## 关闭记录

- ZeroEngine：PR #39 已合并；canonical merge commit 与 tree 分别为 `6f9ee5d3258a4eaf53fdffbd273a3e27e08482da`、`a81014fc5aead5fa6fd6d68384b6e803e2d925df`。
- POB：Plastic `cs:16824` 已精确提交 `Packages/manifest.json`、`Packages/packages-lock.json`、`POB.Editor.asmdef`、`AddressableDebugWindow.cs`、`ZeroEngineDashboardModule.json`、`POB.EditorUiCoverage.Tests.Editor.asmdef`、`POBEditorUiRouteCoverage.json`、`POBEditorUiRouteCoverageTests.cs`、`POBDashboardPanelProvider.cs` 及其 `.meta`，共 10 个文件。
- 验收：PR 必需检查通过；POB 三条目标测试 3/3 passed、Console 0 error、生产配置哈希不变、19 个业务 pin 一致且无 `file:`。内部 Editor 工具的可见路线与确定性自动测试已覆盖全部验收项，因此独立人工验收与 acceptance card 均为 N/A。
- 交接：POB AfterCheckin 服务 CI 已按项目 helper 异步触发，不是同步关闭门；本任务未等待其服务端结果。无发布、迁移或回滚待办。
- 清洁度：POB 本任务 task/claims/Unity ownership 已释放，目标 pending 为 0；本 docs-only closeout 不触碰 POB。关闭 PR 合并后，本任务 Git 分支与隔离工作树必须清理。
