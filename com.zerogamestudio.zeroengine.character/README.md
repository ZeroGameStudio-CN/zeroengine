# ZeroEngine.Character

角色成长系统模块，提供装备、天赋、队伍、阵型和职业管理。

## 功能概览

| 模块 | 说明 | 状态 |
|------|------|------|
| **Equipment** | 装备系统 (槽位/强化/套装) | ✅ 已实现 |
| **TalentTree** | 天赋树 (节点图/效果/Provider) | ✅ 已实现 |
| **Party** | 队伍系统 (成员/槽位/领袖) | ✅ v2.4.0 |
| **Formation** | 阵型系统 (位置/修正器) | ✅ v2.4.0 |
| **Job** | 职业系统 (主副职业/技能/JP) | ✅ v2.5.0 |

---

## EquipmentLoadout（逐角色装备内核）

`ZeroEngine.Equipment.EquipmentLoadout<TSlot, TEquipment>` 是普通 C# 对象，每个角色持有一份；不创建 GameObject，不依赖全局角色、存档服务或项目业务类型。`EquipmentManager` 组件也使用该内核管理槽位和装备属性，组件额外提供套装、强化事件和存档适配。

构造时由宿主提供可用槽位、装备到槽位的映射、`IEnumerable<EquipmentStatModifier>` 属性映射、`Func<StatId, Stat>` 属性查找，以及可选的穿戴资格判断。宿主保留等级、门派、内容 ID、背包转移和事件/存档映射；槽位状态和属性增删只由内核维护。查找/映射函数必须只读，所有装备属性必须能解析到角色已初始化的 `Stat`；不存在的属性、不可用槽位、无效修饰器或资格拒绝均保留旧装备。

- `Equipped` / `GetEquipped(slot)`：唯一槽位状态的只读视图；空槽位不写入字典。
- `TryEquip(item, out replaced, commitExternal = null)` / `Unequip(slot, commitExternal = null)`：替换和卸装；重复穿戴不累加。内核复制修饰器并按精确对象移除，不会误删共享来源的其他效果。
- `TryRestore(items)`：先解析全部内容，原子替换整个装备集合；重复槽位或无效内容不会清空旧集合。
- `RefreshStats()`：强化或宿主重建属性对象后重新绑定现有装备，保留已有装备资格，不重复穿戴。返回 `false` 时保留此前的属性贡献。
- `Clear()` / `Dispose()`：清除本内核持有的贡献。`Dispose()` 后拒绝后续变更；运行时所有权方应在角色退场时调用。

变更保持受影响 `CurrentStat` 的当前百分比；属性应用异常时撤回本次贡献并恢复旧贡献和当前值。自定义 `Stat` 实现必须支持添加/移除精确修饰器；回滚本身异常会向调用方传播，不能把不可恢复的目标错误当作成功。读取内容或映射函数抛出的异常发生在变更前，同样不会清空旧装备。

可选的 `Func<bool> commitExternal` 用于同线程背包交换：回调可读取暂存的新槽位和属性，返回 `false` 时内核恢复旧槽位、旧修饰器与原始当前资源值。资格或属性预检失败时不调用回调；回调期间拒绝重入写入。回调必须保证失败或抛出异常时外部状态没有变化，成功后不得再抛出异常；这是同步提交边界，不是分布式事务。宿主在整个调用成功返回之后发布业务事件。

`EquipmentInstance.GetStatModifiers()` 与 `GetCalculatedModifiers()` 共用一次基础/强化、附魔和宝石计算；前者保留每条修饰器对应的 `StatId`，避免把某个属性错误地添加到全部基础属性。内置 `StatType` 转为规范化名称（例如 `Attack` → `attack`）；消费项目若使用领域化 ID，应在内容映射时转换到自己的稳定 ID。

组件存档恢复通过 `ResolveEquipmentData` 或 `TryRestore(saveData, resolver)` 解析定义；解析失败保留当前装备，不再先清空后跳过未实现的恢复。

定向 EditMode 程序集：`ZeroEngine.Character.Tests.Editor`。测试 fixture：`ZeroEngine.Tests.Character.EquipmentLoadoutTests`、`ZeroEngine.Tests.Character.EquipmentInstanceStatModifierTests`，覆盖两角色隔离、替换去重、拒绝/部分失败回滚、资源百分比、清理恢复、属性重建和三类属性来源映射。消费项目运行包测试时需将 `com.zerogamestudio.zeroengine.character` 加入测试可见包列表（`testables`）。

---

## Job System (职业系统) v2.5.0

八方旅人风格的职业系统，支持主职业+副职业、技能学习、JP 经验。

