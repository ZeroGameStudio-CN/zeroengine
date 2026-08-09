# ZeroEngine Dashboard 适配器归位与视觉优化

- 状态：Implemented
- 最后更新：2026-08-10
- 基线：ZeroEngine canonical `c0be719f9058717ae63891d618f0bdb1462a73ef`；POB 执行基线 `/main cs:16807`
- 设计与执行授权：Authorized；来源为用户要求移除 POB Tab 并参考 POB Dashboard 美化
- 终端操作授权：Authorized；用户已授权恢复测试副作用并继续 canonical 发布、POB pin 与精确提交

## 目标

ZeroEngine Dashboard 继续按已安装模块自动加载，但项目适配器只替换或补充其宿主模块，不再生成 POB 等消费项目专属模块 Tab。界面在不引入 Odin 或新资源依赖的前提下，采用与 POB Dashboard 一致的清晰分组、状态色和主次按钮层级。

## 非目标

- 不迁移或嵌入各模块窗口内部 UI。
- 不改现有 `MenuItem` 路径、Profile 调用或模块业务逻辑。
- 不把 POB Dashboard 本身纳入 ZeroEngine 工具目录。
- 不引入 Odin、UI Toolkit、图片资源或运行时代码。
- 不自动安装、移除或升级任何包。

## 当前与预期行为

当前每个含可见入口的有效描述符都直接成为 Tools 左侧模块项；空描述符原本就只保留 Installed 状态、不生成空 Tab。因此 POB Formula 和项目描述符会显示 `POB Formula`、`POB Editor Tools`。替代关系只隐藏入口，不改变入口的展示归属。

预期行为：

1. 描述符入口可通过可选 `mountModuleId` 挂到另一个已声明模块。
2. 入口身份、菜单、来源、诊断及 `replaces` 仍由原描述符所有；只改变展示归属。
3. 挂载目标缺失或被隔离时，入口 fail closed，不回退为项目专属 Tab，并产生可定位诊断。
4. 模块进入 Tools 当且仅当挂载解析后的可见入口数大于 0；自有入口和挂载入口均计入。无自有展示入口、也未承载挂载入口的适配器模块只保留在 Installed/Project adapters 状态页。
5. POB Formula 入口挂到 Formula，POB 配置器挂到 Config Pipeline，POB Data Manager 挂到 Data Toolkit；POB Dashboard 从项目描述符移除，原菜单保持可直接打开。

## 协议与不变量

`entries[].mountModuleId` 是 schema v1 的可选兼容字段：

- 格式与 `moduleId` 相同；缺省时入口挂在自己的 `moduleId`。
- 可指向包或项目描述符，但目标必须是本次目录构建中的唯一有效模块。
- 入口 `FullId` 永远保持 `<ownerModuleId>/<entryId>`，冲突、替代和执行语义不变。
- 同一宿主模块内按入口 `order`、显示名、`FullId` 确定性排序。
- Tools 模块成员资格只由挂载解析后的可见入口数决定；空适配器不生成 Tab，承载挂载入口的空宿主可以生成 Tab。
- 挂载目标不存在时记录 `mount-target-missing` warning 并隐藏入口；目标因重复 ID 被隔离时也按缺失处理。
- `mountModuleId` 格式非法属于描述符校验失败：整份描述符不进入目录，记录 `descriptor-invalid` error；静态门使用同一稳定 ID 正则拒绝该描述符。
- Dashboard 不按 `POB`、包名前缀、菜单路径或来源类型做 UI 特判。

Data Toolkit 增加空描述符，使已安装模块成为可挂载的稳定宿主；空描述符本身不伪造通用菜单。

## 视觉与交互

- 顶部使用标题、说明、模块/工具/诊断计数和明确的刷新操作。
- Tools、Installed、Diagnostics 保持三个主页面；搜索只在相关内容中即时过滤。
- 左侧模块选择使用宽行、选中强调和工具数量，不显示项目适配器空 Tab。
- 右侧使用模块分组标题与卡片；入口卡展示分类、安全等级、说明、菜单路径和高辨识度 Open/Run 主按钮。
- Connected、warning、error、只读/写入风险均同时使用文字与颜色，不只依赖颜色。
- 适配入口卡显示原所有者名，避免挂载后模糊责任归属。
- 窄窗口保留滚动，不截断操作；使用 Unity 内置 IMGUI 样式，兼容深浅主题和键盘焦点。

## 影响范围

### ZeroEngine

