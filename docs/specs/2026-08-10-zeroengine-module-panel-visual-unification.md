# ZeroEngine 模块面板视觉统一

- 状态：Final
- 最后更新：2026-08-10
- 设计基线：ZeroEngine canonical `3abd18065a5a996b0517b6126edcedc1d195edd1`；POB `/main cs:16808`
- 设计批准：Approved；用户要求当前会话与 Claude 双审通过后实施
- 执行授权：Authorized；范围为本 spec 的实现与本地验证
- 终端操作授权：Not requested

## 结论

统一，但不把所有窗口塞进一个 Dashboard，也不复制 POB Dashboard 的 Odin 实现。新增独立、Editor-only 的 `com.zerogamestudio.zeroengine.editor-ui` 叶子包，提供一套视觉 token、IMGUI 组件和纯代码 UI Toolkit/GraphView 样式；Dashboard 与各模块窗口共同依赖它。Dashboard 仍按已安装模块自动发现入口，各窗口仍以原菜单、原 Profile、原业务流程独立打开。

本轮覆盖 Dashboard 可直接打开的 28 条上游窗口入口，以及 POB 挂载的 4 条入口，共 32 条用户入口、30 个唯一窗口类。视觉以现有 POB Dashboard 的信息层级为参考，但只提取标题、分组、状态、按钮、列表和空状态语言，不引入 Odin、字体、位图或运行时依赖。新增基础包对 Git URL 消费者属于显式升级边界：Unity 2022.3 不会从同仓 Git URL 自动补齐该依赖，升级任一受影响包时必须在工程 manifest 同时直接 pin editor-ui。

## 目标

1. 所有 Dashboard 直接可达模块窗口具有一致的标题层级、区块、状态表达、操作层级、间距和深浅主题表现。
2. 窗口继续保持领域最合适的结构：表单仍是表单，目录仍是目录，图编辑器仍是图编辑器，多栏工作台仍保留多栏。
3. 新安装模块只要声明 Dashboard window 入口并使用公共视觉包，就能自动获得统一外观；不修改自动发现协议。
4. 不改变任何窗口的数据、写入、安全确认、Profile、持久化和菜单语义。
5. 建立可持续的窗口清单、视觉组件图库和自动门，防止以后再次出现各包各画一套。

## 非目标

- 不把模块窗口嵌入 Dashboard，不新增 POB 专属 Tab，不改变 `mountModuleId`、`FullId`、`replaces` 或目录排序。
- 不重做运行时游戏 UI，不触碰玩家界面、Prefab、Scene 或生产配置。
- 不重构业务模型、数据访问、验证规则、导入导出、保存格式或现有 EditorPrefs key。
- 不统一文案语言，也不借视觉改造重命名菜单、按钮、字段或业务概念。
- 不要求各模块拥有完全相同的布局，不进行像素级 POB Dashboard 复刻。
- 不覆盖非 Dashboard 直接入口的二级弹窗、导出器和诊断窗；其后续纳入须单独列清单。`Dialog Node Inspector` 本身已有直接入口，因此属于本轮。
- 不改已经作为视觉参考的 `POBDashboardWindow`。

## 现状与范围

当前 Dashboard shell 已完成自动发现、POB 入口归位和卡片视觉优化，但打开入口后的模块窗口仍分别维护颜色、间距、标题、按钮和状态。有些窗口是原始 IMGUI 表单，有些已有包内 helper，有些是 UI Toolkit/GraphView，视觉和交互密度不一致。

### 必须完成的直接入口

| 来源 | 入口 | 数量 |
| --- | --- | ---: |
| `com.zerogamestudio.analytics` | Analytics Dashboard | 1 |
| `com.zerogamestudio.zeroengine.config-pipeline` | Config Pipeline | 1 |
| `com.zerogamestudio.zeroengine.formula` | Formula Catalog、Formula Workbench | 2 |
| `com.zerogamestudio.zeroengine.tce` | TCE Graph Editor | 1 |
| legacy `com.zerogamestudio.zeroengine` | Ability、Achievement、Behavior Tree Editor/Viewer、Calendar、Crafting、Dialog Graph/Node Inspector、Equipment、Global Search、Inventory、Translation Checker、Loot Table、Mod Creator/Validator、Notification、Quest、Relationship、Settings、Shop、Talent Tree、Tutorial Editor/Graph | 23 |
| POB 挂载入口 | Formula Catalog、Formula Workbench、Data Manager、Configurator | 4 |

