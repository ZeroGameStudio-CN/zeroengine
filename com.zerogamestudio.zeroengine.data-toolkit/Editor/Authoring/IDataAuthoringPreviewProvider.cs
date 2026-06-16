using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public interface IDataAuthoringPreviewProvider
    {
        string ProviderId { get; }
        int Order { get; }
        bool CanPreview(Object asset);
        void DrawPreview(DataAuthoringPreviewContext context);
    }
}
