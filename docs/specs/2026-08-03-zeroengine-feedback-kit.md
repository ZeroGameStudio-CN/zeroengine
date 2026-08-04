# ZeroEngine 通用玩家反馈套件

- **状态：** Final
- **最后更新：** 2026-08-04
- **检查基线：** ZeroEngine 不可变提交 `7afa51fb151448cbfcf99c752d87170e127a73bc`；POB Plastic 提交 `cs:16744`
- **适用版本：** `com.zerogamestudio.analytics` 保持 Unity 2021.3+；可选玩家 UI 包要求 Unity 2022.3+
- **授权边界：** 用户已于 2026-08-04 批准实现、验证、提交、推送和 POB checkin；合并、发布和部署仍不在本次范围

## 1. 结论

把 POB 已验证的反馈上传能力拆成两层：

1. `com.zerogamestudio.analytics` 负责无 UI 的可靠提交、ZIP 打包、附件预算、持久队列、后台补传和成功事件。
2. 新增可选 `com.zerogamestudio.zeroengine.feedback`，负责最低可用、可换肤的玩家反馈表单，并复用 `ZeroEngine.UI.Toast` 显示短状态。

POB 不再维护一套 ZIP 上传器，只保留存档、备份、构建信息、Steam 身份、截图选择、项目本地化和现有面板皮肤等薄适配。不得把 `BugReportPanel`、`BasePanel`、`PanelManager`、`POBAlert`、StandaloneFileBrowser 或 POB 字体素材整体搬入 ZeroEngine。

## 2. 目标与非目标

### 2.1 目标

1. 具备标准 UGUI EventSystem 的新 Unity 2022.3+ 项目，只需固定同一 ZeroEngine 提交、创建 Analytics 配置并调用一次 `FeedbackPanel.Open()`，即可获得文本反馈、可选联系方式、日志、后台可靠上传和默认 UI；不要求自制上传器或 Prefab。
2. 玩家提交后只等待本地 ZIP 与队列记录持久化；成功后面板关闭并提示“上传中”，当前进程收到 HTTP 成功响应后提示“上传成功”。
3. 通用包提供安全预算、截图处理、日志截断、时间线脱敏和项目附件扩展点，项目适配器不能复制 ZIP/队列实现。
4. 默认面板在没有项目素材时仍可见、可操作、可本地化、可用键鼠/手柄/触摸完成；项目可以替换主题、文案和宿主面板。
5. POB 迁移后保留现有玩家行为、三张可选截图、存档诊断和最多五份备份，不回归已实现的后台上传体验。

### 2.2 非目标

- 不增加反馈历史、上传进度、百分比、手动重试、处理状态、飞书状态、游戏内回复或客服收件箱。
- 不修改 `/upload` 服务端协议、ZGS Ops、飞书通知链路或部署配置。
- 不自动扫描整个 `persistentDataPath`；通用默认只收集明确允许的日志、时间线和玩家选择的附件。
- 不在通用包中集成 StandaloneFileBrowser、Steamworks、Easy Save、Addressables 或某个项目的面板管理器。
- 不把 POB Logo、品牌图、字体或 Prefab 作为运行时核心素材。POB 风格皮肤只有在素材权属和字体许可单独确认后，才能作为非默认 `Samples~` 内容进入后续范围。
- 不在本规格内建设服务端幂等；响应丢失后的重复投递继续沿用现有服务端处理能力。

## 3. 已检查的当前状态

- Analytics `1.6.2` 无 UI 依赖并兼容 Unity 2021.3；`FeedbackUploadQueue` 已在基线提交中具备可靠入队、退避唤醒和成功事件。
- SDK 默认 `ZipAttachmentUploader.Upload()` 仍先调用 `UploadWithRetry()`，最长等待前台重试后才入队，因此新项目直接使用 SDK 仍会复现 POB 的长等待。
- ZeroEngine 通用 ZIP 上传器为 910 行，POB 上传器为 995 行；两者重复实现 ZIP、截图缩放、日志尾部截断、条目/字节预算、manifest、文件重读、路径清洗和清理。
- POB 专用部分是存档与备份筛选、`POBBuildInfo`、存档健康、平台诊断、Steam 身份、项目时间线和 `POBAlert`；这些不应进入 Analytics 核心。
- `CrashReporter.ReportBugWithAttachments()` 当前无条件覆盖调用方提供的 `TimelineJson`，会绕过 POB 已写的 80 条、64 KiB、敏感键脱敏逻辑；通用化时必须修复。
- `ZeroEngine.UI.Toast` 已支持无场景配置的运行时默认 Presenter、项目文案解析器和可生成的换肤 Prefab，可直接承担反馈状态提示。
- POB 现有反馈面板依赖 `BasePanel`、`PanelManager`、`LocalizationUtilities`、`PlatformServiceFactory` 和 StandaloneFileBrowser，不能原样成为通用面板。

