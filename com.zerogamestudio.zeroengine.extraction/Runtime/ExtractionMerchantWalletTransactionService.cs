using System;
using System.Globalization;

namespace POB.Extraction
{
    public sealed class ExtractionMerchantWalletTransactionResult
    {
        public SharedMetaWalletProfileSagaRequest Request;
        public SharedMetaWalletProfileSagaResult SagaResult;

        public ExtractionMerchantWalletTransactionResult(
            SharedMetaWalletProfileSagaRequest request,
            SharedMetaWalletProfileSagaResult sagaResult)
        {
            Request = request;
            SagaResult = sagaResult;
        }

        public bool IsSuccess => SagaResult?.IsSuccess == true;
    }

    public sealed class ExtractionMerchantWalletTransactionService
    {
        public const float SellValueMultiplier = 0.5f;

        private readonly IExtractionProfileStore store;
        private readonly ISharedMetaWallet wallet;
        private readonly ExtractionPlayableConfig config;
        private readonly SharedMetaWalletProfileSaga saga;

        public ExtractionMerchantWalletTransactionService(
            IExtractionProfileStore store,
            ISharedMetaWallet wallet,
            ExtractionPlayableConfig config)
        {
            this.store = store;
            this.wallet = wallet;
            this.config = config;
            saga = new SharedMetaWalletProfileSaga(store, wallet);
        }

        public ExtractionMerchantWalletTransactionResult TryPurchase(
            string transactionId,
            string offerId,
            string purchasedItemInstanceId)
        {
            if (!ExtractionFeatureSwitch.Enabled
                || string.IsNullOrEmpty(transactionId)
                || string.IsNullOrEmpty(offerId)
                || string.IsNullOrEmpty(purchasedItemInstanceId)
                || !ExtractionMerchantRotationService.TryGetOffer(config, offerId, out var offer)
                || !offer.IsValid
                || !offer.UsesSharedBloodSample)
            {
                return Invalid();
            }

            string payload = ExtractionStableHash.ComputeSha256(
                "zeroengine.extraction.merchant-purchase:v1",
                offerId,
                purchasedItemInstanceId,
                offer.ItemDefinitionId,
                offer.Quantity.ToString(CultureInfo.InvariantCulture),
                offer.PriceQuantity.ToString(CultureInfo.InvariantCulture));
            var request = new SharedMetaWalletProfileSagaRequest(
                transactionId,
                "merchant.purchase",
                ExtractionMerchantOfferDefinition.BloodSampleResourceId,
                offer.PriceQuantity,
                payload,
                SharedMetaWalletProfileFlow.DebitThenProfile);
            var result = saga.Execute(
                request,
                profile => CanPurchase(profile, offer, purchasedItemInstanceId),
                profile => ApplyPurchase(profile, offer, purchasedItemInstanceId));
            return new ExtractionMerchantWalletTransactionResult(request, result);
        }

        public bool TryGetSellQuote(string itemInstanceId, out int creditQuantity)
        {
            creditQuantity = 0;
            var profile = store?.Load()?.Profile;
            return TryGetSellQuote(profile, itemInstanceId, out creditQuantity);
        }

        public ExtractionMerchantWalletTransactionResult TrySell(
            string transactionId,
            string itemInstanceId,
            int expectedCreditQuantity)
        {
            if (!ExtractionFeatureSwitch.Enabled
                || string.IsNullOrEmpty(transactionId)
                || string.IsNullOrEmpty(itemInstanceId)
                || expectedCreditQuantity <= 0)
            {
                return Invalid();
            }

            string payload = ExtractionStableHash.ComputeSha256(
                "zeroengine.extraction.merchant-sell:v1",
                itemInstanceId,
                expectedCreditQuantity.ToString(CultureInfo.InvariantCulture));
            var request = new SharedMetaWalletProfileSagaRequest(
                transactionId,
                "merchant.sell",
                ExtractionMerchantOfferDefinition.BloodSampleResourceId,
                expectedCreditQuantity,
                payload,
                SharedMetaWalletProfileFlow.ProfileThenCredit);
            var result = saga.Execute(
                request,
                profile => TryGetSellQuote(profile, itemInstanceId, out int quote)
                           && quote == expectedCreditQuantity,
                profile => ApplySell(profile, itemInstanceId));
            return new ExtractionMerchantWalletTransactionResult(request, result);
        }

