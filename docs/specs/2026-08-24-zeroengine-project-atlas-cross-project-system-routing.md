# ZeroEngine Project Atlas：跨项目系统图谱与程序 / Agent 路由

- 状态：Implemented
- 实施状态：As-Built（实现与终审完成；canonical Git 提交、消费项目正式 pin 与 POB Packages checkin 已获授权并进入收尾）
- 最后更新：2026-08-24
- ZeroEngine 基线：`08fe92d9e3309fe8897bd7691845cdb081853a20`；Project Atlas `1.1.0`，Dashboard `4.7.0`，editor-ui `1.5.0`
- 已检查消费样本：P5 / POB 当前本地联调 pin 指向本任务 worktree；正式交付要求两个项目改为同一已推送 Git commit
- 设计批准：Approved；用户于 2026-08-24 回复“干吧，正好顺便整理一下项目”
- 执行授权：Authorized；范围为本 Spec 的 ZE 通用包、P5 首接、POB 交叉验证及本地验证
- 终端操作授权：Authorized；2026-08-24 用户要求正式收尾，包含 ZE Git commit / push、P5 / POB 正式 Git pin、POB Packages 两文件 Plastic checkin
- 人类工作台后续设计：`2026-08-24-project-feature-workbench-redesign.md`。该 Spec 覆盖本文件的“三种 UI 视图”和面板信息架构；本文件继续作为 V1 技术图谱、覆盖门与 Agent 路由的 As-Built 记录。

## 结论

“项目图谱”应当是所有 ZeroGameStudio Unity 项目可复用的 ZE 能力，而不是 P5 私有面板。推荐新增可选、Editor-only 的 `com.zerogamestudio.zeroengine.project-atlas` 包：它拥有通用目录模型、引用解析 SPI、覆盖校验、Markdown 投影和 Dashboard 工作区面板；现有 `com.zerogamestudio.zeroengine.dashboard` 继续只做发现、宿主、导航和安全交互，不吸收项目语义。

每个消费项目拥有自己的图谱目录、项目引用解析器、覆盖范围和根 Agent 规则。图谱由同一份组合模型投影为三种阅读方向：面向非程序成员的“项目与功能”、面向程序的“架构与路由”、面向 Agent 的“改动合同”。三种视图不是三份文档或三套事实。

P5 作为首个完整接入项目；POB 作为第二个真实项目和跨 Unity / 目录形态验证样本。只有 P5 与 POB 都通过相同通用合同，Project Atlas 才可声明为跨项目稳定能力；其他项目后续按相同合同接入，不复制 P5 代码或目录假设。

## 目标

1. 在一个可搜索、可导航的项目图谱中整合项目级系统、配置入口、程序边界、依赖关系、验证路径和 Agent 改动合同。
2. 让策划、美术、运营、测试等非程序成员先看到“这个功能做什么、何时使用、在哪里配置、改动会影响什么”，无需先理解程序集和包。
3. 让程序在同一节点看到真实入口、程序集 / 包归属、上下游、数据流、编辑器入口和验证路线。
4. 让 Agent 在同一节点看到必读规则、ZE / 项目归属、允许修改面、禁止绕过的入口、必跑验证和图谱更新触发条件。
5. 用覆盖校验证明项目的关键结构已进入图谱，避免依赖手写数量、记忆或一次性扫描报告。
6. 复用 Dashboard 4.x 的声明式面板、响应式布局、typed action、安全确认和诊断能力，不新增第二个工具中心。

## 非目标

- 不提供玩家运行时功能，不进入 Player 构建，不修改存档、配置值、Scene、Prefab 或生产资源。
- 不扫描源码后猜测“业务意义”、负责人或 Agent 规则；语义由项目明确维护，结构事实由权威源解析。
- 不替代 `AGENTS.md`、架构规则、包清单、程序集策略、配置 Schema、测试目录或业务说明；图谱只组合和引用它们。
- 不把现有配置编辑器、数据工具或业务面板复制成第二个写入口；图谱只说明并导航到唯一所有者。
- V1 不实现可拖拽、自由布局的节点画布，不保存人工节点坐标，也不引入 GraphView、网页前端或远端服务。关系以可读的上游 / 下游 / 适配 / 数据流列表呈现，底层仍使用图模型。
- 不从 JSON 执行任意菜单、反射方法、Shell、网络请求或任意绝对路径。
- 不要求一次把所有项目同时迁移；通用包、P5 首接和 POB 交叉验证分阶段完成。

## 当前状态与问题

### ZeroEngine

- Dashboard `4.6.2` 已从注册的 UPM 包和项目 `Assets/**/Editor/ZeroEngineDashboardModule.json` 发现 schema v2 描述符。
- Dashboard 已通过 editor-ui 的 `IEditorWorkspacePanelProvider` 懒创建面板，并支持 `IEditorWorkspaceFullWidthPanel`、响应式布局、typed action、`navigation/read-only/project-write/destructive` 安全语义和失败隔离。
- Dashboard 官方包目录当前由 `com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/DashboardPackageCatalog.cs` 维护。
- 现有能力足以承载 Project Atlas；不需要修改 Dashboard 描述符 schema、工作区生命周期或 editor-ui 公共布局合同。

### P5

P5 已有可复用的权威事实，但目前分散：

- `docs/rules/runtime-assembly-dependency-policy.json`：运行时程序集和允许依赖。
- `Packages/manifest.json`、`Packages/packages-lock.json`：直接安装包和统一 ZE pin。
- `Config/config-project.json`：配置集、工作簿和表。
- `Assets/Scripts/Editor/Dashboard/ZeroEngineDashboardModule.json` 与 `P5DashboardPanelProvider.cs`：项目面板和入口。
- `Assets/Scripts/Editor/Tools/P5EditorToolsProfileRegistration.cs`：项目工具、生成器、校验器和测试任务。
- `Assets/Scripts/DevTools/Scenarios/P5DevScenarioCatalog.cs`：运行时开发场景。
- `AGENTS.md`、`docs/rules/**`：Agent 入口、架构边界和验证规则。
- `docs/architecture/system-routing-index.md`：现有人工汇总入口，但会随程序集、配置和面板演进而漂移。

当前缺少一个统一模型来回答“一个系统面向团队、程序和 Agent 分别怎么进入”，也没有覆盖门证明上述权威源都被当前汇总索引引用。

P5 还保留大量 `MenuItem("ZGS/...")` 历史入口。它们与工作台并列暴露相同窗口、生成器、校验器和测试命令，使“工作台是唯一人工入口”只停留在文案层，且项目内部仍把菜单字符串当作调用 API。

### POB

