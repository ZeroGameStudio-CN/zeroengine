# ZeroEngine.Minimap API 文档

> **用途**: 本文档面向 AI 助手，提供 Minimap 模块的快速参考。

---

## 目录结构

| 文件 | 描述 |
|------|------|
| `MinimapEnums.cs` | 枚举定义和数据结构 |
| `MinimapController.cs` | 小地图控制器 |
| `MinimapMarker.cs` | 小地图标记组件 |

---

## MinimapController.cs

**用途**: 管理小地图相机、缩放、旋转模式、RenderTexture

### Public API

```csharp
public class MinimapController : MonoSingleton<MinimapController>
{
    // Properties
    Camera MinimapCamera { get; }
    RenderTexture RenderTexture { get; }
    float OrthographicSize { get; }
    float CurrentZoom { get; }
    Transform FollowTarget { get; set; }
    MinimapZoomMode ZoomMode { get; set; }
    MinimapRotationMode RotationMode { get; set; }

    // Events
    event Action<MinimapEventArgs> OnMinimapEvent;

    // Zoom Control
    void SetZoom(float zoom);
    void ZoomIn(float amount = 5f);
    void ZoomOut(float amount = 5f);

    // Queries
    MarkerIconConfig GetIconConfig(MinimapMarkerType type);
    Vector2 WorldToMinimapNormalized(Vector3 worldPosition);
    bool IsInMinimapBounds(Vector3 worldPosition);
    void GetVisibleMarkers(List<MinimapMarker> results);

    // Utility
    void RefreshCamera();
}
```

**使用示例**:

```csharp
using ZeroEngine.Minimap;

// 缩放控制
MinimapController.Instance.SetZoom(50f);
MinimapController.Instance.ZoomIn(10f);
MinimapController.Instance.ZoomOut(10f);

// 设置跟随目标
MinimapController.Instance.FollowTarget = playerTransform;

// 设置模式
MinimapController.Instance.ZoomMode = MinimapZoomMode.Manual;
MinimapController.Instance.RotationMode = MinimapRotationMode.PlayerUp;

// 获取 RenderTexture (用于 UI RawImage)
rawImage.texture = MinimapController.Instance.RenderTexture;

// 坐标转换
Vector2 normalizedPos = MinimapController.Instance.WorldToMinimapNormalized(worldPos);
bool inBounds = MinimapController.Instance.IsInMinimapBounds(worldPos);

// 获取可见标记
var visibleMarkers = new List<MinimapMarker>();
MinimapController.Instance.GetVisibleMarkers(visibleMarkers);
```

---

## MinimapMarker.cs

**用途**: 挂载到需要在小地图上显示的游戏对象上

### Public API

```csharp
public class MinimapMarker : MonoBehaviour
{
    // Properties
    MinimapMarkerType MarkerType { get; set; }
    Sprite CustomIcon { get; set; }
    Color IconColor { get; set; }
    float IconScale { get; set; }           // 0.5 - 3.0
    bool AlwaysVisible { get; set; }
    float VisibleRange { get; set; }        // >= 1
    bool RotateWithObject { get; set; }
    string Label { get; set; }
    bool ShowLabel { get; set; }

    // Methods
    bool IsVisibleFrom(Vector3 viewerPosition);
    float GetRotation();
    void Setup(MinimapMarkerType type, Sprite icon = null, Color? color = null);
}
```

**使用示例**:

```csharp
using ZeroEngine.Minimap;

// 添加标记组件
var marker = npc.AddComponent<MinimapMarker>();
marker.Setup(MinimapMarkerType.NPC, npcIcon, Color.green);

// 配置可见性
marker.AlwaysVisible = false;
marker.VisibleRange = 50f;

// 配置图标
marker.IconScale = 1.5f;
marker.RotateWithObject = true;

// 配置标签
marker.Label = "商人";
marker.ShowLabel = true;

// 检查可见性
bool visible = marker.IsVisibleFrom(playerPosition);

// 获取旋转角度 (用于 UI 显示)
float rotation = marker.GetRotation();
```

---

## MinimapMarkerManager (静态类)

**用途**: 静态追踪所有活动的小地图标记

### Public API

```csharp
public static class MinimapMarkerManager
{
    // Properties
    static IReadOnlyList<MinimapMarker> Markers { get; }
    static int MarkerCount { get; }

    // Events
    static event Action<MinimapMarker> OnMarkerRegistered;
    static event Action<MinimapMarker> OnMarkerUnregistered;

    // Methods
    static void Register(MinimapMarker marker);
    static void Unregister(MinimapMarker marker);
    static void GetMarkersOfType(MinimapMarkerType type, List<MinimapMarker> results);
    static void GetVisibleMarkers(Vector3 viewerPosition, List<MinimapMarker> results);
    static MinimapMarker FindNearest(Vector3 position, MinimapMarkerType type);
    static void ClearAll();
}
```

**使用示例**:

