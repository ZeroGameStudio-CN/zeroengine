namespace ZGS.DataToolkit.Editor
{
    public interface IDataToolkitHeaderActionProvider
    {
        int Order { get; }
        bool IsVisible(DataToolkitContext context);
        void DrawHeaderActions(DataToolkitContext context);
    }
}
