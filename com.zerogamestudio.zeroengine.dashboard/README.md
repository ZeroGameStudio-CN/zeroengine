# ZeroEngine Dashboard

ZeroEngine 编辑器控制中心，支持轻量项目使用。

## 特性

- **通用化设计**：通过条件编译自动适配已安装的包
- **无硬依赖**：只依赖 `zeroengine.core`，轻量项目也能使用
- **插件检测**：自动检测 Odin、DOTween、EasySave、YooAsset 等插件状态
- **可选功能**：根据已安装的包显示/隐藏对应功能

## 安装

### 方式 1：作为独立包（轻量项目）

在 `Packages/manifest.json` 中通过 Git UPM 添加，并与其他 ZeroEngine 包使用同一个测试过的 commit：

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.core": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.core#<tested-commit>",
    "com.zerogamestudio.zeroengine.dashboard": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.dashboard#<tested-commit>"
  }
}
```

本地 `file:` 依赖只用于临时联调，不应提交到共享分支。

### 方式 2：通过主包（完整项目）

主包 `com.zerogamestudio.zeroengine` 的 `package.json` 声明了对本包的依赖。通过 Git UPM 使用主包时，如果消费项目没有配置可解析 ZeroEngine 包的私有 registry，仍应在 `Packages/manifest.json` 中显式添加 dashboard 的 Git URL，并与主包使用同一个 commit。

## 条件编译宏

本包根据已安装的其他包自动定义以下编译宏：

| 包 | 编译宏 | 启用功能 |
|----|--------|----------|
| `zeroengine.persistence` | `ZEROENGINE_HAS_PERSISTENCE` | 清理存档按钮 |
| `zeroengine.economy` | `ZEROENGINE_HAS_ECONOMY` | Inventory 调试工具 |
| `analytics` | `ZEROENGINE_HAS_ANALYTICS` | Analytics Dashboard 入口 |
| `netcode.gameobjects` | `ZEROENGINE_NETCODE` | 网络模块状态显示 |
| `spine-unity` | `SPINE_UNITY` | Spine 模块状态显示 |

## 使用

菜单：`ZeroEngine > Dashboard`

## 版本历史

### 1.0.0
- 从主包独立出来
- 添加条件编译支持
- 支持轻量项目使用
