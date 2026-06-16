namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitContext
    {
        public DataToolkitContext(DataToolkitProjectSettings settings)
        {
            Settings = settings;
        }

        public DataToolkitProjectSettings Settings { get; }
    }
}