- `com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/DashboardCatalog.cs`
- `com.zerogamestudio.zeroengine.dashboard/Editor/ZeroEngineDashboard.cs`
- `com.zerogamestudio.zeroengine.dashboard/Tests/Editor/DashboardCatalogTests.cs`
- `com.zerogamestudio.zeroengine.dashboard/README.md`
- `com.zerogamestudio.zeroengine.dashboard/package.json`
- `com.zerogamestudio.zeroengine.data-toolkit/Editor/ZeroEngineDashboardModule.json` 及 `.meta`
- Dashboard 描述符静态检查与必要的测试清单
- `Tools/Tests/run-dashboard-editmode-tests.ps1` 的 lane 参数绑定及 Unity GUI 进程等待/退出码修复

### POB

- `Packages/com.zerogamestudio.pob.formula/Editor/ZeroEngineDashboardModule.json`
- `Assets/Assets/_Scripts/_POB/Editor/ZeroEngineDashboardModule.json`
- 最终消费时配对更新 `Packages/manifest.json` 与 `Packages/packages-lock.json`

## 兼容、失败与回滚

- 未使用 `mountModuleId` 的现有描述符行为不变。消费适配器的能力门同时要求 Dashboard package `>= 2.1.0`，以及包含 Data Toolkit 空宿主描述符的同一 canonical commit；POB 必须把等价性门所定义的 18 个 ZeroEngine/Analytics 业务依赖 pin 升到该 commit，并在同一 changeset 配对提交 manifest/lock 与 `mountModuleId` 适配描述符。独立 `9bb7feed…` Unity MCP 控制 pin 明确豁免、不升级。版本拆分不受支持：旧 Dashboard 通过 `JsonUtility.FromJson` 忽略未知字段时会复现项目 Tab，其他解析器则可能拒绝整份描述符；新 Dashboard 缺宿主时会 fail closed 隐藏入口。
- Data Toolkit 空描述符在旧 Dashboard 下仍因可见入口数为 0 而不可见，不单独产生空 Tab；回滚按上线逆序执行，先回滚 POB 描述符与 pin，再回滚 Dashboard。
- 挂载只影响目录显示，不改变原菜单；回滚 Dashboard 与 POB 描述符/pin 即恢复原模块 Tab。
- POB 联调不得提交绝对 `file:`；发布前上述 18 个业务依赖 pin 必须回到 canonical Git 同一提交，独立 Unity MCP 控制 pin 保持 `9bb7feed…`。
- 验证不得修改 `Assets/Assets/_Data` 或 `ProjectSettings` 生产配置；一旦检测到偏离，立即阻断并保留证据，只能在明确授权后精确恢复到测试前哈希，恢复门通过前不得继续发布。

## 实施顺序

1. 扩展目录模型、解析与挂载校验，补齐确定性单元测试。
2. 增加 Data Toolkit 空描述符和静态描述符门覆盖。
3. 重做 Dashboard IMGUI 呈现，不改变执行器和发现刷新边界。
4. 更新 POB 两个适配描述符并在本地 pin 联调。
5. 运行最窄 ZeroEngine 测试、静态门、POB 编译与真实窗口路线；核对配置哈希与变更范围。
6. 将 ZeroEngine 分支同步到当时 canonical，复跑门禁并通过 PR/CI 发布新的 canonical merge commit。
7. 将 POB manifest/lock 中上述 18 个 ZeroEngine/Analytics 业务依赖统一 pin 到该 merge commit，保留独立 Unity MCP 控制 pin，再精确提交两个适配描述符与配对 pin。

## 验证