POB 已有项目 Dashboard descriptor、多个 `IEditorWorkspacePanelProvider` 和 `POBEditorUiRouteCoverageTests`，证明项目侧 descriptor/provider 合同不依赖 P5 目录。POB 当前 Dashboard pin 较旧，因此交叉验证阶段必须先按 POB 自身发布合同升级到包含 Project Atlas 的同一 ZE commit；不得使用 `file:` 作为最终消费状态。

## 预期使用方式

### 项目与功能（默认）

进入 `ZGS > 工作台 > 项目图谱` 后默认显示非程序视图。每个系统固定回答：

- 它解决什么问题，当前生命周期是什么；
- 谁会使用、哪个岗位负责维护，典型工作流是什么；
- 去哪里查看或配置，哪个入口才是唯一写入所有者；
- 它依赖或影响哪些系统；
- 出现问题时先看哪份说明或诊断入口。

固定界面文案使用简体中文，技术 ID、类型名、程序集名和包名保留原文。非程序视图不常驻源码路径和完整依赖列表，技术信息通过切换到程序视图或展开详情查看。

### 架构与路由

程序视图在同一系统节点显示：

- ZE / 项目 / 混合 / 第三方归属；
- 运行时入口、composition root、程序集、直接包和编辑器表面；
- 上游、下游、适配、生产与消费关系；
- 配置源到运行时消费的数据流；
- 对应的结构规则、测试夹具和验证 lane。

程序视图不自行推断依赖方向；程序集、包和配置详情由引用解析器从权威源读取并显示当前状态。

### Agent 改动合同

Agent 视图在同一系统节点显示：

- 开始修改前必须阅读的根入口和领域规则；
- 能力应落 ZE、项目或第三方适配层的判断结果；
- 允许修改的所有者边界和必须保持的不变量；
- 必须使用的编辑器 / 设备 / SCM 路由引用；
- 最窄验证和图谱更新触发条件。

Project Atlas 不提高自身指令优先级。根 `AGENTS.md` 和其引用的领域规则始终是约束源；图谱中的 Agent 内容是它们的索引和系统级改动摘要。自动门只能验证规则引用存在、被覆盖且生成物新鲜，不能可靠比较自然语言语义；人工或 Agent 审查发现冲突时，以根规则为准并在同一任务修订图谱。

## 已确认设计决定

### 1. ZE 通用包，项目内容留在项目

| 所有者 | 职责 |
| --- | --- |
| `com.zerogamestudio.zeroengine.project-atlas` | schema、加载与组合、不可变图模型、通用引用解析、项目扩展 SPI、诊断、覆盖比较、Markdown 投影、Dashboard panel provider |
| `com.zerogamestudio.zeroengine.dashboard` | 发现 Project Atlas descriptor、托管面板、导航、帮助、状态和安全交互；只在官方包目录增加 Project Atlas，不读取项目图谱语义 |
| 消费项目 | 项目标识、系统语义、项目引用解析器、覆盖提供器、目录碎片、生成索引、Agent 入口和项目级测试 |
| 各权威源 | 程序集、包、配置、面板、工具、场景和规则的真实结构；Project Atlas 不复制其完整内容 |

Project Atlas 单独成包而不是进入 Dashboard，原因是目录模型、投影和覆盖校验需要独立演进并可在没有 Dashboard UI 的测试中使用；Dashboard 保持通用工作台宿主，不承担 Agent 或项目架构语义。

### 2. 一张组合图，三种投影

系统 ID、引用、关系和权威源只存一份。`team`、`program`、`agent` 是同一系统节点的必填阅读投影，不允许分别维护三份系统清单。

每个系统必须包含三种投影。确实没有人工配置的系统，在 `team.configurationMode` 中显式声明 `none` 并说明原因；不得以空数组掩盖缺失内容。

### 3. 单一根目录 + 显式碎片

所有项目使用相同仓库约定：

- 根清单：`docs/architecture/project-atlas.json`
- 领域碎片：`docs/architecture/project-atlas/*.json`
- 人类 / Agent 投影：`docs/architecture/system-routing-index.md`

根清单只保存项目身份、默认显示设置、显式碎片路径和覆盖排除。source 路径统一使用 `/`，记录大小写必须与仓库一致；重复路径及仅大小写不同的冲突均拒绝，以保证 Windows / Linux checkout 得到同一目录。禁止 glob、递归目录发现、绝对路径和 `..` 路径穿越。小项目也保留一个显式碎片，避免根清单同时承担索引和领域内容两种职责。

`system-routing-index.md` 是确定性派生物，顶部必须标记生成来源和 schemaVersion。项目规则和 Agent 入口可以稳定链接此路径；人工不得直接修改生成正文。

### 4. 语义与结构事实分离

- Project Atlas 碎片拥有：系统名称、团队说明、系统关系、归属判断、Agent 改动摘要以及对权威引用的映射。
- 原权威文件拥有：程序集引用、包 pin、配置表、面板注册、工具任务、测试实现和规则正文。
- UI 和 Markdown 只展示解析后的当前事实，不把解析结果回写为第二份手工配置。
- 所有动态数量由解析结果计算；目录和测试不得固化当前系统数、程序集数、配置表数或面板数。

### 5. 覆盖是显式合同

Project Atlas 定义所有 Unity 消费项目都必须给出结论的五个 coverage dimension：

- 项目生产运行时程序集；
- 项目直接安装的 ZeroEngine 包；
- 项目注册的 Dashboard 工作区面板；
- 项目拥有的配置集；
- 项目命名的验证 lane。

通用包可直接枚举项目的直接 ZE 包和 Dashboard panels；“生产运行时程序集”“配置集”“验证 lane”必须由消费项目的 `IProjectAtlasCoverageProvider` 从项目权威源枚举，因为通用包不能猜测哪个 asmdef 属于生产、哪个配置或测试入口是正式合同。provider 对每个标准 dimension 返回 items，或返回带原因的 `not-applicable`；缺 provider、空结果但未声明不适用、或无原因不适用均使覆盖门失败。

项目 provider 可以增加工具 Profile、运行场景、内容管线或其他 namespaced dimension。每个枚举出的 coverage item 必须由一个系统作为 primary owner，或在根清单中以精确类型、目标和原因排除；同一 package、规则或验证引用仍可被其他系统作为关联引用复用。未归属、多个 primary owner、失效排除和无原因排除均使覆盖门失败。

Unity 内置模块、无项目语义的开发依赖和供应商资源不要求逐项成为系统节点；若它们被 coverage provider 枚举，则必须归入“平台与第三方基础”节点或显式排除，不能静默忽略。

### 6. Agent 遵守依赖仓库规则与自动门

每个接入项目的根 `AGENTS.md` 必须：

