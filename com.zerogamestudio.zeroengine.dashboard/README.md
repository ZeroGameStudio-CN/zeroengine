# ZeroEngine Dashboard

ZeroEngine Dashboard 3.1.1 是一个可选、仅限 Unity Editor 的简体中文工具目录。它从已注册 UPM 包以及项目 `Assets/**/Editor/` 中读取 `ZeroEngineDashboardModule.json`，只展示所有者明确声明的窗口和命令。

## 特性

- 不依赖 Core 或任何可选 ZeroEngine 业务模块；只依赖 Editor-only 的公共 editor-ui，模块也不需要引用 Dashboard。
- 安装、移除或升级带描述符的包后自动刷新目录。
- 通过模块已有 `MenuItem` 懒执行，不反射构造或嵌入窗口。
- 对描述符错误、入口冲突、替代循环和失效菜单提供可见诊断。
- `project-write` 与 `destructive` 命令执行前要求明确确认。
- 项目适配入口可通过 `mountModuleId` 挂到已安装的通用模块，不产生独立项目 Tab。
- 可选 `section` 将大型模块拆成可读分区；共享 `surfaceId` 可把同一宿主窗口的兼容入口合并为一行多动作。
- Tools/System 两页自适应窄宽布局，技术 ID 与菜单路径默认折叠到 Details。
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

Unity 2022.3 不会为 Git URL 包自动解析同仓 editor-ui；两项必须直接 pin 到同一提交。3.1.1 要求 editor-ui 1.1.1。

本地 `file:` 依赖只用于临时联调，不应进入共享分支。

## 描述符

包描述符固定放在 `Editor/ZeroEngineDashboardModule.json`；项目描述符可放在任意 `Assets/**/Editor/ZeroEngineDashboardModule.json`。`moduleId`、入口 ID、菜单路径、安全等级及替代关系必须符合仓库设计规范。

```json
{
  "schemaVersion": 1,
  "moduleId": "com.zerogamestudio.zeroengine.example",
  "displayName": "Example",
  "description": "Example editor tools.",
  "order": 100,
  "documentationPath": "README.md",
  "entries": [
    {
      "id": "open-window",
      "displayName": "Open Window",
      "description": "Open the existing module window.",
      "mountModuleId": "com.zerogamestudio.zeroengine.example",
      "section": "Authoring",
      "surfaceId": "example-studio",
      "surfaceDisplayName": "Example Studio",
      "surfaceActionLabel": "Open",
      "surfaceDefault": true,
      "category": "authoring",
      "kind": "window",
      "menuPath": "ZeroEngine/Example/Open Window",
      "order": 100,
      "safety": "navigation",
      "availability": "always",
      "replaces": []
    }
  ]
}
```

Dashboard 入口：`ZeroEngine > Dashboard`。

`mountModuleId` 可省略；省略时入口显示在自己的模块下。指定后只改变展示归属，入口 ID、来源、替代关系和执行路径不变。目标模块缺失或冲突时入口会隐藏并进入 Diagnostics，不会回退成独立适配器 Tab。

`section` 与全部 `surface*` 字段均可省略，旧描述符保持一入口一行。只有同一展示宿主内共享 `surfaceId` 且 kind、availability、safety、section、显示名和默认动作兼容的入口才会合并；冲突会显示诊断并安全回退为独立行。

## 版本历史

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
