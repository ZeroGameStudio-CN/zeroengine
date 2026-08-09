using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionLockDefinition
    {
        public string LockId;
        public string MapId;
        public List<string> CompatibleKeyDefinitionIds = new();

        public ExtractionLockDefinition(string lockId, string mapId)
        {
            LockId = lockId;
            MapId = mapId;
        }
    }

    public enum ExtractionUnlockResult
    {
        Unlocked = 0,
        AlreadyUnlocked = 1,
        InvalidRequest = 2,
        MissingLock = 3,
        InvalidKeyLocation = 4,
        IncompatibleKey = 5,
        NoDurability = 6,
        InvalidInventoryState = 7
    }

    public static class ExtractionLockService
    {
        public static bool TryUnlock(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            string lockId,
            string keyItemInstanceId,
            out ExtractionUnlockResult result)
        {
            result = ExtractionUnlockResult.InvalidRequest;
            var raid = profile?.ActiveRaid;
            if (!ExtractionFeatureSwitch.Enabled
                || raid == null
                || config == null
                || string.IsNullOrEmpty(lockId)
                || string.IsNullOrEmpty(keyItemInstanceId))
                return false;
            if (raid.Content.OpenedLockIds.Contains(lockId))
            {
                result = ExtractionUnlockResult.AlreadyUnlocked;
                return true;
            }
            if (!TryGetLock(config, raid.MapId, lockId, out var definition))
            {
                result = ExtractionUnlockResult.MissingLock;
                return false;
            }
            if (!profile.Items.TryGet(keyItemInstanceId, out var key)
                || !config.TryGetItemDefinition(key.DefinitionId, out var keyDefinition))
            {
                return false;
            }
            if (!profile.Ownership.TryGetContainer(keyItemInstanceId, out var location)
                || (location != ExtractionInventoryContainerType.RaidBackpack
                    && location != ExtractionInventoryContainerType.InSecureContainer))
            {
                result = ExtractionUnlockResult.InvalidKeyLocation;
                return false;
            }
            if (!IsCompatible(definition, keyDefinition))
            {
                result = ExtractionUnlockResult.IncompatibleKey;
                return false;
            }
            if (keyDefinition.MaxDurability <= 0 || key.CurrentDurability <= 0)
            {
                result = ExtractionUnlockResult.NoDurability;
                return false;
            }

            key.CurrentDurability--;
            if (key.CurrentDurability == 0 && !TryDestroySpentKey(profile, keyItemInstanceId, location))
            {
                key.CurrentDurability++;
                result = ExtractionUnlockResult.InvalidInventoryState;
                return false;
            }

            raid.Content.OpenedLockIds.Add(lockId);
            string receiptId = ExtractionReceiptId.Create(
                ExtractionOperationId.Create("unlock", raid.RaidId, lockId, keyItemInstanceId),
                "lock-opened");
            if (!raid.Content.AppliedReceiptIds.Contains(receiptId))
                raid.Content.AppliedReceiptIds.Add(receiptId);
            result = ExtractionUnlockResult.Unlocked;
            return true;
        }

        private static bool TryDestroySpentKey(
            ExtractionProfileSaveData profile,
            string itemInstanceId,
            ExtractionInventoryContainerType source)
        {
            ExtractionItemGrid grid = source == ExtractionInventoryContainerType.RaidBackpack
                ? profile.ActiveRaidInventory?.RaidBackpack
                : profile.ActiveRaidInventory?.SecureContainer;
            if (grid == null) return false;
            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    source,
                    ExtractionInventoryContainerType.DestroyedByUse))
            {
                return false;
            }

            if (grid.TryRemove(itemInstanceId)) return true;
            profile.Ownership.TryMove(
                itemInstanceId,
                ExtractionInventoryContainerType.DestroyedByUse,
                source);
            return false;
        }

        private static bool IsCompatible(
            ExtractionLockDefinition lockDefinition,
            ExtractionItemDefinition keyDefinition)
        {
            bool keyListsLock = keyDefinition.CompatibleTargetIds != null
                                && keyDefinition.CompatibleTargetIds.Contains(lockDefinition.LockId);
            bool lockListsKey = lockDefinition.CompatibleKeyDefinitionIds != null
                                && lockDefinition.CompatibleKeyDefinitionIds.Contains(keyDefinition.DefinitionId);
            return keyListsLock || lockListsKey;
        }

        private static bool TryGetLock(
            ExtractionPlayableConfig config,
            string mapId,
            string lockId,
            out ExtractionLockDefinition definition)
        {
            foreach (var candidate in config.LockDefinitions)
            {
                if (candidate != null && candidate.LockId == lockId && candidate.MapId == mapId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }
    }
}
