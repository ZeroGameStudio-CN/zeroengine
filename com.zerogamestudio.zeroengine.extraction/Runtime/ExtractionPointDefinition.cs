using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionPointDefinition
    {
        public string PointId;
        public string MapId;
        public int ChannelSeconds;
        public bool DefaultOpen;
        public int OpenAtElapsedSeconds;
        public bool SingleUse;
        public bool AllowEmergencyExtractionOverride;
        public string RequiredItemDefinitionId;
        public bool ConsumeRequiredItemOnExtraction;
        public string RequiredRaidFlagId;

        public bool IsValid =>
            !string.IsNullOrEmpty(PointId)
            && ChannelSeconds > 0
            && OpenAtElapsedSeconds >= 0
            && (!ConsumeRequiredItemOnExtraction || !string.IsNullOrEmpty(RequiredItemDefinitionId));

        public ExtractionPointDefinition(
            string pointId,
            int channelSeconds,
            bool defaultOpen,
            int openAtElapsedSeconds,
            bool singleUse,
            bool allowEmergencyExtractionOverride)
            : this(
                pointId,
                string.Empty,
                channelSeconds,
                defaultOpen,
                openAtElapsedSeconds,
                singleUse,
                allowEmergencyExtractionOverride)
        {
        }

        public ExtractionPointDefinition(
            string pointId,
            string mapId,
            int channelSeconds,
            bool defaultOpen,
            int openAtElapsedSeconds,
            bool singleUse,
            bool allowEmergencyExtractionOverride)
        {
            PointId = pointId;
            MapId = mapId;
            ChannelSeconds = channelSeconds;
            DefaultOpen = defaultOpen;
            OpenAtElapsedSeconds = openAtElapsedSeconds;
            SingleUse = singleUse;
            AllowEmergencyExtractionOverride = allowEmergencyExtractionOverride;
        }
    }
}
