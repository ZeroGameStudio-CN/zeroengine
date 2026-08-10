# ZeroEngine Dashboard V2：信息架构、可读性与面板合并

- 状态：Complete
- 最后更新：2026-08-11
- 实现基线：ZeroEngine canonical `7cc164764c55b70792a4625fe0bb795001e851be`（tree `e39c4d18c648843858851168d3f7491d1894b0f7`）；POB `/main cs:16813`
- 设计批准：Approved；用户于 2026-08-10 明确将本轮审查合同改为当前会话单审
- 执行授权：Authorized；覆盖本 spec、简体中文 label/tooltip 增量及 POB 消费工程 rollout
- 终端操作授权：Authorized；用户于 2026-08-10 回复“继续”授权中文化增量 PR/merge、最终 canonical pin 与精确 Plastic checkin

## 结论

第二轮不再继续“给旧布局套统一皮肤”，而是收紧信息架构：Dashboard 改为紧凑的工具工作区，将 `Installed` 与 `Diagnostics` 合为 `System`，把技术细节移入按需展开区，并用描述符的可选展示元数据组织长列表和同一工作面的多个入口。

本轮真正合并一个高置信度工作面：`Formula Catalog` 与 `Formula Workbench` 合为单实例 `Formula Studio`，原菜单、Profile 与公开打开方法继续有效并切换到对应页签。POB Formula 两条适配路线仍注入 POB Profile，但 Dashboard 只显示一个 Formula Studio 工作面和一个轻量 `POB` 上下文标记，不出现独立 POB 模块页。

`Data Manager` 与 `Configurator` 不合成一个窗口：前者是通用资产浏览/编辑，后者包含生成、Apply 与 AssetDatabase 刷新，安全等级和包所有权不同；本轮仍由各自宿主模块展示。Behavior Tree、Tutorial、Dialog、Mod 等成对窗口是后续候选，不在没有领域专项验收的本轮强行合并。

本 spec 是 `2026-08-10-zeroengine-module-panel-visual-unification.md` 的 V2 增量；仅在明确列出的布局与合并决策上替代其“不得合并窗口”非目标，其余依赖、安全、发布和配置保护合同继续生效。

## 目标与非目标

### 目标

1. 默认视图先呈现“我能做什么”，不先呈现包名、模块 ID、菜单路径和重复说明。
2. 760、960、1440 point 宽度下层级稳定，窄窗口无重叠、横向截断或不可达主操作。
3. 自动发现、挂载、替代和安全元数据继续由已安装模块及项目描述符驱动。
4. 同一工作面的多个入口可以在不破坏 `FullId`、`replaces` 和旧菜单的前提下合并展示。
5. Formula Catalog/Workbench 成为一个真实窗口实例，而不只是两个外观相似的启动卡片。

### 非目标

- 不把所有业务编辑器嵌入 Dashboard，也不引入 Odin、网页框架、位图主题或运行时依赖。
- 不改业务数据、序列化、Profile 内容、扫描/写入时机、Undo、dirty、确认或 EditorPrefs 语义。
- 不把 Data Manager 与 Configurator 合成窗口，不把不同安全等级的操作放进同一主操作区。
- 不在本轮合并 Behavior Tree Editor/Viewer、Tutorial Editor/Graph、Dialog Graph/Node Inspector、Mod Creator/Validator；只记录候选，不产生兼容性承诺。
- 不翻译 29 个被启动业务编辑器各自的内部领域 UI；本轮语言范围是 Dashboard shell、正式模块描述符和唯一合并工作面 Formula Studio。

## 已确认问题

基线源码显示：

- Dashboard 顶部是大标题卡片、三块指标和第二行导航；在 460-point 最小高度下占用过多首屏空间。
- `Tools / Installed / Diagnostics` 使用固定 360-point 工具栏，左栏固定 224 point；窄宽度仍按宽屏结构绘制。
- 模块标题卡与每个工具卡连续嵌套，入口只需一次 Open/Run，却长期显示 `moduleId`、`menuPath`、category 和 safety，扫描成本高。
- `Installed` 页面显示搜索框，但包列表没有使用 `_search` 过滤，是明确的交互缺陷。
- legacy 描述符把 23 个入口放在一个 `ZeroEngine Legacy` 长列表中，没有用户任务分组。
- POB 挂载入口虽然已归位到上游宿主，主标题仍反复出现 `POB ...`，并额外显示 `ADAPTER · ...`，上下文信息压过工具身份。
- Formula Catalog 已提供“打开工作台”，两窗口共享同一 Active Profile、公式对象和连续工作流，属于重复表面。
- 第一轮公共 `Header` 仍是带强调线的大卡片；多数窗口只接入标题外壳，未获得紧凑工具栏、行式动作、受限正文宽度或响应式布局。