### 核心类型

```csharp
// 职业类型
public enum JobType
{
    None = 0,
    // 物理系
    Warrior = 1,    // 剑士
    Thief = 2,      // 盗贼
    Hunter = 3,     // 猎人
    Monk = 4,       // 武僧
    // 魔法系
    Scholar = 5,    // 学者
    Cleric = 6,     // 神官
    Dancer = 7,     // 舞者
    Merchant = 8,   // 商人
    // 高级职业
    RuneBlade = 100,  // 魔剑士
    Starseer = 101,   // 星术师
    Warmaster = 102,  // 武器大师
    Inventor = 103,   // 发明家
    Custom = 1000     // 自定义职业
}

// 职业槽位类型
public enum JobSlotType
{
    Primary,    // 主职业 (不可更换)
    Secondary,  // 副职业 (可自由切换)
    Extra       // 额外职业槽
}

// 技能学习状态
public enum SkillLearnStatus
{
    Locked,     // 未解锁
    Available,  // 可学习
    Learned,    // 已学习
    Mastered    // 已精通 (永久保留)
}
```

### 快速开始

```csharp
using ZeroEngine.Character.Job;

// 1. 初始化职业系统
JobManager.Instance.Initialize("player", JobType.Warrior);

// 2. 设置副职业
JobManager.Instance.SetSecondaryJob(JobType.Scholar);

// 3. 获得 JP 并升级
JobManager.Instance.AddJP(500, applyToBoth: true);

// 4. 学习技能
JobManager.Instance.LearnSkill(JobType.Warrior, "slash_combo");

// 5. 获取可用技能 (主职业 + 副职业 + 精通)
var skills = JobManager.Instance.GetAvailableSkills();

// 6. 监听事件
JobManager.Instance.OnJobLevelUp += args =>
    Debug.Log($"{args.JobType} 升级: Lv.{args.OldLevel} -> Lv.{args.NewLevel}");

JobManager.Instance.OnSkillLearned += args =>
    Debug.Log($"学习技能: {args.Skill.DisplayName}");

JobManager.Instance.OnSecondaryJobChanged += args =>
    Debug.Log($"副职业: {args.OldJobType} -> {args.NewJobType}");
```

### 职业数据配置

```csharp
// 创建职业数据 (ScriptableObject)
// 菜单: Create > ZeroEngine > Job > Job Data
[CreateAssetMenu(fileName = "JobData", menuName = "ZeroEngine/Job/Job Data")]
public class JobDataSO : ScriptableObject
{
    public JobType JobType;              // 职业类型
    public JobCategory Category;         // 基础/高级/隐藏
    public string DisplayName;           // 显示名称
    public JobStatBonus BaseStatBonus;   // 基础属性加成
    public JobStatBonus LevelUpBonus;    // 每级成长
    public WeaponCategory AllowedWeapons; // 可用武器
    public List<JobSkillSO> Skills;      // 职业技能
    public bool IsUnlockedByDefault;     // 初始解锁
}
```

### 技能系统

```csharp
// 技能学习
var job = JobManager.Instance.PrimaryJob;
var skillStatus = job.GetSkillStatus(mySkill);

if (skillStatus == SkillLearnStatus.Available)
{
    // 消耗 JP 学习技能
    job.LearnSkill(mySkill);
}

// 技能精通 (换职业后仍可使用)
int availableSP = 1000;
job.MasterSkill(mySkill, ref availableSP);

// 装备支援技能 (精通技能可跨职业装备)
JobManager.Instance.EquipSupportSkill("healing_light", slotIndex: 0);
```

### 属性计算

```csharp
// 获取当前职业组合的属性加成
var totalBonus = JobManager.Instance.GetTotalStatBonus();

// 转换为 StatModifier 列表 (对接 StatSystem)
var modifiers = totalBonus.ToStatModifiers();
foreach (var (statName, value) in modifiers)
{
    statController.AddModifier(statName, value, ModifierType.Flat, "Job");
}

// 获取可用武器类型
WeaponCategory weapons = JobManager.Instance.GetAllowedWeapons();
if ((weapons & WeaponCategory.Sword) != 0)
{
    // 可以装备剑
}
```

### 职业解锁

```csharp
// 检查职业是否解锁
if (!JobManager.Instance.IsJobUnlocked(JobType.RuneBlade))
{
    // 高级职业需要满足条件
    // 例如: 击败祭坛守护者、多职业达到一定等级
}

// 手动解锁职业
JobManager.Instance.UnlockJob(JobType.Starseer);

// 监听解锁事件
JobManager.Instance.OnJobUnlocked += args =>
    ShowUnlockAnimation(args.JobData);
```