前五行是 28 条上游描述符 `kind=window` 入口。POB Formula 两条入口继续复用上游 Formula 两个窗口；Data Manager 增加上游 `DataToolkitWindow`，Configurator 增加 POB `POBExtractionConfigPipelineWindow`。因此总计为 32 条入口、30 个唯一窗口类。

Dashboard shell 也迁移到公共视觉包作为无回归样板，但不计入 30 个模块窗口。命令型入口只保持现有 Dashboard 卡片表现，不纳入窗口改造。

### 技术族

- IMGUI：绝大多数表单、目录、报告、搜索、多栏工作台和 POB Configurator。
- UI Toolkit/GraphView：`BTGraphEditorWindow`、`DialogGraphEditorWindow`、`DialogNodeInspector`、`TalentTreeEditorWindow`、`TutorialGraphEditorWindow`。
- 收敛实现：`GlobalSearchWindow` 当前有 Odin/无 Odin 条件分支，本轮改为一个内建 `EditorWindow` 实现，公共视觉合同与验收均不要求 Odin。

## 视觉语言

视觉目标是“统一的 Unity 编辑器工具”，不是网页后台或游戏 UI。所有控件优先使用 Unity 标准字体、焦点、快捷键、滚动和缩放行为。

### 信息层级

每个窗口按需要组合以下层级，未使用的层级不占空白：

1. **窗口标题区**：标题、短说明、当前上下文和最多一个主状态。
2. **工具栏**：搜索、过滤、刷新、创建等高频操作；搜索不与危险操作混排。
3. **区块卡片**：相关字段和操作放入命名区块，支持说明、折叠和验证摘要。
4. **工作区**：列表、表格、表单、图画布或多栏编辑区，保持领域结构。
5. **状态区**：空、加载、成功、警告、错误、禁用原因与下一步操作。

### Token

公共包只暴露语义 token，不允许业务窗口直接依赖固定 RGB：

- 间距：`4 / 8 / 12 / 16 / 24`，分别用于紧凑内距、控件间距、卡片内距、区块间距、页面分隔。这是可逆的实施默认值，采用 4-point grid 以匹配 Unity 编辑器的紧凑行密度，视觉取证不通过时可在公共 token 内整体调整。
- 尺寸：标准行高、工具栏高、主按钮高、图标尺寸、分隔线和最小点击区域。
- 表面：窗口背景、工具栏、卡片、嵌套卡片、选中、悬停、禁用和分隔线。
- 文字：主标题、区块标题、正文、辅助、禁用、链接和等宽诊断值。
- 状态：中性、进行中、成功、警告、错误；颜色必须与文字或标准图标共同出现。
- 操作：Primary、Secondary、Destructive、Quiet；Destructive 不得仅以红色区分，并继续使用原确认流程。

token 由 `EditorGUIUtility.isProSkin` 选择系统深浅主题。IMGUI 的 `EnsureCurrent()` 在 skin bool 改变时重建；纯代码 UI Toolkit style 在 `CreateGUI`/元素重建时应用，已打开的 UI Toolkit 窗口不承诺原地跟随主题切换，切换后重开窗口或 domain reload 生效。布局使用 Unity point 和现有缩放，不硬编码屏幕像素。

### 公共组件

`com.zerogamestudio.zeroengine.editor-ui` 至少提供：

- `EditorUiPalette` / `EditorUiTokens`：纯语义调色板和尺寸。唯一纯函数 `ResolveForSkin(bool isProSkin)` 生成 palette；生产 `Current` 精确调用 `ResolveForSkin(EditorGUIUtility.isProSkin)`，Tests assembly 通过 `InternalsVisibleTo` 调用同一函数构造 Light/Dark 预览值，不维护第二份 preview 颜色。
- `EditorUiStyles`：缓存的 IMGUI `GUIStyle`、`GUIContent` 与图标访问。
- `EditorUiGUILayout`：Header、Toolbar、Section、Status、EmptyState、ValidationSummary、KeyValueRow、ActionRow、SplitPane chrome。
- `EditorUiElements`：在元素创建时直接应用同语义 palette、spacing 和状态 style；不加载 USS、不在 layout/update 中重复写 style。
- GraphView 适配方法：直接应用画布工具栏、侧栏、Inspector、节点状态和验证态；不接管节点业务、端口连线或序列化。
- `EditorUiSurfaceAttribute`：标记已接入公共视觉合同的窗口类型，供覆盖门绑定入口与类型。

