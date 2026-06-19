# ZeroEngine.Input

输入管理包。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core, Unity Input System

## 包含模块

### InputSystem
- `InputManager` - 输入管理器
- 设备检测、重绑定、上下文切换

## 快速使用

```csharp
using ZeroEngine.InputSystem;

// 获取输入
var move = InputManager.Instance.GetMoveInput();

// 设备检测
var device = InputManager.Instance.CurrentDevice;

// 重绑定
InputManager.Instance.StartRebind("Jump", callback);
```

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
