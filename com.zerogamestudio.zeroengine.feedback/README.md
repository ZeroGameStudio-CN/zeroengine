# ZeroEngine Feedback

Minimal player feedback UI on top of the queue-first ZGS Analytics submission service.

Version 1.0.2 localizes the Dashboard setup label, tooltip, and write confirmation to Simplified Chinese without changing the installer route.

Version 1.0.3 adds an opt-in two-outcome presentation with short failure codes and requires Analytics 2.0.2.

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
