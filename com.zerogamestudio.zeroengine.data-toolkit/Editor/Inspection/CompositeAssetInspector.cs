using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public sealed class CompositeAssetInspector : IAssetInspector
    {
        private readonly IAssetInspector odinInspector = new OdinReflectionAssetInspector();
        private readonly IAssetInspector fallbackInspector = new UnityFallbackAssetInspector();
        private readonly Dictionary<IDataToolkitAssetInspectorProvider, IAssetInspector> customInspectors = new();
        private IReadOnlyList<IDataToolkitAssetInspectorProvider> customInspectorProviders = Array.Empty<IDataToolkitAssetInspectorProvider>();
        private IDataToolkitAssetInspectorProvider activeInspectorProvider;
        private DataToolkitContext context;
        private IAssetInspector activeInspector;

        public bool CanInspect(Object asset)
        {
            return asset != null;
        }

        public void SetCustomInspectors(
            DataToolkitContext context,
            IEnumerable<IDataToolkitAssetInspectorProvider> providers)
        {
            foreach (var customInspector in customInspectors.Values)
            {
                customInspector?.Dispose();
            }

            customInspectors.Clear();
            activeInspectorProvider = null;
            activeInspector = null;
            this.context = context;
            customInspectorProviders = (providers ?? Array.Empty<IDataToolkitAssetInspectorProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ToArray();
        }

        public bool HasCustomInspectorFor(Object asset)
        {
            return TryGetCustomInspectorProvider(asset, out _);
        }

        public void SetTarget(Object asset)
        {
            var nextInspectorProvider = TryGetCustomInspectorProvider(asset, out var provider) ? provider : null;
            var nextInspector = nextInspectorProvider != null
                ? GetOrCreateCustomInspector(nextInspectorProvider)
                : odinInspector.CanInspect(asset)
                    ? odinInspector
                    : fallbackInspector;

            if (activeInspector != nextInspector)
            {
                if (activeInspectorProvider == null)
                {
                    activeInspector?.Dispose();
                }

                activeInspector = nextInspector;
                activeInspectorProvider = nextInspectorProvider;
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
            foreach (var customInspector in customInspectors.Values)
            {
                customInspector?.Dispose();
            }

            customInspectors.Clear();
            activeInspectorProvider = null;
            activeInspector = null;
        }

        private IAssetInspector GetOrCreateCustomInspector(IDataToolkitAssetInspectorProvider provider)
        {
            if (customInspectors.TryGetValue(provider, out var inspector))
            {
                return inspector ?? fallbackInspector;
            }

            inspector = provider.CreateInspector(context);
            customInspectors[provider] = inspector;
            return inspector ?? fallbackInspector;
        }

        private bool TryGetCustomInspectorProvider(Object asset, out IDataToolkitAssetInspectorProvider provider)
        {
            provider = null;
            if (asset == null)
            {
                return false;
            }

            foreach (var candidate in customInspectorProviders)
            {
                try
                {
                    if (candidate.CanInspect(context, asset))
                    {
                        provider = candidate;
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            return false;
        }
    }
}
