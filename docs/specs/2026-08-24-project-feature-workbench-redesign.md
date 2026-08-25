# 项目功能工作台：面向项目人员的功能导航与配置直达

- 状态：Implemented（2026-08-25 DataManager 式三栏已实现；截图验收发现的叠字、裁切、重复标题、按钮感不足和视觉重心偏斜已纳入 1.1.4 可读性修订）
- 最后更新：2026-08-25
- 已检查基线：ZeroEngine commit `4797ab9f6309e1b0fa32741dcf6df0801425d82f`；Project Atlas `1.1.0`、Dashboard `4.7.0`、editor-ui `1.5.0`、Data Toolkit `2.1.1` 的三栏数据管理布局、P5 当前功能目录与 15 个项目面板
- 设计批准：Approved；用户于 2026-08-24 回复“自审修订spec好了就开干”
- 执行授权：Authorized；范围为本 Spec 的本地实现、迁移、自审与验证
- 本轮布局批准：Approved；用户于 2026-08-25 确认采用 DataManager 相同布局结构，并明确指出下拉导航不直观
- 终端操作授权：Authorized；2026-08-24 用户要求完成 ZE Git commit / push、P5 / POB 正式 pin 与 POB Packages 两文件 Plastic checkin
- 关系：本 Spec 覆盖并替代《ZeroEngine Project Atlas：跨项目系统图谱与程序 / Agent 路由》中“同一图谱三种 UI 视图”和“项目图谱面板信息架构”的产品设计；原 Spec 的技术目录、覆盖门、生成文档、Agent 合同、唯一 `ZGS/工作台` 入口和安全边界继续有效。

## 结论

工作台中的默认面板不再是技术“图谱”，而是面向策划、关卡、叙事、美术、测试和项目管理人员的“项目功能”。它按游戏功能组织为“角色、世界与地图、战斗、任务与叙事、物品与经济、UI 与表现、进度与发行”，每个功能直接回答：能做什么、有哪些子功能、从哪里配置、如何预览、如何检查。

程序与 Agent 不再占用工作台的人类界面。技术系统图、程序集、包、数据流、Agent 改动边界和覆盖诊断继续由 `docs/architecture/system-routing-index.md`、根 `AGENTS.md`、Project Atlas JSON 和自动测试承载。它们与人类功能目录通过稳定 route / reference ID 关联，但不强迫“一项人类功能等于一个程序系统”。

“打开配置”采用工作台内部面板切换和可选深链接：先切换到现有唯一配置所有者，再定位到该面板的目标页签、筛选或对象。不会把所有编辑器再次嵌套进功能地图，也不会恢复菜单树、反射方法或字符串菜单调用。

## 目标

1. 项目人员打开 `ZGS/工作台` 后，首先看到可理解的项目功能，而不是程序集、包、system ID 或 Agent 规则。
2. 用户可从“领域 → 功能 → 配置 / 预览 / 检查动作”进入现有配置所有者，不必知道 Data Toolkit、Config Pipeline 或具体文件路径。
3. “角色”能展开角色档案、队伍、属性装备、武学技能、经脉、敌人与 AI；“世界与地图”能展开 World、Area、地图导航、小地图、交互和 NPC 等项目功能。
4. 配置入口保持唯一所有者；功能工作台只负责说明和导航，不复制编辑表单、配置值或写入逻辑。
5. Project Atlas 继续提供跨项目通用的目录加载、验证、引用解析和技术投影，但人类功能分类由消费项目定义。
6. P5 作为完整功能目录样本，POB 以最小真实目录证明模型不依赖 P5 的功能名称和目录结构。

## 非目标

- 不在工作台中展示程序架构图、程序集列表、包 pin、Agent 必读规则、coverage item 或 JSON 字段路径。
- 不把“数据管理”“配置管线”“命令中心”等技术工具名称当作项目功能分类。
- 不在功能面板内重新实现角色、地图、任务、经济等配置编辑器。
- 不允许 JSON 指定菜单路径、程序集限定方法、反射调用、Shell、URL 或任意执行代码。
- 不自动创建、修改或修复生产配置；导航本身只读，写入仍由目标面板按原安全合同执行。
- 不在首版引入自由画布、节点连线、拖拽布局、网页前端、远端知识库或运行时 Player 功能。

## 当前行为与根因

已检查的当前实现存在以下事实：