1. 将 `docs/architecture/system-routing-index.md` 声明为系统定位与 Agent 路由入口；
2. 要求涉及系统、程序集、包、配置集、Dashboard 面板、工具 Profile、验证 lane 或领域规则的新增、移动、重命名、退役时，同一任务更新 Project Atlas 源并重新生成索引；
3. 要求通过项目的 Project Atlas 覆盖与新鲜度测试；
4. 明确图谱不覆盖更高优先级安全、SCM、Unity、设备和发布规则。

面板无法单独强制 Agent 行为，因此“AGENTS 入口 + 图谱覆盖门 + 生成物新鲜度门”三者缺一不可。Project Atlas 包只提供可验证机制，不宣称读取过面板的 Agent 会自动合规。

### 7. `ZGS` 顶层只保留工作台

- 所有 ZE 包和消费项目在 Unity 顶栏的 `ZGS` 根菜单下只允许注册 `ZGS/工作台`；系统图谱、项目面板和项目命令均从工作台内部进入。
- P5 现有命令实现、验证方法和调用链不得因菜单收口而删除。P5 使用 Editor-only 的 typed command catalog 保存原命令身份，由工作台“命令中心”发现、搜索、分类和执行。
- `EditorApplication.ExecuteMenuItem("ZGS/...")` 不再是 P5 内部 API。既有调用和测试迁移到 command catalog；非 `ZGS` 的 Unity / 供应商菜单仍可由受控兼容路由转发。
- 项目写入和清理动作继续使用工作台的 `project-write` / `destructive` 确认，不能因移除菜单而弱化安全级别。命令验证函数在执行前仍生效。
- 根 `AGENTS.md` 和永久测试共同禁止新增 `MenuItem("ZGS/...")`。新增项目命令必须进入 command catalog 与命令中心，不得恢复平行菜单树。

## 目录与数据合同

### 根清单

根清单 schema v1 至少包含：

| 字段 | 合同 |
| --- | --- |
| `schemaVersion` | 整数 `1`；未知版本拒绝加载，不静默降级 |
| `project.id` | 仓库内稳定、小写 ID |
| `project.displayName` | 面向团队的项目名称 |
| `project.summary` | 一段非技术项目说明 |
| `project.rootAgentRule` | 指向根 Agent 入口的 `doc` 引用 ID |
| `sources[]` | 有序、显式、项目相对的领域碎片路径 |
| `coverageExclusions[]` | 精确 `kind/target/reason`；只允许排除 provider 实际枚举的项 |

### 领域碎片

每个领域碎片包含全局唯一的 `references[]` 和 `systems[]`。所有稳定 ID 使用小写 ASCII，允许 `.`、`-`、`_`；比较使用 ordinal，不随系统语言变化。

引用字段：

| 字段 | 合同 |
| --- | --- |
| `id` | 组合目录内唯一的稳定 ID |
| `kind` | 内置类型或项目命名空间类型，例如 `path`、`doc`、`assembly`、`package`、`dashboard-panel`、`p5.config-set` |
| `target` | 由对应 resolver 解释的稳定目标；不得包含可执行脚本 |
| `displayName` | 面向人的名称 |
| `required` | `true` 时未解析即为 error；`false` 时为 warning |
| `coverageOwnerSystemId` | 当引用对应 coverage item 时指定唯一 primary owner；该系统必须在 program 投影引用它，其他系统仍可复用该引用 |

系统字段：

| 字段 | 合同 |
| --- | --- |
| `id/displayName/summary/category/order/keywords/ownerRoles` | 稳定身份、团队文案、分组、负责岗位与确定性排序；不写易漂移的个人姓名 |
| `lifecycle` | `active`、`experimental`、`retiring`、`retired` |
| `ownership` | `framework`、`project`、`mixed`、`vendor` |
| `team` | 用途、使用者、工作流、配置方式、配置与诊断引用；`configurationMode=none` 时必须给出原因 |
| `program` | 入口引用、结构引用、数据流、系统关系和验证引用 |
| `agent` | 必读规则引用、所有者边界、改动摘要、验证引用和更新触发条件 |
| `relations[]` | 指向另一 system ID 的 `depends-on`、`feeds`、`adapts`、`extends` 或 `validates` 有向关系 |

V1 示例只表达结构，不固定 P5 领域：

```json
{
  "schemaVersion": 1,
  "references": [
    {
      "id": "rules.architecture",
      "kind": "doc",
      "target": "docs/rules/architecture.md",
      "displayName": "架构规则",
      "required": true
    },
    {
      "id": "validation.example-boundary",
      "kind": "validation-lane",
      "target": "example-boundary",
      "displayName": "示例边界验证",
      "required": true
    }
  ],
  "systems": [
    {
      "id": "example-system",
      "displayName": "示例系统",
      "summary": "说明玩家或生产流程获得的能力。",
      "category": "gameplay",
      "order": 100,
      "keywords": ["示例"],
      "ownerRoles": ["程序"],
      "lifecycle": "active",
      "ownership": "mixed",
      "team": {
        "audiences": ["策划", "测试"],
        "workflows": ["查看状态"],
        "configurationMode": "none",
        "configurationReason": "示例节点不定义业务配置。",
        "configurationRefs": []
      },
      "program": {
        "entryRefs": [],
        "structureRefs": ["rules.architecture"],
        "dataFlow": [],
        "verificationRefs": ["validation.example-boundary"]
      },
      "agent": {
        "readFirstRefs": ["rules.architecture"],
        "changeBoundary": "通用能力进入 ZE，项目只保留配置与适配。",
        "verificationRefs": ["validation.example-boundary"],
        "updateTriggers": ["程序集或配置入口变化"]
      },
      "relations": []
    }
  ]
}
```

正式 schema 还必须约束引用存在性、枚举值、关系目标、重复 ID 和 `configurationMode` 条件。每个 active / experimental 节点必须有非空 summary、ownerRoles、team 用途，至少一个 program 入口或结构引用，以及非空 agent 必读与验证引用；示例中的空业务引用不能作为正式项目节点的覆盖豁免。

## 公共接口与解析规则

### 纯模型与服务

Project Atlas Editor assembly 使用 `Newtonsoft.Json` 做严格、带字段路径的解析，拒绝重复属性和未知结构字段；未知 schemaVersion 仍单独 fail closed。它提供以下公共职责边界：

- `ProjectAtlasCatalogLoader`：读取固定根清单和显式碎片，返回不可变组合目录或结构诊断。
- `ProjectAtlasValidator`：执行 schema、引用、关系、覆盖、Agent 三投影和生成物新鲜度校验。
- `ProjectAtlasGraph`：按稳定 ID 保存系统、引用、边和诊断；不持有 Unity Object 或业务类型。
- `ProjectAtlasMarkdownProjector`：从已验证图模型生成确定性 Markdown；纯渲染不写盘。
- `ProjectAtlasProjectWriter`：仅在显式 project-write 动作中原子替换 `system-routing-index.md`，写前展示差异摘要，失败不留下半文件。