- Dashboard EditMode 测试：普通缺省自归属、跨模块挂载、缺失目标、重复目标、替代后挂载、稳定排序、非法目标 ID，以及挂载前后所有者身份、菜单、安全确认、来源路径和替代目标不变。
- 上游静态描述符门：8 个描述符合法；明确断言 Data Toolkit 空宿主描述符存在且 `entries` 为空，所有非空入口菜单仍匹配所有者源码。
- POB 跨仓静态门：以 POB `manifest.json` 已安装包名过滤待发布 canonical 描述符集合，先断言 Formula、Config Pipeline、Data Toolkit 三个宿主包均已安装，再断言四个预期入口全部挂到该集合中存在且唯一的宿主；适配器无自归属入口，POB Dashboard 入口不存在。原五个菜单路径分别做过真实打开验证。
- Dashboard-only 与 dashboard-with-modules 最窄测试项目编译通过。
- 源码终审确认 `DashboardCatalogDiscovery.Discover()` 只由既有刷新边界调用，`OnGUI` 不扫描包或 Assets；UI 只使用内置 IMGUI、主题条件色、最小尺寸和滚动布局，不依赖 Odin。状态通过文字与颜色共同表达，标准 IMGUI TextField/Button 保留键盘焦点。真实窗口路线负责确认菜单可打开与 Console 0 error，截图不作为发布硬门。
- POB：`ZeroEngine/Dashboard` 可打开；目录单元测试与 POB 跨仓静态门共同证明 Tools 中没有 `POB Formula` 或 `POB Editor Tools` 模块项、四个 POB Profile 入口分别归入 Formula、Config Pipeline、Data Toolkit、POB Dashboard 不在目录；各入口的原菜单路径已分别真实打开，最终 Console 0 error。
- POB 验证前后生产配置 SHA-256 清单一致，任务外 pending 未变化。
- 发布门：Dashboard package 版本为 2.1.0，ZeroEngine PR 只包含本任务提交且基于目标 canonical；POB manifest/lock 无 `file:`，18 个业务依赖 pin 指向同一新 merge commit，独立 Unity MCP 控制 pin 仍为 `9bb7feed…`。
- 等价性门：从 POB 联调基线 `09059cad20d5e074f45a174ad25a749b5d67dedf` 到同步目标 `c0be719f9058717ae63891d618f0bdb1462a73ef`，比较 POB manifest 中当前共享 `57dd8473…` revision 的全部 18 个 ZeroEngine/Analytics 业务依赖所对应的 canonical 包根路径；diff 必须为空。独立 `9bb7feed…` Unity MCP 控制 pin 不在本次升级范围。
- canonical 漂移门：PR 合并前再次 fetch；若 `origin/main` 仍为 `c0be719f…` 可继续。若已前进，以上述 18 个实际消费包根为同一比较范围检查 `c0be719f…` 到新 canonical：任一路径非空则现有 POB 等价证据失效，停止发布并重新授权验证；全部为空才允许 rebase，并复跑 ZeroEngine 描述符门、PowerShell AST、diff check 与两个最窄 Unity lane。
- 联调树绑定门：POB 实机联调所用提交 `7f26119…` 的生产消费对象为 Dashboard `Editor` tree `f66d499728b872ccb7e9930d6d4935001c72911c`、Dashboard `package.json` blob `50929ebb72fbfcfb9b85db00055c0d4e658de6d4`、Data Toolkit 包根 tree `bbd56fdea00a68b732b75713d60f4022194566b9`；最终 canonical merge commit 的同路径 object 必须逐一相等，才可复用既有 POB Unity 证据并进入 pin。Dashboard `Tests/**` 的后续最小断言只由 ZeroEngine 两条 lane 验证，不属于 POB 生产消费面。任一生产消费 object 不等立即停止，不执行 POB 写入或 Unity 重验。

## 实施证据

