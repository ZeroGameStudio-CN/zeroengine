using ZeroEngine.EditorUI;

namespace ZeroEngine.Formula.Editor
{
    [EditorToolActionProvider("zeroengine.formula")]
    public sealed class FormulaToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            switch (actionId)
            {
                case "formula-catalog":
                    return new DelegateEditorToolAction(context =>
                    {
                        FormulaCatalogWindow.OpenWithProfile(FormulaEditorProfileRegistry.ActiveProfile);
                        return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开公式目录。");
                    });
                case "formula-workbench":
                    return new DelegateEditorToolAction(context =>
                    {
                        FormulaWorkbenchWindow.OpenWithProfile(FormulaEditorProfileRegistry.ActiveProfile);
                        return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开公式工作台。");
                    });
                case "scan-formula-assets":
                    return new DelegateEditorToolAction(context =>
                    {
                        FormulaAssetScanner.RunMenu();
                        return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "公式资源扫描完成，请查看 Console。");
                    });
                default:
                    return null;
            }
        }
    }
}
