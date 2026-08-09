namespace POB.Extraction
{
    public sealed class ExtractionMapSelectionInfo
    {
        public readonly string MapId;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly string RaidRoomName;
        public readonly int DurationSeconds;
        public readonly int ThreatLevel;
        public readonly int DangerLevel;
        public readonly int RecommendedGearLevel;
        public readonly bool AllowEmergencyExtraction;

        public ExtractionMapSelectionInfo(
            string mapId,
            string displayName,
            string description,
            string raidRoomName,
            int durationSeconds,
            int threatLevel,
            int dangerLevel,
            int recommendedGearLevel,
            bool allowEmergencyExtraction)
        {
            MapId = mapId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            RaidRoomName = raidRoomName ?? string.Empty;
            DurationSeconds = durationSeconds;
            ThreatLevel = threatLevel;
            DangerLevel = dangerLevel;
            RecommendedGearLevel = recommendedGearLevel;
            AllowEmergencyExtraction = allowEmergencyExtraction;
        }

        public static ExtractionMapSelectionInfo FromDefinition(ExtractionMapDefinition map)
        {
            if (map == null) return null;

            string displayName = string.IsNullOrEmpty(map.DisplayName)
                ? map.MapId
                : map.DisplayName;
            return new ExtractionMapSelectionInfo(
                map.MapId,
                displayName,
                map.Description,
                map.RaidRoomName,
                map.DurationSeconds,
                map.ThreatLevel,
                map.DangerLevel,
                map.RecommendedGearLevel,
                map.AllowEmergencyExtraction);
        }
    }
}
