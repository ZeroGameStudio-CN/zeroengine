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

        // Raid mechanics are additive. The legacy constructors leave these at
        // Normal/zero so old point data keeps its previous behavior.
        public ExtractionPointMode Mode = ExtractionPointMode.Normal;
        // Legacy compatibility alias. Formal rules authoring uses
        // OpenAtElapsedSeconds + OpenDurationSeconds for a timed window.
        public int TimedWindowSeconds;
        public int RequiredGateCount;
        public int OpenDurationSeconds;
        public int RequiredItemQuantity;
        public ExtractionItemRarity RequiredItemRarity;

        public int EffectiveOpenDurationSeconds =>
            OpenDurationSeconds > 0 ? OpenDurationSeconds : TimedWindowSeconds;

        public bool IsValid =>
            !string.IsNullOrEmpty(PointId)
            && ChannelSeconds > 0
            && OpenAtElapsedSeconds >= 0
            && Enum.IsDefined(typeof(ExtractionPointMode), Mode)
            && TimedWindowSeconds >= 0
            && RequiredGateCount >= 0
            && OpenDurationSeconds >= 0
            && RequiredItemQuantity >= 0
            && Enum.IsDefined(typeof(ExtractionItemRarity), RequiredItemRarity)
            && (!ConsumeRequiredItemOnExtraction || !string.IsNullOrEmpty(RequiredItemDefinitionId))
            && IsModeConfigurationValid;

        private bool IsModeConfigurationValid
        {
            get
            {
                switch (Mode)
                {
                    case ExtractionPointMode.Normal:
                    case ExtractionPointMode.Random:
                        return TimedWindowSeconds == 0
                            && RequiredGateCount == 0
                            && OpenDurationSeconds == 0
                            && RequiredItemQuantity == 0;
                    case ExtractionPointMode.Timed:
                        return EffectiveOpenDurationSeconds > 0
                            && RequiredGateCount == 0
                            && RequiredItemQuantity == 0;
                    case ExtractionPointMode.Gate:
                        return RequiredGateCount > 0
                            && TimedWindowSeconds == 0
                            && OpenDurationSeconds == 0
                            && RequiredItemQuantity == 0;
                    case ExtractionPointMode.Boss:
                        return EffectiveOpenDurationSeconds > 0
                            && RequiredGateCount == 0
                            && RequiredItemQuantity == 0
                            && !string.IsNullOrEmpty(RequiredRaidFlagId);
                    case ExtractionPointMode.Sacrifice:
                        return EffectiveOpenDurationSeconds > 0
                            && RequiredItemQuantity > 0
                            && !ConsumeRequiredItemOnExtraction
                            && RequiredGateCount == 0;
                    default:
                        return false;
                }
            }
        }

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

        public ExtractionPointDefinition(
            string pointId,
            string mapId,
            int channelSeconds,
            bool defaultOpen,
            int openAtElapsedSeconds,
            bool singleUse,
            bool allowEmergencyExtractionOverride,
            ExtractionPointMode mode,
            int timedWindowSeconds,
            int requiredGateCount,
            int openDurationSeconds,
            int requiredItemQuantity,
            ExtractionItemRarity requiredItemRarity)
            : this(
                pointId,
                mapId,
                channelSeconds,
                defaultOpen,
                openAtElapsedSeconds,
                singleUse,
                allowEmergencyExtractionOverride)
        {
            Mode = mode;
            TimedWindowSeconds = timedWindowSeconds;
            RequiredGateCount = requiredGateCount;
            OpenDurationSeconds = openDurationSeconds;
            RequiredItemQuantity = requiredItemQuantity;
            RequiredItemRarity = requiredItemRarity;
        }
    }
}