普通加载、验证和绘制只读。写入器不修改根清单、领域碎片或任何业务配置。

### 引用解析 SPI

`IProjectAtlasReferenceResolver` 按唯一 `kind` 解析一个引用，返回：显示值、存在 / 缺失 / 不适用状态、权威来源、可选的 typed navigation，以及诊断。resolver 不改变项目状态。

Project Atlas 内置：

- `path` / `doc`：仅允许项目根内路径；选择或打开现有文件，不创建文件；
- `assembly`：读取 `.asmdef` 并展示名称、路径和直接引用；
- `package`：只读取项目 manifest / lock，展示直接性、版本和 pin 状态，不发起 Package Manager 请求；
- `dashboard-panel`：读取项目 `Assets/**/Editor/ZeroEngineDashboardModule.json`，验证 `moduleId/panelId/providerId`；
- `validation-lane`：展示项目声明的稳定验证 ID，本身不执行测试。

`IProjectAtlasCoverageProvider` 以稳定 `projectId/providerId/dimensionId` 枚举项目权威源中的 required coverage item，或返回带原因的 `not-applicable`。P5 等消费项目只为通用 resolver 无法理解的类型增加 namespaced resolver，例如 `p5.config-set`、`p5.editor-tool-profile`、`p5.dev-scenario`。

resolver 和 coverage provider 通过 TypeCache 在首次激活或显式刷新时发现，Domain reload 后缓存失效。抽象类型、构造异常、重复 kind / provider ID 或 projectId 不匹配均产生可定位 error；不得回退为任意反射调用。发现结果在当前 Domain 缓存，`OnGUI` 不重复扫描类型或文件。

### 安全边界

- catalog 中不存在命令、菜单路径、程序集限定方法名、Shell 或 URL 执行字段。
- 结构 schema、include 和路径安全验证必须先于任何 resolver 调用；未通过的 target 不进入解析阶段。
- typed navigation 只能由已编译 resolver 返回，并继承 editor-ui 的安全等级；JSON 不能把动作伪装为只读。
- 项目相对路径规范化后必须仍位于项目根；绝对路径、UNC、驱动器路径和 `..` 越界均拒绝。include 或引用路径任一段为符号链接 / reparse point 时直接拒绝，不尝试跟随到项目根外。
- 加载、选择节点、切换视图和刷新目录不写盘、不刷新 AssetDatabase、不调用 Package Manager、不联网、不进入 Play Mode。
- “重新生成路由索引”是唯一内置 project-write 动作，必须显式点击并确认；打开面板不自动修复或生成。

## 面板信息架构

Project Atlas 通过自己的 `Editor/ZeroEngineDashboardModule.json` 声明一个 full-width 工作区面板，provider 只引用 editor-ui；包本身不依赖 Dashboard assembly。未安装 Dashboard 时包仍可编译和运行测试，descriptor 保持无副作用。

稳定绑定为：`moduleId=com.zerogamestudio.zeroengine.project-atlas`、`displayName=项目图谱`、`scope=universal`、`panelId=project-atlas`、`providerId=zeroengine.project-atlas`、`availability=always`、`safety=project-write`。模块 `order=5` 是可逆的首版默认，用于排在项目业务工具前成为团队导航入口，不参与数据合同。面板整体标记 project-write 是因为包含生成索引动作；浏览和导航仍是只读 / navigation，生成按钮在 Play Mode 禁用并使用独立确认。

面板布局：

1. 顶部：项目名称、目录状态、覆盖状态、显式刷新、生成索引。
2. 视图切换：`项目与功能`、`架构与路由`、`Agent 改动合同`。
3. 系统导航：按 category 分组，支持名称、ID、关键词、引用目标搜索，并可按 ownership / lifecycle / diagnostic 过滤。
4. 系统详情：显示当前视图的投影和可点击关系；上游 / 下游节点在同一面板切换，不打开第二窗口。
5. 诊断：缺失规则引用、覆盖缺口、重复 ID、失效关系和生成物过期集中显示，包含来源碎片和字段路径。

标准 / 宽窗口使用系统列表 + 详情双栏；紧凑宽度切换为顶部系统选择器 + 单列详情。阈值和控件沿用 editor-ui 现有响应式合同，不新增项目可调像素值。搜索、选择、视图和滚动只保存在现有 workspace 本机状态，不写项目文件。

## 数据与状态流

```text
项目图谱根清单 + 显式领域碎片 + 项目权威源
                    ↓
        Loader → 结构 / 路径 Validator
                    ↓
              Resolver / Coverage
                    ↓
           语义 / 覆盖 Validator
                    ↓
       不可变 ProjectAtlasGraph + Diagnostics
             ↙                         ↘
Dashboard 三视图（只读）       Markdown 确定性投影
                                      ↓ 显式确认
                 docs/architecture/system-routing-index.md
                                      ↓
                    AGENTS 入口 + 项目覆盖 / 新鲜度门
```

同一刷新周期只构造一个不可变 graph snapshot；三种 UI 视图和 Markdown 使用该 snapshot。加载失败时不展示旧快照冒充当前状态：面板显示诊断，已提交的 Markdown 仍作为只读历史入口，但新鲜度门失败。

## 精确影响范围

### ZeroEngine 首版

新增：

- `com.zerogamestudio.zeroengine.project-atlas/package.json`、README、目录和配对 `.meta`
- `com.zerogamestudio.zeroengine.project-atlas/Editor/**`：模型、严格 JSON schema / 加载、解析、覆盖、诊断、Markdown、写入器和 workspace provider
- `com.zerogamestudio.zeroengine.project-atlas/Editor/ZeroEngineDashboardModule.json`
- `com.zerogamestudio.zeroengine.project-atlas/Tests/Editor/**`
- `Tools/Tests/run-project-atlas-editmode-tests.ps1`

更新：

- `com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/DashboardPackageCatalog.cs`：增加官方 Project Atlas 包及 editor-ui 依赖闭包
- Dashboard package / README / tests：同步包目录变化和版本
- `Tools/Tests/Test-ZeroEngineDashboardDescriptors.ps1`
- `Tools/Tests/Test-ZeroEngineEditorUiContract.ps1`
- `Tools/Tests/run-dashboard-editmode-tests.ps1`：with-modules lane 纳入 Project Atlas descriptor/provider
- `.github/workflows/tests.yml`：仅在现有自动发现不足以执行新包最窄 lane 时增加对应 job / path；不得削弱现有检查