公共包位于 Editor-only assembly `ZeroEngine.EditorUI.Editor`，`includePlatforms=[Editor]`、`autoReferenced=false`、`unity=2022.3`，无 Runtime assembly、USS、Odin、网络或第三方包依赖，不新增位图、字体或品牌资源。公共组件不包含任何用户可见业务文案。`EditorUiStyles.EnsureCurrent()` 每次入口只比较缓存的 `isProSkin` bool，变化时重建样式；不依赖不存在的主题切换事件。

### 覆盖清单合同

单一机器可读清单位于 `com.zerogamestudio.zeroengine.editor-ui/Tests/Editor/Fixtures/EditorUiWindowCoverage.json`，schema v1 的每个 target 固定包含：`targetId`、可空 `descriptorFullId`、`countsTowardModuleTotal`、`sourcePath`、`typeName`、`technology`、`migrationStatus`、`integrationMethod`、`exceptions[]`；每个 exception 包含精确 `method`、封闭枚举 `token=Color|Color32|GUIStyle`、`scope=domain-canvas|domain-chart` 和 `reason`。28 条描述符目标的 `descriptorFullId` 非空。Data Toolkit 使用 `targetId=pob-mounted/data-toolkit`、空 descriptor、计数为 true；Dashboard shell 使用 `targetId=dashboard-shell`、空 descriptor、计数为 false。两者不参与 FullId 集合相等；coverage JSON 共 30 条记录，其中 29 条计入模块目标总数。

PowerShell contract 门、上游 C# 测试/gallery 与 POB 覆盖测试都读取这一份 JSON；不得复制上游映射。PowerShell 先以非空 `descriptorFullId` 做 28 条双向相等，再验证全部 30 条记录的源码、类型、`migrationStatus=migrated`、`integrationMethod` marker 和例外；模块类型计数只取 `countsTowardModuleTotal=true` 的 29 项。每个 exception 的方法必须存在于同一源码，且只对白名单 token 生效；scope 不是两个领域枚举、方法等于 `integrationMethod` 或方法正文含 header/card/status marker 时一律拒绝。

颜色字面量只允许出现在生产文件 `EditorUiPalette.cs`；这是唯一文件级公共 palette 白名单，`EditorUiTokens.cs` 只含尺寸。editor-ui 其他生产文件和 coverage 中每个 `integrationMethod` 禁止 `Color/Color32` 与顶层 `GUIStyle`。领域画布/图表的既有例外按上段 schema 逐条记录，header/card/status 永不豁免；Tests 不属于生产颜色禁令，但只能从 resolver 取期望 palette。

### 响应式和可访问性

- 以 640、960、1440 point 三种可用宽度验收；这是可逆的窄/标准/宽视觉取证默认值，覆盖现有工具的实际停靠形态，不成为生产窗口最小宽度。窄宽度改为堆叠或滚动，不截断主操作、不让标签覆盖输入框。
- 长路径、错误和值可选中复制；截断时提供完整 tooltip。
- 状态不只靠颜色；标准输入、按钮、列表继续支持键盘焦点和 Tab 顺序。
- 图编辑器在不改变缩放、平移、框选和快捷键的前提下统一工具栏、侧栏、选中和验证态。
- 不新增密度、主题等用户偏好，避免额外持久状态和配置迁移。

## 架构决定

### 选择独立 Editor UI 叶子包

模块不能依赖 Dashboard：Dashboard 是发现与导航 shell，不应成为所有编辑器窗口的基础依赖。视觉代码也不能进入 runtime core，否则会污染 Player 依赖边界。各包复制 helper 会继续产生视觉漂移。

因此新增 `com.zerogamestudio.zeroengine.editor-ui`，被以下包的 Editor assembly 单向引用：

- `com.zerogamestudio.analytics`
- `com.zerogamestudio.zeroengine`
- `com.zerogamestudio.zeroengine.config-pipeline`
- `com.zerogamestudio.zeroengine.dashboard`
- `com.zerogamestudio.zeroengine.data-toolkit`
- `com.zerogamestudio.zeroengine.formula`
- `com.zerogamestudio.zeroengine.tce`

