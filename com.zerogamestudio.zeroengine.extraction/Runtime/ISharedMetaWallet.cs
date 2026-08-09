using System;

namespace POB.Extraction
{
    public enum SharedMetaWalletTransactionStatus
    {
        Succeeded = 0,
        AlreadyApplied = 1,
        InsufficientFunds = 2,
        InvalidRequest = 3,
        StoreUnavailable = 4,
        Conflict = 5
    }

    [Serializable]
    public sealed class SharedMetaWalletBalance
    {
        public string ResourceId;
        public int Balance;

        public SharedMetaWalletBalance(string resourceId, int balance)
        {
            ResourceId = resourceId;
            Balance = balance;
        }
    }

    [Serializable]
    public sealed class SharedMetaWalletTransactionRequest
    {
        public string TransactionId;
        public string ResourceId;
        public int Quantity;
        public string PayloadHash;

        public SharedMetaWalletTransactionRequest(
            string transactionId,
            string resourceId,
            int quantity,
            string payloadHash)
        {
            TransactionId = transactionId;
            ResourceId = resourceId;
            Quantity = quantity;
            PayloadHash = payloadHash;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(TransactionId)
            && !string.IsNullOrEmpty(ResourceId)
            && Quantity > 0
            && !string.IsNullOrEmpty(PayloadHash);
    }

    [Serializable]
    public sealed class SharedMetaWalletTransactionResult
    {
        public SharedMetaWalletTransactionStatus Status;
        public int Balance;
        public string Message;

        public bool IsSuccess =>
            Status == SharedMetaWalletTransactionStatus.Succeeded
            || Status == SharedMetaWalletTransactionStatus.AlreadyApplied;

        public SharedMetaWalletTransactionResult(
            SharedMetaWalletTransactionStatus status,
            int balance,
            string message = null)
        {
            Status = status;
            Balance = balance;
            Message = message;
        }
    }

    public interface ISharedMetaWallet
    {
        bool TryQuery(string resourceId, out SharedMetaWalletBalance balance);

        SharedMetaWalletTransactionResult TryDebit(SharedMetaWalletTransactionRequest request);

        SharedMetaWalletTransactionResult TryCredit(SharedMetaWalletTransactionRequest request);
    }
}