## 4. 已确认设计与不变量

### 4.1 包边界

1. Analytics 继续保持无 UI、无 TMP、无 POB 依赖；其 Unity 2021.3 兼容线不因玩家面板提高。
2. 新包命名为 `com.zerogamestudio.zeroengine.feedback`，命名空间为 `ZeroEngine.Feedback`，依赖同一提交的：
   - `com.zerogamestudio.analytics` `1.7.0`；
   - `com.zerogamestudio.zeroengine.ui` `2.0.0`；
   - UI 包已有的 TextMeshPro/UGUI 依赖。
3. Analytics 从 `1.6.2` 升到 `1.7.0`：这是新增公共提交合同并改变默认 ZIP 上传器完成时机的次版本更新。新 Feedback 包首次稳定版本为 `1.0.0`。这两个版本号是可逆的版本管理默认，不代表已发布。
4. 不让 `zeroengine.ui` 反向依赖 Analytics；不需要反馈功能的 UI 项目不会被迫安装 Analytics。

### 4.2 玩家交互

默认面板只包含：标题、必填的多行问题描述、可选联系方式、条件显示的最多三张附件、发送和取消。三张附件上限沿用 POB 已验证布局与预算，是本版的可逆默认；需要更多附件的项目使用自定义 View。

状态语义固定为：

```text
提交
  -> 校验描述与配置
  -> 创建 ZIP
  -> 持久化队列项
     -> 成功：回调 AcceptedLocally -> 关闭面板 -> Toast“上传中”
        -> 后台 HTTP 2xx 且队列移除已持久化：当前进程 Toast“上传成功”
        -> 网络失败：静默保留并补传，重启后继续
     -> 失败：保留面板与输入，恢复按钮，Toast“上传失败，请重试”
```

- “上传中”只表示本地可靠接收，不表示服务器收到。
- “上传成功”只能由真实 HTTP 成功且队列项已持久化移除触发。
- 旧队列在新进程补传成功保持静默。
- ZIP 创建、入队、超时、淘汰、过期和文件缺失均不得伪造成功。
- 不创建历史行、状态页或回复入口。

### 4.3 默认外观与素材

- 运行时没有配置 Presenter 时，包用 UGUI/TMP 自动生成中性遮罩、面板、输入框和按钮；默认颜色与纯色图形不依赖图片素材。
- `FeedbackUiTheme` 可替换字体、颜色、Sprite 和间距；字段缺失时回落到中性默认。
- 编辑器菜单 `ZeroEngine/Feedback/Install Default UI` 将 `FeedbackUiTheme.asset` 和 `FeedbackPanel.prefab` 生成到消费项目 `Assets/ZeroEngineGenerated/Feedback`，供项目安全修改；不要求修改 PackageCache。
- Toast 直接复用 `ZeroEngine.UI.Toast` 的自动 Presenter；项目已有 Alert 适配器时可替换状态 Presenter。
- 核心包不复制 POB 字体或美术。POB 继续使用自己的 Prefab/皮肤，未来项目则有中性最低保证。

## 5. Analytics 接口、数据与状态流

新增公共合同如下；字段可用属性实现，但名称和语义不变：

