using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionOperationJournalContractTests
    {
        private const string OperationId =
            "operation:v1:e64d8651524edb2522f319234d171c7ef6afc721a8381ac47f7026bb2eb3bc98";

        [Test]
        public void StableHash_FixedVector_MatchesCrossProcessContract()
        {
            string hash = ExtractionStableHash.ComputeSha256(
                "loot.spawn-point",
                "42",
                "loot-a");
            int value = ExtractionStableHash.ComputeInt32(
                "loot.spawn-point",
                "42",
                "loot-a");

            Assert.AreEqual(
                "sha256:c1df1bcb0c34434adde20a64014958bbe6883d068d927cc32f40cab96bd8fb06",
                hash);
            Assert.AreEqual(-1042342965, value);
        }

        [Test]
        public void StableIds_FixedVector_MatchesCrossProcessContract()
        {
            string operationId = ExtractionOperationId.Create(
                "raid.failure",
                "raid-001",
                "PlayerDeath");
            string receiptId = ExtractionReceiptId.Create(operationId, "snapshot");

            Assert.AreEqual(OperationId, operationId);
            Assert.AreEqual(
                "receipt:v1:5231eaaa8cc82434014c196cb047f1a5799bc6e3c68cdc5385646757bd1399e1",
                receiptId);
        }

        [Test]
        public void StableHash_NullEmptyAndFieldBoundaries_DoNotCollide()
        {
            string nullHash = ExtractionStableHash.ComputeSha256("test", null, "a");
            string emptyHash = ExtractionStableHash.ComputeSha256("test", string.Empty, "a");
            string splitHash = ExtractionStableHash.ComputeSha256("test", "ab", "c");
            string joinedHash = ExtractionStableHash.ComputeSha256("test", "a", "bc");

            Assert.AreNotEqual(nullHash, emptyHash);
            Assert.AreNotEqual(splitHash, joinedHash);
        }

        [Test]
        public void Journal_CommittedRoundTrip_ReplaysPresentationWithoutAddingDuplicateEntry()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            string payloadHash = ExtractionStableHash.ComputeSha256("operation.payload", "payload-a");

            Assert.IsTrue(ExtractionOperationJournalService.TryPrepare(
                profile.OperationJournal,
                OperationId,
                "raid.failure",
                payloadHash,
                out var entry,
                out var firstAction));
            Assert.AreEqual(ExtractionOperationReplayAction.ApplyDomain, firstAction);
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCommitted(entry));

            string json = ExtractionProfileSerialization.ToJson(profile);
            ExtractionProfileSaveData reloaded = ExtractionProfileSerialization.FromJson(json);

            Assert.IsTrue(ExtractionOperationJournalService.TryPrepare(
                reloaded.OperationJournal,
                OperationId,
                "raid.failure",
                payloadHash,
                out var replayedEntry,
                out var replayAction));
            Assert.AreEqual(ExtractionOperationReplayAction.ReplayPresentation, replayAction);
            Assert.AreEqual(1, reloaded.OperationJournal.Entries.Count);
            Assert.IsTrue(replayedEntry.TryGetState(out var state));
            Assert.AreEqual(ExtractionOperationState.Committed, state);
        }

        [Test]
        public void Journal_CompletedReceipt_ReplayIsNoOp()
        {
            var journal = new ExtractionOperationJournal();
            const string payloadHash = "sha256:payload";
            Assert.IsTrue(ExtractionOperationJournalService.TryPrepare(
                journal,
                OperationId,
                "raid.failure",
                payloadHash,
                out var entry,
                out _));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCommitted(entry));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCompleted(entry));

            Assert.IsTrue(ExtractionOperationJournalService.TryPrepare(
                journal,
                OperationId,
                "raid.failure",
                payloadHash,
                out var replayedEntry,
                out var replayAction));
            Assert.AreEqual(ExtractionOperationReplayAction.NoOp, replayAction);
            Assert.IsTrue(replayedEntry.TryGetState(out var state));
            Assert.AreEqual(ExtractionOperationState.Completed, state);
            Assert.AreEqual(1, journal.Entries.Count);
        }

        [Test]
        public void Journal_SameOperationWithDifferentPayload_FailsClosed()
        {
            var journal = new ExtractionOperationJournal();
            Assert.IsTrue(ExtractionOperationJournalService.TryPrepare(
                journal,
                OperationId,
                "raid.failure",
                "sha256:first",
                out var entry,
                out _));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCommitted(entry));

            bool prepared = ExtractionOperationJournalService.TryPrepare(
                journal,
                OperationId,
                "raid.failure",
                "sha256:different",
                out _,
                out var replayAction);

            Assert.IsFalse(prepared);
            Assert.AreEqual(ExtractionOperationReplayAction.Conflict, replayAction);
            Assert.AreEqual(1, journal.Entries.Count);
            Assert.IsTrue(entry.TryGetState(out var state));
            Assert.AreEqual(ExtractionOperationState.Committed, state);
        }

        [Test]
        public void Journal_StateMachine_IsForwardOnlyAndIdempotent()
        {
            var entry = new ExtractionOperationJournalEntry(
                OperationId,
                "raid.failure",
                ExtractionOperationState.Prepared,
                "sha256:payload");

            Assert.IsFalse(ExtractionOperationJournalService.TryMarkCompleted(entry));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCommitted(entry));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCommitted(entry));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCompleted(entry));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCompleted(entry));
            Assert.IsFalse(ExtractionOperationJournalService.TryMarkCompensated(entry));
            Assert.IsTrue(entry.TryGetState(out var state));
            Assert.AreEqual(ExtractionOperationState.Completed, state);
        }

        [Test]
        public void Journal_CompensatedReceipt_IsTerminalAndSerializedValuesStayStable()
        {
            Assert.AreEqual(0, (int)ExtractionOperationState.Prepared);
            Assert.AreEqual(1, (int)ExtractionOperationState.Committed);
            Assert.AreEqual(2, (int)ExtractionOperationState.Completed);
            Assert.AreEqual(3, (int)ExtractionOperationState.Compensated);
            Assert.AreEqual(4, (int)ExtractionOperationState.CompensationPending);

            var journal = new ExtractionOperationJournal();
            Assert.IsTrue(ExtractionOperationJournalService.TryPrepare(
                journal,
                OperationId,
                "shared-wallet.debit",
                "sha256:wallet-payload",
                out var entry,
                out _));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCommitted(entry));
            Assert.IsTrue(ExtractionOperationJournalService.TryMarkCompensated(entry));

            Assert.IsTrue(ExtractionOperationJournalService.TryPrepare(
                journal,
                OperationId,
                "shared-wallet.debit",
                "sha256:wallet-payload",
                out var replayedEntry,
                out var replayAction));
            Assert.AreEqual(ExtractionOperationReplayAction.NoOp, replayAction);
            Assert.IsTrue(replayedEntry.TryGetState(out var state));
            Assert.AreEqual(ExtractionOperationState.Compensated, state);
        }
    }
}
