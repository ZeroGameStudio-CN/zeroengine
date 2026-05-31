namespace ZGS.DataToolkit.Editor
{
    public interface IDataToolkitFooterProvider
    {
        int Order { get; }
        bool IsVisible(DataToolkitContext context);
        void DrawFooter(DataToolkitContext context);
    }
}
