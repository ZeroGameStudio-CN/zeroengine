using System;

namespace POB.Extraction
{
    public enum ExtractionContainerSearchResult
    {
        Started = 0,
        Paused = 1,
        Progressed = 2,
        Revealed = 3,
        Completed = 4,
        InvalidRequest = 5,
        NotOpened = 6,
        NotActive = 7,
        MissingConfiguration = 8
    }

    public static class ExtractionContainerSearchService
    {
        private const string RevealReceiptType = "container-reveal";

        public static bool TryStart(
            ExtractionRaidSession raid,
            string containerId,
            out ExtractionContainerSearchResult result)
        {
            result = ExtractionContainerSearchResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            var manifest = raid?.Content?.LootManifest;
            if (manifest == null || string.IsNullOrEmpty(containerId)) return false;
            manifest.EnsureInitialized();
            if (!manifest.TryGetContainer(containerId, out var container) || !container.Opened)
            {
                result = ExtractionContainerSearchResult.NotOpened;
                return false;
            }

            if (!string.IsNullOrEmpty(manifest.ActiveSearchContainerId)
                && manifest.ActiveSearchContainerId != containerId
                && manifest.TryGetContainer(manifest.ActiveSearchContainerId, out var previous))
            {
                previous.SearchState.Paused = true;
            }

            manifest.ActiveSearchContainerId = containerId;
            container.SearchState.Paused = false;
            result = container.SearchState.Completed
                ? ExtractionContainerSearchResult.Completed
                : ExtractionContainerSearchResult.Started;
            return true;
        }

        public static bool TryPause(
            ExtractionRaidSession raid,
            string containerId,
            out ExtractionContainerSearchResult result)
        {
            result = ExtractionContainerSearchResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            var manifest = raid?.Content?.LootManifest;
            if (manifest == null || !manifest.TryGetContainer(containerId, out var container)) return false;
            container.SearchState.Paused = true;
            if (manifest.ActiveSearchContainerId == containerId)
                manifest.ActiveSearchContainerId = null;
            result = ExtractionContainerSearchResult.Paused;
            return true;
        }

        public static bool TryAdvance(
            ExtractionRaidSession raid,
            ExtractionPlayableConfig config,
            string containerId,
            float gameplayDeltaSeconds,
            float characterSearchSpeedMultiplier,
            out ExtractionContainerLootEntry revealedEntry,
            out ExtractionContainerSearchResult result)
        {
            revealedEntry = null;
            result = ExtractionContainerSearchResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled || gameplayDeltaSeconds < 0f) return false;

            var manifest = raid?.Content?.LootManifest;
            if (manifest == null || config == null || !manifest.TryGetContainer(containerId, out var container))
                return false;
            if (!container.Opened)
            {
                result = ExtractionContainerSearchResult.NotOpened;
                return false;
            }
            if (container.SearchState.Completed)
            {
                result = ExtractionContainerSearchResult.Completed;
                return true;
            }
            if (manifest.ActiveSearchContainerId != containerId || container.SearchState.Paused)
            {
                result = ExtractionContainerSearchResult.NotActive;
                return false;
            }
            if (!ExtractionRaidLootManifestGenerator.TryGetContentDefinitions(
                    config,
                    manifest,
                    out _,
                    out var profile)
                || profile.RevealTimes?.BaseRevealSeconds == null)
            {
                result = ExtractionContainerSearchResult.MissingConfiguration;
                return false;
            }

            if (!TryGetCurrentEntry(container, out var current))
            {
                Complete(manifest, container);
                result = ExtractionContainerSearchResult.Completed;
                return true;
            }

            float speed = Math.Max(0.25f, Math.Min(4f, characterSearchSpeedMultiplier));
            float requiredSeconds = Math.Max(
                0.25f,
                profile.RevealTimes.BaseRevealSeconds.Get(current.Rarity)
                * container.SearchTimeMultiplier
                / speed);
            container.SearchState.CurrentEntryElapsedSeconds += gameplayDeltaSeconds;
            if (container.SearchState.CurrentEntryElapsedSeconds < requiredSeconds)
            {
                result = ExtractionContainerSearchResult.Progressed;
                return true;
            }

            current.State = ExtractionContainerLootEntryState.Revealed;
            current.RevealReceiptId = ExtractionReceiptId.Create(
                ExtractionOperationId.Create(
                    "container-reveal",
                    raid.RaidId,
                    container.ContainerId,
                    current.EntryId),
                RevealReceiptType);
            if (!raid.Content.AppliedReceiptIds.Contains(current.RevealReceiptId))
                raid.Content.AppliedReceiptIds.Add(current.RevealReceiptId);
            container.SearchState.CurrentRevealOrder++;
            container.SearchState.CurrentEntryElapsedSeconds = 0f;
            revealedEntry = current;

            if (container.SearchState.CurrentRevealOrder >= container.Entries.Count)
            {
                Complete(manifest, container);
                result = ExtractionContainerSearchResult.Completed;
            }
            else
            {
                result = ExtractionContainerSearchResult.Revealed;
            }

            return true;
        }

        public static bool CanTransfer(
            ExtractionRaidSession raid,
            string containerId,
            string entryId)
        {
            return TryGetEntry(raid, containerId, entryId, out var entry)
                   && entry.State == ExtractionContainerLootEntryState.Revealed;
        }

        public static bool TryMarkTransferred(
            ExtractionRaidSession raid,
            string containerId,
            string entryId,
            string transferReceiptId)
        {
            if (!ExtractionFeatureSwitch.Enabled
                || string.IsNullOrEmpty(transferReceiptId)
                || !TryGetEntry(raid, containerId, entryId, out var entry)
                || entry.State != ExtractionContainerLootEntryState.Revealed)
            {
                return false;
            }

            entry.State = ExtractionContainerLootEntryState.Transferred;
            entry.TransferReceiptId = transferReceiptId;
            if (!raid.Content.AppliedReceiptIds.Contains(transferReceiptId))
                raid.Content.AppliedReceiptIds.Add(transferReceiptId);
            return true;
        }

        private static bool TryGetCurrentEntry(
            ExtractionRaidContainerManifest container,
            out ExtractionContainerLootEntry entry)
        {
            foreach (var candidate in container.Entries)
            {
                if (candidate != null
                    && candidate.RevealOrder == container.SearchState.CurrentRevealOrder
                    && candidate.State == ExtractionContainerLootEntryState.CommittedHidden)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private static bool TryGetEntry(
            ExtractionRaidSession raid,
            string containerId,
            string entryId,
            out ExtractionContainerLootEntry entry)
        {
            entry = null;
            var manifest = raid?.Content?.LootManifest;
            if (manifest == null
                || !manifest.TryGetContainer(containerId, out var container)
                || string.IsNullOrEmpty(entryId))
            {
                return false;
            }

            foreach (var candidate in container.Entries)
            {
                if (candidate != null && candidate.EntryId == entryId)
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        private static void Complete(
            ExtractionRaidLootManifest manifest,
            ExtractionRaidContainerManifest container)
        {
            container.SearchState.Completed = true;
            container.SearchState.Paused = true;
            container.SearchState.CurrentEntryElapsedSeconds = 0f;
            if (manifest.ActiveSearchContainerId == container.ContainerId)
                manifest.ActiveSearchContainerId = null;
        }
    }
}