- `ProjectAtlasWorkspacePanel` 顶部固定显示“项目与功能 / 架构与路由 / Agent 改动合同”三个页签，并把 ownership、lifecycle、system ID 和诊断筛选直接暴露给所有用户。
- P5 目录只有 6 个宽泛系统节点：项目基础与数据、应用流程与世界、角色武侠成长与战斗、叙事任务存档与 DLC、物品内容与 UI、开发工具与验证。一个节点混入多个岗位、多个工作流和多个配置入口，不能回答“角色下面具体有什么”。
- 当前“定位”只对 path / doc / assembly 调用文件管理器；`ProjectAtlasReferenceResolution` 没有实际导航动作，未使用 editor-ui 已存在的 `IEditorWorkspaceNavigator` 和 `EditorWorkspaceNavigation.CreateAction(...)`。
- P5 已有 15 个 Dashboard 项目面板，其中角色质量、经脉、经济、叙事任务、武侠成长、本地化、数据管理、表格配置、世界编制等均可作为现有唯一所有者，无需再造编辑器。

根因不是文案不足，而是将团队任务、程序系统和 Agent 改动合同强制放进同一分类树。三类受众共享权威引用是正确的，但共享导航结构不成立。

## 已确认设计决定

### 1. 一个知识基础，两种模型

Project Atlas 维护两个相连但不同的只读模型：

| 模型 | 使用者 | 分类方式 | 载体 |
| --- | --- | --- | --- |
| 项目功能目录 | 项目人员 | 领域 → 功能 → 工作动作 | `docs/project/feature-map.json` 与显式功能碎片；工作台“项目功能”面板 |
| 技术系统路由 | 程序、Agent、CI | 系统 → 结构引用 → 规则 / 验证 | 现有 Project Atlas JSON、生成 Markdown、根 `AGENTS.md` 与测试 |

两者可通过稳定 reference / route ID 互相校验，但不要求一一对应。一个“角色”功能可以跨角色、数据、UI、战斗程序集；一个底层事件系统也可以服务多个功能而不出现在人类导航中。

### 2. 工作台只显示人类功能面

- Project Atlas 包名、技术模型和稳定 panel ID `project-atlas` 保留；用户可见名称改为“项目功能”，section 改为“项目导航”。
- 删除面板中的“架构与路由”“Agent 改动合同”页签，以及 ownership、lifecycle、system ID 和技术诊断筛选。
- “生成路由索引”“覆盖诊断”等维护动作移到工作台的“命令中心 / 项目工具”，不占用项目人员首页。
- 首次打开工作台默认进入“项目功能”；之后可恢复用户上次面板。工作台 shell 提供“返回项目功能”和路由历史，避免进入配置器后迷失。
- `ZGS` 顶栏仍严格只保留 `ZGS/工作台`。

### 3. 使用工作台切换与深链接，不重复嵌套编辑器

从功能页点击动作后：

```text
项目功能 > 角色 > 角色档案 > 打开配置
              ↓
Dashboard 选择已有 owner panel
              ↓
目标 panel 接收可选 routeId，定位页签 / 筛选 / 对象
              ↓
顶部显示来源面包屑，可一键返回“角色 > 角色档案”
```

不选择“把所有配置器嵌入项目功能的内部 Tab”方案。该方案会复制 EditorWindow 生命周期、状态保存、滚动和安全确认，并形成第二个宿主。推荐方案复用当前 Dashboard panel 生命周期，只增加稳定路由和返回上下文。

### 4. 以任务语言呈现，不暴露存储技术

功能页显示：

- 功能名称与一句话用途；
- “可以做什么”的子功能清单；
- 适用岗位；
- `打开配置`、`预览效果`、`运行检查`、`查看说明` 四类动作；
- “可配置 / 仅查看 / 暂无日常配置 / 入口不可用”的中文状态。

默认不显示 route ID、moduleId、panelId、文件路径、程序集、包名、ownership、lifecycle 或 coverage。技术标识只用于内部验证和可复制的维护诊断，不进入普通详情正文。

### 5. 配置导航是 typed route，不是任意执行

功能 JSON 只引用稳定 `routeId`。已编译 resolver 将 routeId 解析为以下受控动作之一：

- `workspace`：切换到已注册的 Dashboard `moduleId/panelId`；
- `workspace-deep-link`：切换面板后向明确实现接收接口的 panel / embedded view 发送已注册 `subrouteId`；
- `project-asset`：选择并 ping 项目根内已存在的资产或文件夹；
- `documentation`：打开项目根内已存在的说明文件。

