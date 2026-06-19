using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAssetChangePreview
    {
        public static readonly DataAssetChangePreview Empty = new(Array.Empty<DataAssetFieldChange>());

        public DataAssetChangePreview(IEnumerable<DataAssetFieldChange> changes)
        {
            Changes = (changes ?? Array.Empty<DataAssetFieldChange>())
                .Where(change => change != null)
                .OrderBy(change => change.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.AssetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.FieldPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<DataAssetFieldChange> Changes { get; }
        public int PendingCount => Changes.Count(change => change.State == DataAssetFieldChangeState.Pending);
        public int AppliedCount => Changes.Count(change => change.State == DataAssetFieldChangeState.Applied);
        public int UnchangedCount => Changes.Count(change => change.State == DataAssetFieldChangeState.Unchanged);
        public int BlockedCount => Changes.Count(change => IsBlocked(change.State));
        public bool HasPendingChanges => PendingCount > 0;
        public bool HasBlockingIssues => BlockedCount > 0;

        public string Summary =>
            $"{PendingCount} changes, {UnchangedCount} unchanged, {BlockedCount} blocked, {AppliedCount} applied";

        private static bool IsBlocked(DataAssetFieldChangeState state)
        {
            return state == DataAssetFieldChangeState.MissingAsset ||
                   state == DataAssetFieldChangeState.MissingField ||
                   state == DataAssetFieldChangeState.UnsupportedFieldType ||
                   state == DataAssetFieldChangeState.InvalidValue;
        }
    }
}
