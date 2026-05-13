using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class CompositeAssetInspector : IAssetInspector
    {
        private readonly IAssetInspector odinInspector = new OdinReflectionAssetInspector();
        private readonly IAssetInspector fallbackInspector = new UnityFallbackAssetInspector();
        private IAssetInspector activeInspector;

        public bool CanInspect(Object asset)
        {
            return asset != null;
        }

        public void SetTarget(Object asset)
        {
            var nextInspector = odinInspector.CanInspect(asset) ? odinInspector : fallbackInspector;
            if (activeInspector != nextInspector)
            {
                activeInspector?.Dispose();
                activeInspector = nextInspector;
            }

            activeInspector.SetTarget(asset);
        }

        public void Draw()
        {
            activeInspector?.Draw();
        }

        public void Dispose()
        {
            odinInspector.Dispose();
            fallbackInspector.Dispose();
            activeInspector = null;
        }
    }
}