```csharp
using ZeroEngine.Minimap;

// 查询所有敌人标记
var enemies = new List<MinimapMarker>();
MinimapMarkerManager.GetMarkersOfType(MinimapMarkerType.Enemy, enemies);

// 获取玩家可见的标记
var visible = new List<MinimapMarker>();
MinimapMarkerManager.GetVisibleMarkers(playerPosition, visible);

// 查找最近的任务目标
var nearestQuest = MinimapMarkerManager.FindNearest(playerPosition, MinimapMarkerType.Quest);

// 监听标记事件
MinimapMarkerManager.OnMarkerRegistered += marker =>
{
    Debug.Log($"Marker added: {marker.MarkerType}");
};

MinimapMarkerManager.OnMarkerUnregistered += marker =>
{
    Debug.Log($"Marker removed: {marker.MarkerType}");
};

// 统计
Debug.Log($"Total markers: {MinimapMarkerManager.MarkerCount}");
```

---

## MinimapEnums.cs

### MinimapMarkerType

```csharp
public enum MinimapMarkerType
{
    Player,         // 玩家
    PartyMember,    // 队友
    Enemy,          // 敌人
    NPC,            // NPC
    Quest,          // 任务目标
    Waypoint,       // 路标
    Building,       // 建筑
    Portal,         // 传送门
    Item,           // 物品
    Treasure,       // 宝箱
    Shop,           // 商店
    Custom          // 自定义
}
```

### MinimapZoomMode

```csharp
public enum MinimapZoomMode
{
    Fixed,          // 固定缩放
    Dynamic,        // 动态缩放 (根据移动速度)
    Manual          // 手动缩放
}
```

### MinimapRotationMode

```csharp
public enum MinimapRotationMode
{
    NorthUp,        // 北朝上 (固定)
    PlayerUp        // 玩家朝上 (旋转)
}
```

### MinimapEventType

```csharp
public enum MinimapEventType
{
    MarkerAdded,
    MarkerRemoved,
    ZoomChanged,
    TargetChanged
}
```

### MinimapEventArgs

```csharp
public class MinimapEventArgs
{
    MinimapEventType Type { get; }
    MinimapMarker Marker { get; }
    float ZoomLevel { get; }
    Transform Target { get; }

    // Factory methods
    static MinimapEventArgs MarkerAdded(MinimapMarker marker);
    static MinimapEventArgs MarkerRemoved(MinimapMarker marker);
    static MinimapEventArgs ZoomChanged(float zoom);
    static MinimapEventArgs TargetChanged(Transform target);
}
```

### MarkerIconConfig

```csharp
[Serializable]
public class MarkerIconConfig
{
    MinimapMarkerType MarkerType;
    Sprite Icon;
    Color Color = Color.white;
    float Scale = 1f;
}
```

---

## 架构图

```
Minimap System (v1.13.0)
├── MinimapMarkerType                  # 12 种标记类型
├── MinimapZoomMode / RotationMode     # 模式枚举
├── MinimapEventArgs                   # 事件参数
├── MarkerIconConfig                   # 图标配置
├── MinimapMarker                      # 标记组件
│   ├── Visibility Control             # 可见性控制
│   └── Icon/Label Config              # 图标/标签配置
├── MinimapMarkerManager               # 静态标记管理器
│   └── Marker Queries                 # 标记查询
└── MinimapController                  # 小地图控制器
    ├── Camera Setup                   # 相机设置
    ├── RenderTexture                  # 渲染纹理
    ├── Follow Target                  # 目标跟随
    ├── Zoom Control                   # 缩放控制
    └── Rotation Modes                 # 旋转模式
```

---

## Inspector 配置

### MinimapController

| 字段 | 类型 | 描述 |
|------|------|------|
| `_minimapCamera` | Camera | 小地图相机 (可自动创建) |
| `_cameraHeight` | float | 相机高度 |
| `_renderTextureSize` | int | RenderTexture 分辨率 |
| `_cullingMask` | LayerMask | 相机剔除遮罩 |
| `_followTarget` | Transform | 跟随目标 |
| `_smoothFollow` | bool | 平滑跟随 |
| `_followSpeed` | float | 跟随速度 |
| `_zoomMode` | enum | 缩放模式 |
| `_orthographicSize` | float | 初始正交大小 |
| `_minZoom` / `_maxZoom` | float | 缩放范围 |
| `_zoomSpeed` | float | 缩放速度 |
| `_rotationMode` | enum | 旋转模式 |
| `_markerIconConfigs` | List | 标记图标配置列表 |

### MinimapMarker

| 字段 | 类型 | 描述 |
|------|------|------|
| `_markerType` | enum | 标记类型 |
| `_customIcon` | Sprite | 自定义图标 |
| `_iconColor` | Color | 图标颜色 |
| `_iconScale` | float | 图标缩放 |
| `_alwaysVisible` | bool | 始终可见 |
| `_visibleRange` | float | 可见范围 |
| `_rotateWithObject` | bool | 跟随对象旋转 |
| `_label` | string | 标签文本 |
| `_showLabel` | bool | 显示标签 |

---

## 完整使用示例

### 场景设置

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.Minimap;

