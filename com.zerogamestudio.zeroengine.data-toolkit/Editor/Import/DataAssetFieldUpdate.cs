namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAssetFieldUpdate
    {
        public DataAssetFieldUpdate(string assetPath, string fieldPath, string newValue)
        {
            AssetPath = NormalizePath(assetPath);
            FieldPath = fieldPath ?? string.Empty;
            NewValue = newValue ?? string.Empty;
        }

        public string AssetPath { get; }
        public string FieldPath { get; }
        public string NewValue { get; }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
