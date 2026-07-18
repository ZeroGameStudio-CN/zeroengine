using System;
using System.Globalization;

namespace POB.Extraction
{
    public enum SharedMetaWalletProfileFlow
    {
        DebitThenProfile = 0,
        ProfileThenCredit = 1
    }

    public enum SharedMetaWalletProfileSagaStatus
    {
        Succeeded = 0,
        PresentationPending = 1,
        AlreadyCompleted = 2,
        Compensated = 3,
        InsufficientFunds = 4,
        InvalidRequest = 5,
        StoreUnavailable = 6,
        Conflict = 7,
        DomainRejected = 8,
        RecoveryPending = 9
    }

    [Serializable]
    public sealed class SharedMetaWalletProfileSagaRequest
    {
        private const string JournalHashDomain =
            "zeroengine.extraction.shared-wallet-profile-saga:v1";
        private const string CompensationHashDomain =
            "zeroengine.extraction.shared-wallet-compensation:v1";

        public string TransactionId;
        public string OperationType;
        public string ResourceId;
        public int Quantity;
        public string PayloadHash;
        public SharedMetaWalletProfileFlow Flow;

        public SharedMetaWalletProfileSagaRequest(
            string transactionId,
            string operationType,
            string resourceId,
            int quantity,
            string payloadHash,
            SharedMetaWalletProfileFlow flow)
        {
            TransactionId = transactionId;
            OperationType = operationType;
            ResourceId = resourceId;
            Quantity = quantity;
            PayloadHash = payloadHash;
            Flow = flow;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(OperationType)
            && Enum.IsDefined(typeof(SharedMetaWalletProfileFlow), Flow)
            && CreateWalletRequest().IsValid;

        internal string CreateJournalPayloadHash()
        {
            return ExtractionStableHash.ComputeSha256(
                JournalHashDomain,
                PayloadHash,
                ((int)Flow).ToString(CultureInfo.InvariantCulture),
                ResourceId,
                Quantity.ToString(CultureInfo.InvariantCulture));
        }

        internal SharedMetaWalletTransactionRequest CreateWalletRequest()
        {
            return new SharedMetaWalletTransactionRequest(
                TransactionId,
                ResourceId,
                Quantity,
                PayloadHash);
        }

        internal SharedMetaWalletTransactionRequest CreateCompensationRequest()
        {
            string transactionId = ExtractionReceiptId.Create(
                TransactionId,
                "shared-wallet.compensation");
            string payloadHash = ExtractionStableHash.ComputeSha256(
                CompensationHashDomain,
                TransactionId,
                ResourceId,
                Quantity.ToString(CultureInfo.InvariantCulture),
                PayloadHash);
            return new SharedMetaWalletTransactionRequest(
                transactionId,
                ResourceId,
                Quantity,
                payloadHash);
        }
    }

    [Serializable]
    public sealed class SharedMetaWalletProfileSagaResult
    {
        public SharedMetaWalletProfileSagaStatus Status;
        public int Balance;

        public SharedMetaWalletProfileSagaResult(
            SharedMetaWalletProfileSagaStatus status,
            int balance)
        {
            Status = status;
            Balance = balance;
        }

        public bool IsSuccess =>
            Status == SharedMetaWalletProfileSagaStatus.Succeeded
            || Status == SharedMetaWalletProfileSagaStatus.PresentationPending
            || Status == SharedMetaWalletProfileSagaStatus.AlreadyCompleted;

        public bool NeedsPresentation =>
            Status == SharedMetaWalletProfileSagaStatus.Succeeded
            || Status == SharedMetaWalletProfileSagaStatus.PresentationPending;
    }

    /// <summary>
    /// Coordinates one profile mutation with one idempotent wallet receipt.
    /// Debit writes Prepared before charging. Credit commits the profile before paying.
    /// Compensation is itself persisted before the reversing wallet receipt is applied.
    /// </summary>
    public sealed class SharedMetaWalletProfileSaga
    {
        private readonly IExtractionProfileStore store;
        private readonly ISharedMetaWallet wallet;

        public SharedMetaWalletProfileSaga(
            IExtractionProfileStore store,
            ISharedMetaWallet wallet)
        {
            this.store = store;
            this.wallet = wallet;
        }