## Dashboard V2

### 页面结构

Dashboard 只保留两个一级页：

1. `Tools`：模块导航、搜索、按工作流分组的工作面与动作。
2. `System`：健康摘要、诊断、已安装包和项目适配器。

`Installed` 与 `Diagnostics` 合并到 `System`。默认先显示错误/警告和健康状态；包清单使用紧凑行并置于次级折叠区。System 搜索必须同时过滤诊断、包名、版本、模块和项目适配器，不再出现无效搜索框。

### 紧凑布局

- Header 改为单个紧凑工具栏：左侧标题与一句说明，中间内联 `modules / tools / issues`，右侧刷新；不再用大卡片包裹指标。
- `Tools/System` 页签与当前页搜索位于同一导航行。搜索提供占位提示和清除图标；切页后保留字符串，但按当前页语义过滤。
- `position.width >= 900` 时使用 196-point 模块栏与单列工作面列表；`position.width < 900` 时模块栏替换为顶部 popup，内容占满宽度。该断点是可逆实施默认值，不写入 EditorPrefs。
- 模块栏只显示用户名、工具数和分组，不显示 package ID。技术 ID、来源路径和 menu path 默认隐藏在 `Details` disclosure 中。
- 模块内容使用一个模块标题和紧凑 `ActionRow` 列表；同一组内用分隔线，不为每个动作再套完整 help-box 卡片。
- 每行固定层级为：名称与一句说明、上下文/安全 chip、右侧主动作。`navigation` 不显示冗余安全 chip；`read-only/project-write/destructive` 必须显示文字，不只靠颜色。
- legacy 模块用户名改为 `Core Tools`，并通过描述符 `section` 分组。精确映射为：
  - `Content`：Ability、Achievement、Crafting、Equipment、Inventory、Loot Table、Quest、Shop。
  - `Logic & Flow`：Behavior Tree Graph/Viewer、Dialog Graph/Node Inspector、Talent Tree、Tutorial Editor/Graph。
  - `World & Meta`：Calendar、Notification、Relationship、Settings。
  - `Diagnostics & Modding`：Global Search、Translation Checker、Mod Creator/Validator。
  内部 `moduleId`、entry id 和原 order 不变。
- 项目适配器不成为独立模块页；其入口使用宿主模块和通用工作面名称，`POB` 只作为小型 context chip。无项目适配器时不保留空位置。

### 描述符增量

保持 `schemaVersion: 1`，新增可选字段并向后兼容：

- entry `section`：宿主模块内的用户任务分组；空值归入 `General`。
- entry `surfaceId`：同一宿主模块内的稳定工作面键；空值使用完整 entry ID，避免挂载来源之间误合并。
- entry `surfaceDisplayName`：合并行标题；同一 surface 的非空值必须一致。
- entry `surfaceActionLabel`：同一 surface 内每条原入口的短动作名；空值使用 `Open/Run`。
- entry `surfaceDefault`：至多一条为 true；未声明时按现有 order 取首条。

Catalog 不改 `FullId`、mount、replacement 或执行对象，只构造只读 `DashboardSurface` 投影供 UI 使用。surface 成员必须属于同一显示宿主、同一 `kind`、同一 availability 和同一 safety；冲突产生明确 diagnostic，并回退为独立行，不能静默丢入口。

Formula 上游和 POB adapter 的 Catalog/Workbench 两条 entry 保留原 `FullId` 与 `replaces`，但共享 `surfaceId=formula-studio`；Dashboard 因此显示一行 `Formula Studio`，提供 `Catalog` 与 `Workbench` 两个次级动作。POB descriptor 的主显示名去掉重复 `POB` 前缀，context chip 仍明确当前适配器。

### 简体中文 label 与 tooltip

