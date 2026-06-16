namespace ZGS.DataToolkit.Editor
{
    public interface IDataToolkitToolbarProvider
    {
        int Order { get; }
        bool IsVisible(DataToolkitContext context);
        void DrawToolbar(DataToolkitContext context);
    }
}
