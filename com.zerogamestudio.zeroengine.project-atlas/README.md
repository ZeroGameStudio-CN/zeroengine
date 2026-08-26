# ZeroEngine Project Atlas

Project Atlas 1.1.6 是可选、仅限 Unity Editor 的跨项目项目导航包。工作台中的“项目功能”只面向项目人员，按消费项目定义的领域和功能说明用途、可完成工作、适用岗位、配置状态和可用入口。程序与 Agent 使用的系统目录、引用覆盖、生成索引和改动合同继续保留在代码、JSON、Markdown 与自动门中，不复制到普通功能界面。

功能工作区保留工作领域、功能列表、功能说明三栏；窗口变窄时导航栏按可用宽度同步收缩，长标题显示省略号并通过 Tooltip 提供完整名称和说明。三栏在宿主为其保留的局部绘制区域内布局，不覆盖面板标题、说明或搜索栏；只有窄于三栏可读下限时才启用横向滚动。

## 功能目录合同

- 根清单固定为 `docs/project/feature-map.json`，片段由根清单从 `docs/project/features/*.json` 显式引用；不支持 glob、绝对路径或 `..`。
- 领域、功能、岗位、能力和可见文案由消费项目拥有；包不包含 P5、POB 或其他项目的功能分类。
- JSON action 只引用稳定 `routeId`。项目通过 Editor-only `IProjectFeatureRouteProvider` 把 route 绑定到 typed workspace route；JSON 不能执行菜单、方法、Shell、URL 或任意反射入口。
- configurable 功能必须有唯一可用配置入口；明确没有配置入口的功能使用 `none` 并说明原因。未知、重复或缺失 route 会 fail closed，但不阻断其他功能浏览。
- 普通加载、搜索、岗位筛选、导航和返回只读。目标 owner panel 自己继续负责 project-write / destructive 确认。

## 项目合同

- 根清单固定为 `docs/architecture/project-atlas.json`。
- 领域碎片固定放在 `docs/architecture/project-atlas/*.json`，由根清单逐项显式引用；不支持 glob、绝对路径或 `..`。
- 确定性投影固定写入 `docs/architecture/system-routing-index.md`。
- 技术目录的加载、搜索和导航只读；生成索引由消费项目自己的受控维护命令调用 `ProjectAtlasProjectWriter`，普通“项目功能”界面不提供写入动作。
- 项目通过 Editor-only 的 resolver 与 coverage provider 接入自己的权威源；包不包含 P5、POB 或其他项目特判。

## 安装

在消费项目中把 Project Atlas、Dashboard 和 editor-ui 直接 pin 到同一个经过验证的 ZeroEngine Git commit。Unity 2022.3 不会为同仓 Git URL 自动解析全部依赖，因此同仓包应保持一致 pin。

安装后从 `ZGS > 工作台` 打开“项目功能”。面板使用与 Data Manager 一致的“项目领域 → 具体功能 → 功能详情与入口”三栏结构；领域和功能统一使用居中的标准选择按钮，保留明确的底板、边框、悬停与选中反馈，配置状态只在右侧详情中展示，避免把按钮拆成左右信息栏而造成视觉偏斜。岗位说明自动换行；窄窗口通过横向滚动保留三栏，不把领域或功能折叠为下拉。项目尚未创建功能根清单时，面板只显示接入说明，不创建文件；技术目录仍按原 Project Atlas 合同独立验证。

## 安全边界

目录 JSON 不包含菜单、方法、Shell、URL 执行或任意反射入口。所有路径在 resolver 运行前验证为项目根内的相对路径；resolver 异常隔离为诊断，不影响其他引用类型或 Dashboard。
