# ZeroEngine Dashboard

ZeroEngine Dashboard 4.3.0 是一个可选、仅限 Unity Editor 的简体中文工作台。它从已注册 UPM 包以及项目 `Assets/**/Editor/` 中读取 `ZeroEngineDashboardModule.json`，只展示所有者明确声明的窗口、命令、资料和内嵌面板。

## 特性

- 不依赖 Core 或任何可选 ZeroEngine 业务模块；只依赖 Editor-only 的公共 editor-ui，模块也不需要引用 Dashboard。
- 安装、移除或升级带描述符的包后自动刷新目录。
- 通过 editor-ui 的 typed action provider 懒执行；Dashboard 不反射任意方法，也不依赖业务包。
- 对描述符错误、入口冲突、替代循环和失效菜单提供可见诊断。
- `project-write` 与 `destructive` 命令执行前要求明确确认。
- 项目适配入口可通过 `mountModuleId` 挂到已安装的通用模块，不产生独立项目 Tab。
- 可选 `section` 将大型模块拆成可读分区；共享 `surfaceId` 可把同一宿主窗口的兼容入口合并为一行多动作。
- 首页/工具库/系统/帮助四页采用自适应布局：首页左侧汇总项目面板，工具库按任务、范围、安全和可用性筛选，系统页集中健康状态与诊断，帮助页单独承载用途、用法与技术信息。
- 工具库以 surface 为主行，同一窗口的次要动作收入“更多”；文档类 entry 通过 `contentType: "reference"` 独立进入相关资料，不参与动作 surface。
- 说明、使用方法、安全影响和技术来源进入 tooltip 或独立帮助页，不占用工作面板正文。
- 本机记忆上次页面、面板、搜索、筛选和主要滚动位置；失效面板会安全回到首页总览。
- 工作台先显示框架再延迟发现目录和恢复面板；同一脚本 Domain 内重开复用目录快照。
- 首页左侧面板可拖拽跨模块排序并持久化，也可一键恢复描述符默认顺序。
- 固定 label、状态、安全提示与可操作控件 tooltip 使用简体中文；品牌缩写和技术标识保持原值。
- 不安装包、不写 manifest、不清理 PlayerPrefs/存档、不写项目资源。

## 安装