---

## Party System (队伍系统) v2.4.0

管理队伍成员、槽位、出战/后备切换。

### 核心类型

```csharp
// 队伍成员接口
public interface IPartyMember
{
    string MemberId { get; }
    string DisplayName { get; }
    PartyMemberType MemberType { get; }
    bool IsAlive { get; }
    bool CanAct { get; }
    int PartySlotIndex { get; set; }
    int Level { get; }
    Transform Transform { get; }
}

// 成员类型
public enum PartyMemberType
{
    Player,      // 玩家角色
    Companion,   // NPC 队友
    Mercenary,   // 雇佣兵
    Summon,      // 召唤物
    Temporary,   // 临时成员
    Pet          // 宠物
}

// 槽位类型
public enum PartySlotType
{
    Active,      // 出战
    Reserve,     // 后备
    Temporary,   // 临时 (战斗召唤)
    Pet          // 宠物
}
```

### 快速开始

```csharp
using ZeroEngine.Party;

// 1. 实现队伍成员
public class MyCharacter : PartyMemberBase
{
    public override bool IsAlive => _health > 0;

    // ... 角色逻辑
}

// 2. 配置 PartyManager (Inspector 中设置 PartyConfigSO)
// PartyConfigSO 定义:
//   - MaxActiveMembers = 4 (出战)
//   - MaxReserveMembers = 4 (后备)
//   - AllowSwitchInCombat = true

// 3. 添加成员
var character = FindObjectOfType<MyCharacter>();
PartyManager.Instance.AddMember(character, preferActive: true);

// 4. 切换出战/后备
PartyManager.Instance.SwapSlots(0, 4);  // 交换槽位 0 和 4
PartyManager.Instance.ActivateMember(reserveMember);  // 后备 -> 出战
PartyManager.Instance.DeactivateMember(activeMember); // 出战 -> 后备

// 5. 监听事件
PartyManager.Instance.OnMemberJoined += args =>
    Debug.Log($"{args.Member.DisplayName} 加入队伍");

PartyManager.Instance.OnSlotChanged += args =>
{
    if (args.IsActivated)
        Debug.Log($"{args.Member.DisplayName} 上场!");
};

PartyManager.Instance.OnPartyWiped += () =>
    Debug.Log("队伍全灭!");
```

### 查询 API

```csharp
// 获取成员
var leader = PartyManager.Instance.Leader;
var allActive = PartyManager.Instance.GetActiveMembers();
var allReserve = PartyManager.Instance.GetReserveMembers();
var aliveActive = PartyManager.Instance.GetAliveActiveMembers();

// 统计
int activeCount = PartyManager.Instance.ActiveMemberCount;
float avgLevel = PartyManager.Instance.AverageLevel;
bool isFull = PartyManager.Instance.IsActiveFull;
bool isWiped = PartyManager.Instance.IsWiped;

// 检查成员
bool inParty = PartyManager.Instance.HasMember(character);
var member = PartyManager.Instance.GetMemberById("hero_001");
```

### PartyConfigSO 配置

```csharp
[CreateAssetMenu(fileName = "PartyConfig", menuName = "ZeroEngine/Party/Party Config")]
public class PartyConfigSO : ScriptableObject
{
    public int MaxActiveMembers = 4;       // 出战队伍最大人数
    public int MaxReserveMembers = 4;      // 后备队伍最大人数
    public int MaxTemporarySlots = 2;      // 临时槽位数量
    public int MaxPetSlots = 1;            // 宠物槽位数量
    public bool AllowSwitchInCombat = true; // 战斗中切换
    public bool SwitchCostsAction = true;   // 切换消耗行动
}
```

---

## Formation System (阵型系统) v2.4.0

管理队伍阵型、成员位置、战术修正。

### 核心类型

```csharp
// 阵型槽位
public class FormationSlot
{
    public int SlotIndex;              // 对应 PartySlot
    public Vector3 LocalPosition;      // 相对位置
    public Vector3 LocalRotation;      // 相对旋转
    public FormationPosition PositionType; // Front/Middle/Back/Flank/Center
    public float DefenseModifier;      // 防御修正 (-0.5 ~ 0.5)
    public float AttackModifier;       // 攻击修正 (-0.5 ~ 0.5)
    public float ThreatWeight;         // 仇恨权重 (0 ~ 2)
}

// 位置类型
public enum FormationPosition
{
    Front,   // 前排 (坦克)
    Middle,  // 中排
    Back,    // 后排 (远程/法师)
    Flank,   // 侧翼
    Center   // 中心 (指挥官)
}

// 阵型类型
public enum FormationType
{
    Standard,   // 标准
    Offensive,  // 进攻
    Defensive,  // 防御
    Wedge,      // V形
    Circle      // 圆形
}
```

