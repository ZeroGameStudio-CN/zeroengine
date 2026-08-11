using ZeroEngine.EditorUI;

namespace ZeroEngine.Feedback.Editor
{
    [EditorToolActionProvider("zeroengine.feedback")]
    public sealed class FeedbackToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            if (actionId != "install-default-ui")
                return null;
            return new DelegateEditorToolAction(context =>
            {
                FeedbackInstaller.Install();
                return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "默认反馈界面安装流程已完成。");
            });
        }
    }
}