新包初始版本采用 `1.0.0`，这是新 UPM 包的可逆常规默认；在当前基线只直接依赖 editor-ui `1.4.0` 和仓库已采用的 `com.unity.nuget.newtonsoft-json` `3.2.1`，不依赖任何 ZE 业务包或 Dashboard assembly。Dashboard 官方包目录只需要声明同仓 editor-ui 闭包；registry 依赖由 Project Atlas 自身 package.json 声明。Dashboard 按仓库版本规则做最小兼容版本增量。若实现基线已前移，依赖和版本使用该基线的同提交兼容值，并在本 Spec As-Built 中记录，不形成跨提交版本分裂。

### P5 首接

- `Packages/manifest.json`、`Packages/packages-lock.json`：Project Atlas 与其他 ZE 包 pin 到同一测试 commit
- `docs/architecture/project-atlas.json`
- `docs/architecture/project-atlas/*.json`
- `docs/architecture/system-routing-index.md`：由现有手写入口迁移为生成投影，保持稳定路径
- `Assets/Scripts/Editor/Architecture/ProjectAtlas/**`：P5 resolver / coverage provider；沿用 `ZGS.Editor.Architecture`，增加 `ZeroEngine.ProjectAtlas.Editor` 以及读取正式工具 / 场景元数据所需的最窄 P5 assembly 引用，禁止形成反向引用或循环
- `Assets/Scripts/DevTools/Scenarios/P5DevScenarioCatalog.cs`：增加只返回已声明 descriptor / ID、不会调用 `EnsureRegistered()` 的纯只读查询；Atlas 刷新不得注册场景或安装退出处理器
- `Assets/Tests/EditMode/Architecture/**`：目录、覆盖、新鲜度、AGENTS 入口和引用解析测试
- `AGENTS.md`、`docs/rules/architecture.md`：只增加图谱入口、更新触发和优先级边界，不复制系统清单

P5 的现有 Dashboard descriptor/provider、程序集策略、Config 项目清单、工具注册和 Dev Scenario catalog 保持权威源；只在它们缺少稳定、无副作用的只读查询时做最窄补充，不把其内容搬入 Project Atlas 包。coverage provider 可构造 `P5EditorToolsProfileRegistration.CreateProfile()` 的纯描述模型，但不得调用任务、生成器、测试执行或 `P5DevScenarioCatalog.EnsureRegistered()`。

### POB 交叉验证

- 在独立 POB 任务中将 Dashboard、editor-ui、Project Atlas 与其他本次涉及 ZE 包统一 pin 到同一测试 commit。
- 新增同一标准路径的 POB root catalog、领域碎片和生成索引；resolver / coverage provider 放入 `Assets/Assets/_Scripts/_POB/Editor/Tools/Dashboard/ProjectAtlas/**`，沿用现有 `POB.Editor` Editor-only assembly。
- 在 `Assets/Assets/_Scripts/_POB/Tests/Editor/ZeroEngineDashboard/` 新增 `POBProjectAtlasCoverageTests.cs`；现有 `POBEditorUiRouteCoverageTests.cs` 只增加 Project Atlas descriptor/provider 的窄路由断言，避免继续膨胀为目录语义测试。
- 不在 ZE 中加入 `POB.*` 引用、POB 目录常量或 POB 业务规则。

其他项目的批量接入、发布和 SCM 提交不属于首版执行范围，必须各自获得授权并遵守项目路由。

## 兼容、迁移与失败处理

### 兼容

- Dashboard schema v2、provider ID、既有 panel ID 和项目面板不变；Project Atlas 作为普通贡献包接入。P5 历史 `ZGS` 子菜单是本次明确退役的兼容面，原命令身份只作为 command catalog 的迁移键与搜索关键词保留。
- 未安装 Project Atlas 的项目行为不变。
- 安装包但没有根清单时，面板显示可读的“尚未接入”诊断，不创建示例目录、不写项目。
- `schemaVersion: 1` 只接受已知字段和枚举；未知 schemaVersion fail closed。V1 不做静默自动迁移。
- 已生成 Markdown 在 Unity / Dashboard 不可用时仍可由人和 Agent 阅读，但项目验证必须能识别其是否过期。

### P5 迁移

1. 先建立目录与 P5 resolver / coverage 测试，保持现有 `system-routing-index.md` 未变。
2. 按现有权威源填充系统节点；不得根据旧文档中的固定计数反推覆盖完成。
3. 在临时路径生成 Markdown，与旧索引逐系统核对；缺失内容回到 catalog 或引用解析器修正。
4. 只有覆盖门通过后，才用生成结果替换既有 `system-routing-index.md`，并更新 AGENTS 入口。
5. 迁移不删除原权威规则；旧索引中仍有价值但不属于系统图谱的长说明移回对应 `docs/rules/**`，图谱只保留链接和摘要。

### 失败与恢复

- 根清单缺失：显示未接入；不算包错误。
- JSON、include、ID 或关系错误：拒绝构造 graph，显示字段级 error，不自动修改源文件。
- required reference 缺失或 coverage 不完整：graph 可用于诊断，但覆盖门失败，生成动作禁用。
- optional reference 缺失：显示 warning，允许生成并在 Markdown 中保留可见警告。
- resolver 异常：隔离到对应 kind，其他引用继续解析；异常 kind 的 required refs 使覆盖门失败。
- Markdown 写入失败：保留原文件，临时文件由写入器清理；不得留下截断结果。
- 项目结构变化但图谱未更新：新鲜度或 coverage 测试失败，必须在同一任务补齐，不自动删除节点。

### 回滚

- ZE 可移除 Project Atlas 包和 Dashboard 官方目录条目；Dashboard / editor-ui 既有功能不迁移数据，因此无需恢复用户状态。
- 消费项目可成对回退 manifest / lock，并保留最后生成的 Markdown 供只读查询；不得只回退一个 pin 文件。
- 若项目决定退出图谱，先把生成索引转回明确所有者的普通文档，再移除 AGENTS 强制入口、catalog 和 adapter；不由卸包自动删除项目文件。
- 不重写 Git / Plastic 历史，不清理用户工作区，不自动恢复生产配置。

## 生产约束

### 性能与可靠性

- `OnGUI` 只绘制当前 immutable snapshot，不扫描 TypeCache、Assets、Packages 或项目文件。
- 目录读取和 resolver 执行只发生在首次激活、Domain 变化或显式刷新；切换系统和三种视图不重新解析。
- 显式碎片按根清单顺序加载，节点按 `order` 后 `id` 稳定排序；Markdown 固定使用 UTF-8 无 BOM、LF 换行且不写生成时间，使相同输入逐字节确定。
- 任何单个 resolver 或引用失败不得使 Dashboard 其他面板、系统页和包管理不可用。

### 安全与隐私

