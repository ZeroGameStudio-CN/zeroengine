# ZeroEngine Dashboard

ZeroEngine Dashboard 2.1 是一个可选、仅限 Unity Editor 的工具目录。它从已注册 UPM 包以及项目 `Assets/**/Editor/` 中读取 `ZeroEngineDashboardModule.json`，只展示所有者明确声明的窗口和命令。

## 特性

- 不依赖 Core 或任何可选 ZeroEngine 模块；模块也不需要引用 Dashboard。
- 安装、移除或升级带描述符的包后自动刷新目录。
- 通过模块已有 `MenuItem` 懒执行，不反射构造或嵌入窗口。
- 对描述符错误、入口冲突、替代循环和失效菜单提供可见诊断。
- `project-write` 与 `destructive` 命令执行前要求明确确认。
- 项目适配入口可通过 `mountModuleId` 挂到已安装的通用模块，不产生独立项目 Tab。
- 不安装包、不写 manifest、不清理 PlayerPrefs/存档、不写项目资源。

## 安装

在消费项目的 `Packages/manifest.json` 中添加 Git UPM 依赖，并与其他 ZeroEngine 包固定到同一个测试提交：

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.dashboard": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.dashboard#<tested-commit>"
  }
}
```

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

## 版本历史

### 2.1.0

- 支持适配入口挂载到已安装宿主模块，避免消费项目专属 Tab。
- 优化模块、工具卡片和状态页的 IMGUI 视觉层级。

### 2.0.0

- 使用声明式模块发现替代中央硬编码和条件编译。
- 移除第三方插件探测、网络包安装、YooAsset 自动配置和数据清理入口。
- 增加确定性冲突/替代处理、安全确认与诊断。

### 1.0.0

- 从主包独立出来。