        public ExtractionMerchantWalletTransactionResult TryPaidRefresh(
            string transactionId,
            int seed)
        {
            var loaded = store?.Load();
            int cost = loaded?.Profile == null
                ? 0
                : ExtractionMerchantRotationService.GetPaidRefreshCost(
                    loaded.Profile.Merchant,
                    config?.MerchantRotation);
            if (!ExtractionFeatureSwitch.Enabled
                || string.IsNullOrEmpty(transactionId)
                || cost <= 0
                || config == null)
            {
                return Invalid();
            }

            string rotationId = ExtractionOperationId.Create("merchant.paid-refresh", transactionId);
            string payload = ExtractionStableHash.ComputeSha256(
                "zeroengine.extraction.merchant-paid-refresh:v1",
                rotationId,
                seed.ToString(CultureInfo.InvariantCulture),
                cost.ToString(CultureInfo.InvariantCulture));
            var request = new SharedMetaWalletProfileSagaRequest(
                transactionId,
                "merchant.paid-refresh",
                ExtractionMerchantOfferDefinition.BloodSampleResourceId,
                cost,
                payload,
                SharedMetaWalletProfileFlow.DebitThenProfile);
            var result = saga.Execute(
                request,
                profile => CanRefresh(profile, rotationId, seed, cost),
                profile => ApplyRefresh(profile, rotationId, seed, cost));
            return new ExtractionMerchantWalletTransactionResult(request, result);
        }

        public SharedMetaWalletProfileSagaResult Complete(
            ExtractionMerchantWalletTransactionResult transaction)
        {
            return transaction?.Request == null
                ? new SharedMetaWalletProfileSagaResult(
                    SharedMetaWalletProfileSagaStatus.InvalidRequest,
                    0)
                : saga.Complete(transaction.Request);
        }

        private bool CanPurchase(
            ExtractionProfileSaveData profile,
            ExtractionMerchantOfferDefinition offer,
            string purchasedItemInstanceId)
        {
            if (profile == null
                || config == null
                || !config.TryGetItemDefinition(offer.ItemDefinitionId, out var definition)
                || profile.Items.TryGet(purchasedItemInstanceId, out _)
                || profile.Ownership.TryGetContainer(purchasedItemInstanceId, out _)
                || !TryGetOfferState(profile.Merchant, offer.OfferId, out var state)
                || state.RemainingStock <= 0)
            {
                return false;
            }

            return profile.Stash.TryFindFreeSlotWithRotation(
                definition.Width,
                definition.Height,
                definition.CanRotate,
                out _,
                out _,
                out _);
        }

        private void ApplyPurchase(
            ExtractionProfileSaveData profile,
            ExtractionMerchantOfferDefinition offer,
            string purchasedItemInstanceId)
        {
            if (!config.TryGetItemDefinition(offer.ItemDefinitionId, out var definition)
                || !TryGetOfferState(profile.Merchant, offer.OfferId, out var state)
                || !profile.Stash.TryFindFreeSlotWithRotation(
                    definition.Width,
                    definition.Height,
                    definition.CanRotate,
                    out int x,
                    out int y,
                    out bool rotated))
            {
                throw new InvalidOperationException("Purchase preconditions changed.");
            }

            var item = new ExtractionItemInstance(
                purchasedItemInstanceId,
                offer.ItemDefinitionId,
                offer.Quantity,
                "merchant",
                offer.OfferId);
            ExtractionItemActionPolicyService.ApplyDefinitionPolicyToInstance(definition, item);
            if (!profile.Items.Register(item)
                || !profile.Stash.TryPlace(item, definition, x, y, rotated)
                || !profile.Ownership.Register(
                    purchasedItemInstanceId,
                    ExtractionInventoryContainerType.Stash))
            {
                throw new InvalidOperationException("Purchase mutation failed.");
            }
            state.RemainingStock--;
        }

        private bool TryGetSellQuote(
            ExtractionProfileSaveData profile,
            string itemInstanceId,
            out int creditQuantity)
        {
            creditQuantity = 0;
            if (profile == null
                || config == null
                || !profile.Items.TryGet(itemInstanceId, out var item)
                || !config.TryGetItemDefinition(item.DefinitionId, out var definition)
                || !TryFindOwnership(profile, itemInstanceId, out var entry))
            {
                return false;
            }

            if (!ExtractionItemActionPolicyService.CanSell(definition, item)
                || !IsSellableBaseLocation(entry)) return false;
            long raw = (long)definition.Value * Math.Max(0, item.Quantity);
            creditQuantity = (int)Math.Min(int.MaxValue, raw * 1 / 2);
            return creditQuantity > 0;
        }