受影响包各自增加精确 package dependency `"com.zerogamestudio.zeroengine.editor-ui": "1.0.0"`，并在 Editor asmdef 引用 `ZeroEngine.EditorUI.Editor`。legacy 包的 `ZeroEngine.Editor` 与 `ZeroEngine.ModSystem.Editor` 均须引用；其现有 Dashboard dependency 同步要求 `3.0.0`。POB 的 `POB.Extraction.ConfigPipeline.Editor.asmdef` 也须引用该 assembly。

新包版本从 `1.0.0` 开始。由于 Git URL 消费者必须新增直接 manifest pin，这不是无操作升级；采用 SemVer 的显式破坏性升级默认值：Analytics `2.0.0`、legacy ZeroEngine `2.0.0`、Config Pipeline `2.0.0`、Dashboard `3.0.0`、Data Toolkit `2.0.0`、Formula `0.4.0`、TCE `0.2.0`。1.x/2.x 包提升 major，0.x 包提升 minor；这是基于当前已检视版本的可逆发布默认值，避免把缺依赖导致的编译失败误标为兼容 minor。Dashboard 当前“零 package dependency / 零 assembly reference”门改为“只允许 editor-ui 这一项基础依赖”，继续禁止直接引用任一可选业务模块。

### 迁移边界

- Formula 现有 `FormulaEditorGUILayout` 保留为领域 wrapper，内部转调公共组件，避免一次改写报告和 Profile 语义。
- Data Toolkit 保留三栏、分隔拖拽、搜索、选择和 EditorPrefs；只替换 header、surface、section、status、button 和 list chrome。
- GraphView 窗口保留图模型、GraphElement 生命周期、序列化和命令；仅通过公共方法应用容器、状态和节点 token。
- `GlobalSearchWindow` 合并为单一、内建 `EditorWindow` 实现，移除 Odin 条件呈现分支；搜索模式、输入、结果和执行方法保持等价。这样 canonical lane 可实际编译唯一生产实现，也避免视觉合同被 Odin define 分叉。
- POB Configurator 只替换窗口呈现，并保持提取配置、Addressables、验证和写入路径原样。

## 行为与安全不变量

视觉改造不得改变：

- `MenuItem` 路径、Dashboard descriptor、模块自动发现、挂载、替代、排序和安全确认。
- Profile 注入、窗口类型、`titleContent`、`minSize`、业务状态和已有 EditorPrefs key；640-point 验收通过内部堆叠/滚动完成，不改窗口最小尺寸。
- 数据查询、AssetDatabase/文件/网络访问时机、缓存、刷新、验证、保存和导入导出。
- 选中项、折叠、分栏宽度、图位置、Undo、dirty 标记和序列化。
- 原有危险操作的确认、禁用条件、失败信息和诊断证据。

公共视觉层自身不得调用 AssetDatabase、PackageManager、文件、网络、反射发现、菜单或业务命令；不得在 `OnGUI`/layout pass 中引入扫描或数据刷新。视觉层只渲染调用方提供的状态并返回显式用户交互结果。

## 影响范围

### ZeroEngine