未知 routeId、目标 panel 缺失、subroute 不被目标接收、资产缺失或越界路径均 fail closed：按钮禁用并显示项目人员可理解的原因。底层诊断保留 routeId 和来源字段，供程序与 Agent 在文档 / 测试中定位。

### 6. 目标面板继续拥有写入和安全等级

“打开配置”本身是 navigation，不代表执行写入。进入目标面板后：

- 浏览和检查沿用 navigation / read-only；
- 修改、生成、重建沿用 project-write 确认；
- 清理、删除沿用 destructive 确认；
- 功能目录不得降低、覆盖或伪装目标动作的安全等级。

## 面板信息架构

### 默认布局

所有窗口宽度使用同一套 DataManager 式三栏导航；标准 / 宽窗口直接铺满可用宽度：

```text
┌──────────────────────────────────────────────────────────────────────┐
│ 项目功能    [搜索功能或工作内容]     [我的岗位：全部 ▼]       [刷新] │
├──────────────┬──────────────────────┬────────────────────────────────┤
│ 项目领域     │ 具体功能             │ 功能详情与入口                 │
│ 角色与成长   │ 角色档案与队伍       │ 用途、适用岗位、配置状态       │
│ 世界与地图   │ 属性与装备           │ 可以做什么                     │
│ 战斗         │ 武学与技能           │ [打开配置] [预览] [检查] [说明]│
│ 任务与叙事   │ 经脉成长             │                                │
│ ……           │ 敌人与 AI            │                                │
└──────────────┴──────────────────────┴────────────────────────────────┘
```

三栏均有明确边框、固定标题、独立滚动和稳定选中态；领域列与功能列之间可拖动调整宽度，并通过 EditorPrefs 恢复。紧凑窗口不再把领域或功能折叠为下拉，也不切换成另一套信息结构；当可用宽度小于三栏最小宽度时，主体使用横向滚动完整保留三栏和主要动作。Project Atlas 复用 DataManager 的布局结构和交互习惯，但不依赖 Data Toolkit 包，数据与 typed route 所有权仍在 Project Atlas。

### 搜索与岗位筛选

- 搜索只匹配中文功能名、同义词、工作动作和项目术语，例如“角色、人物、NPC、地图、Area、商店、掉落、任务、对话”。
- 岗位筛选使用项目维护的 audience tags，例如策划、关卡、叙事、美术、技术美术、测试、运营；默认“全部”。
- 搜索结果直接显示所属领域和可用动作，不显示技术 ID。

### 路由后的上下文

- Dashboard shell 保存来源 `domainId/featureId/actionId`，不写项目文件。
- 切换到 owner panel 后显示“返回项目功能：角色 > 角色档案与队伍”。
- 返回时恢复原领域、功能、搜索和滚动状态。
- 普通手动切换面板不会伪造来源历史；Domain reload 后允许回到上次稳定 panel，但无效 route state 必须丢弃而不是循环跳转。

## P5 首版功能目录

P5 首版必须按玩家 / 内容生产功能拆分，不能沿用当前 6 个技术系统节点作为人类目录：

| 领域 | 首版功能 | 主要直达所有者 |
| --- | --- | --- |
| 角色与成长 | 角色档案与队伍、属性与装备、武学与技能、经脉成长、敌人与 AI | 数据管理的目标分类、角色批处理预览、角色数据体检、武侠成长编制、经脉树体检 |
| 世界与地图 | 世界结构、Area 与场景、地图导航与小地图、交互点、NPC 与日程 | 世界与 Area 的目标页签、数据管理的 World / Interaction / NPC 分类、项目资产选择 |
| 战斗 | 遭遇与队伍、Classic 战斗、Tactics 战斗、战斗 UI、战斗表现与音频 | 表格配置器的战斗表、数据管理的 Encounter / Skill 分类、战斗检查与受控生成动作 |
| 任务与叙事 | 对话、任务、剧情演出、本地化文本 | 叙事与任务图的目标页签、本地化编制、对应项目资产选择 |
| 物品与经济 | 物品与装备、商店、掉落表、经济平衡、收藏成就与稀有遭遇 | 数据管理的 Item / Equipment 分类、表格配置器的经济表、经济平衡仪表板、收藏成就与稀有遭遇 |
| UI 与表现 | 主菜单与流程 UI、HUD、队伍与背包、任务 UI、小地图、角色与战斗表现 | 对应 Prefab / UIViewDatabase 资产选择、专用预览 / 校验动作；没有安全日常配置入口时明确说明 |
| 进度与发行 | 存档与继续游戏、内容进度、DLC、构建与发布检查 | 项目工具中的只读检查 / 精准验证；不把发布动作暴露为普通配置 |

