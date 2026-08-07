using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionMapAccessService
    {
        public static bool IsMapUnlocked(ExtractionProfileSaveData profile, string mapId)
        {
            if (profile == null || string.IsNullOrEmpty(mapId)) return false;
            profile.EnsureInitialized();
            foreach (var unlockedMapId in profile.UnlockedMapIds)
            {
                if (unlockedMapId == mapId) return true;
            }

            return false;
        }

        public static bool TryUnlockMap(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            string mapId)
        {
            if (profile == null || config == null || string.IsNullOrEmpty(mapId)) return false;
            if (!config.TryGetMap(mapId, out var map) || map == null || !map.IsValid) return false;

            profile.EnsureInitialized();
            if (IsMapUnlocked(profile, mapId)) return true;

            profile.UnlockedMapIds.Add(mapId);
            return true;
        }

        public static bool TryGetSelectableMaps(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            List<ExtractionMapDefinition> results)
        {
            if (profile == null || config == null || results == null) return false;
            profile.EnsureInitialized();
            results.Clear();

            foreach (var mapId in profile.UnlockedMapIds)
            {
                if (!config.TryGetMap(mapId, out var map) || map == null || !map.IsValid) continue;
                if (ContainsMap(results, map.MapId)) continue;
                results.Add(map);
            }

            return results.Count > 0;
        }

        public static bool TryGetSelectableMapInfos(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            List<ExtractionMapSelectionInfo> results)
        {
            if (profile == null || config == null || results == null) return false;
            profile.EnsureInitialized();
            results.Clear();

            foreach (var mapId in profile.UnlockedMapIds)
            {
                if (!config.TryGetMap(mapId, out var map) || map == null || !map.IsValid) continue;
                if (ContainsMapInfo(results, map.MapId)) continue;

                var info = ExtractionMapSelectionInfo.FromDefinition(map);
                if (info != null) results.Add(info);
            }

            return results.Count > 0;
        }

        private static bool ContainsMap(List<ExtractionMapDefinition> maps, string mapId)
        {
            foreach (var map in maps)
            {
                if (map != null && map.MapId == mapId) return true;
            }

            return false;
        }

        private static bool ContainsMapInfo(List<ExtractionMapSelectionInfo> maps, string mapId)
        {
            foreach (var map in maps)
            {
                if (map != null && map.MapId == mapId) return true;
            }

            return false;
        }
    }
}
