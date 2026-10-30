# ZeroEngine Feedback

Minimal player feedback UI on top of the queue-first ZGS Analytics submission service.

Version 1.0.2 localizes the Dashboard setup label, tooltip, and write confirmation to Simplified Chinese without changing the installer route.

Version 1.1.2 requires Analytics 2.1.2 for consistent legacy retry classification and idempotency.

Version 1.1.1 requires Analytics 2.1.1, which fixes compilation of the retry loop.

Version 1.1.0 adds an opt-in two-outcome presentation with short failure codes and requires Analytics 2.1.0.

## Install

Pin this package, `com.zerogamestudio.analytics`, and `com.zerogamestudio.zeroengine.ui`
to the same tested ZeroEngine commit.

```json
"com.zerogamestudio.zeroengine.feedback": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.feedback#<tested-commit>"
```

Create the normal Analytics config, ensure the scene has the project's chosen UGUI
`EventSystem`, then open the form:

```csharp
using ZeroEngine.Feedback;

FeedbackPanel.Open();
```

The default form contains a required description, optional contact, Send and Cancel.
Attachments are hidden unless an `IFeedbackAttachmentPicker` is configured. It supports
at most three selected files.

```csharp
FeedbackPanel.Configure(new FeedbackUiConfiguration
{
    TextResolver = projectTextResolver,
    RequestDecorator = projectRequestDecorator,
    AttachmentPicker = projectAttachmentPicker,
    StatusPresenter = projectStatusPresenter,
    Theme = projectTheme
});
```

`IFeedbackRequestDecorator` may add identity, metadata and
`IFeedbackPackageContributor` instances. It must not implement upload or ZIP handling.

After the ZIP and queue record are persisted, the panel closes and shows `Uploading`.
Network failure is silent and retries in the background. `Uploaded` appears only after
an HTTP success for a submission created in the current process. This package does not
provide feedback history, progress, processing state or player replies.

Projects can opt into success/failure-only presentation without changing other consumers:

```csharp
FeedbackPanel.Configure(new FeedbackUiConfiguration
{
    ShowUploadingStatus = false,
    ShowUploadFailureCodes = true,
    TextResolver = projectTextResolver,
    StatusPresenter = projectStatusPresenter
});
```

In this mode local acceptance is silent. The first failed upload attempt for each
current-process submission shows `UploadFailed (Fxxx)`; later retries remain silent.
A confirmed success still shows `Uploaded` exactly once. Restored queues show neither
failure nor success notifications. A local packaging/persistence failure shows its
short code and keeps the form available. `IFeedbackStatusPresenter` receives the
localized, code-suffixed text and must preserve it when presenting the status.
`FeedbackTextCatalog.ResolveShortFailure(languageCode)` supplies the short failure
label in the project's selected language (13 supported locales), independently of
the operating-system language. Use it from the project's existing text resolver.

Open `ZGS > 工作台 > 系统与安装`, enable `高级工具`, then run `安装默认反馈界面` to generate an editable theme and prefab
under `Assets/ZeroEngineGenerated/Feedback`. Assign a TMP font that covers every project
language; no third-party font or POB artwork is bundled.

## ZeroEngine Dashboard

The optional Dashboard exposes the package-owned typed installer action as a confirmed `project-write` command. The installer keeps ownership of its own write safety and this package does not depend on Dashboard.

## 反馈报错码与排查

在 `ZGS > 工作台 > 首页` 中选择「玩家反馈 > 反馈报错查询」，可直接在窗口内按报错码或关键词查看含义与处理方法。
帮助中的「相关资料 > 文档」保留本手册作为补充资料。
本节适用于 Analytics 2.1.2 / Feedback 1.1.2 的短码合同及保留该合同的后续版本。
短码描述提交或上传失败的阶段，不代表玩家反馈内容中的游戏 Bug 类型。

### F203：证书 / TLS 连接错误

`F203` 表示上传的传输错误包含 `certificate`、`ssl` 或 `tls`，归类为证书 / TLS 错误。
它不单独证明服务器证书过期，也不能仅凭此码确定具体根因。可依次检查设备日期与时间、
换用可信网络对比、检查代理或安全软件是否拦截 HTTPS；维护人员再检查上传域名的证书有效期、
域名匹配、完整证书链和 TLS 握手。不要关闭证书校验来处理此错误。

已入队的反馈会保留；F203 不做短间隔重试，而是进入 6 小时冷却后复查。
界面显示失败只代表本次尝试失败，不能据此认定反馈已永久丢失或已被服务器接收。

#### 需要收集的信息与判断顺序

记录报错时间与时区、平台及系统版本、实际包版本或构建编号、来源分支，以及 `Player.log`
中 F203 前后的脱敏日志。分支当前代码不等于玩家实际运行的包；本机访问上传域名正常，
也不能替代玩家环境验证。日志保留错误上下文，去除密钥和个人信息。

