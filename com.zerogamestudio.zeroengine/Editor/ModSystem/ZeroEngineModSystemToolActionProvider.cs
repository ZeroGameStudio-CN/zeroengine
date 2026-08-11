using System;
using ZeroEngine.EditorUI;

namespace ZeroEngine.ModSystem.Editor
{
    [EditorToolActionProvider("zeroengine.modsystem")]
    public sealed class ZeroEngineModSystemToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            switch (actionId)
            {
                case "mod-creator": return Open(ModCreatorWindow.ShowWindow, "已打开模组创建器。");
                case "mod-exporter": return Open(ModExporter.ShowWindow, "已打开模组导出工具。");
                case "mod-validator": return Open(ModValidatorWindow.ShowWindow, "已打开模组校验器。");
                default: return null;
            }
        }

        private static IEditorToolAction Open(Action action, string message)
        {
            return new DelegateEditorToolAction(context =>
            {
                action();
                return new EditorToolActionResult(EditorToolActionStatus.Succeeded, message);
            });
        }
    }
}