每个“主要直达所有者”在实现时必须落为一个或多个稳定 route；表中没有要求新增同名大窗口。若现有 owner 缺少精确页签或筛选，只给该 owner 增加最窄 deep-link 接收能力。

## 数据合同

### 人类功能根清单

统一路径：

- 根清单：`docs/project/feature-map.json`
- 显式碎片：`docs/project/features/*.json`

根清单使用严格 schema，至少包含 `schemaVersion`、`projectId`、有序显式 sources 和默认领域。仍禁止 glob、绝对路径、`..` 和大小写冲突。

### 领域

每个 domain 包含：

- 稳定 `id`、中文 `displayName`、一句话 `summary`、`order`；
- `audienceTags` 与中文 `keywords`；
- 显式 `featureIds`，决定可见顺序。

### 功能

每个 feature 包含：

- 稳定 `id`、`domainId`、中文 `displayName`、面向项目人员的 `summary`；
- `capabilities[]`：用户能完成的工作，不写程序集或数据流；
- `audienceTags[]`、`keywords[]`；
- `configurationMode`：`configurable`、`read-only`、`none`；为 `none` 时必须有中文原因；
- `actions[]`：稳定 `id`、中文 `label`、`intent`、`routeId`、是否 primary。

`intent` 只允许 `configure`、`preview`、`validate`、`help`。每个 configurable 功能至少有一个可解析的 configure 动作；read-only 功能至少有 preview / validate / help；none 功能不得伪造配置按钮。

### 路由

route 不在 JSON 中保存菜单或可执行目标。项目 Editor-only adapter 以稳定 routeId 注册：

- 显示名称与目标类型；
- Dashboard module / panel 与可选 subroute；或安全项目资产 / 文档目标；
- 当前可用性与不可用原因；
- navigation action。

目录验证要求每个 action 恰好解析到一个 route；重复、缺失或项目 ID 不匹配均为阻断错误。技术系统引用可以记录 feature / route 的覆盖关系，但不反向把程序集信息投影到人类 UI。

Project Atlas 提供确定的公共合同：

- `[ProjectFeatureRouteProvider(projectId, providerId)]`：声明一个项目 route provider；
- `IProjectFeatureRouteProvider.GetRoutes(ProjectAtlasContext)`：返回当前项目的 immutable route descriptors，不执行动作；
- `ProjectFeatureRouteDescriptor`：保存 `routeId/displayName/kind/available/disabledReason/IEditorToolAction`；
- `ProjectFeatureRouteCatalog`：通过 TypeCache 发现 provider，按 routeId 建立唯一映射并隔离 provider 异常。

同一 routeId 出现在多个 provider、provider 项目不匹配、available=false 却无中文原因，或 descriptor 没有 typed action 时均拒绝进入可执行目录。

## 公共接口与状态流

### editor-ui / Dashboard

以非破坏方式新增接口，不修改现有 `IEditorWorkspaceNavigator`：

- `EditorWorkspaceRoute`：`moduleId/panelId/subrouteId` 与只读来源上下文；
- `IEditorWorkspaceRouteNavigator`：请求切换并应用可选 subroute；
- `IEditorWorkspaceRouteReceiver`：目标 panel 或 embedded view 声明并接收已知 subroute；
- `EditorWorkspaceNavigation.CreateRouteAction(...)`：生成 typed navigation action。

Dashboard 实现 route navigator、返回上下文和 EditorPrefs 状态保存。`EditorWindowWorkspacePanel<TWindow>` 只在 `TWindow` 实现 receiver 时转发 subroute；未知 subroute 返回失败，不回退到反射或字符串方法。

### Project Atlas

- 新增 immutable feature catalog 模型、严格 loader、validator、搜索投影和 route resolution。
- `ProjectFeatureRouteDescriptor` 持有唯一可执行的 `IEditorToolAction`；feature action 只通过 `ProjectFeatureRouteCatalog` 取得该 typed action，不复用字符串 target 执行。
- `ProjectAtlasWorkspacePanel` 改为只绘制 feature catalog；技术 graph 不再参与普通 OnGUI。
- 技术 graph 的 loader、coverage、Markdown projector 和测试继续 headless 使用。

### P5 adapter

