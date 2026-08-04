# ZeroEngine Feedback

Minimal player feedback UI on top of the queue-first ZGS Analytics submission service.

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

Run `ZeroEngine/Feedback/Install Default UI` to generate an editable theme and prefab
under `Assets/ZeroEngineGenerated/Feedback`. Assign a TMP font that covers every project
language; no third-party font or POB artwork is bundled.
