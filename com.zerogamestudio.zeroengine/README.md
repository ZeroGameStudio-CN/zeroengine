# ZeroEngine 框架文档

[![Version](https://img.shields.io/badge/version-2.0.0-blue.svg)](CHANGELOG.md)
[![Unity](https://img.shields.io/badge/unity-2022.3+-lightgrey.svg)](https://unity.com)

## 概述

ZeroEngine 是一个模块化的 Unity 游戏开发框架，提供常用游戏系统的标准化实现。

---

## 文档导航

| 文档 | 说明 |
|------|------|
| [CHANGELOG.md](CHANGELOG.md) | 版本历史和更新记录 |
| [ROADMAP.md](../../ROADMAP.md) | 版本路线图和开发规划 |
| [CLAUDE.md](../../CLAUDE.md) | AI 开发助手指南 |
| `Runtime/{Module}/README.md` | 各模块 API 文档 |

---

## 核心模块

### 1. Core (核心)
- **Singleton<T>**: 单例基类
- **EventManager**: 全局事件系统

### 2. Save (存档)
- **SaveManager**: 底层存档管理器（EasySave3 可选）
- **(v1.1.0)** `ISaveProvider` 接口、`JsonSaveProvider` 纯 JSON 后端
- **(v1.1.0)** 自动选择后端：ES3 > JSON
- **(v1.8.0)** `SaveSlotManager`: 多槽位存档管理器
  - 支持 8+ 存档槽位 + 1 自动存档槽位
  - `ISaveable` 接口：模块化系统注册，自动收集/分发存档数据
  - `AutoSaveController`: 定时、场景切换、任务完成等触发自动存档
  - `ScreenshotCapture`: 存档预览截图捕获

### 3. StatSystem (属性系统)
- **Stat**: 属性类，支持 Flat/PercentAdd/PercentMult 修饰器
- **StatController**: 属性控制器
- **(v1.1.0)** 事件驱动：`OnValueChanged`、`OnAnyStatChanged`
- **(v1.1.0)** `InitStat` 重载支持 min/max 约束

### 4. BuffSystem (Buff系统)
- **BuffHandler**: Buff 管理器
- **BuffConfigSO**: Buff 配置

### 5. AbilitySystem (技能系统)
- **AbilityDataSO**: 技能配置（Trigger/Condition/Effect 组件）
- **AbilityHandler**: 技能施放控制器
- **(v1.1.0)** `AbilityInstance`: 运行时技能实例（等级、冷却）
- **(v1.1.0)** `AbilityCastState` 状态机：Idle/Casting/Executing/Recovering
- **(v1.1.0)** 打断机制、事件回调

### 6. Inventory (背包系统)
- **InventoryManager**: 背包管理器
- **InventoryItemSO**: 物品数据

### 7. Quest (任务系统)
- **QuestManager**: 任务管理器
- **QuestConfigSO**: 任务配置

### 8. UI (UI框架)

#### UI.Core (v1.6.0+)
- **UIManager**: 工业级 UI 管理器核心 (MonoSingleton)
  - 7 层级系统: Background/Main/Screen/Popup/Overlay/Top/System
  - 面板栈管理、遮罩管理、ESC 键关闭
  - 异步加载 (Addressables 可选)
- **UIViewBase**: 视图基类，完整生命周期
  - OnCreate -> OnOpen -> OnResume -> OnPause -> OnClose -> OnDestroy
  - 内置动画: Fade/Scale/SlideLeft/SlideRight/SlideTop/SlideBottom/Custom
  - 焦点管理 (手柄/键盘导航支持)
- **UIViewConfig**: 视图配置 (层级/显示模式/关闭模式/动画等)
- **UIViewDatabase**: ScriptableObject 配置数据库
- **UIUtility/UIExtensions**: 工具类和扩展方法

#### UI.MVVM
- **MVVMView<T>**: MVVM 视图基类
- **BindingContext**: 数据绑定上下文

### 9. Command (命令系统)
- **CommandManager**: 支持 Undo/Redo 的命令管理器

### 10. FSM (状态机)
- **StateMachine**: 有限状态机实现

### 11. BehaviorTree (行为树) (v1.3.0+)
- **BehaviorTree**: 行为树控制器
- **Blackboard**: 共享数据黑板
- **节点类型**: Sequence, Selector, Parallel, Repeater, Inverter, Conditional, ActionNode, WaitNode 等
- **FSM 集成**: RunFSMNode (BT 中运行 FSM), BTStateNode (FSM 中运行 BT), FSMBlackboardAdapter

### 12. Utils (工具类) (v1.2.0+)
- **ZeroLog**: 统一日志系统，为所有模块提供一致的日志格式
  - 支持 Info/Warning/Error 四个日志级别
  - 模块常量避免拼写错误
  - 运行时启用/禁用和级别控制
- **DateUtils**: 日期时间工具（时间戳、格式化、倒计时）
- **UIUtils**: UI 动画工具（CanvasGroup 淡入淡出、Tooltip 定位）
- **ZeroEase**: 缓动曲线枚举（解耦 DOTween 依赖）

## 使用方式
1. 将 `ZeroEngine` 文件夹导入项目
2. 通过菜单 **ZeroEngine -> Dashboard** 打开管理面板
3. 各模块单例在需要时自动初始化

## 可选依赖
- **UniTask**: 异步任务增强
- **EasySave3**: 存档功能
- **DOTween**: UI 动画
- **Unity Localization**: 多语言支持

## 单元测试

ZeroEngine 提供 EditMode 单元测试，覆盖核心模块。

### 运行测试

**在 Unity 编辑器中**:
1. 打开 `Window` -> `General` -> `Test Runner`
2. 选择 `EditMode` 标签
3. 点击 `Run All`

**命令行**:
```bash
Unity.exe -runTests -testPlatform EditMode -projectPath "<项目路径>" -testResults Logs/test-results.xml
```

### 测试覆盖

| 模块 | 测试文件 | 覆盖范围 |
|------|----------|----------|
| BehaviorTree | `BlackboardTests.cs` | 数据存储、类型化访问器 |
| BehaviorTree | `BehaviorTreeTests.cs` | 节点执行、组合逻辑 |
| FSM | `StateMachineTests.cs` | 状态切换、生命周期 |
| MVVM | `BindablePropertyTests.cs` | 值变更、格式化、验证 |
| MVVM | `ValidatorsTests.cs` | 验证器组合 |

详细信息见 `Tests/Editor/README.md`。

### 13. Localization (本地化)
- **LocalizationManager**: 封装 Unity Localization，简化多语言支持（可选依赖）
- **LocalizedText**: UI 组件，自动更新 Text/TMP 内容

### 14. Network (网络)
- **ZeroNetworkManager**: 基于 NGO 的网络管理器，支持配置化启动
- **ServerConfig**: 服务器配置（Assets/ZeroEngine/Network/Config）
- **启动参数**: 支持 `-port`, `-ip`, `-batchmode` 等标准参数控制
- **(v1.1.0)** `ZeroNetworkBehaviour`: NetworkBehaviour 便捷基类
- **(v1.1.0)** `ReconnectionHandler`: 自动断线重连（指数退避）

### 15. ModSystem (Mod系统)
- **Package dependency**: ModSystem has graduated into `com.zerogamestudio.zeroengine.modsystem`.
- **Core contracts**: new projects should depend on `ZeroEngine.ModSystem` from the standalone package.
- **Legacy compatibility**: old `$type` JSON import, `ModLoader`, hot reload, Lua hooks, legacy editor tools, and Steam helpers are available from the standalone package's legacy/optional assemblies.
- **Project adapters**: games own card, weapon, quest, localization, save, and runtime config semantics.

### 16. Pool (对象池) (v1.2.0+)
- **ObjectPool**: 通用对象池实现

### 17. Audio (音频) (v1.2.0+)
- **AudioManager**: 音频播放管理器

### 18. InputSystem (输入系统) (v1.2.0+)
- **InputManager**: 输入管理器，整合旧/新 Input System

### 19. Performance (性能优化) (v1.4.0+)
- **ZeroGC**: 统一入口 API，提供零 GC 的集合池和缓存访问
- **ListPool<T>**: 线程安全泛型 List 对象池
- **DictionaryPool<K,V>**: 线程安全泛型 Dictionary 对象池
- **StringBuilderPool**: StringBuilder 对象池
- **PooledList<T>** / **PooledDictionary<K,V>**: IDisposable 自动归还包装器
- **FormattedValueCache**: 格式化值缓存，避免重复格式化产生的 GC

### 20. Debugging (运行时调试) (v1.4.0+)
- **DebugManager**: 调试系统入口，管理所有调试模块
- **IDebugModule**: 调试模块接口
- **BTDebugger**: BehaviorTree 行为树调试器，追踪节点执行状态
- **FSMDebugger**: StateMachine 状态机调试器，记录状态转换
- **BuffMonitor**: Buff 系统监控，追踪 Buff 生命周期
- **StatMonitor**: Stat 系统监控，追踪属性值变化
- **PoolProfiler**: 对象池性能分析，采集 ZeroGC 池统计

### 21. Combat (战斗系统) (v1.15.0+)
- **CombatManager**: 战斗管理器核心 (MonoSingleton)
  - 战斗单位注册/伤害处理/队伍关系/范围查询
- **ICombatant**: 战斗单位接口
- **DamageCalculator**: 伤害计算器 (可配置公式/处理器管线)
- **DamageData/DamageResult**: 伤害数据结构 (readonly struct)
- **DamageType**: 伤害类型枚举 (Physical/Magical/Elemental)
- **TargetSelector**: 目标选择器 (多种选择策略)
- **TargetFilter/RangeChecker**: 目标过滤和范围检测工具
- **HealthComponent**: 生命值组件
- **DamageReceiver**: 伤害接收器 (部位伤害)
- **DeathHandler**: 死亡处理器 (视觉/音效/掉落)

### 22. Projectile (弹道系统) (v1.16.0+)
- **ProjectileBase**: 弹道基类 (Linear/Parabolic/Homing/Spiral)
- **ProjectileManager**: 弹道管理器 (对象池管理)
- **ProjectileHitEffect/TrailEffect**: 命中和拖尾效果

### 23. Spawner (生成器系统) (v1.16.0+)
- **SpawnerBase**: 生成器抽象基类
- **SpawnManager**: 生成管理器 (MonoSingleton)
- **WaveSpawner/AreaSpawner/PointSpawner/TriggerSpawner**: 各类生成器
- **SpawnCondition**: 生成条件 (Time/KillCount/Trigger)

### 24. AI (AI & Behavior 系统) (v1.17.0+)

**Common (通用组件)**
- **IAIBrain**: AI 大脑通用接口
- **AIContext**: AI 执行上下文
- **AIBlackboard**: AI 黑板数据共享
- **AIAgent**: AI 代理组件 (多大脑管理)

**UtilityAI (效用 AI - 自研)**
- **ResponseCurve**: 响应曲线 (11 种类型)
- **Consideration**: 考量因素基类 (支持 StatSystem/BuffSystem/Combat 集成)
- **UtilityAction**: 效用行动基类 (冷却/状态管理)
- **UtilityBrain**: 效用 AI 大脑 (MonoBehaviour, IAIBrain)

**GOAP (目标导向行动规划 - crashkonijn/GOAP 适配)**
- **GOAPBridge**: GOAP 桥接器
- **ZeroGOAPAgent**: ZeroEngine GOAP Agent 包装器
- 需要安装: `https://github.com/crashkonijn/GOAP.git?path=Package#3.0.0`

**NPCSchedule (NPC 日程系统 - 自研)**
- **ScheduleEntry**: 日程条目 (时间范围/日期过滤)
- **ScheduleAction**: 日程行动基类
- **NPCScheduleController**: NPC 日程控制器
- **NPCScheduleSO**: 日程 ScriptableObject