- 新增 P5 route provider，集中把 feature routeId 映射到现有 Dashboard panel、subroute、项目资产或说明。
- 数据管理、表格配置、叙事任务、世界编制等 owner 只补充首版目录需要的 deep-link receiver；不复制其编辑 UI。
- `P5WorkbenchCommandCatalog` 继续拥有维护 / 生成 / 测试命令，但普通功能页只引用专用、可解释的动作，不把完整命令中心当作配置入口。

## 兼容、迁移与失败处理

### 兼容

- `project-atlas` package、provider ID、panel ID、技术 catalog 路径、生成 Markdown 路径和 P5 command routing 保持稳定。
- 用户可见 displayName 从“项目图谱”改为“项目功能”；这是明确的产品文案与信息架构变更。
- 原三个技术 UI 页签直接退役，不保留隐藏开关；程序与 Agent 使用原文档 / 自动门。
- 当前 Project Atlas 尚未发布 canonical commit，P5 / POB 使用本地 file pin，因此允许在正式发布前一刀切迁移，不维护已发布 UI 状态兼容层。

### 迁移

1. 先落 feature schema、route API 与合成测试，不改变 P5 当前面板。
2. 建立 P5 功能目录和 route provider，逐项证明 route 可达。
3. 为现有 owner panel 补齐所需 deep link，并建立返回上下文。
4. 将 Project Atlas 面板切换为“项目功能”，把生成 / 覆盖维护动作迁到命令中心。
5. P5 人工验收后，为 POB 建立最小真实功能目录并跑相同通用门。
6. 更新原 Project Atlas Spec As-Built、README、生成文档说明和消费 pin；发布与 SCM 仍需独立授权。

### 失败与恢复

- 无 feature map：显示“项目尚未建立功能导航”，不展示技术 graph 代替，不自动创建文件。
- 目录结构错误：显示简短中文阻断信息；完整字段诊断写入技术日志 / 测试结果。
- route 缺失：对应动作禁用，其他功能仍可浏览和导航。
- deep link 失败：保持目标 owner panel 可用，显示“已打开配置工具，但未能定位到具体分类”，并允许返回原功能；不得执行默认写入。
- Domain reload 或目标 panel 被移除：清除失效 route state，回到项目功能或有效的上次面板。
- 回滚时可恢复旧 Project Atlas UI 包版本；feature JSON 为只读新增数据，不影响生产配置，保留或移除均不得自动修改业务资产。

## 生产约束

- feature catalog 和 route registry 在激活 / 显式刷新时构建 immutable snapshot；OnGUI 不扫描 TypeCache、Assets、Packages 或配置值。
- 搜索和切换只访问内存快照；project-asset 导航只加载已声明的单个资产，不做 `AssetDatabase.Refresh` 或全项目扫描。
- 打开项目功能、选择领域、切换功能、执行 navigation、返回历史均不得产生 Plastic pending。
- 固定界面文案使用简体中文；状态不能仅用颜色；键盘可到达搜索、领域、功能、页签和主要动作。
- 长中文和禁用原因自动换行；技术 ID 不挤占普通布局。
- 420 / 760 / 960 / 1440 point 均保持“领域 → 功能 → 详情与入口”可见结构；不得以 Popup / 下拉替代领域或功能导航。
- 项目功能数据不得包含个人姓名、凭据、绝对路径或生产配置值。

## 精确影响范围

### ZeroEngine

- `com.zerogamestudio.zeroengine.project-atlas/Editor/ProjectAtlasModels.cs`
- `com.zerogamestudio.zeroengine.project-atlas/Editor/ProjectAtlasCatalog.cs`
- `com.zerogamestudio.zeroengine.project-atlas/Editor/ProjectAtlasWorkspacePanel.cs`
- `com.zerogamestudio.zeroengine.project-atlas/Editor/ProjectAtlasProjection.cs`：只在技术投影说明需要同步时更新，不把 feature map 强制生成到技术索引
- `com.zerogamestudio.zeroengine.project-atlas/Editor/ZeroEngineDashboardModule.json`
- `com.zerogamestudio.zeroengine.project-atlas/Tests/Editor/**`
- `com.zerogamestudio.zeroengine.editor-ui/Editor/Workspace/EditorWorkspacePanel.cs` 及对应测试
- `com.zerogamestudio.zeroengine.dashboard/Editor/ZeroEngineDashboard.cs`、状态模型及对应测试
- package 版本、README、静态合同与测试脚本的必需同步

### P5