```csharp
namespace ZGS.Analytics
{
    public sealed class FeedbackSubmissionRequest
    {
        public string UserMessage;                 // 必填
        public string Contact;                     // 可选，玩家明确输入
        public string UserName;                    // 可选项目身份
        public string[] FilesToInclude;            // 明确选择的文件
        public Dictionary<string, object> ExtraData;
        public TimelineLogger.TimelineEntry[] TimelineEntries; // null 时取当前快照
        public IFeedbackPackageContributor[] Contributors;
    }

    public enum FeedbackSubmissionFailure
    {
        None,
        InvalidRequest,
        NotConfigured,
        PackageCreationFailed,
        QueuePersistenceFailed
    }

    public readonly struct FeedbackSubmissionResult
    {
        public string SubmissionId { get; }
        public bool AcceptedLocally { get; }
        public FeedbackSubmissionFailure Failure { get; }
    }

    public readonly struct FeedbackUploadCompletion
    {
        public string SubmissionId { get; }
    }

    public interface IFeedbackPackageContributor
    {
        void Collect(FeedbackPackageContext context, FeedbackPackageCollector collector);
    }

    public static class FeedbackSubmissionService
    {
        public static event Action<FeedbackUploadCompletion> UploadSucceeded;
        public static IEnumerator Submit(
            FeedbackSubmissionRequest request,
            Action<FeedbackSubmissionResult> completed);
    }
}
```

`FeedbackPackageCollector` 只开放以下受预算控制的动作：

- `AddFile(sourcePath, archiveRelativePath, kind, priority)`；`kind` 只区分 Log、Screenshot、ProjectState、Generic。
- `AddText(archiveRelativePath, content, priority)`。
- `AddMetadata(key, value)`；值写入 manifest 前执行长度限制和敏感键过滤。

优先级固定为核心反馈文本与 manifest、日志诊断、截图、项目状态、普通附件；同级保持注册顺序。POB 自行限制最多五份备份，但不能绕过 Analytics 的总预算。

提交合同：

1. `SubmissionId` 使用 32 位小写 GUID 字符串，并进入 ZIP 文件名、manifest 和队列项；不得把 ZIP 路径作为跨层业务标识。
2. `completed` 恰好调用一次。只有 ZIP 已关闭且队列 JSON 已成功持久化时返回 `AcceptedLocally=true`；回调发生在后台成功事件可能投递之前。
3. `FeedbackUploadQueue.PendingUpload` 增加可选 `submissionId`；旧 JSON 缺失该字段时继续加载。现有 `TryEnqueue`、`Enqueue`、`UploadWithRetry` 和 `QueuedUploadSucceeded(zipPath)` 保留兼容。
4. 新成功事件只为非空 `SubmissionId` 在 HTTP 成功、队列移除持久化完成后触发；ZIP 删除为尽力操作。旧队列只触发现有路径事件，订阅者逐个隔离异常。
5. `ZipAttachmentUploader.Upload()` 改为复用新服务并在本地接受后结束，不再执行玩家等待型前台重试；失败原因只写诊断，旧无返回值 API 不伪造网络成功。
6. `CrashReporter.ReportBugWithAttachments()` 不再覆盖非空调用方时间线。旧 `TimelineJson` 作为最多 64 KiB 的兼容文本保留，并在 README 明确由调用方预先脱敏；新 API 的强类型 `TimelineEntries` 和结构化 `bug_report` 必须经过通用脱敏器。
7. Contributors 为单次请求对象，不设置全局可变注册表；Contributor 抛异常时跳过该 Contributor、写 manifest/诊断并继续核心反馈，不得破坏本地接受。
8. 结构化 `bug_report` 在 ZIP 队列持久化后以同一 `SubmissionId` 和同一份脱敏时间线写入现有 Analytics 事件路径；事件写入失败只记诊断，不回滚已可靠接收的 ZIP，也不得因旧/新 API 转发而重复记录。

## 6. 打包、安全与资源预算

沿用已验证的现有硬限制：ZIP 最多 90 个条目、未压缩总量最多 45 MiB、manifest 预留 1 MiB、每份日志最多保留尾部 4 MiB、截图最长边界不超过 1920×1080 总像素并编码 JPEG 质量 70。项目 Contributor 不得提高这些值；后续调整需单独以真实上传数据批准。

通用时间线策略从 POB 上移：

- 只取最近 80 条；UTF-8 JSON 最多 64 KiB；单值最多 512 字符。
- 默认脱敏键包含 `password`、`token`、`secret`、`auth`、`authorization`、`cookie`、`email`、`phone`、`contact`、`address`；专用 `Contact` 字段是玩家主动输入，不受时间线键过滤影响。
- manifest 只写逻辑标签、归档相对路径、大小、状态和原因，不写本机绝对路径、联系方式或反馈正文。
- 默认不递归上传目录。旧 `AttachmentUploadRequest.DirectoriesToInclude` 仅为兼容保留并在 README 标注风险；新 UI 与新服务不使用它。
- Feedback 提交对应的结构化 `bug_report` 与 ZIP 使用同一份脱敏后的强类型时间线；不得再通过旧 `CrashReporter.BuildReportProps` 附加未脱敏快照。
- 日志不得输出反馈正文、联系方式、上传密钥或附件内容；只记录 `SubmissionId`、失败枚举、队列数量和文件大小。
- `X-Upload-Secret` 继续只来自 Analytics 配置并进入请求头；新包不新增凭据存储。