- 默认离线；不收集遥测，不上传项目结构，不读取凭据、用户目录或项目根外文件。
- UI 不显示文件内容中的密钥或配置值，只展示 catalog 明确声明的摘要和结构元数据。
- 生成文档不嵌入绝对路径、用户名、本机包缓存路径或临时目录。
- 所有 project-write 仍通过 editor-ui 安全确认；没有 destructive 内置动作。

### 可访问性与本地化

- 状态同时使用文字和图标 / 颜色，不用颜色作为唯一信号。
- 所有搜索、筛选、刷新、生成、关系和导航控件提供非空简体中文 tooltip。
- 长路径和技术 ID 可完整复制；视觉截断时 tooltip 保留全文。
- V1 固定中文界面，不引入运行时 Localization 依赖；catalog 业务名称由项目负责。

## 实施顺序与依赖

1. 在 ZE 新包实现 schema/model、显式 include、纯 loader/validator、path 安全和确定性 Markdown 单元测试。
2. 实现内置 resolver、项目扩展 SPI、coverage 比较和失败隔离；先用两个不含 P5 / POB 类型的合成项目 fixture 证明模型通用。
3. 实现 full-width Dashboard panel provider、三视图和响应式导航；接入现有 editor-ui，不修改 Dashboard schema。
4. 将新包加入 Dashboard 官方包目录、静态合同和最窄测试 lane；完成 ZE 包 README 和版本同步。
5. 在 P5 建立项目 catalog、resolver / coverage provider 和生成投影；通过 P5 覆盖后更新 AGENTS 入口。
6. 在独立 POB 授权任务中按相同合同接入；不得复制 P5 adapter。POB 通过后才能把跨项目稳定性验收标记完成。
7. 在 P5 将所有历史 `ZGS` 子菜单迁移到 typed command catalog，新增工作台“命令中心”，迁移内部菜单字符串调用并建立“唯一顶栏入口”永久门；POB 当前没有项目侧 `ZGS` 子菜单，无需复制 P5 命令层。
8. 实现和验证完成后把同一 Spec 更新为 As-Built；提交、PR、发布、P5 Plastic、POB Plastic 和其他项目 rollout 均需各自授权。

依赖顺序固定为 `editor-ui + Newtonsoft.Json → project-atlas`，Dashboard 只在运行时发现 Project Atlas descriptor。Project Atlas 与 Dashboard 不得引用任何消费项目程序集；消费项目 adapter 保持 Editor-only，可引用读取权威元数据所需的最窄项目程序集，但项目运行时不得反向引用 adapter 或 Project Atlas Editor assembly。

## 验证路线与通过信号

### ZeroEngine

- `pwsh -File Tools/Tests/Test-ZeroEngineDashboardDescriptors.ps1`：退出码 0；新 descriptor schema、provider ID、安全级别和“业务包不依赖 Dashboard”合同通过。
- `pwsh -File Tools/Tests/Test-ZeroEngineEditorUiContract.ps1`：退出码 0；Project Atlas package / asmdef / editor-ui 版本合同与官方包目录一致。
- `pwsh -File Tools/Tests/run-project-atlas-editmode-tests.ps1`：退出码 0；结果 XML 存在、`total > 0`、`failed = 0`，Unity log 无 compile error。
- `pwsh -File Tools/Tests/run-dashboard-editmode-tests.ps1`：dashboard-only 与 with-modules lane 均通过；安装新包后只新增一个有效 Atlas panel，无 descriptor / provider diagnostic。
- `git diff --check`：退出码 0；完整任务 diff 中 package、README、版本、测试、`.meta` 和 Spec 一致。

合成 fixture 至少覆盖：最小项目、多个碎片、跨碎片关系、重复 / 大小写冲突的 source 路径、重复 ID、未知 schema、绝对 / 穿越 / 符号链接越界、required / optional 引用、重复 resolver、resolver 异常、coverage 排除、确定性 Markdown、无根清单和过期生成物。

### P5

使用 P5 `AGENTS.md` 指定的 Unity workspace router，运行 `ZGS.Tests.EditMode` 的 `Unit;Boundary` 最窄 lane；最低证据为退出码 0、结果 XML `total > 0/failed = 0`、命中 Project Atlas fixture、Unity log 无 compile error。

P5 专项断言：

- runtime assembly policy 中每个非退役生产程序集有且仅有一个 primary owner，或以有效原因排除；
- 每个直接 ZE 包、P5 Dashboard panel、config set 和命名验证 lane 有且仅有一个 primary owner；
- P5 工具 Profile 与 Dev Scenario 的项目 coverage provider 无缺口；
- `AGENTS.md` 包含稳定图谱入口和更新触发合同；
- 重新生成后 `system-routing-index.md` 无 diff，或当前任务同时提交预期 diff；
- 验证前后用户 / 设计师生产配置哈希和任务外 Plastic pending 不变。
- 已加载程序集中的 `ZGS/...` `MenuItem` 去重后严格等于 `ZGS/工作台`；P5 `Assets/Scripts/**/*.cs` 不再声明 `MenuItem`，也不直接调用 `EditorApplication.ExecuteMenuItem` 绕过命令路由。
- command catalog 无重复执行方法、缺失执行方法、非法验证签名或安全级别冲突；工作台“命令中心”可搜索并执行迁移后的窗口、生成、校验和测试命令。

Live Editor 通过 `ZGS > 工作台 > 项目图谱` 抽验：三种视图可切换；搜索可命中中文名、system ID 和引用；关系导航留在同一面板；打开 / 刷新不产生项目 pending；只有显式确认生成索引才产生目标 Markdown 变更；最终 Console 0 error。

### POB

按 POB 自身 Unity 与 Plastic 路由运行 Project Atlas fixture 和现有 Dashboard route coverage。通过信号为：同一通用包在 POB 目录形态下加载；POB panels / 配置 / 验证覆盖无缺口；ZE assembly 不出现 `POB.*` 或 POB 路径；打开面板不改变生产配置或任务外 pending。

## 验收标准