- `docs/project/feature-map.json`
- `docs/project/features/*.json`
- `Assets/Scripts/Editor/Architecture/ProjectAtlas/**`：feature route provider / resolver
- `Assets/Scripts/Editor/Dashboard/P5DashboardPanelProvider.cs` 与需要 deep link 的现有 owner panel
- `Assets/Scripts/Editor/Dashboard/ZeroEngineDashboardModule.json`：可见名称 / section / 维护工具位置的必要同步
- `Assets/Tests/EditMode/P5DashboardIntegrationTests.cs`
- `Assets/Tests/EditMode/Architecture/P5ProjectAtlasCoverageTests.cs` 或拆出的 feature catalog 专项 fixture
- 原 Project Atlas 生成索引仅在技术引用变化时重新生成

### POB

- 标准 feature map 路径、最小项目 route provider、现有 Dashboard route coverage 的窄增量；不得复制 P5 领域或 route ID。

## 实施顺序与依赖

1. 先以合成项目实现 feature catalog、严格验证、搜索和 route 缺失负向测试。
2. 在 editor-ui / Dashboard 增加非破坏 route navigator、receiver、历史和状态恢复合同。
3. 重写 Project Atlas workspace panel 为人类功能导航，并把技术维护动作移出普通界面。
4. 建立 P5 七个领域、功能清单和 route provider；按“角色”“世界与地图”优先补齐配置深链接，再完成其余领域。
5. 跑 P5 静态、EditMode、live route 与视觉验收；发现没有 owner 的功能时先回到对应领域设计，不用命令中心兜底冒充配置器。
6. 用 POB 最小目录验证通用性，完成 Spec As-Built 和发布前最终审查。

依赖顺序为 `editor-ui route contract → Dashboard route host → Project Atlas feature UI → P5 / POB adapters`。消费项目 runtime assembly 不得引用任何 feature editor adapter。

## 验证路线与通过信号

### ZeroEngine 自动验证

- feature schema：合法目录、重复 ID、失效 domain / feature、未知 intent、配置模式与动作不一致、route 缺失、越界 source 均有明确测试。
- route contract：panel-only、deep link、unknown subroute、receiver 缺失、返回上下文、Domain reload 状态恢复均通过。
- Dashboard / editor-ui 现有必需 lane 不回退；Project Atlas 面板不再包含程序 / Agent 页签或技术筛选。
- 420 / 760 / 960 / 1440 point gallery 覆盖长中文、无结果、route 禁用、紧凑布局和键盘焦点。

### P5 自动验证

- 每个 domain 引用的 feature 存在且唯一；每个 feature action 的 routeId 恰好解析一次。
- configurable 功能存在可用 configure route；none 功能显示原因且没有配置动作。
- 所有 workspace route 指向已注册 descriptor/provider；所有 deep link 在目标 owner 的声明 route 集中存在。
- 点击 navigation 不修改生产配置、不新增 Plastic pending、不执行写入命令。
- 程序 / Agent 技术 catalog 的覆盖与新鲜度门继续通过；`ZGS` 顶栏仍只剩工作台。

### P5 人工场景

1. 新进入项目的角色策划不输入技术词，从“角色”看到角色档案、属性装备、武学技能、经脉和敌人 AI，并可打开对应配置 / 体检入口。
2. 关卡人员从“世界与地图”看到 World、Area、地图导航、小地图、交互点和 NPC，并能直达对应 owner 的目标页签或资产。
3. 叙事人员从“任务与叙事”分别进入对话、任务和本地化，而不是先理解程序集或数据工具。
4. 从任一配置器可返回原功能详情，搜索、岗位筛选和滚动状态保持。
5. 普通界面不出现 framework/project/mixed、active、coverage、assembly、Agent contract 或 JSON 字段路径。

### POB 交叉验证

POB 使用自己的领域名称和 route provider，通过同一 schema、面板、route 和缺失入口门；ZE 代码与测试中不得出现 P5 / POB 业务特判。

## 验收标准

