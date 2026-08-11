using ZeroEngine.EditorUI;

namespace ZeroEngine.TCE.Editor
{
    [EditorToolActionProvider("zeroengine.tce")]
    public sealed class TceToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            switch (actionId)
            {
                case "graph-editor":
                    return new DelegateEditorToolAction(context =>
                    {
                        TceEditorWindow.OpenMenu();
                        return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开 TCE 图编辑器。");
                    });
                case "regenerate-component-catalog":
                    return new DelegateEditorToolAction(context =>
                    {
                        TceComponentCatalogWriter.RegenerateComponentCatalog();
                        return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "TCE 组件目录已重新生成。");
                    });
                default:
                    return null;
            }
        }
    }
}