## 7. Feedback UI 包合同

主要 API：

```csharp
FeedbackPanel.Configure(FeedbackUiConfiguration configuration); // 可选
FeedbackPanel.Open();                                           // 最低接入入口
FeedbackPanel.Close();

IEnumerator FeedbackSubmissionController.Submit(
    FeedbackFormData data,
    Action<FeedbackSubmissionResult> completed);
```

`FeedbackUiConfiguration` 可注入四个窄接口：

- `IFeedbackTextResolver`：按 `FeedbackTextId` 返回项目文案。
- `IFeedbackRequestDecorator`：补充身份、ExtraData 和 Contributors，不得直接上传。
- `IFeedbackAttachmentPicker`：可选文件选择器；未提供时整个附件区隐藏。
- `IFeedbackStatusPresenter`：默认映射到 Toast；POB 映射到 `POBAlert`。

默认文本 ID 固定为 `Title`、`Description`、`ContactOptional`、`AttachmentOptional`、`Send`、`Cancel`、`Uploading`、`Uploaded`、`UploadFailed`。包提供 POB 当前 13 种语言对应的短中性保底文本，并按 `Application.systemLanguage` 选择；项目 Resolver 优先，未知语言回退英文。布局不得以固定字符数决定宽度，所有标签可换行，按钮使用最小宽度加自适应扩展。

默认 View：

- 遵守 Safe Area；内容高度不足时滚动，发送/取消栏保持可见。
- 自动创建 Overlay Canvas，但复用消费项目已有的 UGUI EventSystem；安装器在 EventSystem 缺失时警告，不在运行时猜测或注入可能与项目 Input System 冲突的输入模块。
- 首次打开聚焦描述框；键盘 Tab/方向键、手柄导航和触摸均可完成输入、附件、发送和取消。
- 提交期间禁用重复发送；取消只能在提交前关闭。点击发送后到本地接受/失败的短窗口内禁止销毁提交状态，结果到达后按固定状态流处理。
- 项目未配置支持当前语言字符的 TMP 字体时，安装器给出明确警告；包不通过复制来源不明的 POB 字体掩盖问题。

`FeedbackSubmissionController` 同时供默认 View 与项目自有 View 使用；进程内 SubmissionId 与成功提示由包内静态 `FeedbackStatusCoordinator` 持有并在 `SubsystemRegistration` 清空，不能依赖某个面板实例跨场景存活。
Controller 独占三种状态提示：本地接受显示 `Uploading`、本地失败显示 `UploadFailed`、当前进程真实成功显示 `Uploaded`；View 只负责表单状态，并在收到 `AcceptedLocally` 后关闭，项目不得再复制这套判断。

## 8. POB 迁移

1. `POBBugReportAttachmentUploader` 替换为 `POBFeedbackPackageContributor`：只枚举主存档、最多五份最新备份，添加存档健康、`POBBuildInfo`、系统和平台诊断；不再引用 `System.IO.Compression`，不持有 ZIP/manifest/截图/队列代码。
2. 删除 `POBBugReportUploadResult` 静态桥，并从 `IPOBAnalyticsService`、`POBAnalytics` 与 `POBZgsAnalyticsService` 删除仅由反馈面板使用的 `ReportBugWithAttachments` 转发；反馈面板直接使用通用 `FeedbackSubmissionController`，不在 POB Contracts 再复制一份提交结果协议。其他事件、Timeline 和验证能力保持不变。
3. `BugReportPanel` 保留现有 Prefab、POB 面板生命周期、三张截图按钮和 StandaloneFileBrowser，只把表单数据交给通用提交 Controller；不再构造 ZIP、维护进程路径集合或自行序列化时间线。`POB.Analytics` 在运行时配置项目 RequestDecorator/Contributor，避免 `POB.Runtime` 反向引用 uploader 程序集。
4. POB 项目 Resolver 继续使用现有本地化表；状态 Presenter 继续调用 `POBAlert`，确保项目轻提示策略不被绕过。
5. 成功入队仍显示现有 `Uploading` 并关闭面板；当前进程真实成功仍显示一次 `Uploaded`；网络失败静默；本地失败保留内容并显示 `UploadFailed`。
6. POB 的 manifest/lock 必须同时固定最终验证过的同一 ZeroEngine 提交；不得提交本机 `file:` 路径。