public class MinimapSetup : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage _minimapImage;
    [SerializeField] private Slider _zoomSlider;
    [SerializeField] private Transform _player;

    [Header("Icons")]
    [SerializeField] private Sprite _playerIcon;
    [SerializeField] private Sprite _enemyIcon;
    [SerializeField] private Sprite _questIcon;

    private void Start()
    {
        // 绑定 RenderTexture 到 UI
        _minimapImage.texture = MinimapController.Instance.RenderTexture;

        // 配置缩放滑块
        _zoomSlider.onValueChanged.AddListener(zoom =>
        {
            MinimapController.Instance.SetZoom(zoom);
        });

        // 设置跟随目标
        MinimapController.Instance.FollowTarget = _player;

        // 添加玩家标记
        var playerMarker = _player.gameObject.AddComponent<MinimapMarker>();
        playerMarker.Setup(MinimapMarkerType.Player, _playerIcon, Color.blue);
    }
}
```

### 动态标记管理

```csharp
using UnityEngine;
using ZeroEngine.Minimap;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private Sprite _enemyIcon;

    public void SpawnEnemy(Vector3 position)
    {
        var enemy = Instantiate(enemyPrefab, position, Quaternion.identity);

        // 自动添加标记
        var marker = enemy.AddComponent<MinimapMarker>();
        marker.Setup(MinimapMarkerType.Enemy, _enemyIcon, Color.red);
        marker.AlwaysVisible = false;
        marker.VisibleRange = 30f;
    }
}

// MinimapMarker 在 OnDisable 时自动从 Manager 注销
// 当敌人被销毁时，标记自动移除
```

### 任务追踪

```csharp
using UnityEngine;
using ZeroEngine.Minimap;

public class QuestTracker : MonoBehaviour
{
    [SerializeField] private Sprite _questIcon;
    private MinimapMarker _currentQuestMarker;

    public void SetQuestTarget(Transform target)
    {
        // 移除旧标记
        if (_currentQuestMarker != null)
            Destroy(_currentQuestMarker);

        // 添加新标记
        _currentQuestMarker = target.gameObject.AddComponent<MinimapMarker>();
        _currentQuestMarker.Setup(MinimapMarkerType.Quest, _questIcon, Color.yellow);
        _currentQuestMarker.AlwaysVisible = true;  // 任务目标始终可见
        _currentQuestMarker.Label = "任务目标";
        _currentQuestMarker.ShowLabel = true;
    }

    public void ClearQuestTarget()
    {
        if (_currentQuestMarker != null)
        {
            Destroy(_currentQuestMarker);
            _currentQuestMarker = null;
        }
    }
}
```

### 导航系统集成

```csharp
using UnityEngine;
using System.Collections.Generic;
using ZeroEngine.Minimap;

public class NavigationHelper : MonoBehaviour
{
    public Transform GetNearestShop(Vector3 fromPosition)
    {
        var shop = MinimapMarkerManager.FindNearest(fromPosition, MinimapMarkerType.Shop);
        return shop != null ? shop.transform : null;
    }

    public List<Transform> GetAllQuestTargets()
    {
        var questMarkers = new List<MinimapMarker>();
        MinimapMarkerManager.GetMarkersOfType(MinimapMarkerType.Quest, questMarkers);

        var targets = new List<Transform>();
        foreach (var marker in questMarkers)
            targets.Add(marker.transform);

        return targets;
    }

    public int CountEnemiesInRange(Vector3 position, float range)
    {
        int count = 0;
        foreach (var marker in MinimapMarkerManager.Markers)
        {
            if (marker.MarkerType != MinimapMarkerType.Enemy) continue;

            float distance = Vector3.Distance(position, marker.transform.position);
            if (distance <= range)
                count++;
        }
        return count;
    }
}
```

---

## 与 UI 集成

### RawImage 显示

```csharp
// 基本绑定
rawImage.texture = MinimapController.Instance.RenderTexture;

// 缩放控制按钮
zoomInButton.onClick.AddListener(() => MinimapController.Instance.ZoomIn());
zoomOutButton.onClick.AddListener(() => MinimapController.Instance.ZoomOut());

// 切换旋转模式
rotationToggle.onValueChanged.AddListener(playerUp =>
{
    MinimapController.Instance.RotationMode = playerUp
        ? MinimapRotationMode.PlayerUp
        : MinimapRotationMode.NorthUp;
});
```

### 标记 UI 覆盖层

如果需要在 UI 上显示标记图标 (而不是通过相机渲染):

```csharp
// 获取标记的归一化位置
Vector2 normalizedPos = MinimapController.Instance.WorldToMinimapNormalized(marker.transform.position);

// 转换为 UI 坐标
Vector2 uiPos = new Vector2(
    normalizedPos.x * minimapRect.width,
    normalizedPos.y * minimapRect.height
);

// 检查是否在范围内
if (MinimapController.Instance.IsInMinimapBounds(marker.transform.position))
{
    markerIcon.rectTransform.anchoredPosition = uiPos;
    markerIcon.gameObject.SetActive(true);
}
else
{
    markerIcon.gameObject.SetActive(false);
}
```

---

## 版本历史

| 版本 | 变更 |
|------|------|
| v1.13.0 | 初始版本，从 ZGSProject_5 迁移 |
