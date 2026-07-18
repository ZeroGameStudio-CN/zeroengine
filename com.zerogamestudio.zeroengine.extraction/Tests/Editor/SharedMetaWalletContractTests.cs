using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class SharedMetaWalletContractTests
    {
        [Test]
        public void TransactionStatus_NumericValuesStayStable()
        {
            Assert.AreEqual(0, (int)SharedMetaWalletTransactionStatus.Succeeded);
            Assert.AreEqual(1, (int)SharedMetaWalletTransactionStatus.AlreadyApplied);
            Assert.AreEqual(2, (int)SharedMetaWalletTransactionStatus.InsufficientFunds);
            Assert.AreEqual(3, (int)SharedMetaWalletTransactionStatus.InvalidRequest);
            Assert.AreEqual(4, (int)SharedMetaWalletTransactionStatus.StoreUnavailable);
            Assert.AreEqual(5, (int)SharedMetaWalletTransactionStatus.Conflict);
            Assert.AreEqual(0, (int)SharedMetaWalletProfileFlow.DebitThenProfile);
            Assert.AreEqual(1, (int)SharedMetaWalletProfileFlow.ProfileThenCredit);
            Assert.AreEqual(0, (int)SharedMetaWalletProfileSagaStatus.Succeeded);
            Assert.AreEqual(1, (int)SharedMetaWalletProfileSagaStatus.PresentationPending);
            Assert.AreEqual(2, (int)SharedMetaWalletProfileSagaStatus.AlreadyCompleted);
            Assert.AreEqual(3, (int)SharedMetaWalletProfileSagaStatus.Compensated);
            Assert.AreEqual(4, (int)SharedMetaWalletProfileSagaStatus.InsufficientFunds);
            Assert.AreEqual(5, (int)SharedMetaWalletProfileSagaStatus.InvalidRequest);
            Assert.AreEqual(6, (int)SharedMetaWalletProfileSagaStatus.StoreUnavailable);
            Assert.AreEqual(7, (int)SharedMetaWalletProfileSagaStatus.Conflict);
            Assert.AreEqual(8, (int)SharedMetaWalletProfileSagaStatus.DomainRejected);
            Assert.AreEqual(9, (int)SharedMetaWalletProfileSagaStatus.RecoveryPending);
        }

        [Test]
        public void TransactionRequest_RequiresStableIdResourceQuantityAndPayloadHash()
        {
            var valid = new SharedMetaWalletTransactionRequest(
                "tx-001",
                "BloodSample",
                3,
                "sha256:payload");

            Assert.IsTrue(valid.IsValid);
            Assert.IsFalse(new SharedMetaWalletTransactionRequest("", "BloodSample", 3, "sha256:payload").IsValid);
            Assert.IsFalse(new SharedMetaWalletTransactionRequest("tx-001", "", 3, "sha256:payload").IsValid);
            Assert.IsFalse(new SharedMetaWalletTransactionRequest("tx-001", "BloodSample", 0, "sha256:payload").IsValid);
            Assert.IsFalse(new SharedMetaWalletTransactionRequest("tx-001", "BloodSample", 3, "").IsValid);
        }

        [Test]
        public void TransactionResult_TreatsReplayAsSuccessful()
        {
            var applied = new SharedMetaWalletTransactionResult(
                SharedMetaWalletTransactionStatus.Succeeded,
                balance: 7);
            var replay = new SharedMetaWalletTransactionResult(
                SharedMetaWalletTransactionStatus.AlreadyApplied,
                balance: 7);
            var failed = new SharedMetaWalletTransactionResult(
                SharedMetaWalletTransactionStatus.InsufficientFunds,
                balance: 2);

            Assert.IsTrue(applied.IsSuccess);
            Assert.IsTrue(replay.IsSuccess);
            Assert.IsFalse(failed.IsSuccess);
        }
    }

    public class SharedMetaWalletProfileSagaTests
    {
        private const string TransactionId =
            "operation:v1:44b33455f263739b2a922b67d777162dd6fd3029ae0e1b9f207be42cb6935ad0";
        private const string OperationType = "merchant.purchase";
        private const string ResourceId = "BloodSample";
        private const string PayloadHash = "sha256:wallet-profile-payload";

        [Test]
        public void Debit_PrepareCommitFailure_DoesNotTouchWalletOrDomain()
        {
            var store = new FaultInjectingProfileStore { FailCommitNumber = 1 };
            var wallet = new FaultInjectingWallet(balance: 10);
            var saga = new SharedMetaWalletProfileSaga(store, wallet);

            var result = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                _ => true,
                profile => profile.Merchant.PaidRefreshCount++);

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.StoreUnavailable, result.Status);
            Assert.AreEqual(10, wallet.Balance);
            Assert.AreEqual(0, store.CurrentProfile.Merchant.PaidRefreshCount);
            Assert.IsEmpty(store.CurrentProfile.OperationJournal.Entries);
        }

        [Test]
        public void Debit_WalletUnavailableAfterPrepared_RetryCompletesExactlyOnce()
        {
            var store = new FaultInjectingProfileStore();
            var wallet = new FaultInjectingWallet(balance: 10) { FailNextDebit = true };
            var saga = new SharedMetaWalletProfileSaga(store, wallet);
            int applyCalls = 0;

            var first = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                _ => true,
                profile =>
                {
                    applyCalls++;
                    profile.Merchant.PaidRefreshCount++;
                });

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.StoreUnavailable, first.Status);
            Assert.AreEqual(10, wallet.Balance);
            AssertJournalState(store, ExtractionOperationState.Prepared);

            var retry = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                _ => true,
                profile =>
                {
                    applyCalls++;
                    profile.Merchant.PaidRefreshCount++;
                });

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.Succeeded, retry.Status);
            Assert.AreEqual(6, wallet.Balance);
            Assert.AreEqual(1, applyCalls);
            Assert.AreEqual(1, store.CurrentProfile.Merchant.PaidRefreshCount);
            AssertJournalState(store, ExtractionOperationState.Committed);
        }

        [Test]
        public void Debit_ProfileCommitFailureAfterWallet_RetryDoesNotDoubleDebit()
        {
            var store = new FaultInjectingProfileStore { FailCommitNumber = 2 };
            var wallet = new FaultInjectingWallet(balance: 10);
            var saga = new SharedMetaWalletProfileSaga(store, wallet);
            int applyCalls = 0;

            var first = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                _ => true,
                profile =>
                {
                    applyCalls++;
                    profile.Merchant.PaidRefreshCount++;
                });

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.RecoveryPending, first.Status);
            Assert.AreEqual(6, wallet.Balance);
            Assert.AreEqual(0, store.CurrentProfile.Merchant.PaidRefreshCount);
            AssertJournalState(store, ExtractionOperationState.Prepared);

            var retry = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                _ => true,
                profile =>
                {
                    applyCalls++;
                    profile.Merchant.PaidRefreshCount++;
                });

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.Succeeded, retry.Status);
            Assert.AreEqual(6, wallet.Balance);
            Assert.AreEqual(2, applyCalls, "The failed draft may run once, but only one mutation may persist.");
            Assert.AreEqual(1, store.CurrentProfile.Merchant.PaidRefreshCount);
            AssertJournalState(store, ExtractionOperationState.Committed);
        }

        [Test]
        public void Debit_CompensationInterrupted_RetryRestoresWalletAndTerminates()
        {
            var store = new FaultInjectingProfileStore();
            var wallet = new FaultInjectingWallet(balance: 10) { FailNextCredit = true };
            var saga = new SharedMetaWalletProfileSaga(store, wallet);
            int validationCalls = 0;

            bool CanApply(ExtractionProfileSaveData _)
            {
                validationCalls++;
                return validationCalls == 1;
            }

            var first = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                CanApply,
                profile => profile.Merchant.PaidRefreshCount++);

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.RecoveryPending, first.Status);
            Assert.AreEqual(6, wallet.Balance);
            Assert.AreEqual(0, store.CurrentProfile.Merchant.PaidRefreshCount);
            AssertJournalState(store, ExtractionOperationState.CompensationPending);

            var retry = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                _ =>
                {
                    Assert.Fail("Compensation replay must not re-enter domain validation.");
                    return false;
                },
                _ => Assert.Fail("Compensation replay must not apply the domain mutation."));

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.Compensated, retry.Status);
            Assert.AreEqual(10, wallet.Balance);
            Assert.AreEqual(0, store.CurrentProfile.Merchant.PaidRefreshCount);
            AssertJournalState(store, ExtractionOperationState.Compensated);
        }

        [Test]
        public void Debit_DomainMutationThrowsAfterPartialChange_CompensatesWithoutPersistingDraft()
        {
            var store = new FaultInjectingProfileStore();
            var wallet = new FaultInjectingWallet(balance: 10);
            var saga = new SharedMetaWalletProfileSaga(store, wallet);

            var result = saga.Execute(
                Request(SharedMetaWalletProfileFlow.DebitThenProfile),
                _ => true,
                profile =>
                {
                    profile.Merchant.PaidRefreshCount++;
                    throw new InvalidOperationException("Injected partial domain mutation.");
                });

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.Compensated, result.Status);
            Assert.AreEqual(10, wallet.Balance);
            Assert.AreEqual(0, store.CurrentProfile.Merchant.PaidRefreshCount);
            AssertJournalState(store, ExtractionOperationState.Compensated);
        }

        [Test]
        public void Credit_WalletFailureAfterProfileCommit_RetryCreditsWithoutReapplyingDomain()
        {
            var store = new FaultInjectingProfileStore();
            var wallet = new FaultInjectingWallet(balance: 10) { FailNextCredit = true };
            var saga = new SharedMetaWalletProfileSaga(store, wallet);
            int applyCalls = 0;

            var first = saga.Execute(
                Request(SharedMetaWalletProfileFlow.ProfileThenCredit),
                _ => true,
                profile =>
                {
                    applyCalls++;
                    profile.Merchant.PaidRefreshCount++;
                });

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.RecoveryPending, first.Status);
            Assert.AreEqual(10, wallet.Balance);
            Assert.AreEqual(1, applyCalls);
            Assert.AreEqual(1, store.CurrentProfile.Merchant.PaidRefreshCount);
            AssertJournalState(store, ExtractionOperationState.Committed);

            var retry = saga.Execute(
                Request(SharedMetaWalletProfileFlow.ProfileThenCredit),
                _ => true,
                _ => applyCalls++);

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.PresentationPending, retry.Status);
            Assert.AreEqual(14, wallet.Balance);
            Assert.AreEqual(1, applyCalls);
            Assert.AreEqual(1, store.CurrentProfile.Merchant.PaidRefreshCount);
        }

        [Test]
        public void Complete_CommitFailure_ReplaysPresentationAndCompletesWithoutDuplicateMutation()
        {
            var store = new FaultInjectingProfileStore();
            var wallet = new FaultInjectingWallet(balance: 10);
            var saga = new SharedMetaWalletProfileSaga(store, wallet);
            int applyCalls = 0;
            var request = Request(SharedMetaWalletProfileFlow.DebitThenProfile);

            Assert.AreEqual(
                SharedMetaWalletProfileSagaStatus.Succeeded,
                saga.Execute(
                    request,
                    _ => true,
                    profile =>
                    {
                        applyCalls++;
                        profile.Merchant.PaidRefreshCount++;
                    }).Status);

            store.FailCommitNumber = store.CommitCount + 1;
            Assert.AreEqual(
                SharedMetaWalletProfileSagaStatus.RecoveryPending,
                saga.Complete(request).Status);

            Assert.AreEqual(
                SharedMetaWalletProfileSagaStatus.PresentationPending,
                saga.Execute(request, _ => true, _ => applyCalls++).Status);
            Assert.AreEqual(1, applyCalls);
            Assert.AreEqual(6, wallet.Balance);

            Assert.AreEqual(SharedMetaWalletProfileSagaStatus.Succeeded, saga.Complete(request).Status);
            Assert.AreEqual(
                SharedMetaWalletProfileSagaStatus.AlreadyCompleted,
                saga.Execute(request, _ => true, _ => applyCalls++).Status);
            Assert.AreEqual(1, applyCalls);
            Assert.AreEqual(6, wallet.Balance);
            AssertJournalState(store, ExtractionOperationState.Completed);
        }

        private static SharedMetaWalletProfileSagaRequest Request(SharedMetaWalletProfileFlow flow)
        {
            return new SharedMetaWalletProfileSagaRequest(
                TransactionId,
                OperationType,
                ResourceId,
                quantity: 4,
                PayloadHash,
                flow);
        }

        private static void AssertJournalState(
            FaultInjectingProfileStore store,
            ExtractionOperationState expected)
        {
            var entries = store.CurrentProfile.OperationJournal.Entries;
            Assert.AreEqual(1, entries.Count);
            Assert.IsTrue(entries[0].TryGetState(out var state));
            Assert.AreEqual(expected, state);
        }

        private sealed class FaultInjectingProfileStore : IExtractionProfileStore
        {
            private readonly ExtractionInMemoryProfileStore inner = new();

            public int FailCommitNumber { get; set; }
            public int CommitCount { get; private set; }
            public ExtractionProfileSaveData CurrentProfile => inner.Load().Profile;

            public ExtractionProfileLoadResult Load() => inner.Load();

            public ExtractionProfileCommitResult Commit(ExtractionProfileDraft draft)
            {
                CommitCount++;
                if (CommitCount == FailCommitNumber)
                {
                    return ExtractionProfileCommitResult.Failed(
                        ExtractionProfileCommitFailure.WriteFailed,
                        "Injected profile commit failure.",
                        inner.Load());
                }

                return inner.Commit(draft);
            }

            public ExtractionProfileSaveData LoadProfile() => inner.LoadProfile();

            public ExtractionProfileCommitResult SaveProfile(ExtractionProfileSaveData profile)
            {
                if (!inner.Load().TryCreateDraft(profile, out var draft))
                {
                    return ExtractionProfileCommitResult.Failed(
                        ExtractionProfileCommitFailure.InvalidDraft,
                        "Unable to create test draft.",
                        inner.Load());
                }

                return Commit(draft);
            }
        }

        private sealed class FaultInjectingWallet : ISharedMetaWallet
        {
            private readonly Dictionary<string, WalletReceipt> receipts = new();

            public FaultInjectingWallet(int balance)
            {
                Balance = balance;
            }

            public int Balance { get; private set; }
            public bool FailNextDebit { get; set; }
            public bool FailNextCredit { get; set; }

            public bool TryQuery(string resourceId, out SharedMetaWalletBalance balance)
            {
                balance = new SharedMetaWalletBalance(resourceId, Balance);
                return string.Equals(resourceId, ResourceId, StringComparison.Ordinal);
            }

            public SharedMetaWalletTransactionResult TryDebit(SharedMetaWalletTransactionRequest request)
            {
                if (FailNextDebit)
                {
                    FailNextDebit = false;
                    return Result(SharedMetaWalletTransactionStatus.StoreUnavailable);
                }

                if (!TryReplay(request, debit: true, out var replay)) return replay;
                if (Balance < request.Quantity)
                    return Result(SharedMetaWalletTransactionStatus.InsufficientFunds);

                Balance -= request.Quantity;
                receipts.Add(request.TransactionId, new WalletReceipt(request, debit: true));
                return Result(SharedMetaWalletTransactionStatus.Succeeded);
            }

            public SharedMetaWalletTransactionResult TryCredit(SharedMetaWalletTransactionRequest request)
            {
                if (FailNextCredit)
                {
                    FailNextCredit = false;
                    return Result(SharedMetaWalletTransactionStatus.StoreUnavailable);
                }

                if (!TryReplay(request, debit: false, out var replay)) return replay;
                Balance += request.Quantity;
                receipts.Add(request.TransactionId, new WalletReceipt(request, debit: false));
                return Result(SharedMetaWalletTransactionStatus.Succeeded);
            }

            private bool TryReplay(
                SharedMetaWalletTransactionRequest request,
                bool debit,
                out SharedMetaWalletTransactionResult result)
            {
                result = null;
                if (!receipts.TryGetValue(request.TransactionId, out var receipt)) return true;

                result = receipt.Matches(request, debit)
                    ? Result(SharedMetaWalletTransactionStatus.AlreadyApplied)
                    : Result(SharedMetaWalletTransactionStatus.Conflict);
                return false;
            }

            private SharedMetaWalletTransactionResult Result(SharedMetaWalletTransactionStatus status)
            {
                return new SharedMetaWalletTransactionResult(status, Balance);
            }
        }

        private sealed class WalletReceipt
        {
            private readonly string transactionId;
            private readonly int quantity;
            private readonly string payloadHash;
            private readonly bool debit;

            public WalletReceipt(SharedMetaWalletTransactionRequest request, bool debit)
            {
                transactionId = request.TransactionId;
                quantity = request.Quantity;
                payloadHash = request.PayloadHash;
                this.debit = debit;
            }

            public bool Matches(SharedMetaWalletTransactionRequest request, bool expectedDebit)
            {
                return request != null
                       && debit == expectedDebit
                       && string.Equals(transactionId, request.TransactionId, StringComparison.Ordinal)
                       && quantity == request.Quantity
                       && string.Equals(payloadHash, request.PayloadHash, StringComparison.Ordinal);
            }
        }
    }
}