### 快速开始

```csharp
using ZeroEngine.Party;

// 1. 创建阵型配置 (ScriptableObject)
// 菜单: Create > ZeroEngine > Party > Formation

// 2. 配置 FormationManager
// - 添加可用阵型列表
// - 设置默认阵型
// - 设置阵型锚点

// 3. 切换阵型
FormationManager.Instance.SetFormation(offensiveFormation, animate: true);
FormationManager.Instance.CycleFormation(forward: true);

// 4. 获取战术修正
float defMod = FormationManager.Instance.GetDefenseModifier(member);
float atkMod = FormationManager.Instance.GetAttackModifier(member);
float threat = FormationManager.Instance.GetThreatWeight(member);

// 5. 监听事件
FormationManager.Instance.OnFormationChanged += args =>
    Debug.Log($"阵型: {args.OldFormation?.FormationName} -> {args.NewFormation.FormationName}");

FormationManager.Instance.OnMemberPositionUpdated += args =>
    Debug.Log($"{args.Member.DisplayName} 移动到 {args.NewPosition}");
```

### 阵型创建

```csharp
// 使用编辑器创建 FormationSO
// 右键菜单: Create > ZeroEngine > Party > Formation

// 或使用内置模板 (在 Inspector 中右键)
// - 创建默认4人阵型
// - 创建V形阵型
```

### 位置更新模式

```csharp
// 即时更新
FormationManager.Instance.UpdateMode = PositionUpdateMode.Instant;

// 平滑移动
FormationManager.Instance.UpdateMode = PositionUpdateMode.Smooth;
FormationManager.Instance.MoveSpeed = 5f;

// 手动控制 (由外部系统处理)
FormationManager.Instance.UpdateMode = PositionUpdateMode.Manual;
var targetPos = FormationManager.Instance.GetMemberTargetPosition(member);
```

---

## Equipment System

装备管理系统。详见 `Runtime/Equipment/README.md`。

```csharp
using ZeroEngine.Equipment;

// 装备物品
var equipment = new EquipmentInstance(weaponData);
EquipmentManager.Instance.TryEquip(equipment, out var replaced);

// 强化装备
EquipmentManager.Instance.TryEnhance(weaponSlot, out var result);

// 套装效果
var setEffects = EquipmentManager.Instance.GetActiveSetEffects();
```

---

## TalentTree System

天赋树系统。详见 `Runtime/TalentTree/README.md`。

```csharp
using ZeroEngine.TalentTree;

// 绑定天赋树
talentController.SetTree(myTalentTree);
talentController.AddPoints(10);

// 分配点数
if (talentController.CanAllocate(node))
{
    talentController.TryAllocatePoint(node);
}

// 获取属性修正
var mods = talentController.GetAllStatModifiers();
```

---

## 依赖

- `com.zerogamestudio.zeroengine.core` (2.0.0) - Core utilities
- `com.zerogamestudio.zeroengine.data` (2.0.0) - StatSystem, BuffSystem
- `com.zerogamestudio.zeroengine.persistence` (2.0.0) - Save/Load support
- `com.zerogamestudio.zeroengine.economy` (2.0.0) - Equipment item definitions and inventory contracts
- `com.zerogamestudio.zeroengine.formula` (0.6.0) - Preserves formula-backed equipment modifiers

消费项目需在同一 Git commit 显式引用这些 ZE 包；P5 已沿该合同固定全部 ZE 引用，本地 `file:` 验证不作为交付配置。

---

## 版本历史

### v2.4.0 (2026-01-07)
- **Party System** (队伍系统)
  - `IPartyMember` 成员接口
  - `PartyMemberBase` 成员基类
  - `PartySlot` 队伍槽位
  - `PartyManager` 队伍管理器 (MonoSingleton + ISaveable)
  - `PartyConfigSO` 队伍配置
  - 出战/后备切换
  - 领袖系统
  - 队伍状态管理
- **Formation System** (阵型系统)
  - `FormationSlot` 阵型槽位
  - `FormationSO` 阵型配置 (ScriptableObject)
  - `FormationManager` 阵型管理器 (MonoSingleton + ISaveable)
  - 位置/旋转管理
  - 战术修正 (防御/攻击/仇恨)
  - 平滑移动支持
  - Gizmos 可视化

### v2.0.0 (2026-01-05)
- 初始模块化发布 (从 ZeroEngine v1.17.0 拆分)
- Equipment System
- TalentTree System