- Dashboard 固定为简体中文编辑器界面；不增加语言选择器、运行时 Localization 包或 EditorPrefs 状态。`ZeroEngine`、`ZGS`、`TCE`、`UI`、`POB` 等品牌/约定缩写可保留。
- 8 份正式上游描述符和 2 份 POB adapter 描述符的模块名、入口名、说明、section、surface 名、动作名及确认文案使用简体中文。第三方或未来项目描述符仍按其原文显示，不由 Dashboard 猜测翻译。
- 模块 ID、entry ID、`surfaceId`、category/safety 枚举、菜单路径、包名、诊断码和来源路径保持原值；搜索继续匹配中文展示文案以及这些技术字段。
- 所有 Dashboard 可操作控件提供简体中文 tooltip：一级页签、刷新、搜索/清空、模块选择、文档/网页、详情 disclosure、surface 动作以及 System 折叠项。surface 动作 tooltip 直接使用对应 entry 的描述，避免维护第二份含义不同的说明。
- Formula Studio 的窗口标题、页签、固定 label、按钮及其 tooltip 使用简体中文；Profile 名、资产名、路径、JSON/Markdown 和公式业务数据保持原值。
- tooltip 通过 `GUIContent` 和公共 Editor UI 控件传递，不以仅在代码注释中存在的文本充数；危险/写入动作的中文确认与安全 chip 不得因翻译弱化。

## Formula Studio 合并

`FormulaWorkbenchWindow` 成为唯一正常展示的窗口宿主，新增 `Catalog` 与 `Workbench` 两个页签：

- 原 `ZeroEngine/Formula/Formula Catalog`、`ZeroEngine/Formula/Formula Workbench`、POB 两条菜单保持不变。
- `FormulaCatalogWindow.OpenWithProfile(profile)` 改为兼容入口，打开同一个 `FormulaWorkbenchWindow` 并选择 Catalog。
- `FormulaWorkbenchWindow.OpenWithProfile(profile)` 选择 Workbench；`OpenWithFormula(profile, formula)` 选择 Workbench 并选中公式。
- Catalog 行的“Open Workbench”在同一窗口切页并传递所选公式，不创建第二个 Editor tab。
- `FormulaCatalogWindow` 公共类型暂时保留，避免编译期 API 消失；生产菜单和 Dashboard 不再创建其实例。直接外部 `GetWindow<FormulaCatalogWindow>` 不作为受支持入口，并在 README 标为 deprecated。
- Catalog 扫描、筛选、报告和生成逻辑移入非窗口 pane/controller；Workbench evaluation/session、批量预览和曲线逻辑保持原对象与调用顺序。
- Active Profile 仍由 registry 决定；POB 打开后窗口标题使用通用 `Formula Studio`，Profile 名以 context chip/副标题显示，不新增 POB 专属一级页签。
- 当前页签与现有 formula 引用继续由 EditorWindow 序列化；Catalog 行、筛选、scroll 和所有 report 为可重建/瞬态状态。任何菜单调用都在 `Show()` 前显式设置目标页签，不能被上次状态覆盖。
- domain reload 后仍清空 transient report 对象和字符串，保留 hotfix `bef4871` 的行为。

## 公共 Editor UI V2

扩展 `com.zerogamestudio.zeroengine.editor-ui`，不改变其 Editor-only 依赖方向：

- `CompactHeader`：无外层大卡片的标题、上下文、指标与工具栏组合。
- `ActionRow` / `ContextChip` / `SafetyChip` / `Disclosure`：统一行式入口和技术详情。
- `ConstrainedContent`：仅供表单与报告选择使用的最大正文宽度；Data Toolkit、GraphView、目录和三栏工作台不得套用。
- `ResponsiveMode(width)`：纯函数返回 compact/standard，供 Dashboard 和测试使用，不读取或写入持久状态。
- 现有 `Header` 改为更紧凑的 section header，消除重复 accent-line + help-box；业务窗口可渐进采用 CompactHeader，但不得再次定义自己的顶层卡片颜色。

颜色继续来自现有语义 palette。美化重点是层级、密度、对齐、留白和信息隐藏，不增加渐变、阴影、品牌图、非 Unity 字体或大面积高饱和背景。

## 合并边界与后续候选

| 工作面 | 本轮决定 | 原因 |
| --- | --- | --- |
| Installed + Diagnostics | 合并为 System | 同属 Dashboard 自身健康与安装状态，查询源相同 |
| Formula Catalog + Workbench | 合并为 Formula Studio | 同 Profile、同对象、连续浏览→验证流程，已有直接跳转 |
| Data Manager + Configurator | 不合并 | 通用浏览与项目写入管线安全级别/所有权不同 |
| TCE Graph + Catalog regenerate | 不合并 | project-write 维护动作不能折叠进普通导航 surface |
| Behavior Tree Editor + Viewer | 后续候选 | 需先确认只读查看、运行态观察与图编辑状态隔离 |
| Tutorial Editor + Graph | 后续候选 | 表单与图的选择/保存/Undo 需要专项迁移测试 |
| Dialog Graph + Node Inspector | 后续候选 | Inspector 可嵌入，但涉及 GraphView 选中生命周期 |
| Mod Creator + Validator | 后续候选 | 可形成 Mod Studio，但创建与验证失败恢复需单独设计 |

