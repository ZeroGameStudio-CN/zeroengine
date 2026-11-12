using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionMerchantRotationAndWalletTests
    {
        private ExtractionPlayableConfig config;

        [SetUp]
        public void SetUp()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(true);
            config = CreateConfig();
        }

        [TearDown]
        public void TearDown()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(false);
        }

        [Test]
        public void Rotation_SameSeedSameOffers_DifferentSeedsCanChangeOffers()
        {
            var first = new List<ExtractionMerchantOfferState>();
            var replay = new List<ExtractionMerchantOfferState>();
            Assert.IsTrue(ExtractionMerchantRotationService.TryBuildOffers(config, "rotation-a", 7, first));
            Assert.IsTrue(ExtractionMerchantRotationService.TryBuildOffers(config, "rotation-a", 7, replay));
            CollectionAssert.AreEqual(OfferIds(first), OfferIds(replay));

            bool foundDifferent = false;
            for (int seed = 8; seed < 40; seed++)
            {
                var candidate = new List<ExtractionMerchantOfferState>();
                Assert.IsTrue(ExtractionMerchantRotationService.TryBuildOffers(config, "rotation-a", seed, candidate));
                if (string.Join("|", OfferIds(candidate)) == string.Join("|", OfferIds(first))) continue;
                foundDifferent = true;
                break;
            }
            Assert.IsTrue(foundDifferent, "The configured pool must expose seed-driven variation.");
        }

        [Test]
        public void SettledRaid_RotatesAtMostOnce_AndEarlyAbandonDoesNotRotate()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            Assert.IsTrue(ExtractionMerchantRotationService.TryInitialize(profile, config, "initial", 1));
            string initial = profile.Merchant.RotationId;

            Assert.IsTrue(ExtractionMerchantRotationService.TryRotateAfterSettledRaid(
                profile,
                config,
                new ExtractionMerchantRaidOutcome(
                    "raid-early",
                    ExtractionMerchantRaidOutcomeType.Abandon,
                    5,
                    false),
                2));
            Assert.AreEqual(initial, profile.Merchant.RotationId);
            Assert.AreEqual("raid-early", profile.Merchant.LastProcessedSettledRaidId);
            Assert.AreEqual(0, profile.Merchant.FreeRotationCount);

            var valid = new ExtractionMerchantRaidOutcome(
                "raid-valid",
                ExtractionMerchantRaidOutcomeType.Death,
                31,
                false);
            Assert.IsTrue(ExtractionMerchantRotationService.TryRotateAfterSettledRaid(
                profile, config, valid, 3));
            string rotated = profile.Merchant.RotationId;
            Assert.AreEqual(1, profile.Merchant.FreeRotationCount);

            Assert.IsTrue(ExtractionMerchantRotationService.TryRotateAfterSettledRaid(
                profile, config, valid, 99));
            Assert.AreEqual(rotated, profile.Merchant.RotationId);
            Assert.AreEqual(1, profile.Merchant.FreeRotationCount);
        }

        [Test]
        public void PurchaseSellAndPaidRefresh_UseOnlyBloodSampleAndAreIdempotent()
        {
            var initial = ExtractionProfileSaveData.CreateEmpty();
            Assert.IsTrue(ExtractionMerchantRotationService.TryInitialize(initial, config, "initial", 1));
            string offerId = initial.Merchant.Offers[0].OfferId;
            var offer = config.MerchantOfferDefinitions.Find(candidate => candidate.OfferId == offerId);
            Assert.IsNotNull(offer);
            var store = new ExtractionInMemoryProfileStore(initial);
            var wallet = new TestWallet(100);
            var service = new ExtractionMerchantWalletTransactionService(store, wallet, config);

            var purchase = service.TryPurchase("tx-buy", offerId, "bought-1");
            Assert.IsTrue(purchase.IsSuccess);
            int afterPurchase = 100 - offer.PriceQuantity;
            Assert.AreEqual(afterPurchase, wallet.Balance);
            Assert.IsTrue(store.Profile.Items.TryGet("bought-1", out _));

            var replay = service.TryPurchase("tx-buy", offerId, "bought-1");
            Assert.IsTrue(replay.IsSuccess);
            Assert.AreEqual(afterPurchase, wallet.Balance, "replay must not debit twice");
            Assert.IsTrue(service.Complete(replay).IsSuccess);

            Assert.IsTrue(service.TryGetSellQuote("bought-1", out int sellQuote));
            var sale = service.TrySell("tx-sell", "bought-1", sellQuote);
            Assert.IsTrue(sale.IsSuccess);
            Assert.AreEqual(afterPurchase + sellQuote, wallet.Balance);
            Assert.AreEqual(
                ExtractionInventoryContainerType.Sold,
                store.Profile.Ownership.GetRequiredContainer("bought-1"));

            int refreshCost = ExtractionMerchantRotationService.GetPaidRefreshCost(
                store.Profile.Merchant,
                config.MerchantRotation);
            string beforeRotation = store.Profile.Merchant.RotationId;
            var refresh = service.TryPaidRefresh("tx-refresh", 42);
            Assert.IsTrue(refresh.IsSuccess);
            Assert.AreEqual(afterPurchase + sellQuote - refreshCost, wallet.Balance);
            Assert.AreNotEqual(beforeRotation, store.Profile.Merchant.RotationId);
            Assert.AreEqual(1, store.Profile.Merchant.PaidRefreshCount);
            Assert.AreEqual(refreshCost + config.MerchantRotation.PaidRefreshCostIncrement,
                store.Profile.Merchant.NextPaidRefreshCost);
        }

        [Test]
        public void Purchase_ProfilePrepareFailure_DoesNotDebitOrGrantItem()
        {
            var initial = ExtractionProfileSaveData.CreateEmpty();
            Assert.IsTrue(ExtractionMerchantRotationService.TryInitialize(initial, config, "initial", 1));
            var store = new ExtractionInMemoryProfileStore(initial)
            {
                NextCommitFault = ExtractionProfileInMemoryCommitFault.Prepare
            };
            var wallet = new TestWallet(100);
            var service = new ExtractionMerchantWalletTransactionService(store, wallet, config);

            var result = service.TryPurchase("tx-fail", initial.Merchant.Offers[0].OfferId, "never-granted");

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(100, wallet.Balance);
            Assert.IsFalse(store.Profile.Items.TryGet("never-granted", out _));
        }

        [Test]
        public void Sell_PolicyDenied_DoesNotCreditOrRemoveItem()
        {
            var initial = ExtractionProfileSaveData.CreateEmpty();
            var definition = config.ItemDefinitions[0];
            definition.ActionPolicy.CanSell = false;
            var item = new ExtractionItemInstance("no-sell", definition.DefinitionId, 1);
            Assert.IsTrue(initial.Items.Register(item));
            Assert.IsTrue(initial.Stash.TryPlace(item, definition, 0, 0, false));
            Assert.IsTrue(initial.Ownership.Register(item.InstanceId, ExtractionInventoryContainerType.Stash));
            var store = new ExtractionInMemoryProfileStore(initial);
            var wallet = new TestWallet(20);
            var service = new ExtractionMerchantWalletTransactionService(store, wallet, config);

            Assert.IsFalse(service.TryGetSellQuote("no-sell", out _));
            Assert.AreEqual(20, wallet.Balance);
            Assert.IsTrue(store.Profile.Stash.TryGetPlacement("no-sell", out _));
        }

        // Permanent regression: proportional sale, stale-quote rejection and save compatibility.
        [TestCase(10, 10, 100, 1, 50)]
        [TestCase(3, 10, 101, 1, 15)]
        [TestCase(0, 10, 100, 1, 0)]
        [TestCase(-1, 10, 100, 1, 0)]
        [TestCase(20, 10, 100, 1, 50)]
        [TestCase(0, 0, 101, 3, 151)]
        [TestCase(0, -1, 100, 1, 50)]
        [TestCase(1, 100, 100, 1, 0)]
        [TestCase(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)]
        public void SellQuote_Durability_RoundsDownAndClamps(int current, int maximum, int value,
            int quantity, int expected)
        {
            var definition = config.ItemDefinitions[0];
            definition.MaxDurability = maximum;
            definition.Value = value;
            var initial = CreateSellProfile(definition, current, quantity);
            var loaded = ExtractionProfileSerialization.FromJson(UnityEngine.JsonUtility.ToJson(initial));
            var store = new ExtractionInMemoryProfileStore(loaded);
            var wallet = new TestWallet(0);
            var service = new ExtractionMerchantWalletTransactionService(store, wallet, config);

            Assert.AreEqual(expected > 0, service.TryGetSellQuote("durable", out int quote));
            Assert.AreEqual(expected, quote);
            Assert.IsTrue(store.Profile.Items.TryGet("durable", out var item));
            Assert.AreEqual(current, item.CurrentDurability, "Loading/quoting must not refill durability.");
            Assert.AreEqual(expected, ExtractionSellTransactionService.GetSellQuantity(definition, item));
            var sale = service.TrySell("durable-sale", "durable", quote);
            Assert.AreEqual(expected > 0, sale.IsSuccess);
            Assert.AreEqual(expected, wallet.Balance);
            Assert.AreEqual(expected > 0 ? ExtractionInventoryContainerType.Sold : ExtractionInventoryContainerType.Stash,
                store.Profile.Ownership.GetRequiredContainer("durable"));
            if (expected > 0)
            {
                Assert.IsTrue(service.TrySell("durable-sale", "durable", quote).IsSuccess);
                Assert.AreEqual(expected, wallet.Balance, "Replaying a sale cannot credit twice.");
            }
        }

        [Test]
        public void Sell_DurabilityChangesAfterQuote_RejectsOldPriceWithoutMutation()
        {
            var definition = config.ItemDefinitions[0];
            definition.MaxDurability = 10;
            definition.Value = 100;
            var store = new ExtractionInMemoryProfileStore(CreateSellProfile(definition, 10, 1));
            var wallet = new TestWallet(0);
            var service = new ExtractionMerchantWalletTransactionService(store, wallet, config);
            Assert.IsTrue(service.TryGetSellQuote("durable", out int oldQuote));
            store.Profile.Items.TryGet("durable", out var item);
            item.CurrentDurability = 4;

            Assert.IsFalse(service.TrySell("stale-sale", "durable", oldQuote).IsSuccess);
            Assert.AreEqual(0, wallet.Balance);
            Assert.IsTrue(store.Profile.Stash.TryGetPlacement("durable", out _));
            Assert.IsTrue(service.TryGetSellQuote("durable", out int freshQuote));
            Assert.AreEqual(20, freshQuote);
            Assert.IsTrue(service.TrySell("fresh-sale", "durable", freshQuote).IsSuccess);
            Assert.AreEqual(20, wallet.Balance);
        }

        private static ExtractionProfileSaveData CreateSellProfile(ExtractionItemDefinition definition,
            int current, int quantity)
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            var item = new ExtractionItemInstance("durable", definition.DefinitionId, quantity)
            {
                CurrentDurability = current
            };
            Assert.IsTrue(profile.Items.Register(item));
            Assert.IsTrue(profile.Stash.TryPlace(item, definition, 0, 0, false));
            Assert.IsTrue(profile.Ownership.Register(item.InstanceId, ExtractionInventoryContainerType.Stash));
            return profile;
        }

        private static ExtractionPlayableConfig CreateConfig()
        {
            var result = new ExtractionPlayableConfig(4, 4, 2, 2);
            for (int i = 0; i < 3; i++)
            {
                var item = new ExtractionItemDefinition("item-" + i, 1, 1, false, 1)
                {
                    Value = 20 + i * 4
                };
                result.ItemDefinitions.Add(item);
                var offer = new ExtractionMerchantOfferDefinition(
                    "offer-" + i,
                    item.DefinitionId,
                    1,
                    "legacy-unused",
                    12 + i,
                    1)
                {
                    PriceItemDefinitionId = null,
                    PriceResourceId = ExtractionMerchantOfferDefinition.BloodSampleResourceId,
                    InitialStock = 1,
                    SelectionWeight = i + 1
                };
                result.MerchantOfferDefinitions.Add(offer);
            }

            var pool = new ExtractionMerchantOfferPoolDefinition("starter");
            pool.OfferIds.Add("offer-0");
            pool.OfferIds.Add("offer-1");
            pool.OfferIds.Add("offer-2");
            result.MerchantOfferPools.Add(pool);
            result.MerchantSlots.Add(new ExtractionMerchantSlotDefinition("slot-0", pool.PoolId));
            result.MerchantSlots.Add(new ExtractionMerchantSlotDefinition("slot-1", pool.PoolId));
            result.MerchantRotation.MinimumValidRaidGameplaySeconds = 30;
            result.MerchantRotation.BasePaidRefreshCost = 10;
            result.MerchantRotation.PaidRefreshCostIncrement = 5;
            return result;
        }

        private static string[] OfferIds(List<ExtractionMerchantOfferState> offers)
        {
            var ids = new string[offers.Count];
            for (int i = 0; i < offers.Count; i++) ids[i] = offers[i].OfferId;
            return ids;
        }

        private sealed class TestWallet : ISharedMetaWallet
        {
            private readonly Dictionary<string, WalletReceipt> receipts = new();

            internal TestWallet(int balance)
            {
                Balance = balance;
            }

            internal int Balance { get; private set; }

            public bool TryQuery(string resourceId, out SharedMetaWalletBalance balance)
            {
                balance = resourceId == ExtractionMerchantOfferDefinition.BloodSampleResourceId
                    ? new SharedMetaWalletBalance(resourceId, Balance)
                    : null;
                return balance != null;
            }

            public SharedMetaWalletTransactionResult TryDebit(SharedMetaWalletTransactionRequest request)
            {
                return Apply(request, -request.Quantity);
            }

            public SharedMetaWalletTransactionResult TryCredit(SharedMetaWalletTransactionRequest request)
            {
                return Apply(request, request.Quantity);
            }

            private SharedMetaWalletTransactionResult Apply(
                SharedMetaWalletTransactionRequest request,
                int delta)
            {
                if (request == null
                    || !request.IsValid
                    || request.ResourceId != ExtractionMerchantOfferDefinition.BloodSampleResourceId)
                {
                    return new SharedMetaWalletTransactionResult(
                        SharedMetaWalletTransactionStatus.InvalidRequest,
                        Balance);
                }
                if (receipts.TryGetValue(request.TransactionId, out var existing))
                {
                    return existing.PayloadHash == request.PayloadHash && existing.Delta == delta
                        ? new SharedMetaWalletTransactionResult(
                            SharedMetaWalletTransactionStatus.AlreadyApplied,
                            Balance)
                        : new SharedMetaWalletTransactionResult(
                            SharedMetaWalletTransactionStatus.Conflict,
                            Balance);
                }
                if (delta < 0 && Balance < -delta)
                {
                    return new SharedMetaWalletTransactionResult(
                        SharedMetaWalletTransactionStatus.InsufficientFunds,
                        Balance);
                }

                Balance += delta;
                receipts.Add(request.TransactionId, new WalletReceipt(request.PayloadHash, delta));
                return new SharedMetaWalletTransactionResult(
                    SharedMetaWalletTransactionStatus.Succeeded,
                    Balance);
            }
        }

        private sealed class WalletReceipt
        {
            internal WalletReceipt(string payloadHash, int delta)
            {
                PayloadHash = payloadHash;
                Delta = delta;
            }

            internal string PayloadHash { get; }
            internal int Delta { get; }
        }
    }
}