1. 仓库新增独立、Editor-only 的 `com.zerogamestudio.zeroengine.project-atlas`，只直接依赖 editor-ui 与 Newtonsoft.Json；Dashboard 与 Project Atlas 均不引用 ZE 业务包、P5、POB 或其他消费项目程序集。
2. Project Atlas 通过现有 Dashboard schema v2 贡献唯一 full-width panel；不修改 Dashboard schema，不新增顶栏或第二个工作台。
3. 同一组合 graph 为每个 active / experimental 系统提供完整 `team/program/agent` 三投影；三种视图使用相同 system ID、引用和关系。
4. 默认“项目与功能”视图对每个系统可见地回答用途、使用者、负责岗位、工作流、配置入口和影响关系；无配置系统必须显示原因，不能呈现空白配置区。
5. “架构与路由”视图从当前权威源解析并显示归属、入口、程序集 / 包、数据流、上下游和验证引用，不使用手写结构计数。
6. “Agent 改动合同”视图显示根入口、必读规则、ZE / 项目边界、验证和更新触发；项目根 `AGENTS.md` 同时绑定图谱入口、更新义务和覆盖门。
7. 所有项目使用 `docs/architecture/project-atlas.json`、显式碎片目录和 `docs/architecture/system-routing-index.md`；无 glob、绝对路径或越界 include。
8. 相同 catalog 和权威源生成的 Markdown 逐字节确定；人工修改或结构变化导致的新鲜度门失败。
9. required reference、重复系统 / 引用 ID、失效关系、未归属 coverage item、多个 primary owner、无效排除和规则入口缺失均产生可定位 error 并使项目门失败。
10. optional reference 缺失只产生可见 warning；resolver 异常被隔离，Dashboard 其他面板与 Project Atlas 其他 reference kind 仍可用。
11. catalog 无任何任意执行字段；恶意绝对路径、`..`、符号链接越界、伪造菜单 / 方法 / Shell 目标均不能被解析或执行。
12. 打开、搜索、切换视图、选择节点和刷新不写项目、不联网、不进入 Play Mode、不注册运行场景 / 工具、不执行任务、Package Manager 或 AssetDatabase refresh；索引生成只来自显式确认的 project-write 动作。
13. P5 coverage 至少覆盖其非退役生产程序集、直接 ZE 包、Dashboard panels、config sets、命名验证 lanes，以及项目 provider 声明的工具 / 场景项；当前手写索引迁移为同路径生成投影。
14. POB 在不复制 P5 adapter、不修改 ZE 为 POB 特判的前提下，通过相同 schema、SPI、Dashboard panel 和覆盖门；完成前 Project Atlas 不标记为跨项目稳定。
15. 420 / 760 / 960 / 1440 point 的布局测试或 gallery 证据表明系统导航、三视图、长中文、长 ID、warning/error 和主要操作无重叠、不可达或仅颜色表达状态。
16. ZE 静态合同、Project Atlas EditMode、Dashboard 两条 lane、P5 targeted EditMode、P5 live panel 和 POB targeted / route coverage 均满足本 Spec 的通过信号。
17. 实现 diff 只包含本 Spec 列出的 ZE / P5 / POB 路径及其必需 `.meta`、版本和测试同步；用户生产配置、任务外 pending、生成缓存和本机状态不进入提交。
18. Spec 在实现后更新为 As-Built，记录实际 package 版本、canonical commit、消费 pin、测试证据、偏差和剩余 rollout；没有相应终端授权时保持 pending，不宣称已发布或关闭。
19. P5 已加载的 `ZGS` 顶栏入口严格只剩 `ZGS/工作台`；历史项目命令均由工作台“命令中心”的 typed catalog 承载，内部调用不依赖已退役菜单，新增永久测试阻止平行 `ZGS` 子菜单回归。

## 自审记录

### 本轮修订

- 将原先可能落在 P5 的设计改为 ZE 独立通用包，P5 / POB 只保留项目目录和 adapter。
- 将“面板让 Agent 遵守”的弱假设收口为根 AGENTS、覆盖门和生成物新鲜度门三重合同。
- 将 resolver 调整到结构 / 路径安全校验之后，并补齐严格 JSON 依赖、越界路径和任意执行负向门。
- 将 coverage 从“引用一次”修订为“唯一 primary owner + 可复用关联引用”，避免 Core 包等共享基础被错误限制。
- 增加纯只读项目元数据要求，明确 Atlas 刷新不得注册 P5 场景、工具或执行任务。

### 显式需求映射

| 用户要求 | 设计落点 | 验收 |
| --- | --- | --- |
| 图谱应全项目通用 | ZE 独立包、项目 adapter、P5 + POB 双项目稳定门 | 1、2、7、14 |
| 程序与 Agent 两个方向 | 同一 graph 的 program / agent 投影 | 3、5、6 |
| 非程序成员快速理解功能和配置 | 默认 team 视图与唯一配置所有者导航 | 3、4、15 |
| 全项目整合到一起 | 固定根清单、显式碎片、通用 + 项目 coverage | 7、9、13 |
| 后续 Agent 开发必须遵守 | AGENTS 入口、更新触发、coverage / freshness gate | 6、8、9 |
| 像最新 ZE 面板 | Dashboard schema v2、full-width provider、editor-ui 响应式和安全动作 | 2、12、15 |
| `ZGS` 下只保留工作台 | typed command catalog、命令中心、内部调用迁移与唯一入口门 | 19 |

### 复核结论

- 归属复核：框架、schema、投影和通用 UI 在 ZE；项目语义、权威源和规则不下沉，依赖方向成立。
- 单一事实源复核：图谱只拥有语义映射；程序集、包、配置、面板、工具和规则继续由原文件拥有，避免第二份结构事实。
- Agent 合规复核：已明确面板不是强制机制，必须由根 AGENTS 与自动门闭环。
- 安全复核：JSON 无执行能力；路径限制、resolver 隔离、只读刷新和显式 project-write 均有负向验收。
- 可实施性复核：现有 Dashboard 4.6.2 / editor-ui 1.4.0 已具备所需 panel、导航、安全和响应式合同；新增包不要求 Dashboard schema 改造。
- 跨项目复核：P5 与 POB 已存在不同目录和项目 provider 形态；双项目门能识别 P5 特化泄漏。
- 范围复核：V1 不加入自由画布、远端知识库、自动语义推断、运行时能力或配置写入口。

当前自审未发现 Critical 或 Important 设计缺口。用户已批准设计并授权按本 Spec 完成本地实现与验证；提交、PR、发布、P5 / POB Plastic checkin 和其他项目 rollout 仍未授权。

## As-Built（2026-08-24）

### 实际落地