        private void ApplySell(ExtractionProfileSaveData profile, string itemInstanceId)
        {
            if (!profile.Items.TryGet(itemInstanceId, out var item)
                || !config.TryGetItemDefinition(item.DefinitionId, out var definition)
                || !TryFindOwnership(profile, itemInstanceId, out var entry))
            {
                throw new InvalidOperationException("Sell preconditions changed.");
            }

            if (entry.Container == ExtractionInventoryContainerType.EquipmentSlot)
            {
                if (!ExtractionItemLocationService.TryGetEquipment(
                        profile,
                        null,
                        entry.LocationSubtype,
                        out var equipment)
                    || !equipment.TryClear(entry.LocationId, itemInstanceId))
                {
                    throw new InvalidOperationException("Equipped sell item could not be detached.");
                }
            }
            else
            {
                if (!ExtractionItemLocationService.TryGetGrid(profile, null, entry.Container, out var grid)
                    || !grid.TryRemove(itemInstanceId))
                {
                    throw new InvalidOperationException("Sell item could not be detached.");
                }
            }

            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    entry.Container,
                    ExtractionInventoryContainerType.Sold,
                    "merchant",
                    null))
            {
                throw new InvalidOperationException("Sell ownership could not be committed.");
            }
        }

        private bool CanRefresh(
            ExtractionProfileSaveData profile,
            string rotationId,
            int seed,
            int expectedCost)
        {
            if (profile == null
                || ExtractionMerchantRotationService.GetPaidRefreshCost(
                    profile.Merchant,
                    config.MerchantRotation) != expectedCost)
            {
                return false;
            }
            var offers = new System.Collections.Generic.List<ExtractionMerchantOfferState>();
            return ExtractionMerchantRotationService.TryBuildOffers(
                config,
                rotationId,
                seed,
                offers);
        }

        private void ApplyRefresh(
            ExtractionProfileSaveData profile,
            string rotationId,
            int seed,
            int cost)
        {
            if (!ExtractionMerchantRotationService.TryReplaceRotation(
                    profile.Merchant,
                    config,
                    rotationId,
                    seed,
                    resetPaidRefresh: false))
            {
                throw new InvalidOperationException("Paid refresh generation failed.");
            }

            profile.Merchant.PaidRefreshCount++;
            int increment = Math.Max(0, config.MerchantRotation.PaidRefreshCostIncrement);
            profile.Merchant.NextPaidRefreshCost = Math.Min(int.MaxValue, cost + increment);
        }

        private static bool TryGetOfferState(
            ExtractionMerchantState merchant,
            string offerId,
            out ExtractionMerchantOfferState state)
        {
            state = null;
            if (merchant?.Offers == null) return false;
            foreach (var candidate in merchant.Offers)
            {
                if (candidate?.OfferId != offerId) continue;
                if (state != null) return false;
                state = candidate;
            }
            return state != null;
        }

        private static bool TryFindOwnership(
            ExtractionProfileSaveData profile,
            string itemInstanceId,
            out ExtractionOwnershipEntry entry)
        {
            entry = null;
            foreach (var candidate in profile.Ownership.Entries)
            {
                if (candidate?.ItemInstanceId != itemInstanceId) continue;
                if (entry != null) return false;
                entry = candidate;
            }
            return entry != null;
        }

        private static bool IsSellableBaseLocation(ExtractionOwnershipEntry entry)
        {
            return entry.Container == ExtractionInventoryContainerType.Stash
                   || entry.Container == ExtractionInventoryContainerType.Loadout
                   || entry.Container == ExtractionInventoryContainerType.SecureContainer
                   || entry.Container == ExtractionInventoryContainerType.Holding
                   || (entry.Container == ExtractionInventoryContainerType.EquipmentSlot
                       && entry.LocationSubtype == ExtractionItemLocationService.BaseEquipmentLocationSubtype);
        }

        private static ExtractionMerchantWalletTransactionResult Invalid()
        {
            return new ExtractionMerchantWalletTransactionResult(
                null,
                new SharedMetaWalletProfileSagaResult(
                    SharedMetaWalletProfileSagaStatus.InvalidRequest,
                    0));
        }
    }
}
