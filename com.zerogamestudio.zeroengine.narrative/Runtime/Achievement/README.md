# ZeroEngine.Achievement API 文档

> **用途**: 本文档面向AI助手，提供Achievement（成就系统）模块的快速参考。
> **版本**: v1.0.0+
> **最后更新**: 2025-12-31

---

A decoupled, provider-based achievement system that supports local storage and external synchronization (e.g., Steam).

## Structure
- **Core**: `AchievementManager`, `AchievementSO`
- **Logic**: decoupled from platform specifics.
- **Providers**: `SteamAchievementProvider`, `IAchievementProvider`

## Usage

### 1. Define Achievements
Right-click in Project view -> **ZeroEngine** -> **Achievement** -> **Achievement Data** to create a new `AchievementSO`.
- **Id**: Unique string ID (must match Steam Achievement API Name if using Steam).
- **Title/Description**: For UI display.

### 2. Configure Manager
Add registered `AchievementSO` assets to the `AchievementManager`'s list in the Inspector (or load them dynamically).

### 3. Unlock Achievement
Call from anywhere in your game code:

```csharp
// Simple Unlock
ZeroEngine.Achievement.AchievementManager.Instance.Unlock("ACH_WIN_GAME");

// Set Progress (0.0 to 1.0)
// Automatically unlocks if progress >= 1.0
ZeroEngine.Achievement.AchievementManager.Instance.SetProgress("ACH_COLLECT_100", 0.5f);
```

### 4. Events
Subscribe to UI updates:
```csharp
AchievementManager.Instance.OnAchievementUnlocked += (achievementData) => {
    Debug.Log($"Unlocked: {achievementData.Title}");
    // Show UI notification...
};
```

## Providers

### Local Storage (Default)
Achievements are always saved locally using `ZeroEngine.Save` (JSON). This ensures offline support.

### Steam Integration
Requires `com.rlabrecque.steamworks.net` package.
Steam calls compile only for Windows, Linux and macOS Standalone targets. Other
targets, including Android in the Editor, keep the provider API but return
`false` from `Initialize()` without referencing the Steam SDK. Installing the
Steam package alone does not make its SDK available on mobile platforms.

1. Install Steamworks.NET.
2. Add `STEAMWORKS_NET` to Scripting Define Symbols.
3. Ensure `SteamManager` is initialized in your scene.
4. `AchievementManager` will automatically detect and sync with Steam.

### Custom Server
Implement `IAchievementProvider` interface and register it:
```csharp
public class MyServerProvider : IAchievementProvider { ... }

// Register at startup
AchievementManager.Instance.RegisterProvider(new MyServerProvider());
```
