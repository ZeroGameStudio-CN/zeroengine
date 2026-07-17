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
}