- 新包 `com.zerogamestudio.zeroengine.editor-ui/**`，包含 package、Editor assembly、纯代码 IMGUI/UI Toolkit 实现、Tests 及配对 `.meta`。
- 七个消费包的 `package.json`、Editor asmdef 及目标窗口源码。
- 七个消费包的 README/CHANGELOG：明确新增直接 editor-ui pin 的 Git URL 升级步骤和上述版本边界。
- Dashboard shell 源码、asmdef、package 版本及其静态依赖门。
- `Tools/Tests/Test-ZeroEngineDashboardDescriptors.ps1`：保留描述符/所有权检查，更新 Dashboard `3.0.0`、唯一 editor-ui package dependency 和唯一 `ZeroEngine.EditorUI.Editor` assembly reference 的断言。
- 新增 `Tools/Tests/Test-ZeroEngineEditorUiContract.ps1`：直接定位 editor-ui 和七个消费包；读取唯一 coverage JSON，静态解析生产 Dashboard JSON 描述符，校验 package/asmdef 边界、版本、颜色白名单、例外和禁止副作用 token，以及 28 条描述符入口集合相等；Data Toolkit 参与 29 模块类型，Dashboard shell 参与 style 覆盖但不计数。另断言 `GlobalSearchWindow.cs` 不再包含 `ODIN_INSPECTOR` 条件分支。
- `Tools/Tests/run-dashboard-editmode-tests.ps1`、`run-data-toolkit-editmode-tests.ps1` 及新增 editor-ui 最窄 wrapper：每个临时工程都复制 editor-ui，并在生成的 manifest 中加入直接 `file:` 依赖；测试产物继续位于系统临时目录。
- 新增 `Tools/Tests/run-editor-ui-editmode-tests.ps1`，具名 lanes 为 `editor-ui`、`analytics`、`config-pipeline`、`formula`、`tce`、`legacy-all`；前五条各只装目标包、editor-ui 及直接依赖，`legacy-all` 安装 legacy 编译所需的现有模块集合。Dashboard 由 dashboard wrapper 覆盖，Data Toolkit 由其现有 wrapper 覆盖。
- editor-ui Tests assembly 中的组件图库和唯一 coverage JSON；图库菜单只在包被列入 `testables` 的一次性视觉工程中编译，不进入生产消费工程。Tests 对 Unity Test Framework 的引用不计入生产 Editor assembly 的“无第三方依赖”断言。

### POB

- `Assets/Assets/_Scripts/_POB/Extraction/Editor/ConfigPipeline/POBExtractionConfigPipelineIntegration.cs`
- `Assets/Assets/_Scripts/_POB/Extraction/Editor/ConfigPipeline/POB.Extraction.ConfigPipeline.Editor.asmdef`
- 新增 `Assets/Assets/_Scripts/_POB/Tests/Editor/ZeroEngineDashboard/POB.EditorUiCoverage.Tests.asmdef`、`POBEditorUiRouteCoverage.json`、覆盖测试及配对 `.meta`。overlay schema v1 每条 route 包含 `routeFullId`、`typeName`、可空 `upstreamTargetId`、可空 `sourcePath/integrationMethod` 和 `exceptions[]`；三条上游类型必须引用上游 coverage target，Configurator 是唯一带本地 source/method/exception 的记录。POB 测试通过 `PackageInfo.FindForAssembly(typeof(EditorUiSurfaceAttribute).Assembly).resolvedPath` 定位唯一上游 JSON，并对 Configurator 执行与上游相同的 marker、颜色、样式和例外扫描。
- 配对更新 `Packages/manifest.json` 与 `Packages/packages-lock.json`

POB Formula 和 Data Manager adapter 不新增视觉代码；它们通过原 Profile 打开已迁移的上游窗口。其他 POB pending 不在范围内。

## 实施顺序

1. 新建 editor-ui 叶子包、深浅主题 token、IMGUI/UI Toolkit 组件和测试用 visual gallery。
2. 先更新静态门和所有临时测试工程 manifest，使 editor-ui 作为直接依赖可解析；再迁移 Dashboard shell 与一个 IMGUI、一个 UI Toolkit 代表窗口，锁定 API 并跑最窄编译和视觉核对。
3. 按窗口族迁移：表单/配置、列表/目录、报告/诊断、多栏工作台、搜索/Inspector、GraphView。
4. 填充唯一 coverage JSON：28 条描述符入口、Data Toolkit 与 Dashboard shell 绑定目标类、技术族、顶层集成方法、计数标志、迁移状态和精确领域例外；POB overlay 记录四条 route 与 Configurator 本地扫描字段，完成后必须为零遗漏。
5. 迁移 POB Configurator，使用本地联调 pin 验证四条 POB Profile/挂载路线。
6. 完成静态门、包级 EditMode/编译、30 窗口 smoke-open、代表性视觉矩阵和 Console 检查。
7. 经独立终端授权后，ZeroEngine 以一个原子 PR 合入全部基础包、测试工程依赖和窗口迁移；POB 再将业务包统一 pin 到新的 canonical merge commit，并精确 Plastic 提交 POB 两个源码文件与 manifest/lock。

上游使用一个 PR 是有意的：公共包与所有消费包必须同时可解析，避免 canonical 出现窗口已引用但基础包未落地的中间状态。PR 内按上述窗口族拆分可审查提交。

## 验证设计

### 自动门