Ability、Achievement、Inventory、Quest、Shop、Settings 等独立领域编辑器不合并成巨型窗口，只通过 Dashboard section 归组。

## 影响范围

### ZeroEngine

- `com.zerogamestudio.zeroengine.dashboard/Editor/ZeroEngineDashboard.cs`
- `com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/DashboardCatalog.cs`
- Dashboard descriptor、README、CHANGELOG 与 Editor tests
- `com.zerogamestudio.zeroengine.editor-ui/Editor/EditorUiGUILayout.cs`
- `EditorUiStyles.cs`、`EditorUiTokens.cs`、gallery/contract tests
- `com.zerogamestudio.zeroengine.formula/Editor/FormulaCatalogWindow.cs`
- `FormulaWorkbenchWindow.cs` 及新增非窗口 Catalog pane/controller
- Formula descriptor、README、CHANGELOG 与现有 Catalog/Workbench tests
- legacy `com.zerogamestudio.zeroengine/Editor/ZeroEngineDashboardModule.json`
- 既有 Dashboard/editor-ui test wrappers；不新增全项目 Run All

V2 主变更版本为 Dashboard `3.1.0`、editor-ui `1.1.0`、legacy `2.1.0`、Formula `0.5.0`。简体中文增量对实际改动包执行 patch bump：Dashboard `3.1.1`、editor-ui `1.1.1`、Formula `0.5.1`、legacy `2.1.1`，以及 analytics/config-pipeline/data-toolkit/feedback/TCE/UI 各自当前版本的 patch；对应 package.json、README/CHANGELOG 与静态版本门同批更新，不改变既有 editor-ui 直接 pin 合同。

### POB

- `Packages/com.zerogamestudio.pob.formula/Editor/ZeroEngineDashboardModule.json`
- `Assets/Assets/_Scripts/_POB/Editor/ZeroEngineDashboardModule.json`
- 仅在入口类型/显示合同变化所需时更新 POB Dashboard route coverage test/fixture
- 最终才成对更新 `Packages/manifest.json` 与 `Packages/packages-lock.json`

POB Data Manager、Configurator、Formula Profile 和生产配置逻辑不改；其他 pending 不在范围内。

## 行为、安全与兼容不变量

- 现有菜单路径、entry `FullId`、mount、replaces、order、confirmation、availability 与安全枚举保持。
- 描述符新字段全部可选；旧描述符视觉退化为 General + 单 entry surface，不报错。
- surface 只合并显示与导航，不合并 command 执行、确认或诊断结果；运行态错误仍绑定原 `FullId`。
- 一个 surface 内某条 entry 失败时，只禁用/标错该动作；其他成员不得因共享行而被连带禁用。
- Formula 两页共享窗口但不共享不应共享的 transient result；切页不触发扫描、评估、写盘或 AssetDatabase.Refresh。
- Dashboard/Editor UI 不新增文件、AssetDatabase、网络、PackageManager 访问时机；Catalog discovery 的只读行为保持。
- 不修改 POB `ProjectSettings`、资产、Prefab、Scene 或生产配置。若验证产生配置变化或新的 `outcome_unknown`，立即停止，不重放、不自动恢复。
- 最终 POB business pins 必须同 commit 且无 `file:`；独立 unity-mcp-control pin 不变。

## 实施顺序