        public SharedMetaWalletProfileSagaResult Execute(
            SharedMetaWalletProfileSagaRequest request,
            Func<ExtractionProfileSaveData, bool> canApplyDomain,
            Action<ExtractionProfileSaveData> applyDomain)
        {
            if (!IsValid(request, canApplyDomain, applyDomain))
                return Result(SharedMetaWalletProfileSagaStatus.InvalidRequest, request);
            if (store == null || wallet == null)
                return Result(SharedMetaWalletProfileSagaStatus.StoreUnavailable, request);
            if (!TryOpen(request, out var draft, out var entry, out var action, out bool isNew, out var failure))
                return Result(failure, request);

            switch (action)
            {
                case ExtractionOperationReplayAction.ResumeCompensation:
                    return RecoverCompensation(request);
                case ExtractionOperationReplayAction.ReplayPresentation:
                    return ReplayCommitted(request);
                case ExtractionOperationReplayAction.NoOp:
                    return ResolveTerminal(entry, request);
                case ExtractionOperationReplayAction.ApplyDomain:
                    return request.Flow == SharedMetaWalletProfileFlow.DebitThenProfile
                        ? ExecuteDebit(request, canApplyDomain, applyDomain, draft, isNew)
                        : ExecuteCredit(request, canApplyDomain, applyDomain, draft, entry);
                default:
                    return Result(SharedMetaWalletProfileSagaStatus.Conflict, request);
            }
        }

        public SharedMetaWalletProfileSagaResult Complete(SharedMetaWalletProfileSagaRequest request)
        {
            if (request == null || !request.IsValid)
                return Result(SharedMetaWalletProfileSagaStatus.InvalidRequest, request);
            if (store == null || wallet == null)
                return Result(SharedMetaWalletProfileSagaStatus.StoreUnavailable, request);
            if (!TryOpen(request, out var draft, out var entry, out var action, out bool isNew, out var failure)
                || isNew)
            {
                return Result(isNew ? SharedMetaWalletProfileSagaStatus.Conflict : failure, request);
            }

            if (action == ExtractionOperationReplayAction.ResumeCompensation)
                return RecoverCompensation(request);
            if (action == ExtractionOperationReplayAction.NoOp)
                return ResolveTerminal(entry, request);
            if (action != ExtractionOperationReplayAction.ReplayPresentation)
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request);

            var walletResult = ApplyWallet(request);
            if (!walletResult.IsSuccess)
                return MapWalletFailure(walletResult, request, committedProfile: true);
            if (!ExtractionOperationJournalService.TryMarkCompleted(entry) || !Commit(draft))
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, walletResult.Balance);

            return Result(SharedMetaWalletProfileSagaStatus.Succeeded, request, walletResult.Balance);
        }

        private SharedMetaWalletProfileSagaResult ExecuteDebit(
            SharedMetaWalletProfileSagaRequest request,
            Func<ExtractionProfileSaveData, bool> canApplyDomain,
            Action<ExtractionProfileSaveData> applyDomain,
            ExtractionProfileDraft preparedDraft,
            bool isNew)
        {
            if (isNew)
            {
                if (!CanApply(canApplyDomain, preparedDraft.Profile))
                    return Result(SharedMetaWalletProfileSagaStatus.DomainRejected, request);
                if (!Commit(preparedDraft))
                    return Result(SharedMetaWalletProfileSagaStatus.StoreUnavailable, request);
            }

            var walletResult = wallet.TryDebit(request.CreateWalletRequest());
            if (!walletResult.IsSuccess)
            {
                return walletResult.Status == SharedMetaWalletTransactionStatus.StoreUnavailable
                    ? MapWalletFailure(walletResult, request, committedProfile: false)
                    : TerminateRejectedDebit(request, walletResult);
            }

            if (!TryOpen(request, out var draft, out var entry, out var action, out _, out _))
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, walletResult.Balance);
            if (action == ExtractionOperationReplayAction.ResumeCompensation)
                return RecoverCompensation(request);
            if (action == ExtractionOperationReplayAction.ReplayPresentation)
                return Result(SharedMetaWalletProfileSagaStatus.PresentationPending, request, walletResult.Balance);
            if (action == ExtractionOperationReplayAction.NoOp)
                return ResolveTerminal(entry, request, walletResult.Balance);
            if (action != ExtractionOperationReplayAction.ApplyDomain)
                return Result(SharedMetaWalletProfileSagaStatus.Conflict, request, walletResult.Balance);

            if (!CanApply(canApplyDomain, draft.Profile))
                return BeginCompensation(request, walletResult.Balance);

            try
            {
                applyDomain(draft.Profile);
            }
            catch (Exception)
            {
                return BeginCompensation(request, walletResult.Balance);
            }

