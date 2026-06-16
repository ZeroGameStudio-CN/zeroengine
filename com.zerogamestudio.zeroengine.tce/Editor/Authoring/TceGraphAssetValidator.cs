using System.Collections.Generic;

namespace ZeroEngine.TCE.Editor
{
    public static class TceGraphAssetValidator
    {
        public static IReadOnlyList<TceValidationIssue> Validate(TceGraphAsset asset)
        {
            if (asset == null)
                return TceGraphValidator.Validate(null);

            var issues = new List<TceValidationIssue>();
            if (asset.GraphSchemaVersion < TceGraphSchema.CurrentVersion)
            {
                issues.Add(new TceValidationIssue(
                    TceValidationSeverity.Error,
                    TceValidationCodes.GraphMigrationRequired,
                    TceGraphSerializedAccess.GraphSchemaVersionProperty,
                    $"Graph schema version {asset.GraphSchemaVersion} must be migrated to {TceGraphSchema.CurrentVersion}."));
            }
            else if (asset.GraphSchemaVersion > TceGraphSchema.CurrentVersion)
            {
                issues.Add(new TceValidationIssue(
                    TceValidationSeverity.Error,
                    TceValidationCodes.GraphVersionUnsupported,
                    TceGraphSerializedAccess.GraphSchemaVersionProperty,
                    $"Graph schema version {asset.GraphSchemaVersion} is newer than supported version {TceGraphSchema.CurrentVersion}."));
            }

            issues.AddRange(TceGraphValidator.Validate(asset.Graph));
            return issues;
        }
    }
}