## 9. 文件范围

### 9.1 ZeroEngine

- 本规格：`docs/specs/2026-08-03-zeroengine-feedback-kit.md`
- Analytics 修改：
  - `com.zerogamestudio.analytics/Runtime/Core/FeedbackUploadQueue.cs`
  - `com.zerogamestudio.analytics/Runtime/Core/IAttachmentUploader.cs`
  - `com.zerogamestudio.analytics/Runtime/Core/ZipAttachmentUploader.cs`
  - `com.zerogamestudio.analytics/Runtime/Core/CrashReporter.cs`
  - `com.zerogamestudio.analytics/Runtime/AnalyticsService.cs`
  - 新增 `FeedbackSubmissionContracts.cs`、`FeedbackSubmissionService.cs`、`FeedbackPackageCollector.cs`、`FeedbackTimelineSerializer.cs` 及 `.meta`
  - Analytics Editor tests、README、`package.json`
- 新包：`com.zerogamestudio.zeroengine.feedback/`
  - `package.json`、README、Runtime/Editor/Test asmdefs 及 `.meta`
  - Runtime facade、Controller、`FeedbackStatusCoordinator`、默认 View、Theme、文本目录和四个适配接口
  - `FeedbackInstaller`、Editor/PlayMode tests、`Samples~/DefaultFeedbackPanel`
- 根测试工程：`manifest.json` 与 `packages-lock.json` 增加本地 Feedback 包。
- `com.zerogamestudio.zeroengine.ui` 仅被依赖；除交叉文档链接外不改 Toast 行为。

### 9.2 POB

- `Assets/Assets/_Scripts/_POB/Analytics/Uploader/POBBugReportAttachmentUploader.cs` 及 `.meta`：移动/替换为 Contributor
- `Assets/Assets/_Scripts/_POB/Analytics/Contracts/POBBugReportUploadResult.cs` 及 `.meta`：删除
- `Assets/Assets/_Scripts/_POB/Analytics/Contracts/POBAnalytics.cs`
- `Assets/Assets/_Scripts/_POB/Analytics/Uploader/POBZgsAnalyticsService.cs`
- `Assets/Assets/_Scripts/_POB/UI/Panels/BugReportPanel.cs`
- `Assets/Assets/_Scripts/_POB/POB.Runtime.asmdef`
- `Assets/Assets/_Scripts/_POB/Analytics/Uploader/POB.Analytics.asmdef`
- `Assets/Assets/_Scripts/_POB/Tests/Editor/UI/POB.UI.Tests.Editor.asmdef` 与定向测试
- `Packages/manifest.json` 与 `Packages/packages-lock.json`
- 现有 `Assets/Assets/_Prefabs/UI/Panel/BugReportPanel.prefab` 默认不改层级和美术；只有发现缺失绑定时才允许同一范围内修复并做 Prefab 回归。

不修改 POB 本地化文本、服务端、Addressables、飞书或其他面板。

## 10. 兼容、迁移、失败恢复与回滚

- Analytics 公共旧类型和方法保留编译兼容；README 明确 `ReportBugWithAttachments`/默认 `Upload` 完成现在代表本地接受，不代表 HTTP 成功。需要网络完成信号的新代码使用 `FeedbackSubmissionService.UploadSucceeded`。
- 队列 JSON 只增加可选字段，不迁移、不清空；旧队列继续补传且不弹成功提示。
- 创建 ZIP 或持久队列失败时删除本次未入队 ZIP；已持久化项在网络失败、进程退出或重启后继续补传。
- 队列仍保持最多 10 条、最长 7 天的现有策略；淘汰与过期只记录诊断，不显示玩家成功或失败。
- Rollout 顺序为 Analytics 合同与测试、新 Feedback 包、POB 本地 pin 迁移、干净 Git pin、第二消费项目 smoke。未完成第二消费项目前不得宣称“所有项目可快速接入”。
- 回滚 POB 时必须同时回滚代码和 manifest/lock pin。Analytics 新增 API与可选队列字段可独立保留；新 Feedback 包可从消费者 manifest 移除，不迁移玩家存档或服务端数据。
- 本规格不授权合并、发布、部署或删除远端分支。

