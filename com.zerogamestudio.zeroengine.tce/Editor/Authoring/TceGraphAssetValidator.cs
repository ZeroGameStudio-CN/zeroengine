using System.Collections.Generic;

namespace ZeroEngine.TCE.Editor
{
    public static class TceGraphAssetValidator
    {
        public static IReadOnlyList<TceValidationIssue> Validate(TceGraphAsset asset)
        {
            return asset == null
                ? TceGraphValidator.Validate(null)
                : TceGraphValidator.Validate(asset.Graph);
        }
    }
}
