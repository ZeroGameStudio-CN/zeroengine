using ZeroEngine.EditorUI;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    [EditorToolActionProvider("zeroengine.config-pipeline")]
    public sealed class ConfigPipelineToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            if (actionId != "config-pipeline")
                return null;
            return new DelegateEditorToolAction(context =>
            {
                ConfigPipelineWindow.Open();
                return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开配置管线。");
            });
        }
    }
}
