# ZeroEngine.Input

## 2.1 additions

- `InputBindingService` resolves actions and bindings by GUID, supports
  Swap/Reject/Allow conflicts, rejects continuous pointer/touch coordinates,
  resets one or all overrides, and round-trips the complete asset override JSON.
- `InputDevicePresentationTracker` separates generic binding families from
  Xbox/PlayStation/Nintendo glyph presentation. Call `RecordIntent` only for
  deliberate input; `IsDeliberatePointerIntent` filters passive mouse movement.
- Existing `InputManager` APIs remain compatible.

输入管理包。

## 版本
- **当前版本**: 2.1.1（整合候选，尚未发布）
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


## Consolidation dependency correction

The 2.1.1 consolidation candidate explicitly declares Unity's built-in
JSON Serialize module (`com.unity.modules.jsonserialize`) for this package's
direct `JsonUtility` calls. Consumers no longer need an unrelated installed
package to supply that module. This candidate is not a published release.