1. 为 descriptor/model 增加 section/surface 投影、冲突诊断和纯逻辑测试。
2. 扩展公共 Editor UI 的紧凑 header、row、chip、disclosure 与响应式纯函数及 gallery。
3. 重写 Dashboard shell 为 Tools/System、响应式模块选择和行式内容；更新 legacy 分组及 POB 通用显示名。
4. 将 Formula Catalog 内容拆为 pane/controller，由 Workbench host 提供 Catalog/Workbench 页签；保留所有兼容入口。
5. 更新唯一 editor-ui coverage：28 条 descriptor 记录仍逐 FullId 保留，Formula 两条都绑定 `FormulaWorkbenchWindow` 和 `formula-studio`；Data Toolkit 与 Dashboard shell 记录仍单列。28 条上游 descriptor route 合并为 27 个唯一窗口类型；coverage 中的 POB Data Toolkit 宿主使正常类型为 28 个，POB Configurator 补齐后合并验收为 29 个，Dashboard shell 单列。`FormulaCatalogWindow` 只做 compatibility API 测试，不计入视觉 surface 总数。
6. 将 Dashboard shell、正式描述符、POB adapter 与 Formula Studio 固定文案翻译为简体中文，并通过共享 `GUIContent` 控件补齐动作 tooltip。
7. 跑静态门、Dashboard/Formula/editor-ui 最窄测试和全模块编译；再在安全 live Editor 路线验证 Dashboard、Formula 两个旧菜单与 POB 两个旧菜单只产生一个 Formula 窗口、最终 Console 0。
8. 视觉人工门核对 760/960/1440、真实当前 Unity 主题、长路径/长文案、零诊断/有诊断和 POB adapter 状态。
9. 终端提交、PR、canonical pin 和 Plastic checkin 在用户明确授权后执行；本轮授权已取得。

## 验证与验收标准

1. Dashboard 只有“工具”“系统”两个一级页；系统页同时包含诊断、已安装包和项目适配器，当前页搜索对所有可见集合生效。
2. 760、960、1440 point 下无重叠、横向截断、无滚动截断或不可达主操作；小于 900 point 使用 module popup，大于等于 900 使用 196-point sidebar。
3. Header 不再使用完整 help-box hero；module ID、source path、menu path 默认不可见但可在 Details 中复制。
4. legacy 23 条入口按四个 section 呈现，用户可见模块名为“核心工具”，内部 module ID、FullId 和顺序不变。
5. 旧 schema v1 描述符不加新字段仍可加载；非法 surface 组合产生 diagnostic 并逐 entry 回退，任何入口、确认和错误证据都不丢失。
6. Formula 上游和 POB 的两个原 FullId 在 Dashboard 各折叠为一个 Formula Studio surface；Catalog/Workbench 次级动作仍分别执行原 menu path。
7. 四个原 Formula 菜单与三个公开 Open API 均打开同一个正常 FormulaWorkbenchWindow 实例并选择正确页签；Catalog→Workbench 传递公式且不新建第二个 tab。
8. Formula 扫描、筛选、生成、评估、批量预览、曲线、Profile 注入和 domain-reload transient reset 的现有测试保持通过；切页本身无业务副作用。
9. POB adapter 不在 module sidebar 形成独立项，入口主标题无重复 `POB` 前缀，但当前 Profile/adapter 仍以文字 context chip 可见。
10. Data Manager 与 Configurator 保持不同宿主和两个独立窗口；Apply/project-write 标签和原确认/执行路径不弱化。
11. coverage 仍逐项覆盖 28 条上游 descriptor FullId，Formula 两条 route 共同指向一个 host；上游 descriptor route 为 27 个唯一窗口类型，加入 POB Data Toolkit 宿主为 28 个，再加入 Configurator 后为 29 个，Dashboard shell 单列，compatibility-only Catalog facade 不混入视觉计数。
12. Dashboard/editor-ui/Formula/legacy 的中文增量版本分别为 `3.1.1/1.1.1/0.5.1/2.1.1`，package、README、CHANGELOG 与静态门一致。
13. editor-ui、Dashboard、Formula、legacy descriptor 静态门与各自具名 EditMode lane 通过；不运行 Unity Test Runner Run All。
14. live Editor 从 Dashboard、两个上游 Formula 旧菜单和两个 POB Formula 旧菜单验证窗口可见、页签正确、只有一个 Formula host，最终 Console 0 error；验证前后 POB 生产配置哈希和任务外 Plastic pending 不变。
15. 按用户最新授权采用当前会话单审；最终 spec/diff 的 Critical、Important 均为 0。任何材料设计变更都会使本轮审查证据失效并要求重新自审。
16. Dashboard shell、8 份正式上游描述符、2 份 POB adapter 描述符和 Formula Studio 的固定用户文案均为简体中文；品牌缩写与技术字段除外，菜单/API/ID 不变。
17. Dashboard 页签、刷新、搜索/清空、模块选择、文档/网页、详情、surface 动作、System 折叠项及 Formula Studio 的页签和按钮均有非空简体中文 tooltip；写入动作同时保留中文安全提示与确认。
18. 自动化测试验证中文展示、tooltip 传递、中文搜索及旧英文菜单/API 兼容；live Editor 最终可见 Dashboard/Formula Studio 且 Console 0 error。

