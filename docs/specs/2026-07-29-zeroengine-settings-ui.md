# ZeroEngine 通用设置 UI

- 状态：Implemented（待消费项目固定提交与人工视觉验收）
- 更新日期：2026-07-30
- 基线：ZeroEngine `194392dd24b83bd16ac5602a60c5484088c67b82`；GalleryKeeper Plastic `cs:42`；GameShopKeeper Plastic `cs:11`

## 目标

在 `com.zerogamestudio.zeroengine.settings` 内提供可直接使用、可换肤的完整 UGUI 标准设置页与改键页。安装同一包版本的游戏默认得到相同的设置 ID、分类、控件和设备切换能力；游戏只需提供设置读写、动作描述、文案及可选主题素材，并以追加方式放入项目专属设置。

## 非目标

- 不改变 `ISettingsService`、`SettingsSession`、存档格式或迁移协议。
- 不替代项目的主菜单、Logo、背景和美术构图。
- 不自动推断项目专属设置或 InputAction 业务含义；项目显式提供动作描述和项目扩展。
- 不强制项目使用同一主菜单背景、Logo、字体或美术主题。

## 当前问题

GalleryKeeper 的设置控件由 `GalleryStartScreen` 使用固定像素位置生成。显示页中的标签、滑条、开关和按钮没有共同的行容器，较长文本或外层尺寸变化时会错位；菜单装饰分割线还穿过设置内容区。4K Game View 使用正确的 CanvasScaler 比例，但绝对坐标缺陷会按比例放大，并非分辨率本身导致。

首轮接入后，Gallery 的改键弹窗仍保留项目内绝对坐标和固定双设备列。690 逻辑像素宽的默认宿主中，逐行“默认”按钮越出遮罩；动作数量继续增加时，标题、动作行和固定页脚也会相互覆盖。

## 已确认设计

1. UI 放入现有 settings 包的独立程序集 `ZeroEngine.Settings.UI`，继续一包安装；后端程序集不引用 UI 程序集。
2. 使用 Unity UGUI，与 GalleryKeeper、POB 的现有 UI 技术一致；settings 包增加 `com.unity.ugui` 依赖。
3. `SettingsUiTheme` 为可选 ScriptableObject，承载字体、颜色和 Sprite。主题为空或字段缺失时使用代码内默认颜色、内置字体和纯色图形。
4. `SettingsUiLayoutBuilder` 在调用方提供的 `RectTransform` 内生成：
   - 标题/副标题区；
   - 等宽分类标签栏；
   - 每分类独立的 ScrollRect；
   - 使用相对锚点的滑条、开关、选项和操作行；
   - 等宽底部“恢复默认/保存返回”按钮栏。
5. 行顺序由 `VerticalLayoutGroup` 管理，不允许调用方传入 Y 坐标。标签、控件和值使用同一套相对列，文本启用 best-fit。
6. Builder 返回控件引用、值文本和分类内 Selectable 顺序；设置语义、事件和手柄导航继续由消费项目绑定。
7. GalleryKeeper 使用包内 builder 重建设置页，保留当前字段、监听器、分类、保存及重置行为；项目装饰分割线移动到菜单卡左侧，不由包控制。
8. 改键保底 UI 使用“设备页签 + 单列动作列表”，而不是强制同时显示键鼠和手柄：
   - 页签数量由消费项目提供，可覆盖键鼠、手柄、方向盘或其他设备族；
   - 默认选择最后一次明确输入的设备族；
   - 动作、当前绑定和单项恢复位于同一响应式行；
   - 标题、设备页签和页脚固定，动作区使用 ScrollRect，数量不设 UI 上限；
   - 键盘、鼠标和手柄均能切换设备、遍历动作及完成/取消，焦点移动到遮罩外动作时自动滚入可见区。
9. 同时展示所有设备不是通用包强制规范。Microsoft 的要求是所有受支持输入方式都能完成导航和改键，并未要求同屏并列；并排视图可作为宽屏项目皮肤，设备页签作为更稳健的通用默认。
10. 组合键使用 Unity Input System composite 的 part binding GUID 表示，显示值可组合成 `LB + X`。通用输入服务继续按 part 重绑定和持久化；包含组合键的动作还应提供可映射为单键的替代 binding，避免把同时按键作为不可绕过的操作门槛。
11. `StandardSettingsUiBuilder` 是完整标准设置页的唯一清单所有者，固定生成：
    - 操作：键鼠灵敏度、手柄灵敏度、手柄死区、反转 Y、震动、手柄图标、改键入口；
    - 显示：窗口模式、分辨率、刷新率、垂直同步、帧率上限、画质；
    - 声音：主音量、音乐音量、音效音量；
    - 辅助：UI 缩放、高对比度、减少动态效果、语言。
    隐藏的 `BindingOverrides` 不直接显示，由改键入口管理。
