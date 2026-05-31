using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public interface IDataAuthoringDetailSection
    {
        string SectionId { get; }
        string Title { get; }
        int Order { get; }
        bool CanDraw(Object asset);
        void DrawSection(DataAuthoringPreviewContext context);
    }
}
