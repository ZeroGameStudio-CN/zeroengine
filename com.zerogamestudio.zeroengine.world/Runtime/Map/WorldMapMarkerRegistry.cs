using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.World.Map
{
    public sealed class WorldMapMarkerRegistry
    {
        private readonly List<IWorldMapMarkerProvider> _providers = new List<IWorldMapMarkerProvider>();
        private readonly List<WorldMapMarkerDefinition> _scratch = new List<WorldMapMarkerDefinition>();
        private string _lastError;

        public IReadOnlyList<IWorldMapMarkerProvider> Providers => _providers;
        public string LastError => _lastError ?? string.Empty;

        public bool RegisterProvider(IWorldMapMarkerProvider provider)
        {
            if (provider == null || _providers.Contains(provider))
            {
                return false;
            }

            _providers.Add(provider);
            return true;
        }

        public bool UnregisterProvider(IWorldMapMarkerProvider provider)
        {
            return provider != null && _providers.Remove(provider);
        }

        public void ClearProviders()
        {
            _providers.Clear();
            _lastError = string.Empty;
        }

        public bool TryCollectMarkers(
            List<WorldMapMarkerDefinition> results,
            out string error,
            WorldMapMarkerFilter filter = default,
            WorldMapDiscoveryState discoveryState = null)
        {
            if (results == null)
            {
                error = "Marker result list is null.";
                _lastError = error;
                return false;
            }

            results.Clear();
            _scratch.Clear();
            for (var i = 0; i < _providers.Count; i++)
            {
                _providers[i]?.CollectMarkers(_scratch);
            }

            var knownIds = new HashSet<string>();
            foreach (var marker in _scratch.OrderBy(marker => marker.Priority).ThenBy(marker => marker.MarkerId))
            {
                if (!WorldMapStableId.IsStableId(marker.MarkerId))
                {
                    error = $"World map marker id '{marker.MarkerId}' must use stable lowercase id characters.";
                    _lastError = error;
                    results.Clear();
                    return false;
                }

                if (!knownIds.Add(marker.MarkerId))
                {
                    error = $"Duplicate world map marker id '{marker.MarkerId}'.";
                    _lastError = error;
                    results.Clear();
                    return false;
                }

                if (filter.Allows(marker, discoveryState))
                {
                    results.Add(marker);
                }
            }

            error = string.Empty;
            _lastError = string.Empty;
            return true;
        }
    }
}
