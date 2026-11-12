using ZeroEngine.EditorUI;

namespace ZeroEngine.PortraitSpeech.Editor
{
    [EditorToolActionProvider("zeroengine.portrait-speech")]
    public sealed class PortraitSpeechToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            if (actionId != "portrait-speech-authoring") return null;
            return new DelegateEditorToolAction(context =>
            {
                DialoguePortraitAuthoringWindow.OpenWindow();
                return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开对话头像口型工具。");
            });
        }
    }
}
