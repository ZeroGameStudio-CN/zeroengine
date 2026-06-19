# ZeroEngine.SpineSkin API 文档

> **用途**: 本文档面向AI助手，提供SpineSkin（Spine换装）模块的快速参考。
> **版本**: v1.0.0+
> **最后更新**: 2025-12-31

---

Spine换装与多角色管理的可选模块。

## 依赖

- **必须**: ZeroEngine 核心
- **必须**: [Spine-Unity](https://zh.esotericsoftware.com/spine-unity) (com.esotericsoftware.spine.spine-unity)
- **可选**: TextMeshPro (UI文本)
- **可选**: DOTween (UI动画)

> **注意**: 如果未安装Spine-Unity，此模块不会编译（通过`SPINE_UNITY`宏控制）。

---

## 快速开始

### 1. 创建配置

1. 右键 `Assets` → `Create` → `ZeroEngine` → `SpineSkin` → `Config`
2. 命名为 `SpineSkinConfig`
3. 配置以下内容：
   - 拖入 `SkeletonDataAsset`
   - 设置皮肤命名模式
   - 添加皮肤槽位

### 2. 设置场景

```
GameObject
├── CharacterManager (Component)
│   └── SpineSkinManager (Component)
│       └── SkeletonAnimation (Spine组件)
├── UI
│   ├── CharacterSelectView
│   └── SkinCustomizationView
```

### 3. 基本使用

```csharp
// 获取管理器
var charManager = FindObjectOfType<CharacterManager>();
var skinManager = charManager.SkinManager;

// 创建角色
var character = charManager.CreateCharacter("我的角色", "Female");

// 换装
skinManager.EquipSkin("Hair", "Female/Hair/Hair-01");
skinManager.EquipSkin("Clothes", "Female/Clothes/Dress-01");

// 保存
charManager.SaveCurrentCharacterSkins();
```

---

## 配置说明

### SpineSkinConfig

| 属性 | 类型 | 说明 |
|------|------|------|
| `SkeletonDataAsset` | SkeletonDataAsset | Spine骨骼数据 |
| `SkinNamePattern` | string | 皮肤命名模式，支持 `{gender}`, `{slot}`, `{name}` |
| `GenderNames` | List\<string\> | 性别列表，如 ["Female", "Male"] |
| `DefaultGenderIndex` | int | 默认性别索引 |
| `MaxCharacterCount` | int | 最大角色数，0=无限 |
| `SkinSlots` | List | 皮肤槽位配置 |
| `AnimationDuration` | float | UI动画时长 |
| `ButtonAppearDelay` | float | 按钮出现延迟 |
| `EmptySlotIcon` | Sprite | 空槽位图标 |

### SkinSlotConfig

| 属性 | 类型 | 说明 |
|------|------|------|
| `SlotId` | string | 槽位唯一ID |
| `DisplayName` | string | 显示名称 |
| `Icon` | Sprite | 分类图标 |
| `IsRequired` | bool | 是否必须装备 |
| `SortOrder` | int | UI排序 |
| `DefaultSkin` | string | 默认皮肤名 |

---

## 皮肤命名规范

默认规范: `{gender}/{slot}/{name}`

示例:
```
Female/Hair/Hair-Short
Female/Hair/Hair-Long
Male/Hair/Hair-Short
Female/Clothes/Dress-01
Female/Clothes/Shirt-01
```

### 自定义命名

修改 `SkinNamePattern`:
- `{slot}/{name}` - 无性别区分
- `{gender}_{slot}_{name}` - 使用下划线分隔
- `Skins/{gender}/{slot}/{name}` - 添加前缀

---

## API 参考

### SpineSkinManager

```csharp
// 换装
void EquipSkin(string slotId, string skinName);
void UnequipSkin(string slotId);
void ApplySkins();
void SetGender(string gender);
void SetAllSkins(string gender, Dictionary<string, string> skins);

// 查询
string GetEquippedSkin(string slotId);
List<string> GetAvailableSkins(string slotId, string gender = null);
Dictionary<string, string> ExportEquippedSkins();

// 属性
IReadOnlyDictionary<string, string> EquippedSkins { get; }
string CurrentGender { get; }

// 事件
event Action<string, string> OnSkinChanged;
event Action<string> OnGenderChanged;
```

### CharacterManager

```csharp
// 角色管理
CharacterData CreateCharacter(string name, string gender = null);
void SelectCharacter(string id);
void SelectCharacter(CharacterData character);
bool DeleteCharacter(string id);
void UpdateCharacterName(string id, string newName);
void SaveCurrentCharacterSkins();

// 存档
CharacterSaveData ExportSaveData();
void ImportSaveData(CharacterSaveData data);

// 属性
CharacterData CurrentCharacter { get; }
IReadOnlyList<CharacterData> Characters { get; }
bool CanCreateCharacter { get; }
bool HasAnyCharacter { get; }

// 事件
event Action<CharacterData> OnCharacterCreated;
event Action<CharacterData> OnCharacterSelected;
event Action<string> OnCharacterDeleted;
event Action OnCharactersLoaded;
```

---

## 存档集成

### 与 ZeroEngine SaveManager 集成

```csharp
// 保存
var saveData = characterManager.ExportSaveData();
SaveManager.Instance.Set("characters", saveData);
await SaveManager.Instance.SaveAsync();

// 读取
var saveData = SaveManager.Instance.Get<CharacterSaveData>("characters");
if (saveData != null)
    characterManager.ImportSaveData(saveData);
```

---

## UI 集成

### CharacterSelectView

| 事件 | 说明 |
|------|------|
| `OnCreateCharacterRequested` | 点击创建按钮，应打开 SkinCustomizationView |
| `OnCharacterConfirmed` | 确认选择角色 |

### SkinCustomizationView

| 事件 | 说明 |
|------|------|
| `OnConfirmed` | 确认换装 |
| `OnCancelled` | 取消换装 |

### 界面流程示例

```csharp
// CharacterSelectView 中
characterSelectView.OnCreateCharacterRequested += () =>
{
    var newChar = characterManager.CreateCharacter("新角色");
    characterManager.SelectCharacter(newChar);
    characterSelectView.gameObject.SetActive(false);
    skinCustomizationView.gameObject.SetActive(true);
};

// SkinCustomizationView 中
skinCustomizationView.OnConfirmed += () =>
{
    skinCustomizationView.gameObject.SetActive(false);
    characterSelectView.gameObject.SetActive(true);
};
```

---

## 文件结构

```
Packages/com.zerogamestudio.zeroengine/Runtime/SpineSkin/
├── ZeroEngine.SpineSkin.asmdef
├── README.md
├── Config/
│   └── SpineSkinConfig.cs
├── Core/
│   ├── CharacterData.cs
│   ├── CharacterManager.cs
│   └── SpineSkinManager.cs
└── UI/
    ├── CharacterSelectView.cs
    └── SkinCustomizationView.cs
```
