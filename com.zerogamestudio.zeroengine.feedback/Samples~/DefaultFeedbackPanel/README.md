# Default Feedback Panel

Call `FeedbackPanel.Open()` to use the runtime-generated neutral panel. To customize it,
open `ZGS > 工作台 > 系统与安装`, enable `高级工具`, run `安装默认反馈界面`, and assign the generated theme and prefab to a
`FeedbackUiConfiguration`, then pass it to `FeedbackPanel.Configure`.

The panel closes after the ZIP and queue record are saved locally. `Uploading` means the
feedback is queued; `Uploaded` is shown only after an HTTP success in the same process.
