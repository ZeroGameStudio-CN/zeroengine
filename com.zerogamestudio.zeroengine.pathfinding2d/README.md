# ZeroEngine.Pathfinding2D

2D 平台寻路系统，为 2D 横版平台游戏提供智能跳跃寻路能力。

## 版本

- **当前版本**: 1.6.19
- **依赖**: ZeroEngine.Core 2.0.0

## 概述

该系统通过扫描场景中的平台碰撞体，自动生成平台节点网络（Point Graph），并计算跳跃链接（Jump Links），让 AI 角色能够智能地在多层平台之间跳跃寻路。

## 特性

- **自动平台检测**：扫描 Collider2D 生成平台节点
- **智能跳跃链接**：基于抛物线物理计算跳跃可达性
- **A* 寻路**：内置 A* 路径搜索算法
- **MoveCommand 系统**：将路径转换为具体移动指令
- **编辑器可视化**：在 Scene 视图实时预览节点和链接
- **调试可视化系统** (v1.3.0+)：运行时 Gizmo 绘制完整路径轨迹（含跳跃抛物线）
- **空间索引加速** (v1.1.0+)：使用 SpatialGrid2D 将节点查询从 O(n) 优化到 O(k)
- **同平台快速路径** (v1.1.0+)：起终点在同一平台时跳过 A*，直接生成 Walk 指令
- **路径验证与回退** (v1.1.0+)：检测路径过期/目标移动/偏离，A* 失败时返回部分路径

## 核心组件

### PlatformGraphGenerator

扫描平台碰撞体，生成寻路节点网络。

```csharp
// 生成平台图
platformGraphGenerator.GeneratePlatformGraph();

// 查找最近节点（使用空间索引加速）(v1.1.0+)
var node = platformGraphGenerator.FindNearestNode(position, maxDistance);

// 查找范围内节点（无 GC 版本）(v1.1.0+)
var results = new List<PlatformNodeData>();
platformGraphGenerator.FindNodesInRangeNonAlloc(position, range, results);
```

### SpatialGrid2D (v1.1.0+)

2D 空间网格索引，加速节点查询。

```csharp
// 自动在 GeneratePlatformGraph() 时构建
// 通过 PlatformGraphGenerator.SpatialGrid 访问

// 查找最近节点 O(k) 而非 O(n)
var nearest = spatialGrid.FindNearest(position, maxDistance);

// 查找范围内所有节点（无 GC）
var results = new List<PlatformNodeData>();
spatialGrid.FindNodesInRange(position, range, results);
```

### JumpLinkCalculator

计算节点之间的跳跃可达性，生成跳跃链接。

```csharp
// 生成跳跃链接
jumpLinkCalculator.GenerateJumpLinks();
```

**v1.1.0 更新**：表面节点也支持垂直下落链接生成（水平距离 <= 1.5 单位）。

### Platform2DPathfinder

执行寻路请求，生成 MoveCommand 序列。

```csharp
// 显式绑定平台图生成器，或使用 PlatformNavigationBootstrap 创建组件
pathfinder.SetGraphGenerator(platformGraphGenerator);

// 请求路径
pathfinder.RequestPath(start, end);

// 获取当前移动指令
var cmd = pathfinder.GetCurrentCommand();

// 路径验证 (v1.1.0+)
var result = pathfinder.ValidatePath(currentPos, targetPos);
// result: Valid / Expired / TargetMoved / Deviated / NoPath

// 自动重新寻路 (v1.1.0+)
bool repathed = pathfinder.TryAutoRevalidate(currentPos, targetPos);
```

### MoveCommand

移动指令数据结构，包含四种类型：
- `Walk`：行走到目标点
- `Jump`：跳跃到目标点（包含跳跃速度）
- `Fall`：自由落体
- `DropDown`：穿过单向平台下落

## 使用示例

### 在 PathfindingGraphUpdater 中集成

```csharp
// PathfindingGraphUpdater 会在房间初始化后自动调用
private void GeneratePlatformGraph()
{
    platformGraphGenerator.GeneratePlatformGraph();
    jumpLinkCalculator.GenerateJumpLinks();
}
```

### 在 AI 状态组件中使用

```csharp
public override void OnPhysicsUpdate()
{
    var cmd = summon.CurrentMoveCommand;
    if (cmd.HasValue)
    {
        switch (cmd.Value.CommandType)
        {
            case MoveCommandType.Walk:
                // 向目标移动
                float dir = Mathf.Sign(cmd.Value.Target.x - position.x);
                movement.SetVelocityX(speed * dir);
                break;

            case MoveCommandType.Jump:
                // 使用计算好的跳跃速度
                movement.SetVelocityY(cmd.Value.JumpVelocityY);
                movement.SetVelocityX(cmd.Value.JumpVelocityX);
                break;
        }
    }
}
```

## 配置

### PlatformGraphConfig

