using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAssetFieldChange
    {
        public DataAssetFieldChange(
            Object asset,
            string assetPath,
            string typeName,
            string assetName,
            string fieldPath,
            string currentValue,
            string newValue,
            DataAssetFieldChangeState state,
            string message)
        {
            Asset = asset;
            AssetPath = NormalizePath(assetPath);
            TypeName = typeName ?? string.Empty;
            AssetName = assetName ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            CurrentValue = currentValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
            State = state;
            Message = message ?? string.Empty;
        }

        public Object Asset { get; }
        public string AssetPath { get; }
        public string TypeName { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string CurrentValue { get; }
        public string NewValue { get; }
        public DataAssetFieldChangeState State { get; private set; }
        public string Message { get; private set; }
        public bool CanApply => Asset != null && State == DataAssetFieldChangeState.Pending;

        public void MarkApplied()
        {
            State = DataAssetFieldChangeState.Applied;
            Message = "Applied.";
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