1. editor-ui EditMode：`Current` 与 `ResolveForSkin(EditorGUIUtility.isProSkin)` 同源，Light/Dark 调用同一 resolver；palette 数据完整、语义对比度下限、IMGUI skin bool 变化时缓存重建、UI Toolkit 新建 root 使用当前 palette、空/警告/错误/禁用状态均可构造。已打开的 UI Toolkit root 只在重建后换肤是明确合同。`ResolveForSkin` 通过 `InternalsVisibleTo("ZeroEngine.EditorUI.Tests.Editor")` 仅对 Tests assembly 可见。
2. `Test-ZeroEngineEditorUiContract.ps1`：公共包为 `1.0.0`、`unity=2022.3`、Editor-only、`autoReferenced=false`、无 runtime/Odin/第三方依赖；七个消费包声明正确 package dependency，且只从其 Editor asmdef 显式引用；Dashboard 不引用可选模块。
3. 入口覆盖分两层。PowerShell 静态解析全部生产描述符 JSON，以 coverage JSON 中非空 `descriptorFullId` 的 28 项做集合相等；Data Toolkit 和 Dashboard shell 的空 descriptor 记录不参与该比较。随后对 coverage JSON 全部 30 条记录检查 `[EditorUiSurface]`、`migrationStatus=migrated` 与 `integrationMethod` marker；其中 29 条 `countsTowardModuleTotal=true`。POB EditMode 测试解析四条 overlay 和两份描述符，复用 coverage JSON 中 Formula/Data Toolkit 三类型，并以同强度扫描新增的 POB Configurator，合并证据为 32 条路线、30 个唯一模块窗口类；Dashboard shell 始终单列、不计入 30。
4. 源码合同门直接扫描 editor-ui `Editor/**`：禁止 AssetDatabase、PackageManager、文件、网络、反射发现、菜单执行、USS/StyleSheet 加载；颜色字面量只白名单 `EditorUiPalette.cs`。coverage JSON 与 POB overlay 指定的每个本地 `integrationMethod` 必须有公共调用 marker，且不得含未列入 schema 例外的 `Color/Color32` 或顶层 `GUIStyle`；header/card/status 不接受例外。窗口既有数据访问不因迁移新增调用点。
5. Dashboard descriptor 门显式更新版本 `3.0.0`、唯一 editor-ui package/asmdef dependency；不得保留 `2.1.0`、零依赖或零引用旧断言。
6. `run-editor-ui-editmode-tests.ps1` 的 `editor-ui`、`analytics`、`config-pipeline`、`formula`、`tce`、`legacy-all` 六条 lane，Dashboard wrapper 和 Data Toolkit wrapper 全部通过；每个生成 manifest 都直接包含 editor-ui，不得用 Unity Test Runner Run All。
7. POB 编译与相关 asmdef 引用通过；联调后的最终 manifest/lock 成对恢复为 Git pin，且不存在业务 `file:` 或 local lock 残留。

对比度下限按普通文字 4.5:1、大号标题和非文字关键边界 3:1 检查；这是采用 WCAG 2.1 AA 的可逆编辑器可用性门，只计算公共包自定义 palette 的明确前景/背景组合，Unity 内建控件和 disabled 控件不伪造未知系统背景、也不重复覆盖颜色。

### 视觉门

测试 assembly 提供 visual gallery，一次展示全部 token 和组件。batchmode lane 只测 palette、结构与组件构造，不调用 `EditorWindow.Show()`。交互式 gallery 通过 Tests assembly 独有的 `ZeroEngine Tests/Editor UI Gallery` 菜单打开；只有生成 manifest 将 editor-ui 加入 `testables` 的一次性视觉工程会编译该菜单，生产消费工程不出现。gallery 的 Light/Dark 使用与生产 `Current` 完全相同的 `ResolveForSkin`，但不会冒充 Unity `EditorStyles` 或内建控件的真实另一主题；生产窗口始终使用当前系统皮肤。该路线不修改 Editor 全局主题或项目配置。

