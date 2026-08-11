using ZeroEngine.EditorUI;

namespace ZeroEngine.UI.Editor.Toast
{
    [EditorToolActionProvider("zeroengine.ui")]
    public sealed class ToastToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            if (actionId != "install-toast-system")
                return null;
            return new DelegateEditorToolAction(context =>
            {
                ToastInstaller.Install();
                return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "通知系统安装流程已完成。");
            });
        }
    }
}