- 新增 Editor-only `com.zerogamestudio.zeroengine.project-atlas`，当前版本 `1.1.5`，直接依赖 editor-ui `1.5.0` 与 Newtonsoft.Json `3.2.1`；实现严格技术目录、覆盖与 Markdown 投影，以及独立的人类功能目录与 typed route 合同。
- Dashboard 从 `4.6.2` 增量到 `4.7.0`，官方目录、工作流和静态合同纳入 Project Atlas，并增加来源上下文、返回入口和 owner deep link 宿主；既有 provider 生命周期未改变。
- P5 已接入六个领域碎片、项目 resolver / coverage provider、只读 Dev Scenario ID 查询、根 Agent 合同、生成索引和永久覆盖 / 新鲜度测试。
- P5 新增 Editor-only `ZGS.Editor.CommandRouting`，将原有 134 个项目 `MenuItem` 注册迁移为 typed command catalog，并在工作台新增 full-width“命令中心”；Formula / TCE 两个项目内包的 5 个残留入口也由 P5 adapter 接回同一目录。原执行方法、校验方法和内部字符串调用均保留，但不再向 Unity 顶栏注册子菜单。
- P5 根 Agent 合同和永久守卫现已共同约束：`ZGS` 顶栏唯一可见入口只能是 `ZGS/工作台`，新增项目命令必须进入 command catalog 与命令中心；Project Atlas tooling 碎片也已把 `p5.zeroengine.adapters/command-center` 纳入唯一 owner 和生成索引。
- POB 已接入四个领域碎片、适配其 `Assets/Assets` 目录的 resolver / coverage provider、根 Agent 合同、生成索引和永久覆盖 / route 测试。交叉验证同时发现并修正了 POB Dashboard descriptor 中重复的 `scope/projectId/projectDisplayName` 尾部字段；严格 JSON 现在可唯一解析。
- ZE 未引入 P5 / POB 程序集引用、项目目录常量或业务特判；一次性跨项目验证代码已删除。

### 验证证据

- ZE Project Atlas 1.1.5：`35/35` 通过，其中功能目录 `12/12`、技术目录与响应式布局 `23/23`；Dashboard route registry `41/41`、editor-ui route contract `12/12` 通过；Dashboard descriptors `9`、Editor UI 静态合同 `descriptors=31 / coverage=33 / modules=31` 通过。
- Dashboard 必需 lane：dashboard-only `129 total / 128 passed / 1 existing ignored`，with-modules `375/375` 通过；结果位于 `C:\Users\2025\AppData\Local\Temp\zeroengine-dashboard-results-45fc63b9f6c84b39bde608b4377e5c6f`。额外 modules-only 探索 lane 曾挂起并被停止，它不是本 Spec 的必需 lane。
- P5 永久 `P5ProjectAtlasCoverageTests`：原始 XML lane `3/3` 通过，结果位于 `C:\Users\2025\AppData\Local\Temp\ZGSAgentTestResults\P5\ProjectAtlasFinal`，结果 XML 与 Unity log 均无失败 / 编译错误；脱敏修复后又通过 live 精准生成 `1/1`，并将三个永久测试逐项各执行 `1/1`、全部通过。当前共享 Console 仍保留另一并行全量回归产生的预期错误日志，因此未据此声明最终 Console 0 error。
- P5 菜单收口后 live scripts refresh / compile 为 `completed`、`failed=false`、`errors=[]`；`P5DashboardIntegrationTests` `18/18` 通过，其中包含 typed catalog 零诊断、源码禁用旧入口、provider 全覆盖和已加载 `ZGS` 根菜单唯一性。官方 Unity `menu` 实际枚举只返回一个 `ZGS` 项，即 `ZGS/工作台`，并已成功执行该入口打开工作台。
- 受影响回归中，`ArchitectureHardeningGuardTests`、`FrameworkGraduationBaselineGuardTests`、`CharacterAuthoringProfileTests` 合计 `43/43` 通过；Formula / TCE 边界与 profile 合计 `8/8` 通过；更新 Atlas tooling 并用正式 writer 重建索引后，三个永久 Atlas 测试逐项 `1/1` 通过。最后一轮从本任务 Console cursor 起读取 error 为 `0` 条。
- POB 实际目录交叉门：`4 systems / 94 references / 84 coverage items`，唯一 owner、引用解析、严格 JSON、生成新鲜度和本机路径脱敏全部通过；最终结果位于 `C:\Users\2025\AppData\Local\Temp\zeroengine-project-atlas-pob-redaction-8658e23f67af4d76b78e6e744da92560`。该门使用正式 ZE loader / validator / writer 和与 POB 永久 provider 等价的临时 test adapter，验证后已删除临时代码。
- 最终 ZE `git diff --check` 通过，仅有仓库换行提示；P5 / POB 的生产配置与任务外 pending 均未回滚、未纳入任何提交。

### 偏差、风险与待授权事项

- P5 与 POB 当前仍以本机 `file:D:/unity/projects/_worktrees/zeroengine-project-atlas-20260824/...` 接入用于提交前验证；本次已获终端授权，必须在交付前把 manifest / lock 成对切到同一已推送 canonical commit，并完成规定验证与 POB 精确 checkin。
- POB 的已打开 Editor 持续处于 Unity CLI Loop 报告的 `Reloading Domain` 状态；共享工作区已按路由器冻结 / 停靠合同尝试一次专门恢复，结束后再次调用仍在 dispatch 前被同一状态拒绝。按共享工作区契约未自行强杀或竞争该 Editor。因此 POB 永久 fixture / route test 尚未在该 live Editor 中执行，当前证据为实际目录跨项目门与静态合同，不能宣称 POB live Unity 门已通过。
- P5 已 live 执行 `ZGS/工作台`，但命令中心的人工逐项点击抽验及 420 / 760 / 960 / 1440 point gallery 未在本次本地自动验证中单独留存；实现复用了已验证的 editor-ui full-width / responsive 合同，但验收项 15 的完整视觉证据仍需在正式 pin 后补证。
- 未执行 commit、push、PR、发布、Plastic checkin 或其他项目 rollout；这些终端动作保持 pending。

### 实现自审修订

- 将三栏导航的硬最小宽度改为按可用空间等比收缩；420 point 不再为了显示完整长标题强制撑宽，按钮和栏头使用省略号并保留完整 Tooltip，极窄窗口仍保留三栏和横向兜底。
- 修复 active / experimental lifecycle 仅靠前后空白绕过 program / agent 必填门的问题，并新增永久负向回归测试。
- 用第二个真实项目的严格加载暴露并修正重复 Dashboard JSON 字段，避免宽松解析器“最后值覆盖”掩盖结构漂移。
- 修复本机绝对 `file:` 包 pin 被投影到生成 Markdown 的隐私 / 可移植性问题；绝对本地依赖统一渲染为 `file:<local>`，并新增永久脱敏回归测试。
- 修复三个以上 provider 重复声明同一功能 routeId 时第三个声明可能重新进入目录的问题；现在重复 ID 集合永久隔离，任意数量重复声明均 fail closed，并有永久回归测试。
- 清理全部 `POB_PROJECT_ATLAS_RESOLVED` 临时编译门、一次性索引生成测试和跨项目 test adapter；最终源中只保留正式 provider、resolver、目录、生成物与永久门。
- 最终自审未发现新的 Critical 或 Important 实现问题；未完成项均是明确的发布 / live 验证授权或共享 Editor 状态，不以“已发布”或“全部验收通过”表述。