| 参数 | 说明 | 默认值 |
|------|------|--------|
| NodeSpacing | 节点间距 | 1.5m |
| EdgeInset | 边缘内缩 | 0.3m |
| GroundLayer | 地面层 | - |
| OneWayPlatformLayer | 单向平台层 | - |
| UseDenseNodes | 启用密集节点模式 (v1.1.0+) | false |
| DenseNodeSpacing | 密集模式节点间距 (v1.1.0+) | 0.75m |
| SpatialGridCellSize | 空间索引格子尺寸 (v1.1.0+) | 3m |

### JumpLinkConfig

| 参数 | 说明 | 默认值 |
|------|------|--------|
| MaxJumpVelocity | 最大跳跃速度 | 14 |
| MaxHorizontalDistance | 最大水平跳跃距离 | 6m |
| MaxJumpHeight | 最大跳跃高度 | 4m |
| GravityScale | 重力缩放 | 3 |
| SurfaceNodeVerticalFallMaxHorizontal | 表面节点垂直下落最大水平距离 (v1.1.0+) | 1.5m |

### PathfinderConfig (v1.1.0+)

| 参数 | 说明 | 默认值 |
|------|------|--------|
| PathRequestInterval | 路径请求间隔 | 0.3s |
| PathExpireTime | 路径过期时间 | 2s |
| ArriveDistance | 到达判定距离 | 0.5m |
| TargetMoveThreshold | 目标移动重寻路阈值 | 2m |
| PathDeviationThreshold | 偏离路径重寻路阈值 | 3m |
| AutoValidatePath | 启用自动路径验证 | true |
| AllowPartialPath | 允许返回部分路径 | true |
| SamePlatformMaxHeightDiff | 同平台快速路径最大高度差 | 0.5m |

## 依赖

- ZeroEngine.Core >= 2.0.0

## 版本历史

### 1.3.0 - 2026-01-18

**调试可视化系统**
- 新增 `PathfindingDebugger` - 运行时 Gizmo 可视化组件
- 新增 `PathfindingDebugConfig` - 颜色、尺寸、开关配置
- 新增 `PathfindingDebugGUI` - 运行时状态 GUI 面板（可选）
- 跳跃轨迹缓存 - `PlatformLinkData.JumpTrajectory` 和 `MoveCommand.JumpTrajectory`
- 轨迹预计算 - 链接生成时缓存抛物线轨迹点，避免每帧重算

**颜色编码方案**
| 颜色 | 移动类型 |
|------|----------|
| 绿色 | Walk 行走 |
| 黄色 | Jump 跳跃（抛物线） |
| 青色 | Fall 下落 |
| 蓝色 | DropDown 穿透平台 |
| 红色 | 完整路径预览 |
| 品红 | 路径终点 |
| 灰色 | 已完成路径段 |

**使用方法**
```csharp
// 添加 PathfindingDebugger 组件到寻路器同一 GameObject
var debugger = gameObject.AddComponent<PathfindingDebugger>();
debugger.Pathfinder = pathfinder;

// 可选：添加运行时 GUI 面板
var gui = gameObject.AddComponent<PathfindingDebugGUI>();
```

### 1.2.0 - 2026-01-18

**新增功能**
- 诊断报告系统 - `GenerateDiagnosticReport()` 输出详细平台图信息
- 跳跃链接诊断 - 输出跳跃尝试次数、失败原因分类
- Odin Inspector 支持 - 诊断按钮在 Inspector 中直接点击

**诊断报告内容**
- 扫描配置（中心、尺寸、层级）
- 扫描到的碰撞体列表（名称、类型、Bounds、Y范围）
- 节点按高度分布及 X 范围
- 链接统计（Walk/Jump/Fall/DropThrough）

### 1.1.0 - 2026-01-16

**新增功能**
- `SpatialGrid2D` - 2D 空间网格索引，将节点查询从 O(n) 优化到 O(k)
- 同平台快速路径 - 起终点在同一平台时跳过 A*，直接生成 Walk 指令
- 路径验证系统 - 检测路径过期、目标移动、偏离路径
- 路径回退策略 - A* 失败时返回部分路径
- 表面节点垂直下落链接 - 不再限于边缘节点

**性能优化**
- 空间索引加速 `FindNearestNode()` 和 `FindNodesInRange()`
- 新增 `FindNodesInRangeNonAlloc()` 零 GC 版本
- 密集节点模式配置选项

**新增配置**
- `PathfinderConfig` - 路径请求、验证、回退策略配置
- `PlatformGraphConfig.UseDenseNodes` - 密集节点模式
- `PlatformGraphConfig.SpatialGridCellSize` - 空间索引格子尺寸
- `JumpLinkConfig.SurfaceNodeVerticalFallMaxHorizontal` - 表面节点下落距离

**代码审查**
- 综合评分: 93/100
- 已修复: 移除未使用的缓存字段

### 1.0.0 - 初始版本

- 平台图自动生成
- 跳跃链接计算
- A* 寻路算法
- MoveCommand 指令系统

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