- `Tools/Tests/New-ZeroEngineEditorUiVisualProject.ps1` 在系统临时目录生成全模块可视化工程，直接 `file:` 引用 editor-ui 与全部相关包并加入测试 assembly；脚本只生成工程，不启动 Unity。执行者按 `unity-test-router` 为该精确项目选择安全 live Editor 路线，再用测试菜单打开 gallery 和 Dashboard。
- gallery 在 640、960、1440 point 下各检查一次自定义 Light/Dark palette，共六个预览视图；不得有重叠、不可达主操作或无滚动截断。内建控件的跨主题保证来自公共包和顶层集成方法的硬编码样式门，截图只对当前真实 Editor 主题负责。
- 六类代表窗口分别取证：Config Pipeline（表单）、Formula Catalog（目录）、Analytics（报告）、Data Toolkit（多栏）、Global Search（搜索/Inspector）、Behavior Tree（UI Toolkit/GraphView）。POB Configurator 另做消费项目取证。
- 不采用像素级 golden snapshot；截图用于人工核对层级、状态、间距和主题，自动门负责结构、覆盖与无错误。
- 上游 29 个类型在上述全模块可视化工程中通过 Dashboard/原菜单 smoke-open；POB 四条 Profile/挂载路线和新增 POB Configurator 类型在 POB live Editor 中验证。两处都必须由 `unity-test-router`/项目租约进入既有菜单或测试 route，不用 `execute_code`；合并为 30 个唯一窗口，标题正确、内容可见、Console 0 error，不要求为每个窗口单独留截图。

### POB 安全门

- 执行期所有 live Unity 操作遵循项目的 Supervisor/claim 和 `unity-test-router` 路由；不用 `execute_code` 做目录或窗口验证。
- 只打开窗口和运行最窄测试，不修改 `ProjectSettings`、生产数据、Prefab 或 Scene。若出现配置变化或新的 outcome_unknown，立即停止并保留证据，不自动恢复或重放。
- 验证前后核对 POB 生产配置哈希与 Plastic pending 范围；任务外 pending 必须保持不变。

## 发布与兼容

- 公共视觉包只影响 Editor，不进入 Player，也不改变序列化数据；现有项目不打开窗口时无运行时影响。但升级解析是显式破坏性边界：通过 Git URL 安装任一受影响包的工程，必须在 manifest 同时直接 pin `com.zerogamestudio.zeroengine.editor-ui` 到同一 canonical commit；缺 pin 必须在 resolve/compile 阶段清晰失败，不能宣传为自动传递依赖。
- ZeroEngine PR 合并前同步当时 canonical，确认没有覆盖后续控制面或其他包变更；所有 required review/CI 正常通过，不自批或绕过门禁。
- POB 最终 manifest/lock 将现有 18 个 ZeroEngine/Analytics 业务 pin 与新增 editor-ui 共 19 个业务 pin 统一指向同一个新 canonical merge commit；独立 `com.zerogamestudio.unity-mcp-control` pin 不在范围内。
- POB 本地联调可按项目规则暂时使用精确 `file:`，但结束联调后必须在同一受控任务中把 manifest/lock 一起恢复为 Git pin；最终 lock 的 `source`/路径不得残留 local/file 信息，恢复门通过前不提交、不声称验证完成。
- POB changeset 只包含 POB Configurator 两个源码/asmdef 文件、POB 覆盖测试资产和配对 manifest/lock；任何额外 pending 都阻断提交。
- 新增 Unity 可见文件必须带配对 `.meta`；不得保留绝对 `file:`。

## 失败与回滚

- 单个窗口迁移若改变业务行为，PR 合入前回退该窗口到旧呈现，不为赶视觉覆盖降低行为测试。合入后以新 PR 精确恢复该窗口旧呈现，但保留 editor-ui package/asmdef 依赖，避免牵动其他已迁移窗口。
- 若公共组件 API 不适合某窗口族，可增加最小语义组件；不得在消费包复制颜色和顶层 card/header 实现。
- 上线后 POB 出现编译或严重可用性问题时，整组回滚 POB Configurator + manifest/lock 到 `cs:16808` 对应 pin；不能只移除 editor-ui pin，否则 POB asmdef 无法解析。
- 上游通过新的纠正 PR 回退窗口呈现，但已发布后保留新的 package 版本、editor-ui 包和依赖合同，避免 Git URL 消费者发生版本倒退；不重写 canonical 历史，不触碰业务数据或 EditorPrefs。尚未升级的 POB 仍固定旧 commit，不受纠正 PR 影响；只有 POB 已 pin 到故障 commit 时才先执行上一条整组 POB 回滚。
- 回滚不需要数据迁移，因为本方案不新增业务持久状态。