| 对比结果 | 优先排查方向 |
| --- | --- |
| 同一包换网络后正常 | 网络、代理或 HTTPS 拦截。 |
| 同一设备、同一网络、相同构建设置下，仅某分支包可重复失败 | 对比该分支与正常包的上传配置、SDK 和构建差异。 |
| 多人、多网络都失败 | 先检查服务端证书链及 TLS，再排查客户端共有问题。 |

这些结果用于缩小范围，不能单独证明根因。**拉到最新不代表 F203 已修复**：
服务端问题应修服务；确认客户端兼容问题后再修反馈 SDK，随后用原失败环境回归。
Dashboard 中的报错说明只提供排查信息，不改变上传行为。

接入新增诊断日志的客户端会在同一条 `[FeedbackUpload] failed` 和原有 `feedback_upload_failure`
事件中记录时间、传输结果、可提取的 curl 编号及 Unity / 应用 / 平台版本。Windows 还记录系统 API
报告的数字版本。`reason` 可出现 `certificate_validity_error`、`certificate_hostname_mismatch`、
`certificate_trust_failed`、`tls_handshake_failed`，无法细分时仍为 `certificate_tls`。
`curl_error=0` 表示未提取到编号。上述内容都是客户端报告的线索，不代表已验证服务器证书的具体问题。
新日志仍不保存完整 URL、错误原文、证书或玩家路径；旧包无法补采，也不能保证下一次一定定位根因。

### 完整短码对照

| 报错码 | 含义 | 建议处理 |
| --- | --- | --- |
| F001 | 请求无效，例如请求为空或描述为空白 | 填写描述；接入方检查提交参数。 |
| F002 | 上传配置未就绪 | 接入方检查 Analytics 上传地址与认证配置。 |
| F101 | 反馈 ZIP 打包失败 | 检查临时目录可写性、剩余空间及打包日志。 |
| F102 | 持久上传队列保存失败 | 检查持久目录可写性、空间和队列保存日志；不能视为已安全入队。 |
| F103 | 上传 ZIP 缺失、无法读取或完整性校验失败 | 查看原因类别，检查文件是否被清理、占用或损坏；原文件缺失时需重新提交。 |
| F201 | 网络连接错误，包括 DNS 解析、连接、发送或接收失败 | 检查联网、DNS、代理及上传服务可达性。 |
| F202 | 上传超时 | 检查网络质量与服务负载，等待后台重试。 |
| F203 | 证书 / TLS 连接错误 | 按上方 F203 步骤排查。 |
| F204 | 响应数据处理失败 | 检查客户端处理错误、响应与中间代理，等待后台重试。 |
| F299 | 未分类错误或无效短码的兜底值 | 保留同一提交编号的诊断日志，按原因类别继续定位。 |

HTTP 400–599 响应直接显示为 `F` 加实际状态码，优先于传输错误分类。
因此下面是常见示例，不是额外的一套固定码表：

| 报错码 | HTTP 含义 | 建议处理 |
| --- | --- | --- |
| F400 | 请求格式错误 | 接入方检查请求格式及接口约定。 |
| F401 | 未授权 | 维护人员检查上传认证配置。 |
| F403 | 访问被拒绝 | 维护人员结合请求编号检查服务端鉴权、权限或网关策略。 |
| F404 | 上传接口不存在 | 检查上传地址和路由。 |
| F408 | 服务端请求超时 | 等待重试，维护人员检查服务端耗时。 |
| F413 | 上传内容过大 | 接入方检查 ZIP 大小、附件预算与服务端限制。 |
| F425 | 服务端要求稍后重试（Too Early） | 等待后台重试。 |
| F429 | 请求过多 / 限流 | 等待退避重试，避免连续重复提交。 |
| F500 / F502 / F503 / F504 | 服务端或网关错误 | 等待后台重试；持续发生时由维护人员排查服务端。 |

### 重试与送达判断

- F201、F202、F204、F299，以及 HTTP 408、425、429 和全部 5xx 属于可重试失败。
  持久队列后台退避为 60 秒、5 分钟、15 分钟、30 分钟，后续保持 30 分钟；实际尝试受进程运行及队列状态影响。
- F203、F002、F103 和其他 HTTP 4xx 不做短间隔重试；已入队项保留并进入 6 小时冷却复查。
  如果 ZIP 已不存在，失效队列记录会被移除。F001、F101、提交阶段的 F002/F102 没有成功入队，应修正原因后重新提交。
- `[FeedbackSubmission] queued` 只证明本地持久入队；`[FeedbackUpload] confirmed` 才表示收到上传成功响应。
  排查时按 `submission` 对应同一条反馈，并结合 `code`、`http`、`request`、`reason`、`attempt` 和 `retryable` 判断。
  启用失败短码展示后，本进程提交首次失败会提示，后续重试静默；从磁盘恢复的旧队列不弹成功或失败提示。

分类定义见 [FeedbackFailureCodes](../com.zerogamestudio.analytics/Runtime/Core/FeedbackSubmissionContracts.cs)，
队列行为见 [FeedbackUploadQueue](../com.zerogamestudio.analytics/Runtime/Core/FeedbackUploadQueue.cs)。
这两个源码相对链接供仓库阅读；UPM 包内可在已安装的 Analytics 包中查看对应文件。