## 11. 实施顺序

1. 在 Analytics 增加提交合同、SubmissionId、队列兼容字段、成功事件、时间线脱敏和受预算 Contributor；先完成纯逻辑与队列回归测试。
2. 让默认 `ZipAttachmentUploader` 复用队列优先服务，保留旧 API；同步 README 和 Analytics `1.7.0`。
3. 新建 Feedback `1.0.0` 包，实现 Controller、运行时默认 View、Toast 适配、短文案、Theme 与安装器；完成布局和交互测试。
4. POB 迁移为 Contributor 与薄 UI 绑定，删除重复 ZIP 上传器和静态结果桥；先用本地 package 路径迭代。
5. ZeroEngine 验证并形成不可变 Git 提交后，POB manifest/lock 同步固定该提交，完成 POB 定向验证和真实反馈 smoke。
6. 在一个不含 POB 代码的干净 Unity 2022.3+ 消费项目完成默认安装 smoke；根据真实结果更新本规格为 as-built，再决定提交、发布与关闭。

## 12. 验证与预期信号

执行 Unity 测试前遵循仓库规定的 Unity Test Routing，只运行下列最窄范围。

### 12.1 ZeroEngine Analytics

- `ZGS.Analytics.Tests.Editor` 全部通过，并新增覆盖：本地接受、五种失败枚举、回调恰好一次、旧队列 JSON、SubmissionId 唯一性、成功事件顺序、订阅者异常隔离。
- Contributor 覆盖文件/文本/metadata、异常隔离、优先级、90 条目与 45 MiB 总预算、4 MiB 日志尾部、截图压缩和路径清洗。
- 时间线覆盖最近 80 条、64 KiB、512 字符、默认敏感键以及调用方时间线不再被无条件覆盖。
- manifest 断言不含测试机绝对路径、联系方式、密钥或反馈正文。

### 12.2 Feedback 包

- Editor tests 在 1920×1080、960×540、1280×800、1080×1920 宿主及模拟 Safe Area 下构建默认面板：所有控件在边界内，内容可滚动，按钮栏可见。
- 使用约两倍英文长度的伪本地化文本，断言标题、标签、按钮不裁切或互相覆盖；13 种保底语言的九个 TextId 均非空。
- PlayMode：打开、输入、取消、重复点击保护、附件区有/无 Provider、AcceptedLocally、成功事件、网络失败静默、本地失败保留输入均通过。
- 无配置 Presenter 时运行时 fallback 可打开；运行安装器后生成的 Prefab/Theme 可替代 fallback。

### 12.3 POB

- POB UI/Analytics 定向 EditMode 全部通过；源码合同断言 POB 反馈实现不再引用 `System.IO.Compression`、`ZipArchive`、`FeedbackUploadQueue.TryEnqueue` 或复制通用预算常量。
- 现有反馈 Prefab 的描述、联系方式、三张截图、发送、取消和本地化绑定完整；scripts-only 编译后 Console error 为 0。
- 正常网络 Player：提交后本地接受即关闭并显示一次“上传中”，HTTP 2xx 后显示一次“上传成功”。
- 断网 Player：提交后关闭且不显示失败；退出、重启、恢复网络后自动补传，旧进程提交不弹成功。
- 模拟 ZIP/队列持久化失败：面板保留、输入不丢失、发送恢复、出现短失败提示。
- ZIP 包含 POB 主存档、最多五份最新备份、构建/系统/存档健康诊断；不包含无关持久目录文件。

### 12.4 干净消费项目

在不含 POB 程序集和素材的 Unity 2022.3+ 项目中：

1. 固定同一提交的 Analytics、UI、Feedback 三个包，创建 `ZGSAnalyticsConfig`。
2. 场景只提供标准 UGUI EventSystem；不创建自定义上传器、View、Prefab 或项目 adapter，只调用 `FeedbackPanel.Open()`。
3. 能完成英文默认表单的正常上传、断网入队、重启补传；生成的 ZIP 含反馈、受限时间线/日志和 manifest。
4. 安装器生成可编辑 Prefab/Theme，替换颜色后无需改包代码即可生效。