12. 标准 builder 返回按 `StandardSettingIds` 索引的强类型控件集合。消费项目负责读写值和业务预览，但不能通过传入子集静默删掉标准控件；项目专属设置只能追加到分类末尾。
13. `StandardSettingsUiText` 内置简体中文和英文保底文案，可由项目按需覆盖。主题、字体、Sprite 和文案替换不改变标准清单与布局契约。
14. `StandardSettingsUiProfile` 集中定义滑条范围、步长和默认显示格式；GalleryKeeper 与 GameShopKeeper 不再各自复制这些 UI 常量。
15. Project 8 与 Project 9 必须由同一 `StandardSettingsUiBuilder` 生成标准部分。两边允许保留各自 adapter、即时预览和项目专属项，但源代码测试要阻止再次手写或筛选标准清单。

设计依据：

- [Xbox Accessibility Guideline 107](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/107) 要求由玩家选择输入机制、支持动作级重映射，并指出同时按多键会形成额外障碍。
- [Xbox Full Input Remapping 标签要求](https://learn.microsoft.com/en-us/xbox/accessibility/accessibility-feature-tags) 要求每一种直接支持的输入方式均可重映射。
- [Unity Input System composite 文档](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.4/manual/ActionBindings.html) 定义 composite root 与可独立重绑定的 part binding。

## 接口与文件

ZeroEngine：

- `com.zerogamestudio.zeroengine.settings/Runtime/UI/SettingsUiTheme.cs`
- `com.zerogamestudio.zeroengine.settings/Runtime/UI/SettingsUiLayoutBuilder.cs`
- `com.zerogamestudio.zeroengine.settings/Runtime/UI/StandardSettingsUiBuilder.cs`
- `com.zerogamestudio.zeroengine.settings/Runtime/UI/SettingsRebindUiLayoutBuilder.cs`
- `com.zerogamestudio.zeroengine.settings/Runtime/UI/SettingsUiSelectionScroller.cs`
- `com.zerogamestudio.zeroengine.settings/Runtime/UI/ZeroEngine.Settings.UI.asmdef`
- `com.zerogamestudio.zeroengine.settings/Tests/Editor/SettingsUiLayoutTests.cs`
- `com.zerogamestudio.zeroengine.settings/Tests/Editor/StandardSettingsUiBuilderTests.cs`
- `com.zerogamestudio.zeroengine.input/Runtime/InputSystem/InputBindingService.cs`
- `com.zerogamestudio.zeroengine.input/Tests/Editor/InputBindingServiceTests.cs`
- `com.zerogamestudio.zeroengine.settings/package.json`
- `com.zerogamestudio.zeroengine.settings/README.md`

GalleryKeeper：

- `Assets/Scripts/Runtime/GalleryStartScreen.cs`
- `Assets/Scripts/Runtime/GalleryHud.cs`
- `Assets/Scripts/Runtime/GalleryRuntimeText.cs`
- `Assets/Scripts/Runtime/GalleryKeeper.Runtime.asmdef`
- `Assets/Tests/Editor/GalleryUiSettingsInputTests.cs`
- `Assets/Tests/PlayMode/GalleryStartPlayModeTests.cs`
- `Packages/manifest.json`
- `Packages/packages-lock.json`

主要 API：

```csharp
var builder = new SettingsUiLayoutBuilder(host, font, theme);
SettingsUiShell shell = builder.BuildShell(title, subtitle);
Button tab = builder.CreateTab(shell, "Display Tab", label);
SettingsUiCategoryView category = builder.CreateCategory(shell, id);
Slider slider = builder.CreateSliderRow(category, id, label, out Text value);
Toggle toggle = builder.CreateToggleRow(category, id, label);
Button choice = builder.CreateChoiceRow(category, id, label, out Text value);
Button footer = builder.CreateFooterButton(shell, "Save Button", label, primary);

var standard = new StandardSettingsUiBuilder(host, font, theme)
    .Build(StandardSettingsUiText.SimplifiedChinese);
Slider pointer = standard.Slider(StandardSettingIds.PointerSensitivity);
Toggle invertY = standard.Toggle(StandardSettingIds.InvertY);
Button quality = standard.Choice(StandardSettingIds.Quality);
SettingsUiCategoryView display = standard.Category(StandardSettingsUiCategory.Display);

// 项目专属设置只能追加。
standard.Layout.CreateSliderRow(display, "Field Of View", "视野范围", out _);
```

## 兼容与回退

- 自定义设置 UI 可继续直接使用 settings 后端，不受新程序集影响。
- 标准快速接入路径不得删减基线；确需完全自定义的项目可继续直接使用底层服务，但不得宣称已接入标准菜单。
- Builder 只操作自己创建的对象；销毁宿主即可完整回滚。
- Gallery 可通过撤销 builder 接入并恢复原生成代码回滚，设置数据无需迁移。
- 主题缺失不阻断启动；CJK 项目应传入本地化字体，否则仅保证内置字体可显示其支持字符。

## 验证

- EditMode：在 690×708、560×600、960×720 宿主尺寸生成完整设置页，强制重建布局。
- EditMode：枚举标准控件，断言除隐藏存储项外的全部 `StandardSettingIds` 恰好出现一次，四个分类及顺序稳定。
- 断言标签、控件和值列不互相覆盖，分类内容可滚动，所有交互控件保持在宿主范围内。
- Gallery EditMode：设置页不再调用带 Y 坐标的旧行工厂，并引用 `ZeroEngine.Settings.UI`。
- Gallery PlayMode：打开显示分类，验证“垂直同步”、开关、帧率滑条位于同一内容区，分类切换、保存和返回仍可用。
- Gallery/GameShop 源码契约：两项目均引用 `StandardSettingsUiBuilder`，不维护标准 ID 子集，不手写标准行。
- 改键布局：在 690×708、560×600、960×720 宿主内生成 2 个设备页签和至少 40 个动作；所有行控件保持在行及遮罩横向范围内，末项可由焦点自动滚入可见区，页脚不移动。
- 组合键：以两个 part binding GUID 验证组合显示、单 part 覆盖、恢复和整资产 JSON 往返。
- 人工：1920×1080 与 3840×2160 Game View 检查显示页；键鼠与手柄各走一次分类切换。

## 实现结果

- `StandardSettingsUiBuilder` 固定生成 21 个标准 ID 对应的 20 行控件、四个分类、统一范围、双语保底文案、分类切换及键盘/手柄导航；分辨率行共同承载宽高，绑定 JSON 由改键入口承载。
- Unity 6000.3.10f1 EditMode：settings 完整程序集 22/22 通过；其中标准设置页覆盖 690×708、560×600、960×720，改键 UI 覆盖 40 个动作和焦点自动滚动。
- Input EditMode：完整程序集 9/9 通过；组合键覆盖多 part 显示、分别覆盖、恢复及整资产 JSON 往返。
- Gallery EditMode：标准 builder 接入约束 1/1 通过。
- Gallery PlayMode：设置/改键响应式流程 1/1、全公开页面英文覆盖 1/1 通过。
- GameShop PlayMode：完整标准定义、设置布局、语言切换与改键流程 1/1 通过。
- 测试发现并修复了 Toggle 误锚到行顶以及旧改键双列越界问题；当前 Toggle、动作行、绑定按钮、单项恢复、内容遮罩、固定页脚及装饰分割线边界均有回归断言。
- 1920×1080 / 3840×2160 人工视觉检查、真实键鼠/手柄走查及提交发布仍待消费项目验收。

## 验收标准

1. 无主题素材时可生成可见、可交互的完整设置页。
2. Gallery 设置页不再为设置行传入绝对 Y 坐标。
3. 690×708、560×600、960×720 三种宿主尺寸下，标签、控件和值列无重叠。
4. 超出可用高度时分类内容可滚动，页脚始终可见。
5. Tabs 与页脚按钮等宽，文本缩放后不越界。
6. 4K 与 1080p 下布局比例一致，Gallery 装饰分割线不穿过设置内容。
7. 设置分类、即时预览、恢复默认、保存返回、语言切换和改键入口行为不回归。
8. settings 后端 API、持久化文档和已有消费者保持兼容。
9. 改键弹窗默认不并排挤压多个设备列；任意动作数量通过滚动容纳，标题、设备页签和页脚始终固定。
10. 设备页签与动作行可完全使用鼠标、键盘方向键和手柄数字导航；选中不可见动作会自动滚入遮罩。
11. Input composite 的多个 part 可按 GUID 显示为组合、分别重绑定和恢复；组合动作存在单键替代路径。
12. Gallery 默认 8 个动作下，动作名、绑定按钮和单项恢复按钮均不越出改键弹窗。
13. Project 8 与 Project 9 锁定同一 settings/input 包提交时，标准设置 ID、分类、控件类型、顺序和保底文案完全一致。
14. 所有可见标准项恰好生成一次；`BindingOverrides` 仅由改键入口管理。
15. 项目可换主题、替换文案并追加专属项，但这些操作不改变或删除标准基线。
