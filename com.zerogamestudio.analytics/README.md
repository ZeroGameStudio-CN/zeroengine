# ZGS Analytics SDK

ZeroGameStudio 游戏分析 SDK，支持自建 ClickHouse 数据平台。

**版本**: 1.6.0
**Unity**: 2021.3+

## 特性

- **零代码初始化** - `[RuntimeInitializeOnLoadMethod]` 自动启动
- **离线队列** - 断网自动缓存，联网后重传
- **可靠事件信封** - 支持顶层 `event_id`、逻辑发生时间和返回前持久化
- **多 Provider** - 可同时向多个后端发送
- **崩溃/Bug 报告** - 自动采集上下文 + 附件上传
- **Timeline 日志** - 本地流水账，支持存档/还原
- **多平台身份** - Steam/Epic/自定义平台绑定

## 安装

在 `Packages/manifest.json` 中添加：

```json
{
  "dependencies": {
    "com.zerogamestudio.analytics": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.analytics#<tested-commit>"
  }
}
```

生产项目使用测试过的 commit，不要依赖浮动分支。

## 配置

1. 菜单 `Create > ZGS > Analytics Config`
2. 放到 `Assets/Resources/ZGSAnalyticsConfig.asset`
3. 填写配置：
   - `App ID` - 游戏标识 (如 POB, LLS)
   - `ZGS Server URL` - FastAPI 服务器地址
   - `ZGS Secret` - 事件上报认证密钥
   - `ZGS Upload Secret` - 反馈上传认证密钥；为空时兼容使用事件密钥
   - `Debug Mode` - 编辑器日志开关

## 自动采集

SDK 自动收集以下数据（无需写代码）：

| 数据 | 说明 |
|------|------|
| `session_start` | 每次启动，含完整设备信息 |
| `session_end` | 退出时，含会话时长 |
| 设备信息 | CPU/GPU/内存/分辨率/系统 |
| 崩溃日志 | 自动捕获 Unity 异常 |

## API 使用

```csharp
using ZGS.Analytics;

// 记录事件
AnalyticsService.LogEvent("level_complete", new Dictionary<string, object>
{
    ["level_id"] = "boss_001",
    ["time_spent"] = 120
});

// 需要幂等重试和本地持久化的生命周期事件
bool accepted = AnalyticsService.TryLogEvent(
    "game_end",
    new Dictionary<string, object> { ["run_id"] = runId },
    new AnalyticsEventOptions(
        eventId: deterministicEventId,
        occurredAtUnixMs: occurredAtUnixMs,
        durable: true));

// 立即开始上传支持显式刷新的 Provider 队列
AnalyticsService.Flush();

// 屏幕追踪
AnalyticsService.TrackScreen("MainMenu");

// 设置用户属性
AnalyticsService.SetUserProperty("vip_level", 3);

// 设置平台身份 (Steam/Epic 等)
AnalyticsService.SetIdentity(new UserIdentity
{
    Platform = "steam",
    PlatformUserId = steamId,
    DisplayName = playerName
});
```

## Timeline 日志

用于 Bug 复现的本地流水账：

```csharp
// 记录关键操作
AnalyticsService.LogTimeline("enter_room", new Dictionary<string, object>
{
    ["room_id"] = "dungeon_01"
});

// 获取 JSON（用于 Bug 报告）
string json = AnalyticsService.GetTimelineJson();

// 存档/读档支持
var snapshot = AnalyticsService.GetTimelineSnapshot();
AnalyticsService.RestoreTimelineSnapshot(snapshot);
```

## Bug 报告

```csharp
// 简单 Bug 报告
AnalyticsService.ReportBug("玩家反馈内容");

// 带附件的 Bug 报告（截图、存档等）
yield return AnalyticsService.ReportBugWithAttachments(new AttachmentUploadRequest
{
    UserMessage = "游戏卡住了",
    UserName = "PlayerName",
    FilesToInclude = new[] { screenshotPath },
    DirectoriesToInclude = new[] { Application.persistentDataPath }
});
```

## 编辑器工具

菜单 `ZGS > Analytics Dashboard` 查看：
- 配置状态
- 运行时 Session/User ID
- 快捷跳转服务端 Dashboard

## 架构

```
┌─────────────────────────────────────────────────┐
│  AnalyticsService (静态入口)                     │
├─────────────────────────────────────────────────┤
│  IAnalyticsProvider                              │
│  ├── ZGSServerProvider (生产)                   │
│  └── DebugProvider (开发调试)                   │
├─────────────────────────────────────────────────┤
│  Core 模块                                       │
│  ├── SessionInfo      会话/用户管理              │
│  ├── DeviceInfo       设备信息采集               │
│  ├── OfflineQueue     离线队列 (防抖批量保存)    │
│  ├── TimelineLogger   流水日志                   │
│  ├── CrashReporter    崩溃/Bug 报告             │
│  └── IdentityManager  多平台身份                 │
└─────────────────────────────────────────────────┘
```

## 数据流

```
Unity Client (SDK)
      ↓ POST /events
FastAPI Server (SQLite WAL 缓冲)
      ↓ 批量写入
ClickHouse (OLAP 存储)
      ↓ 物化视图
Streamlit Dashboard (可视化)
```

## 维护指南

为了确保 AI 助手能够高效维护此 SDK，更新时请遵循以下规则：

1. **版本管理**
   - 每次修改代码逻辑后，必须更新 `package.json` 中的 `version` 字段。
   - 遵循语义化版本 (SemVer) 规范 (X.Y.Z)。

2. **文档同步**
   - 修改 API 接口时，必须同步更新 `README.md` 中的 "API 使用" 章节。
   - 新增配置项时，必须更新 "配置" 章节的说明。

3. **变更记录**
   - 必须在 `README.md` 底部的 "更新日志" 中记录所有变更。
   - 格式：`- [模块] 修改内容描述`。

## 更新日志

### v1.6.1
- API: 新增 `AnalyticsService.Flush()` 与可选的 `IAnalyticsFlushProvider`，允许按需启动队列上传

### v1.6.0
- Event envelope: 新增顶层 `event_id`、逻辑发生时间和不可覆盖的 session 内事件序号
- OfflineQueue: durable 事件在返回前落盘，满队列优先淘汰 buffered 事件并显式报告失败
- API: 新增返回 enqueue 结果的 `TryLogEvent`，保留原有 `LogEvent` 与 `IAnalyticsProvider` 源码兼容；自定义上传 Provider 可实现 `IAnalyticsEnqueueProvider`

### v1.2.0
- OfflineQueue: 添加防抖批量保存 (5秒间隔)
- AnalyticsLog: 支持 DEVELOPMENT_BUILD 条件编译
- JSON: 增强 Unicode 控制字符转义
- Docs: 添加 AI 维护指南及安装说明更新

### v1.1.0
- 移除硬编码敏感信息
- 更新 package 描述

### v1.0.0
- 初始版本