预期通过信号是所有定向测试为绿、两条 Player smoke 成功、Git/Plastic 状态只包含本任务文件，且没有新增无队列记录的反馈 ZIP。

## 13. 验收标准

1. **AC1：** Analytics 与玩家 UI 保持独立包；Analytics 仍可在 Unity 2021.3 且无 TMP/UI 依赖的项目编译。
2. **AC2：** 新项目不用自制上传器或 Prefab，只配置三包和 Analytics 并调用 `FeedbackPanel.Open()` 即获得可用默认反馈流程。
3. **AC3：** 玩家面板只包含描述、可选联系、条件附件、发送和取消，不出现历史、进度、处理状态或回复。
4. **AC4：** 只有 ZIP 与队列记录均持久化成功才返回 `AcceptedLocally`、关闭面板并显示“上传中”。
5. **AC5：** 当前进程的队列项只有在 HTTP 成功且队列移除已持久化后显示一次“上传成功”；旧队列恢复保持静默。
6. **AC6：** 网络失败不打扰玩家并可跨重启补传；本地失败保留面板和输入并允许重试。
7. **AC7：** POB 不再实现 ZIP、manifest、截图压缩、日志截断、队列或成功路径跟踪，只保留项目 Contributor、View/素材和平台适配。
8. **AC8：** POB 继续上传主存档、最多五份备份、构建/系统/存档健康诊断和最多三张玩家选择截图，玩家行为与现有规格一致。
9. **AC9：** 通用时间线满足 80 条、64 KiB、512 字符和敏感键脱敏；结构化事件与 ZIP 使用同一 SubmissionId/脱敏时间线且各记录一次，manifest 与日志不泄露绝对路径、正文、联系信息或密钥。
10. **AC10：** 现有 90 条目、45 MiB、4 MiB 日志和截图预算统一由 Analytics 强制，项目 Contributor 不能绕过。
11. **AC11：** 旧 Analytics API、旧队列 JSON 和旧事件保持编译/恢复兼容；默认上传完成时机变化有 README 与回归测试。
12. **AC12：** 默认 UI 在四种规定尺寸、Safe Area 和两倍英文长度下无不可达控件或文本相互覆盖，13 种保底语言 key 完整。
13. **AC13：** 默认外观不依赖 POB Prefab、字体、品牌图或第三方文件选择器；项目可通过 Theme/Installer 换肤且不修改 PackageCache。
14. **AC14：** ZeroEngine Analytics、Feedback 包、POB 定向自动测试及正常/断网/本地失败 smoke 全部通过，Console error 为 0。
15. **AC15：** 一个不含 POB 代码和素材的干净 Unity 2022.3+ 项目完成默认接入 smoke 后，才可宣称该套件支持项目快速接入。
16. **AC16：** 实现不修改服务端、ZGS Ops、飞书、POB Addressables 或其他面板；默认新服务不递归扫描 `persistentDataPath`，新 Feedback 包不引入已批准依赖之外的第三方运行时包。

## 14. 本地实施记录（2026-08-04）

- 已在独立 ZeroEngine worktree 完成 Analytics `1.7.0`、Feedback `1.0.0`、默认 UI/Installer、兼容测试与文档；POB 已迁移为 Contributor、薄面板绑定和状态 Presenter。
- POB `manifest.json` 与 `packages-lock.json` 已将 Analytics、Feedback、UI 同步固定到上述 Git 提交，未保留本机 `file:` 路径。
- 固定 Git pin 后窄定向 EditMode `32/32`、PlayMode `1/1` 通过；Unity Console 清空后复读为 `0` 条；JSON、asmdef、`.meta` 配对、旧上传类型移除和路径一致性静态检查通过。
- ZeroEngine 分支已提交并推送；POB 已以 `cs:16744` 提交显式任务路径，其他待提交改动未混入。对应 ZGS Ops 事项仍因 `ContentBuilder Reconcile`/`Needs Build` 阻塞，尚未形成可运行 staging Player 包。
- 尚未完成：无 POB 干净消费项目 smoke、正常网络/断网恢复/本地持久化失败 Player smoke，以及上述事项的人工验收。
- 因上述项目仍开放，本规格保持 `Final`，不标记 `Implemented`；合并、发布和部署仍不在本次范围。
