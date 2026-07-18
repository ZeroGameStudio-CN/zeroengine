using System;
using System.Collections.Generic;
using System.Globalization;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionMerchantOfferPoolDefinition
    {
        public string PoolId;
        public List<string> OfferIds = new();

        public ExtractionMerchantOfferPoolDefinition(string poolId)
        {
            PoolId = poolId;
        }
    }

    [Serializable]
    public class ExtractionMerchantSlotDefinition
    {
        public string SlotId;
        public string PoolId;

        public ExtractionMerchantSlotDefinition(string slotId, string poolId)
        {
            SlotId = slotId;
            PoolId = poolId;
        }
    }

    [Serializable]
    public class ExtractionMerchantRotationSettings
    {
        public int MinimumValidRaidGameplaySeconds = 30;
        public int BasePaidRefreshCost = 10;
        public int PaidRefreshCostIncrement = 5;
    }

    public enum ExtractionMerchantRaidOutcomeType
    {
        Success = 0,
        Timeout = 1,
        Death = 2,
        Abandon = 3
    }

    public sealed class ExtractionMerchantRaidOutcome
    {
        public string RaidId;
        public ExtractionMerchantRaidOutcomeType Outcome;
        public int GameplayElapsedSeconds;
        public bool HasParticipationReceipt;

        public ExtractionMerchantRaidOutcome(
            string raidId,
            ExtractionMerchantRaidOutcomeType outcome,
            int gameplayElapsedSeconds,
            bool hasParticipationReceipt)
        {
            RaidId = raidId;
            Outcome = outcome;
            GameplayElapsedSeconds = gameplayElapsedSeconds;
            HasParticipationReceipt = hasParticipationReceipt;
        }
    }

    public static class ExtractionMerchantRotationService
    {
        private const string SelectionHashDomain = "zeroengine.extraction.merchant-offer:v1";

        public static bool TryInitialize(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            string rotationId,
            int seed)
        {
            if (!ExtractionFeatureSwitch.Enabled || profile == null || config == null) return false;
            profile.EnsureInitialized();
            if (profile.Merchant.Offers.Count > 0) return true;
            return TryReplaceRotation(profile.Merchant, config, rotationId, seed, resetPaidRefresh: true);
        }

        public static bool TryRotateAfterSettledRaid(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            ExtractionMerchantRaidOutcome raidOutcome,
            int seed)
        {
            if (!ExtractionFeatureSwitch.Enabled
                || profile == null
                || config == null
                || raidOutcome == null
                || string.IsNullOrEmpty(raidOutcome.RaidId))
            {
                return false;
            }

            profile.EnsureInitialized();
            if (profile.Merchant.LastProcessedSettledRaidId == raidOutcome.RaidId) return true;
            if (!IsValidRaid(config.MerchantRotation, raidOutcome))
            {
                profile.Merchant.LastProcessedSettledRaidId = raidOutcome.RaidId;
                return true;
            }

            string rotationId = ExtractionOperationId.Create("merchant.rotation", raidOutcome.RaidId);
            if (!TryReplaceRotation(profile.Merchant, config, rotationId, seed, resetPaidRefresh: true))
                return false;

            profile.Merchant.LastProcessedSettledRaidId = raidOutcome.RaidId;
            profile.Merchant.FreeRotationCount++;
            return true;
        }

        public static bool TryBuildOffers(
            ExtractionPlayableConfig config,
            string rotationId,
            int seed,
            List<ExtractionMerchantOfferState> results)
        {
            if (results == null) return false;
            results.Clear();
            if (config == null
                || string.IsNullOrEmpty(rotationId)
                || config.MerchantSlots == null
                || config.MerchantSlots.Count == 0)
            {
                return false;
            }

            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in config.MerchantSlots)
            {
                if (slot == null
                    || string.IsNullOrEmpty(slot.SlotId)
                    || !TryGetPool(config, slot.PoolId, out var pool)
                    || !TrySelectOffer(config, pool, rotationId, slot.SlotId, seed, selectedIds, out var offer))
                {
                    results.Clear();
                    return false;
                }

                selectedIds.Add(offer.OfferId);
                results.Add(new ExtractionMerchantOfferState(offer.OfferId, offer.InitialStock));
            }

            return results.Count == config.MerchantSlots.Count;
        }

        public static int GetPaidRefreshCost(
            ExtractionMerchantState state,
            ExtractionMerchantRotationSettings settings)
        {
            if (settings == null) return 0;
            if (state?.NextPaidRefreshCost > 0) return state.NextPaidRefreshCost;
            return Math.Max(1, settings.BasePaidRefreshCost);
        }

        internal static bool TryReplaceRotation(
            ExtractionMerchantState state,
            ExtractionPlayableConfig config,
            string rotationId,
            int seed,
            bool resetPaidRefresh)
        {
            if (state == null || config == null) return false;
            var offers = new List<ExtractionMerchantOfferState>();
            if (!TryBuildOffers(config, rotationId, seed, offers)) return false;

            state.RotationId = rotationId;
            state.RotationSeed = seed;
            state.Offers = offers;
            if (resetPaidRefresh)
            {
                state.PaidRefreshCount = 0;
                state.NextPaidRefreshCost = Math.Max(1, config.MerchantRotation?.BasePaidRefreshCost ?? 1);
            }
            return true;
        }

        private static bool IsValidRaid(
            ExtractionMerchantRotationSettings settings,
            ExtractionMerchantRaidOutcome raidOutcome)
        {
            if (raidOutcome.Outcome == ExtractionMerchantRaidOutcomeType.Success
                || raidOutcome.Outcome == ExtractionMerchantRaidOutcomeType.Timeout)
            {
                return true;
            }

            int minimum = Math.Max(0, settings?.MinimumValidRaidGameplaySeconds ?? 0);
            return raidOutcome.HasParticipationReceipt
                   || raidOutcome.GameplayElapsedSeconds >= minimum;
        }

        private static bool TryGetPool(
            ExtractionPlayableConfig config,
            string poolId,
            out ExtractionMerchantOfferPoolDefinition pool)
        {
            pool = null;
            if (string.IsNullOrEmpty(poolId) || config.MerchantOfferPools == null) return false;
            foreach (var candidate in config.MerchantOfferPools)
            {
                if (candidate?.PoolId != poolId) continue;
                if (pool != null) return false;
                pool = candidate;
            }
            return pool?.OfferIds != null && pool.OfferIds.Count > 0;
        }

        private static bool TrySelectOffer(
            ExtractionPlayableConfig config,
            ExtractionMerchantOfferPoolDefinition pool,
            string rotationId,
            string slotId,
            int seed,
            HashSet<string> selectedIds,
            out ExtractionMerchantOfferDefinition selected)
        {
            selected = null;
            var candidates = new List<ExtractionMerchantOfferDefinition>();
            long totalWeight = 0;
            foreach (string offerId in pool.OfferIds)
            {
                if (selectedIds.Contains(offerId)) continue;
                if (!TryGetOffer(config, offerId, out var offer) || !offer.IsValid || !offer.UsesSharedBloodSample)
                    continue;
                candidates.Add(offer);
                totalWeight += offer.SelectionWeight;
            }
            if (candidates.Count == 0 || totalWeight <= 0) return false;

            uint hash = unchecked((uint)ExtractionStableHash.ComputeInt32(
                SelectionHashDomain,
                rotationId,
                slotId,
                seed.ToString(CultureInfo.InvariantCulture)));
            long roll = hash % totalWeight;
            foreach (var candidate in candidates)
            {
                if (roll < candidate.SelectionWeight)
                {
                    selected = candidate;
                    return true;
                }
                roll -= candidate.SelectionWeight;
            }
            return false;
        }

        internal static bool TryGetOffer(
            ExtractionPlayableConfig config,
            string offerId,
            out ExtractionMerchantOfferDefinition offer)
        {
            offer = null;
            if (config?.MerchantOfferDefinitions == null || string.IsNullOrEmpty(offerId)) return false;
            foreach (var candidate in config.MerchantOfferDefinitions)
            {
                if (candidate?.OfferId != offerId) continue;
                if (offer != null) return false;
                offer = candidate;
            }
            return offer != null;
        }
    }
}
