using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitValidationContext
    {
        public DataToolkitValidationContext(
            DataToolkitProjectSettings settings,
            IEnumerable<DataValidationAsset> assets)
        {
            Settings = settings;
            Assets = (assets ?? Array.Empty<DataValidationAsset>())
                .Where(asset => asset != null)
                .ToArray();
        }

        public DataToolkitProjectSettings Settings { get; }
        public IReadOnlyList<DataValidationAsset> Assets { get; }

        public IEnumerable<DataValidationAsset> GetAssetsOfType<TAsset>() where TAsset : ScriptableObject
        {
            return Assets.Where(asset => asset.Asset is TAsset);
        }
    }
}
