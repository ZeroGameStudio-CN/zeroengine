using ZeroEngine.EditorUI;

namespace ZeroEngine.Multiplayer.Editor
{
    [EditorToolActionProvider("zeroengine.multiplayer")]
    public sealed class MultiplayerToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            switch (actionId)
            {
                case "multiplayer-local-launcher":
                    return new DelegateEditorToolAction(context =>
                    {
                        LocalMultiplayerLauncher.Open();
                        return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开本地多人启动器。");
                    });
                case "multiplayer-validate-setup":
                    return new DelegateEditorToolAction(context =>
                    {
                        MultiplayerSetupValidator.ValidateInstalledSetup();
                        return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已输出多人配置检查结果，请查看 Console。");
                    });
                default:
                    return null;
            }
        }
    }
}