1. `ZGS/工作台` 的默认项目导航可见名称为“项目功能”，不存在“架构与路由”和“Agent 改动合同”普通页签。
2. 程序与 Agent 路由继续由技术 JSON、`system-routing-index.md`、`AGENTS.md` 和自动门承载，功能 UI 不复制技术正文。
3. P5 至少包含本 Spec 定义的七个项目领域；“角色”和“世界与地图”具有完整首版功能清单。
4. 每个功能可见地说明用途、可完成工作、适用岗位、配置状态和可用动作；普通用户无需看到技术 ID 才能区分功能。
5. configurable 功能的主按钮能切换到唯一 owner panel，并在声明 deep link 时定位到正确页签、筛选或对象。
6. 从 route 目标可以返回来源功能，且不会形成嵌套 Dashboard、第二配置表单或重复 EditorWindow 生命周期。
7. JSON 只引用稳定 routeId；菜单、程序集方法、Shell、URL、绝对路径和越界路径不能成为可执行导航。
8. 未知 / 重复 route、缺失 panel、无 receiver、未知 subroute 和缺失资产均 fail closed，错误不会阻断其他功能浏览。
9. 导航、搜索、筛选、返回和刷新不修改项目、不执行写入、不刷新 AssetDatabase、不进入 Play Mode。
10. 目标 panel 的 project-write / destructive 确认保持原强度，功能目录不能覆盖其安全等级。
11. 技术 Project Atlas coverage / freshness、Dashboard、editor-ui、P5 菜单唯一性和现有项目面板测试均不回退。
12. POB 在不复制 P5 功能和不向 ZE 加项目特判的情况下通过同一 feature / route 合同。
13. 420 / 760 / 960 / 1440 point 均使用 DataManager 式三栏；窄窗口通过横向滚动保持领域、功能、详情与主要动作可达，不出现领域或功能下拉。响应式与人工场景验证证明项目人员能从功能语言直达角色、地图、叙事和经济配置，普通 UI 不泄漏技术分类。
14. 实现后本 Spec 更新为 As-Built，记录实际 routes、deep links、测试、视觉证据、发布 / pin / SCM 状态和剩余人工验收。

## 显式需求映射

| 用户要求 | 设计落点 | 验收 |
| --- | --- | --- |
| Agent 和程序员看的不应在这个面板 | 双模型分层、退役技术 UI 页签 | 1、2 |
| 面板是给项目人员看的 | 项目功能目录、任务语言、岗位筛选 | 3、4、13 |
| 能看懂角色下面有什么功能 | 角色与成长的明确功能清单 | 3、4 |
| 能看懂地图下面有什么功能 | 世界与地图的明确功能清单 | 3、4 |
| 能直接传送到对应配置 | typed workspace route、deep link、资产选择 | 5、6、7 |
| 使用控制台内置窗口切换或 Tab | Dashboard panel 切换、功能详情任务页签、route history | 5、6 |
| 全项目通用 | ZE feature schema / route contract、P5 完整样本、POB 交叉门 | 11、12 |

## As-Built（2026-08-24）

