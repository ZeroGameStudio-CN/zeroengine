using System;
using System.Collections.Generic;
using System.Globalization;

namespace POB.Extraction
{
    public enum ExtractionContainerOpenResult
    {
        Opened = 0,
        AlreadyOpened = 1,
        InvalidRequest = 2,
        MissingManifest = 3,
        MissingConfiguration = 4,
        LootRollFailed = 5
    }

    public static class ExtractionContainerLootService
    {
        private const string OpenOperationType = "container-open";
        private const string OpenReceiptType = "container-result";
        private const string EntryHashDomain = "zeroengine.extraction.container-open-entry:v1";
        private const string InstanceHashDomain = "zeroengine.extraction.container-open-instance:v1";
        private const string RollHashDomain = "zeroengine.extraction.container-open-roll:v1";
        private const string RevealOrderHashDomain = "zeroengine.extraction.container-reveal-order:v1";

        public static bool TryOpen(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            string containerId,
            out ExtractionRaidContainerManifest container,
            out ExtractionContainerOpenResult result)
        {
            container = null;
            result = ExtractionContainerOpenResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || profile?.ActiveRaid?.Content == null
                || config == null
                || string.IsNullOrEmpty(containerId))
                return false;

            var manifest = profile.ActiveRaid.Content.LootManifest;
            if (manifest == null)
            {
                result = ExtractionContainerOpenResult.MissingManifest;
                return false;
            }

            manifest.EnsureInitialized();
            if (!manifest.TryGetContainer(containerId, out container)) return false;
            if (!container.Active) return false;
            if (container.Opened)
            {
                result = ExtractionContainerOpenResult.AlreadyOpened;
                return true;
            }

            if (!ExtractionRaidLootManifestGenerator.TryGetContentDefinitions(
                    config,
                    manifest,
                    out var tier,
                    out var lootProfile))
            {
                result = ExtractionContainerOpenResult.MissingConfiguration;
                return false;
            }

            int openSequence = manifest.NextOpenSequence;
            string operationId = ExtractionOperationId.Create(
                OpenOperationType,
                profile.ActiveRaid.RaidId,
                container.ContainerId,
                openSequence.ToString(CultureInfo.InvariantCulture));
            string receiptId = ExtractionReceiptId.Create(operationId, OpenReceiptType);
            var generatedEntries = new List<ExtractionContainerLootEntry>();
            for (int slotIndex = container.Entries.Count;
                 slotIndex < container.TargetContentCount;
                 slotIndex++)
            {
                ExtractionLootPityDefinition pity = ExtractionLootContentPolicy.IsPityEnabled(
                    lootProfile,
                    manifest.RareLootDisabled)
                    ? lootProfile.Pity
                    : null;
                if (!ExtractionRaidLootManifestGenerator.TrySelectLootEntry(
                        config,
                        tier,
                        container.RegionId,
                        container.ContainerTypeId,
                        null,
                        manifest.RaidSeed,
                        RollHashDomain,
                        container.ContainerId,
                        openSequence + ":" + slotIndex,
                        out var tableEntry,
                        out var itemDefinition,
                        manifest.PityState.ConsecutiveMisses,
                        pity,
                        manifest.RareLootDisabled))
                {
                    result = ExtractionContainerOpenResult.LootRollFailed;
                    return false;
                }

                string entryId = ExtractionRaidLootManifestGenerator.CreateStableId(
                    "entry:v1:",
                    EntryHashDomain,
                    manifest.ManifestId,
                    container.ContainerId,
                    openSequence.ToString(CultureInfo.InvariantCulture),
                    slotIndex.ToString(CultureInfo.InvariantCulture));
                string instanceId = ExtractionRaidLootManifestGenerator.CreateStableId(
                    "item:v1:",
                    InstanceHashDomain,
                    manifest.ManifestId,
                    entryId);
                generatedEntries.Add(new ExtractionContainerLootEntry(
                    entryId,
                    instanceId,
                    tableEntry.DefinitionId,
                    tableEntry.Quantity,
                    itemDefinition.Rarity,
                    false));
            }

            container.Entries.AddRange(generatedEntries);
            AssignStableRevealOrder(manifest, container, openSequence);
            UpdatePity(manifest, lootProfile, container);
            container.Opened = true;
            container.OpenSequence = openSequence;
            container.OpenReceiptId = receiptId;
            container.SearchState = new ExtractionContainerSearchState(container.ContainerId)
            {
                ResultReceiptId = receiptId
            };
            manifest.NextOpenSequence++;
            AddUnique(profile.ActiveRaid.Content.OpenedContainerIds, container.ContainerId);
            AddUnique(profile.ActiveRaid.Content.AppliedReceiptIds, receiptId);
            result = ExtractionContainerOpenResult.Opened;
            return true;
        }

        private static void AssignStableRevealOrder(
            ExtractionRaidLootManifest manifest,
            ExtractionRaidContainerManifest container,
            int openSequence)
        {
            var ordered = new List<ExtractionContainerLootEntry>(container.Entries);
            ordered.Sort((left, right) =>
            {
                uint leftHash = unchecked((uint)ExtractionStableHash.ComputeInt32(
                    RevealOrderHashDomain,
                    manifest.RaidSeed.ToString(CultureInfo.InvariantCulture),
                    container.ContainerId,
                    openSequence.ToString(CultureInfo.InvariantCulture),
                    left.EntryId));
                uint rightHash = unchecked((uint)ExtractionStableHash.ComputeInt32(
                    RevealOrderHashDomain,
                    manifest.RaidSeed.ToString(CultureInfo.InvariantCulture),
                    container.ContainerId,
                    openSequence.ToString(CultureInfo.InvariantCulture),
                    right.EntryId));
                int comparison = leftHash.CompareTo(rightHash);
                return comparison != 0
                    ? comparison
                    : string.CompareOrdinal(left.EntryId, right.EntryId);
            });

            for (int index = 0; index < ordered.Count; index++)
                ordered[index].RevealOrder = index;
        }

        private static void UpdatePity(
            ExtractionRaidLootManifest manifest,
            ExtractionLootProfileDefinition profile,
            ExtractionRaidContainerManifest container)
        {
            if (!ExtractionLootContentPolicy.IsPityEnabled(profile, manifest.RareLootDisabled))
            {
                manifest.PityState.ConsecutiveMisses = 0;
                return;
            }

            foreach (var entry in container.Entries)
            {
                if (entry != null && entry.Rarity >= profile.Pity.TargetRarity)
                {
                    manifest.PityState.ConsecutiveMisses = 0;
                    return;
                }
            }

            int maximumMisses = profile.Pity.WeightMultiplierIncrementPerMiss <= 0f
                ? 0
                : (int)Math.Ceiling(
                    Math.Max(0f, profile.Pity.MaximumWeightMultiplier - 1f)
                    / profile.Pity.WeightMultiplierIncrementPerMiss);
            manifest.PityState.ConsecutiveMisses = Math.Min(
                maximumMisses,
                manifest.PityState.ConsecutiveMisses + 1);
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values != null && !values.Contains(value)) values.Add(value);
        }
    }
}