            if (!ExtractionOperationJournalService.TryMarkCommitted(entry) || !Commit(draft))
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, walletResult.Balance);

            return Result(SharedMetaWalletProfileSagaStatus.Succeeded, request, walletResult.Balance);
        }

        private SharedMetaWalletProfileSagaResult ExecuteCredit(
            SharedMetaWalletProfileSagaRequest request,
            Func<ExtractionProfileSaveData, bool> canApplyDomain,
            Action<ExtractionProfileSaveData> applyDomain,
            ExtractionProfileDraft draft,
            ExtractionOperationJournalEntry entry)
        {
            if (!wallet.TryQuery(request.ResourceId, out _))
                return Result(SharedMetaWalletProfileSagaStatus.StoreUnavailable, request);
            if (!CanApply(canApplyDomain, draft.Profile))
                return Result(SharedMetaWalletProfileSagaStatus.DomainRejected, request);

            try
            {
                applyDomain(draft.Profile);
            }
            catch (Exception)
            {
                return Result(SharedMetaWalletProfileSagaStatus.DomainRejected, request);
            }

            if (!ExtractionOperationJournalService.TryMarkCommitted(entry) || !Commit(draft))
                return Result(SharedMetaWalletProfileSagaStatus.StoreUnavailable, request);

            var walletResult = wallet.TryCredit(request.CreateWalletRequest());
            return walletResult.IsSuccess
                ? Result(SharedMetaWalletProfileSagaStatus.Succeeded, request, walletResult.Balance)
                : MapWalletFailure(walletResult, request, committedProfile: true);
        }

        private SharedMetaWalletProfileSagaResult ReplayCommitted(
            SharedMetaWalletProfileSagaRequest request)
        {
            var walletResult = ApplyWallet(request);
            return walletResult.IsSuccess
                ? Result(SharedMetaWalletProfileSagaStatus.PresentationPending, request, walletResult.Balance)
                : MapWalletFailure(walletResult, request, committedProfile: true);
        }

        private SharedMetaWalletProfileSagaResult TerminateRejectedDebit(
            SharedMetaWalletProfileSagaRequest request,
            SharedMetaWalletTransactionResult walletResult)
        {
            if (!TryOpen(request, out var draft, out var entry, out var action, out _, out _)
                || action != ExtractionOperationReplayAction.ApplyDomain
                || !ExtractionOperationJournalService.TryMarkCompensated(entry)
                || !Commit(draft))
            {
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, walletResult.Balance);
            }

            return MapWalletFailure(walletResult, request, committedProfile: false);
        }

        private SharedMetaWalletProfileSagaResult BeginCompensation(
            SharedMetaWalletProfileSagaRequest request,
            int balance)
        {
            if (!TryOpen(request, out var draft, out var entry, out var action, out _, out _))
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, balance);
            if (action == ExtractionOperationReplayAction.ResumeCompensation)
                return RecoverCompensation(request);
            if (action == ExtractionOperationReplayAction.NoOp)
                return ResolveTerminal(entry, request, balance);
            if (action != ExtractionOperationReplayAction.ApplyDomain)
                return Result(SharedMetaWalletProfileSagaStatus.Conflict, request, balance);
            if (!ExtractionOperationJournalService.TryMarkCompensationPending(entry) || !Commit(draft))
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, balance);

            return RecoverCompensation(request);
        }

        private SharedMetaWalletProfileSagaResult RecoverCompensation(
            SharedMetaWalletProfileSagaRequest request)
        {
            var walletResult = wallet.TryCredit(request.CreateCompensationRequest());
            if (!walletResult.IsSuccess)
            {
                return Result(
                    walletResult.Status == SharedMetaWalletTransactionStatus.Conflict
                        ? SharedMetaWalletProfileSagaStatus.Conflict
                        : SharedMetaWalletProfileSagaStatus.RecoveryPending,
                    request,
                    walletResult.Balance);
            }

            if (!TryOpen(request, out var draft, out var entry, out var action, out _, out _))
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, walletResult.Balance);
            if (action == ExtractionOperationReplayAction.NoOp)
                return ResolveTerminal(entry, request, walletResult.Balance);
            if (action != ExtractionOperationReplayAction.ResumeCompensation
                || !ExtractionOperationJournalService.TryMarkCompensated(entry)
                || !Commit(draft))
            {
                return Result(SharedMetaWalletProfileSagaStatus.RecoveryPending, request, walletResult.Balance);
            }

            return Result(SharedMetaWalletProfileSagaStatus.Compensated, request, walletResult.Balance);
        }

        private SharedMetaWalletTransactionResult ApplyWallet(
            SharedMetaWalletProfileSagaRequest request)
        {
            return request.Flow == SharedMetaWalletProfileFlow.DebitThenProfile
                ? wallet.TryDebit(request.CreateWalletRequest())
                : wallet.TryCredit(request.CreateWalletRequest());
        }

        private SharedMetaWalletProfileSagaResult MapWalletFailure(
            SharedMetaWalletTransactionResult walletResult,
            SharedMetaWalletProfileSagaRequest request,
            bool committedProfile)
        {
            SharedMetaWalletProfileSagaStatus status;
            switch (walletResult.Status)
            {
                case SharedMetaWalletTransactionStatus.InsufficientFunds:
                    status = committedProfile
                        ? SharedMetaWalletProfileSagaStatus.Conflict
                        : SharedMetaWalletProfileSagaStatus.InsufficientFunds;
                    break;
                case SharedMetaWalletTransactionStatus.InvalidRequest:
                    status = SharedMetaWalletProfileSagaStatus.InvalidRequest;
                    break;
                case SharedMetaWalletTransactionStatus.Conflict:
                    status = SharedMetaWalletProfileSagaStatus.Conflict;
                    break;
                default:
                    status = committedProfile
                        ? SharedMetaWalletProfileSagaStatus.RecoveryPending
                        : SharedMetaWalletProfileSagaStatus.StoreUnavailable;
                    break;
            }

            return Result(status, request, walletResult.Balance);
        }

        private bool TryOpen(
            SharedMetaWalletProfileSagaRequest request,
            out ExtractionProfileDraft draft,
            out ExtractionOperationJournalEntry entry,
            out ExtractionOperationReplayAction action,
            out bool isNew,
            out SharedMetaWalletProfileSagaStatus failure)
        {
            draft = null;
            entry = null;
            action = ExtractionOperationReplayAction.Conflict;
            isNew = false;
            failure = SharedMetaWalletProfileSagaStatus.StoreUnavailable;

            var loaded = store?.Load();
            if (loaded == null || !loaded.TryCreateDraft(out draft)) return false;

            int entryCount = draft.Profile.OperationJournal.Entries.Count;
            if (!ExtractionOperationJournalService.TryPrepare(
                    draft.Profile.OperationJournal,
                    request.TransactionId,
                    request.OperationType,
                    request.CreateJournalPayloadHash(),
                    out entry,
                    out action))
            {
                failure = SharedMetaWalletProfileSagaStatus.Conflict;
                return false;
            }

            isNew = draft.Profile.OperationJournal.Entries.Count != entryCount;
            return true;
        }

        private bool Commit(ExtractionProfileDraft draft)
        {
            return draft != null && store?.Commit(draft)?.Success == true;
        }

        private SharedMetaWalletProfileSagaResult ResolveTerminal(
            ExtractionOperationJournalEntry entry,
            SharedMetaWalletProfileSagaRequest request,
            int? balance = null)
        {
            if (entry != null && entry.TryGetState(out var state))
            {
                if (state == ExtractionOperationState.Completed)
                    return Result(SharedMetaWalletProfileSagaStatus.AlreadyCompleted, request, balance);
                if (state == ExtractionOperationState.Compensated)
                    return Result(SharedMetaWalletProfileSagaStatus.Compensated, request, balance);
            }

            return Result(SharedMetaWalletProfileSagaStatus.Conflict, request, balance);
        }

        private SharedMetaWalletProfileSagaResult Result(
            SharedMetaWalletProfileSagaStatus status,
            SharedMetaWalletProfileSagaRequest request,
            int? balance = null)
        {
            if (!balance.HasValue
                && request != null
                && wallet != null
                && wallet.TryQuery(request.ResourceId, out var current))
            {
                balance = current.Balance;
            }

            return new SharedMetaWalletProfileSagaResult(status, balance ?? 0);
        }

        private bool IsValid(
            SharedMetaWalletProfileSagaRequest request,
            Func<ExtractionProfileSaveData, bool> canApplyDomain,
            Action<ExtractionProfileSaveData> applyDomain)
        {
            return request != null
                   && request.IsValid
                   && canApplyDomain != null
                   && applyDomain != null;
        }

        private static bool CanApply(
            Func<ExtractionProfileSaveData, bool> canApplyDomain,
            ExtractionProfileSaveData profile)
        {
            try
            {
                return canApplyDomain(profile);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
