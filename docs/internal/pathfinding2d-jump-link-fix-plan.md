# 跳跃链接生成修复计划 v7

**Goal:** 修复边缘节点生成和下落链接过多的问题

**Architecture:** 框架层 (ZeroEngine) 寻路系统

---

## 问题诊断

### 日志数据 (1.6.14)
```
边缘节点=11, 平台数(按高度)=4
跳跃=7, 下落=152, 穿透=0
超高度=147 (已改善，之前 1701)
```

### 平台布局
```
Y=0:   [0.30 -------- 22.00 -------- 32.00 -------- 74.00 -------- 102.70]
Y=2:              [22.30 ---- 31.70]
Y=5:                    [32.30 ---------- 73.70]
Y=10:                         [38.30 ---- 67.70]
```

### 根因分析

| 问题 | 原因 | 位置 |
|------|------|------|
| 落地点没有生成 | `upper.collider == lower.collider` 跳过了 Tilemap 场景（共享 Collider） | 第 471 行 |
| 下落链接过多 (152) | Surface 节点下落时没有检查终点是否是边缘 | 第 230-236 行 |
| 边缘节点没增加 | 因问题1，所有平台对都被跳过 | - |

---

## 修复方案

### Task 1: 移除同 Collider 跳过逻辑

**File:** `PlatformGraphGenerator.cs`
**位置:** 第 470-471 行

**当前代码：**
```csharp
// 同一 Collider 的边缘已由 GenerateHeightTransitionNodes 处理
if (upper.collider == lower.collider) continue;
```

**修改为：**
```csharp
// 注意：不再跳过同 Collider 的边缘
// 因为 Tilemap Composite Collider 场景中所有平台共享同一个 Collider
// GenerateHeightTransitionNodes 只处理同一路径内的边缘，不处理跨高度的边缘
```

（删除这两行代码）

### Task 2: 修复 Surface 节点下落终点检查

**File:** `JumpLinkCalculator.cs`
**位置:** 第 229-236 行

**当前代码：**
```csharp
// 表面节点：仅限垂直下落（水平距离很小）
else if (isSurfaceNode && horizontalDist <= config.SurfaceNodeVerticalFallMaxHorizontal)
{
    if (TryCreateFallLink(fromNode, toNode, obstacleLayer))
    {
        fallLinksCreated++;
    }
}
```

**修改为：**
```csharp
// 表面节点：仅限垂直下落（水平距离很小）
// ★ 终点也必须是边缘节点，防止平台内部生成大量无意义的下落链接
else if (isSurfaceNode && horizontalDist <= config.SurfaceNodeVerticalFallMaxHorizontal)
{
    bool toIsEdge = toNode.NodeType == PlatformNodeType.LeftEdge ||
                    toNode.NodeType == PlatformNodeType.RightEdge;
    if (!toIsEdge)
    {
        fallSkippedToNotEdge++;
        continue;
    }

    if (TryCreateFallLink(fromNode, toNode, obstacleLayer))
    {
        fallLinksCreated++;
    }
}
```

### Task 3: 更新版本号

**Files:** `package.json`, `CHANGELOG.md`
- 版本 1.6.14 → 1.6.15
- 记录修复内容

---

## 文件变更汇总

| 文件 | 变更类型 | 变更位置 |
|------|----------|----------|
| `PlatformGraphGenerator.cs` | 删除 2 行 | 第 470-471 行 |
| `JumpLinkCalculator.cs` | 修改 | 第 229-236 行 |
| `package.json` | 修改 | version 字段 |
| `CHANGELOG.md` | 新增 | 1.6.15 版本记录 |

---

## 验收测试

运行游戏后检查日志：

| 指标 | 修复前 | 修复后预期 |
|------|--------|-----------|
| 边缘节点 | 11 | 15+ |
| 跳跃链接 | 7 | 15+ |
| 下落链接 | 152 | < 30 |

**具体验证：**
1. Y=0 的起跳点应有对应的 Y=2/5 落地点
2. Surface 节点不再生成到 Surface 节点的下落链接
3. 平台间跳跃/下落路径完整可用