## 验收标准

1. Dashboard 仍按已安装模块自动加载，32 条直接入口的身份、归属、菜单、Profile、安全和执行行为不变。
2. 上游 28 条描述符集合与清单双向相等，另含挂载目标 Data Toolkit 共 29 个模块类型；Dashboard shell 作为不计数 coverage 记录接受同强度 style 扫描。POB overlay 补齐四条挂载路线和唯一 POB Configurator，合并为当前 32 条路线、30 个唯一 `[EditorUiSurface]` 模块窗口，没有漏项、fixture 或二级窗口误计。
3. 窗口具有一致的标题、区块、状态、操作、空态、间距和深浅主题语言，同时保留表单、目录、图和多栏工作的领域布局。
4. Dashboard shell、29 个上游模块类型和 POB Configurator 全部进入 marker/style/exception 门，不再各自定义顶层标题、卡片或状态色；领域图形和报告绘制只按机器可读例外保留局部实现。
5. 生产 `Current` 与测试 Light/Dark 使用同一 `ResolveForSkin`；明确文字、状态和关键边界组合满足 WCAG 2.1 AA 数据门。颜色字面量只存在于 `EditorUiPalette.cs`，上游 coverage 与 POB overlay 的本地方法无未列入 schema 例外的硬编码颜色/样式，状态不只依赖颜色，标准键盘焦点仍可用；UI Toolkit 重开后采用新主题，截图不虚报未切换的真实 Editor 主题。
6. 640、960、1440 point 下无控件重叠、不可达主操作或无滚动截断。
7. 公共包为 `1.0.0`、`unity=2022.3`、Editor-only、`autoReferenced=false`、Odin-independent、无 runtime/网络/文件/AssetDatabase 副作用；`OnGUI` 不新增扫描或刷新。`GlobalSearchWindow` 只有一个可编译的内建实现。
8. 原菜单、Profile、EditorPrefs、选择、分栏、Undo、dirty、保存、导入导出和危险确认语义不变。
9. 六条具名 editor-ui lanes、Dashboard wrapper、Data Toolkit wrapper 与静态门全部通过，临时 manifest 均直接包含 editor-ui；batchmode 不调用 `Show()`。全模块可视化工程验证上游 29 类型，POB live Editor 验证四条 Profile/挂载路线，合并 30 个窗口且最终 Console 0 error；代表性视觉证据覆盖六类窗口和 POB Configurator。
10. 七个受影响包的升级文档明确 Git URL 消费者必须增加直接 editor-ui pin，版本分别为 Analytics/legacy/Config Pipeline/Data Toolkit `2.0.0`、Dashboard `3.0.0`、Formula `0.4.0`、TCE `0.2.0`。
11. POB 生产配置和任务外 pending 不变；最终 19 个业务 pin 指向同一 canonical merge commit，manifest/lock 均无 `file:` 或 local source/path 残留，独立 Unity MCP 控制 pin 不变。

## 自审记录

- 范围闭合：入口数由当前生产描述符和 POB 4 条适配路线得出，明确排除 fixture、命令和二级窗口。
- 依赖方向闭合：选择独立 Editor UI 叶子包，避免模块依赖 Dashboard、runtime core 或 Odin。
- 行为边界闭合：视觉层为纯呈现，现有数据和写入时机不迁移。
- 验证闭合：静态覆盖负责“一个不漏”，包级测试负责兼容，代表性截图负责视觉，真实入口负责可用性。
- 发布闭合：上游原子 PR 后再做 POB 19 pin 与精确 Plastic changeset；失败可整组回滚且无数据迁移。
- 双审整改：补齐 Git URL 非传递依赖、测试工程 manifest、独立 contract 门、显式版本、同源主题 resolver、Global Search 单实现、纯代码 UI Toolkit 样式、29+1 覆盖证据、batch/live 分离与精确视觉宿主。
- 双审结论：当前会话源码自审 PASS；Claude CLI Opus 对设计 hash `02843a648c16cc672f1b98b42623c1082d0125643690e2fa8452e14315d11a51` 最终定向复审 PASS，Critical/Important 为 0。其默认 Fable 模型因额度耗尽，按既定回退规则仅本任务改用 Opus。

本 spec 已通过双审并获实现授权；进入代码与本地验证，PR、push 和 Plastic checkin 仍需独立终端授权。