## 回滚

- Dashboard shell 可整体恢复 V1 绘制，同时保留向后兼容的新 descriptor 字段解析；旧描述符和执行身份不需迁移。
- Formula 合并若出现状态/保存回归，恢复两窗口呈现，但保留 pane/controller 的纯逻辑拆分和原菜单；不改业务数据。
- POB 未升级 pin 时不受影响；若已升级，则 manifest/lock 成对回到 `cs:16809` 对应 canonical pin，不能只回一个文件或移除 editor-ui。
- 不重写 Git/Plastic 历史，不自动恢复任何用户生产配置。

## 自审记录

- 范围选择：立即合并两个高置信度表面；跨安全边界和 GraphView 生命周期的候选明确延期。
- 兼容策略：描述符只加可选展示字段，FullId/菜单/Profile 保留；Formula 公共类型和 API 不删除。
- 可读性策略：先减层级、减常驻技术信息、修窄宽响应，再做颜色与装饰。
- 安全策略：surface 只合并显示；冲突 fail-visible 并回退，project-write/destructive 不与普通导航混排。
- 验证策略：纯模型/布局函数自动测，真实窗口与主题由 live Editor 人工门补足，不使用像素 golden。

当前会话已完成源码对照自审，Critical/Important 为 0。Claude Code `2.1.220` 的独立审查未发生：可用性 smoke 被组织策略拒绝（组织已禁用 Claude subscription access）。用户随后明确授权由当前会话单审并继续，因此外部双审不再是本轮 Graduation Gate；spec 已获设计与执行授权，可进入实现。任何材料设计变更仍须回写本文件并重新自审。

## As-Built（2026-08-11）

- Dashboard V2 通过 PR #30 合并；简体中文 label/tooltip 增量通过 PR #31 合并。最终实现 canonical 为 `7cc164764c55b70792a4625fe0bb795001e851be`，tree 为 `e39c4d18c648843858851168d3f7491d1894b0f7`。
- 上游验证完成：8 条 descriptor 静态门、28 条 route/30 条 coverage、46 份 JSON、`git diff --check`；Dashboard dashboard-only 52/53（1 个既有 ignored）、with-modules 150/150；editor-ui/Formula/legacy-all lane 及 Formula EditMode 96/96 均通过。PR #31 的 5 条 Unity 主检查与 5 条报告检查全部通过。
- POB 将 19 个业务包统一 pin 到该 canonical；独立 unity-mcp-control pin 保持不变，POB 自有相对 `file:` adapter 保持不变。Dashboard/Formula adapter 已中文化，coverage fixture 绑定上述 commit/tree，Dashboard 与 Formula 对 editor-ui 的锁定依赖均为 `1.1.1`。
- POB 目标 EditMode `POBEditorUiRouteCoverageTests` 5/5 通过；`ZeroEngine/Dashboard` 菜单成功打开。POB“公式目录”菜单的返回发生一次 `outcome_unknown`，命令未重放；控制面记录 `recovery-5a4278a4ea874dfa`（`contained`），随后只读 UIA/PrintWindow 证据确认动作实际 applied：独立 Formula Studio 位于“公式目录”页，扫描结果为公式 112、错误 0、警告 2。恢复后重新 assert/doctor 为 healthy-owned，最终 Console 0 error。
- 原验收第 14 条的五路 live 菜单逐一重放因上述 unknown 安全规则收紧为“一路 Dashboard + 一路 POB Formula 可见窗口抽验，其他旧菜单/API 由具名兼容测试覆盖”；不再为补齐次数重放导航命令。这是验证路线偏差，不改变菜单、API、ID 或运行语义。
- `ProjectSettings/EditorSettings.asset` 最终 SHA-256 为 `4dd4fa6db8124857132b771bf1cda0127b49c462dc3212342deee3cc4d44488a` 且不 pending；没有提交项目配置、资产、Prefab 或 Scene。
- POB 通过 Plastic `cs:16813` 提交，反查 changeset 恰好包含授权的 6 个文件；其余 pending 保留。最终 required 控制面为 epoch 7、`coordination_error=null`、本任务 tasks=0、claims=0、unity=false。
- 最终 spec/diff 自审 Critical=0、Important=0；实现与 rollout 已完成，本文件可归档。
