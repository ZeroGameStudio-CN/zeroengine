using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionSettlementService
    {
        public static bool CompleteSuccess(ExtractionProfileSaveData profile, IEnumerable<string> extractedItemIds)
        {
            if (!CanSettle(profile) || extractedItemIds == null) return false;

            var movedItems = new List<OwnershipMove>();
            foreach (var itemId in extractedItemIds)
            {
                if (!TryGetSuccessSource(profile, itemId, out var source)
                    || !profile.Ownership.TryMove(itemId, source, ExtractionInventoryContainerType.Stash))
                {
                    Rollback(profile, movedItems);
                    return false;
                }

                movedItems.Add(new OwnershipMove(itemId, source, ExtractionInventoryContainerType.Stash));
            }

            ClearActiveRaid(profile);
            return true;
        }

        public static ExtractionLostRunSnapshot CompleteFailure(
            ExtractionProfileSaveData profile,
            string snapshotId,
            string mapId,
            IEnumerable<string> lostItemIds,
            IEnumerable<string> secureItemIds)
        {
            return TryCompleteFailure(profile, snapshotId, mapId, lostItemIds, secureItemIds, null, out var snapshot)
                ? snapshot
                : null;
        }

        public static bool TryCompleteFailure(
            ExtractionProfileSaveData profile,
            string snapshotId,
            string mapId,
            IEnumerable<string> lostItemIds,
            IEnumerable<string> secureItemIds,
            out ExtractionLostRunSnapshot snapshot)
        {
            return TryCompleteFailure(
                profile,
                snapshotId,
                mapId,
                lostItemIds,
                secureItemIds,
                null,
                out snapshot);
        }

        public static bool TryCompleteFailure(
            ExtractionProfileSaveData profile,
            string snapshotId,
            string mapId,
            IEnumerable<string> lostItemIds,
            IEnumerable<string> secureItemIds,
            ExtractionFailureContext failureContext,
            out ExtractionLostRunSnapshot snapshot)
        {
            return TryCompleteFailure(
                profile, snapshotId, mapId, lostItemIds, secureItemIds,
                failureContext, null, null, out snapshot);
        }

        public static bool TryCompleteFailure(
            ExtractionProfileSaveData profile,
            string snapshotId,
            string mapId,
            IEnumerable<string> lostItemIds,
            IEnumerable<string> secureItemIds,
            string corpseId,
            ExtractionCorpseLootLedger corpseLoot,
            out ExtractionLostRunSnapshot snapshot)
        {
            return TryCompleteFailure(
                profile, snapshotId, mapId, lostItemIds, secureItemIds,
                null, corpseId, corpseLoot, out snapshot);
        }

        public static bool TryCompleteFailure(
            ExtractionProfileSaveData profile,
            string snapshotId,
            string mapId,
            IEnumerable<string> lostItemIds,
            IEnumerable<string> secureItemIds,
            ExtractionFailureContext failureContext,
            string corpseId,
            ExtractionCorpseLootLedger corpseLoot,
            out ExtractionLostRunSnapshot snapshot)
        {
            return TryCompleteFailure(
                profile, snapshotId, mapId, lostItemIds, secureItemIds,
                null, failureContext, corpseId, corpseLoot, out snapshot);
        }

        // M2 SD2.2a：尸袋宽松档新增的"保留一部分 raid/背包物品"通道。keptRaidItemIds 里的物品从
        // InRaid/RaidBackpack 直接移交 Stash(不进 Lost/不进尸体账本)，同源校验复用
        // TryGetFailureLostSource——两者本来就该是"同一个来源池的两种去向"。null/空表=功能关闭，
        // 等价于旧版 8 参数重载的行为，向后兼容其余全部既有重载与调用方。
        public static bool TryCompleteFailure(
            ExtractionProfileSaveData profile,
            string snapshotId,
            string mapId,
            IEnumerable<string> lostItemIds,
            IEnumerable<string> secureItemIds,
            IEnumerable<string> keptRaidItemIds,
            ExtractionFailureContext failureContext,
            string corpseId,
            ExtractionCorpseLootLedger corpseLoot,
            out ExtractionLostRunSnapshot snapshot)
        {
            snapshot = null;
            if (!CanSettle(profile)
                || string.IsNullOrEmpty(snapshotId)
                || lostItemIds == null
                || secureItemIds == null)
            {
                return false;
            }

            bool registersCorpse = !string.IsNullOrEmpty(corpseId);
            if (registersCorpse && corpseLoot == null) return false;

            var movedItems = new List<OwnershipMove>();
            var registeredLostItems = new List<string>();
            var loadoutItems = new List<string>();
            var backpackItems = new List<string>();
            var secureItems = new List<string>();
            foreach (var itemId in lostItemIds)
            {
                if (!TryGetFailureLostSource(profile, itemId, out var source)
                    || !profile.Ownership.TryMove(itemId, source, ExtractionInventoryContainerType.Lost))
                {
                    Rollback(profile, movedItems);
                    return false;
                }

                movedItems.Add(new OwnershipMove(itemId, source, ExtractionInventoryContainerType.Lost));
                registeredLostItems.Add(itemId);
                if (source == ExtractionInventoryContainerType.InRaid)
                    loadoutItems.Add(itemId);
                if (source == ExtractionInventoryContainerType.RaidBackpack)
                    backpackItems.Add(itemId);
            }

            foreach (var itemId in secureItemIds)
            {
                if (!profile.Ownership.TryMove(
                        itemId,
                        ExtractionInventoryContainerType.InSecureContainer,
                        ExtractionInventoryContainerType.SecureContainer))
                {
                    Rollback(profile, movedItems);
                    return false;
                }

                movedItems.Add(
                    new OwnershipMove(
                        itemId,
                        ExtractionInventoryContainerType.InSecureContainer,
                        ExtractionInventoryContainerType.SecureContainer));
                secureItems.Add(itemId);
            }

            if (keptRaidItemIds != null)
            {
                foreach (var itemId in keptRaidItemIds)
                {
                    if (!TryGetFailureLostSource(profile, itemId, out var source)
                        || !profile.Ownership.TryMove(itemId, source, ExtractionInventoryContainerType.Stash))
                    {
                        Rollback(profile, movedItems);
                        return false;
                    }

                    movedItems.Add(new OwnershipMove(itemId, source, ExtractionInventoryContainerType.Stash));
                }
            }

            snapshot = new ExtractionLostRunSnapshot(snapshotId, mapId);
            snapshot.RaidSeed = profile.ActiveRaid?.Seed ?? 0;
            snapshot.ApplyContext(failureContext);
            snapshot.LoadoutItemInstanceIds.AddRange(loadoutItems);
            snapshot.BackpackItemInstanceIds.AddRange(backpackItems);
            snapshot.SecureItemInstanceIds.AddRange(secureItems);
            foreach (var itemId in registeredLostItems)
            {
                snapshot.LostItemInstanceIds.Add(itemId);
                snapshot.RecoverableCandidateItemInstanceIds.Add(itemId);
            }

            // 原子闸门：尸体注册失败则回滚 ownership、不碰 Recovery / ActiveRaid。
            if (registersCorpse && !corpseLoot.RegisterCorpse(corpseId, snapshot))
            {
                Rollback(profile, movedItems);
                snapshot = null;
                return false;
            }

            foreach (var itemId in registeredLostItems)
                profile.Recovery.RegisterLostItem(snapshotId, itemId);

            ClearActiveRaid(profile);
            return true;
        }

        private static bool CanSettle(ExtractionProfileSaveData profile)
        {
            return ExtractionFeatureSwitch.Enabled
                   && profile != null
                   && profile.ActiveRaid != null
                   && !string.IsNullOrEmpty(profile.activeRaidId);
        }

        private static bool TryGetSuccessSource(
            ExtractionProfileSaveData profile,
            string itemId,
            out ExtractionInventoryContainerType source)
        {
            if (!profile.Ownership.TryGetContainer(itemId, out source)) return false;
            return source == ExtractionInventoryContainerType.InRaid
                   || source == ExtractionInventoryContainerType.RaidBackpack
                   || source == ExtractionInventoryContainerType.InSecureContainer
                   || source == ExtractionInventoryContainerType.EquipmentSlot;
        }

        private static bool TryGetFailureLostSource(
            ExtractionProfileSaveData profile,
            string itemId,
            out ExtractionInventoryContainerType source)
        {
            if (!profile.Ownership.TryGetContainer(itemId, out source)) return false;
            // M2 SD2.2a 硬核尸袋档：调用方把 secure 容器物品也并入 lostItemIds(一起判定丢失)，
            // 因此这里同时接受 InSecureContainer 作为有效来源；既有调用方从不把 secure 物品塞进
            // lostItemIds(它们走独立的 secureItemIds 参数)，这里放宽不影响任何既有行为。
            return source == ExtractionInventoryContainerType.InRaid
                   || source == ExtractionInventoryContainerType.RaidBackpack
                   || source == ExtractionInventoryContainerType.InSecureContainer
                   || source == ExtractionInventoryContainerType.EquipmentSlot;
        }

        private static void Rollback(ExtractionProfileSaveData profile, List<OwnershipMove> movedItems)
        {
            for (int i = movedItems.Count - 1; i >= 0; i--)
            {
                var move = movedItems[i];
                profile.Ownership.TryMove(move.ItemId, move.Target, move.Source);
            }
        }

        private static void ClearActiveRaid(ExtractionProfileSaveData profile)
        {
            profile.activeRaidId = null;
            profile.ActiveRaid = null;
            profile.ActiveRaidElapsedSeconds = 0f;
            profile.ActiveRaidInventory = null;
        }

        private struct OwnershipMove
        {
            public string ItemId;
            public ExtractionInventoryContainerType Source;
            public ExtractionInventoryContainerType Target;

            public OwnershipMove(
                string itemId,
                ExtractionInventoryContainerType source,
                ExtractionInventoryContainerType target)
            {
                ItemId = itemId;
                Source = source;
                Target = target;
            }
        }
    }
}
