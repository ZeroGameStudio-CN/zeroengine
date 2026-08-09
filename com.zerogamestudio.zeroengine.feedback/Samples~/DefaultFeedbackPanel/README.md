# Default Feedback Panel

Call `FeedbackPanel.Open()` to use the runtime-generated neutral panel. To customize it,
run `ZeroEngine/Feedback/Install Default UI`, assign the generated theme and prefab to a
`FeedbackUiConfiguration`, then pass it to `FeedbackPanel.Configure`.

The panel closes after the ZIP and queue record are saved locally. `Uploading` means the
feedback is queued; `Uploaded` is shown only after an HTTP success in the same process.