- 2026-08-25 按钮对齐修订：Project Atlas 升为 `1.1.4`。领域与功能统一使用 `EditorUiGUILayout.SelectionButton` 的居中标准按钮；功能列表不再把名称和配置状态左右叠绘到按钮上，配置状态保留在右侧详情中，消除按钮视觉重心偏斜。
- 2026-08-25 按钮感修订：Project Atlas 升为 `1.1.3`。功能行不再照搬 DataManager 的无边框列表选中行，而是在保留名称 / 状态左右分列的同时使用 Unity `miniButton` toggle 底板，恢复常态边框、悬停和持续选中反馈；DataManager 继续使用适合数据列表语义的扁平选中行。
- 2026-08-25 截图验收修订：Project Atlas 升为 `1.1.2`。功能列表改用与 DataManager 一致的单行可选项，功能名左对齐、配置状态右对齐并保留整行选中态；详情区岗位改为可换行说明，移除工作台宿主下重复的“项目功能”标题，栏目文案收敛为“工作领域 / 功能说明与入口 / 可完成的工作 / 直接入口”。新增矩形回归证明最小功能列宽下标题与状态互不重叠；P5 正式 Unity Editor 内本地源码验收精确回归 `1/1`、完整 Project Atlas 测试 `35/35` 通过。
- 2026-08-25 布局修订：Project Atlas 已升为 `1.1.1`；`ProjectAtlasWorkspacePanel` 已统一为 DataManager 式三栏，领域、功能、详情分别使用有边框的独立滚动区；两条分隔线可拖动并通过 EditorPrefs 恢复。原 `DrawCompact` / `DrawFeaturePicker` 下拉路径已删除，420 point 等窄宽度改用主体横向滚动保留三栏。
- 本轮新增精确回归 `WorkspacePanel_NarrowWidth_PreservesThreeColumnsWithoutDropdownNavigation`，在 Router 注册的临时 Unity `6000.3.10f1` 工程中 `1/1` 通过；完整 `ZeroEngine.ProjectAtlas.Tests.Editor` 为 `35/35` 通过，CLI 退出码 0、Unity log 无编译或基础设施错误。证据分别位于 `C:\Users\2025\AppData\Local\Temp\ZGSAgentTestResults\ProjectAtlasThreeColumnExact-20260825-01` 与 `C:\Users\2025\AppData\Local\Temp\ZGSAgentTestResults\ProjectAtlasThreeColumnAssembly-20260825-01`。
- 三栏首版已形成正式 Git commit `a46a9c6df0f55300f46443dae44e0777055d1eec`，P5 的 Project Atlas、Dashboard 与 editor-ui 已原子 pin 到该 commit 并完成 Unity 验收；POB 按用户要求暂不变更，仍保留 `4797ab9f6309e1b0fa32741dcf6df0801425d82f`，等待 P5 人工验收后再同步。
- editor-ui 已提供 typed `EditorWorkspaceRoute`、来源上下文、route navigator / receiver 与 action 工厂；Dashboard 已承载 panel 切换、可选 deep link、来源面包屑、返回项目功能、状态恢复和未知 route fail-closed。
- Project Atlas `1.1.0` 已新增严格的人类功能目录 loader / validator / route provider 合同；工作台面板仅显示岗位、领域、功能、能力、配置状态和工作动作，不再展示程序集、包、Agent 合同与技术图谱。
- P5 已落地 7 个领域、34 个功能、33 个唯一动作 route；世界编制 owner 支持 `world-structure`、`area-scenes`、`map-navigation` 三个 deep link；技术索引由受控 CLI 命令 `p5_project_atlas_generate_index` 生成。
- POB 已落地独立的 6 个领域、12 个功能、12 个唯一 route，映射到 `diagnostics-center`、`project-search`、`project-settings`、`config-pipeline` 四个真实 owner；未复制 P5 领域或 route ID。纯 CLI 静态交叉门通过，增强后的测试程序集编译通过。
- ZeroEngine 静态合同通过：Editor UI descriptors `31`、coverage `33`、modules `31`；Dashboard descriptors `9`；`git diff --check` 仅有既有换行提示。Project Atlas `34/34`、Dashboard route registry `41/41`、editor-ui route contract `12/12` 通过；P5 功能目录为 `7 / 34 / 33`。
- P5 人工场景已验证项目人员视图、角色 / 世界功能、世界 deep link、来源面包屑和返回原功能。用户随后要求只用 CLI，因此后续验证未再操作界面。
- POB 路由编译于 18:17:57 得到 `Tundra build success`、程序集重载完成且无编译错误；随后增强后的精确测试在 Uloop `run-tests` 等待 185 秒后超时，且没有生成新的 TestResults XML，已按路由协议恢复为基础设施失败，不能声明该轮 Unity 测试通过。此前较弱版本的功能目录精确用例已有 1/1 通过证据，但不能代替增强用例。
- 终审补充修复了三个以上 provider 重复声明同一 routeId 时第三个声明可能重新进入目录的问题；重复 ID 现在永久隔离，任意数量重复声明均 fail closed，新增用例已包含在功能目录 `12/12` 中。
- POB 对应旧版 Project Atlas pin 已进入 Plastic `cs:17000`。后续同步必须在 POB 内把 Project Atlas、Dashboard 与 editor-ui 原子切到同一正式 commit，不允许交付本机 `file:`；当前 1.1.2 修订仅先更新 P5。

## 自审与推荐结论

- 已推翻错误前提：“共享权威事实”不等于“共享 UI 分类”；人类功能和技术系统采用不同结构。
- 已复用现有 `IEditorWorkspaceNavigator`、Dashboard provider 和 P5 owner panels；新增的是非破坏 deep-link 合同，不是第二套工具中心。
- 已明确配置直达、返回上下文、不可用状态、安全等级和失败恢复，执行者无需再决定导航方案。
- 已保留 Project Atlas 的 headless 技术价值、Agent 自动门和唯一工作台入口，不因 UI 简化削弱架构治理。
- 当前没有未决产品或架构问题；Graduation Gate、显式需求映射和失败路径自审已通过。唯一待闭合验证是正式 Git pin 后的 POB 增强精确测试；终端提交与 pin 已获授权，必须以最终 commit、Plastic changeset 和 Console 结果收尾。