- Dashboard 描述符静态门通过：8 个上游描述符合法；Data Toolkit 空宿主路径存在且 `entries` 为空的硬断言通过；非空菜单所有权检查无失败；静态脚本以同一稳定 ID 正则执行有效/无效自检，并同时约束 `moduleId` 与 `mountModuleId`。
- Dashboard-only：41 passed、0 failed，`PackageRemovalEvent_RemovesFixtureEntryFromCatalog` 因仅在 removal 环境启用而预期 skip；dashboard-with-modules：139/139 passed。
- `Build_EmptyEntries_KeepsModuleInstalledButNotVisible` 通过，固定当前空描述符不生成空 Tab 的兼容行为；`git show c0be719f…:com.zerogamestudio.zeroengine.dashboard/Editor/Catalog/DashboardCatalog.cs` 另确认旧 2.0 实现的 `VisibleModules` 同样只保留 `VisibleEntries.Count > 0`，且以 `JsonUtility.FromJson<DashboardDescriptorData>` 解析，未知 `mountModuleId` 字段会被忽略。
- `Build_MountedEntryAppearsOnlyUnderTargetModule` 通过：空宿主承载挂载入口后进入 Tools，适配器自身不生成 Tab。
- `Build_MountPreservesOwnerIdentitySafetyAndSource` 通过：挂载后 `ModuleId`/`FullId`、菜单路径、`Safety`/`Confirmation`、`SourcePath` 与 `Replaces` 均保持所有者原值。
- 源码终审通过：`DashboardCatalogDiscovery.Discover()` 只在 `RefreshCatalog()` 调用；`OnGUI` 不扫描包或 Assets；UI 使用内置 IMGUI、深浅主题条件色、最小尺寸与滚动布局，状态标签同时包含文字与颜色，标准 TextField/Button 维持键盘焦点；挂载卡通过 `GetMountedOwnerLabel()` 显示原所有者；静态门同时确认 Dashboard package version 为 2.1.0。
- POB 关键测试：EditMode 150/150 passed；PlayMode 14 项完成、status=succeeded、无失败记录。
- POB `ZeroEngine/Dashboard` 与原 POB Dashboard 菜单均可打开；最终 Console 0 error。
- POB Formula Catalog、Formula Workbench、Data Manager、Configurator 四个原菜单路径均已真实打开；跨仓静态门先确认三宿主均存在于 POB manifest，再确认 mount 分别命中已安装 Formula（2 项）、Data Toolkit（1 项）、Config Pipeline（1 项），两个适配器无自归属入口且不存在 POB Dashboard entry。结合 `Build_MountedEntryAppearsOnlyUnderTargetModule`，目录不会产生 `POB Formula`/`POB Editor Tools` Tab，入口只出现在目标宿主。
- 关键测试曾违反生产配置零写约束并触发发布阻断；`EditorSettings.asset` 临时 flag 已按授权精确恢复，SHA-256 回到测试前基线 `4dd4fa6db8124857132b771bf1cda0127b49c462dc3212342deee3cc4d44488a`，该路径不再 pending，恢复门通过后才恢复发布流程。
- canonical 同步范围核对通过：`09059cad…` 到 `c0be719f…` 只改 `Tools/unity-mcp-supervisor/**`，POB 实际消费的 18 个业务包根全部 diff 为空；rebase 后最窄测试复跑通过。
- 联调提交 `7f26119…` 与 rebase 后 `150cb3b…` 的 Dashboard `Editor` tree、Dashboard `package.json` blob、Data Toolkit 包根 tree 均分别为 `f66d4997…`、`50929ebb…`、`bbd56fde…`；最终 merge 后仍须按联调树绑定门再核一次。
- 测试 wrapper 失败注入门通过：使用可执行但非 Unity 的进程替身运行 dashboard-only lane，替身退出 `64`，wrapper 捕获后以 `1` 返回并输出 `Unity failed for dashboard-only. Exit=64`，证明 CI 不会把 Unity 失败误报为通过。

本次偏离来源是已完成的 POB 关键 PlayMode 路线。恢复门之后，本任务禁止重跑该路线或执行新的 POB Unity 操作；终端发布复用上述已完成测试证据，只执行 Git/Plastic 范围门与最终文件哈希。当前 before/after 哈希门不能证明运行中间态零写，因此不把它表述为零写检测器；它只负责发现净变化并按项目规则阻断。修复 PlayMode 测试基础设施的中间态隔离属于独立任务，不纳入本功能 changeset。

## 终端发布待办

- ZeroEngine PR、CI 与 canonical merge commit。
- POB manifest/lock 统一 Git pin、精确 Plastic changeset 与最终 claims/task 释放。

## 验收标准

1. Tools 中只为有效宿主模块生成模块项，POB 两个适配器不生成独立 Tab。
2. POB Formula 两个入口显示在 Formula 下，并继续替代对应通用入口。
3. POB Configurator 显示在 Config Pipeline 下；POB Data Manager 显示在 Data Toolkit 下。
4. POB Dashboard 不再出现在 ZeroEngine Dashboard，原 `ZGS/工具/POB 仪表盘` 菜单可用。
5. `mountModuleId` 不改变入口身份、替代、菜单执行、安全确认或来源诊断。
6. 缺失/冲突挂载目标时入口不回退、不产生错误 Tab，并显示 `mount-target-missing`。
7. 未声明挂载的既有描述符显示与行为兼容。
8. UI 具备明确标题、状态计数、分组卡片、状态文字与颜色、主操作按钮及深浅主题适配，不依赖 Odin；挂载入口卡显示原所有者，标准 IMGUI 控件保持键盘焦点。
9. 自动发现仍只在既有刷新触发点执行，`OnGUI` 不扫描包或 Assets。
10. ZeroEngine 最窄自动测试和描述符门通过；POB 实机路线 Console 0 error。
11. POB 生产配置最终净值精确恢复到测试前 SHA-256 基线，偏离与授权恢复证据已记录；`EditorSettings.asset` 不再 pending，其他 pending 未纳入任务范围。运行中间态零写隔离已明确外移为独立基础设施任务。
12. 新增 Unity 资产包含 `.meta`；最终 18 个业务依赖 pin 不含绝对 `file:` 并统一指向新 merge commit，独立 Unity MCP 控制 pin 保持 `9bb7feed…`。
13. 发布顺序满足 Dashboard 2.1.0 先落 canonical、POB 适配描述符与同 commit pin 后提交；旧 Dashboard 与 Data Toolkit 空描述符组合不产生空 Tab。