在消费项目的 `Packages/manifest.json` 中添加 Git UPM 依赖，并与其他 ZeroEngine 包固定到同一个测试提交：

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.dashboard": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.dashboard#<tested-commit>",
    "com.zerogamestudio.zeroengine.editor-ui": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.editor-ui#<tested-commit>"
  }
}
```

Unity 2022.3 不会为 Git URL 包自动解析同仓 editor-ui；两项必须直接 pin 到同一提交。4.3.0 要求 editor-ui 1.4.0。

本地 `file:` 依赖只用于临时联调，不应进入共享分支。

## 描述符

包描述符固定放在 `Editor/ZeroEngineDashboardModule.json`；项目描述符可放在任意 `Assets/**/Editor/ZeroEngineDashboardModule.json`。正式入口使用 schema v2 的稳定 provider/action 绑定，不声明菜单路径。

```json
{
  "schemaVersion": 2,
  "moduleId": "com.zerogamestudio.zeroengine.example",
  "displayName": "Example",
  "description": "Example editor tools.",
  "order": 100,
  "scope": "universal",
  "documentationPath": "README.md",
  "entries": [
    {
      "id": "open-window",
      "displayName": "打开示例窗口",
      "description": "打开模块已有的示例窗口。",
      "mountModuleId": "com.zerogamestudio.zeroengine.example",
      "section": "内容创作",
      "surfaceId": "example-studio",
      "surfaceDisplayName": "示例中心",
      "surfaceActionLabel": "打开",
      "surfaceDefault": true,
      "category": "authoring",
      "kind": "window",
      "order": 100,
      "safety": "navigation",
      "availability": "always",
      "visibility": "primary",
      "executionKind": "provider",
      "providerId": "zeroengine.example",
      "actionId": "open-window",
      "contentType": "action",
      "legacyKeywords": ["ZeroEngine/Example/Open Window"],
      "replaces": []
    }
  ]
}
```

工作台入口：`ZGS > 工作台`。

`mountModuleId` 可省略；省略时入口显示在自己的模块下。指定后只改变展示归属，入口 ID、来源、替代关系和执行路径不变。目标模块缺失或冲突时入口会隐藏并进入 Diagnostics，不会回退成独立适配器 Tab。

`section`、`contentType` 与全部 `surface*` 字段均可省略。`contentType` 默认为 `action`；`reference` 只允许 `navigation` 或 `read-only`，在相关资料区展示，不进入动作 surface。只有同一展示宿主内共享 `surfaceId` 且分类、section、显示名和默认动作兼容的 action 才会合并；每个 action 仍保留独立安全等级、可用性和确认。冲突会显示诊断并安全回退为独立行。

entry 可选 `usage` 只在帮助抽屉显示。module 可选 `panels` 数组声明内嵌工作区面板；每项包含稳定 `id/providerId`、显示文案、section、order、safety 与 availability。provider 通过 editor-ui 的 `IEditorWorkspacePanelProvider.CreatePanel(panelId)` 延迟创建；需要画布布局的 panel 可实现 `IEditorWorkspaceFullWidthPanel` 横向铺满内容区，其他 panel 保持可读宽度约束；缺失、重复或异常 provider 不影响工具和系统页。

Dashboard 4.x 仍兼容外部 schema v1，并把它标记为“旧版入口”；第一方正式描述符必须使用 v2。v1 兼容将在首个 5.x 版本移除。

## 版本历史

### 4.3.0

- 首次打开延迟目录发现，重开复用当前 Domain 的目录快照；恢复的面板晚于工作台框架首帧创建。
- 首页左侧面板支持拖拽排序、持久记忆和恢复默认顺序。

### 4.2.0

- 新增独立帮助页，移除工作区与工具列表的常驻说明抽屉。
- 记住上次页面、面板、搜索、筛选和主要滚动位置。
- 支持 typed action provider 通过 editor-ui 导航接口切换现有内嵌面板。

### 4.1.0

- 重排为首页、工具库、系统三页，面板与主流程合并到首页，诊断集中到系统页。
- 工具行改为 surface 优先，次要动作收入“更多”，说明改为 tooltip 和自适应上下文栏/覆盖层。
- schema v2 新增向后兼容的 `contentType`；资料入口与可执行动作分层展示。

### 4.0.0

- 唯一顶栏入口改为 `ZGS/工作台`，正式入口由 typed action provider 执行。
- 新增 schema v2、六类任务导航、动态项目范围、高级/维护筛选和 action 级安全状态。
- 保留外部 schema v1 的 4.x 兼容执行与诊断。

### 3.2.0

- 新增工作区页面、显式 panel descriptor/provider 绑定和单面板生命周期隔离。
- 工具行移除重复常驻说明，增加统一帮助抽屉和测量式响应布局。

### 3.1.1

- Dashboard shell、正式模块描述符与动作 tooltip 使用简体中文，技术 ID、菜单路径和安全语义保持不变。
- V2 信息架构、Formula Studio 合并与 POB rollout 证据见[归档 spec](../docs/specs/2026-08-10-zeroengine-dashboard-v2-information-architecture.md)。

### 3.1.0

- Tools 与 System 两页替代分散的 Installed/Diagnostics Tab，并在窄窗口切换为紧凑模块选择器。
- 增加 section/surface 描述符元数据、冲突诊断、渐进式 Details 和紧凑 action row。
- Formula Catalog/Workbench 在 Dashboard 中显示为一个 Formula Studio surface。

### 3.0.0

- 使用公共 Editor UI 视觉合同，并要求消费工程直接 pin editor-ui 1.0.0。
- 保持描述符发现、挂载、执行、安全和诊断语义不变。

### 2.1.0

- 支持适配入口挂载到已安装宿主模块，避免消费项目专属 Tab。
- 优化模块、工具卡片和状态页的 IMGUI 视觉层级。
- 设计与发布证据见[归档记录](../docs/specs/2026-08-09-zeroengine-dashboard-adapter-polish.md)。

### 2.0.0

- 使用声明式模块发现替代中央硬编码和条件编译。
- 移除第三方插件探测、网络包安装、YooAsset 自动配置和数据清理入口。
- 增加确定性冲突/替代处理、安全确认与诊断。

### 1.0.0

- 从主包独立出来。
