using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public enum ExtractionOperationState
    {
        Prepared = 0,
        Committed = 1,
        Completed = 2,
        Compensated = 3,
        CompensationPending = 4
    }

    public enum ExtractionOperationReplayAction
    {
        Conflict = 0,
        ApplyDomain = 1,
        ReplayPresentation = 2,
        NoOp = 3,
        ResumeCompensation = 4
    }

    [Serializable]
    public class ExtractionOperationJournal
    {
        public List<ExtractionOperationJournalEntry> Entries = new();

        internal void EnsureInitialized()
        {
            Entries ??= new List<ExtractionOperationJournalEntry>();
        }
    }

    [Serializable]
    public class ExtractionOperationJournalEntry
    {
        public string OperationId;
        public string OperationType;
        public string State;
        public string PayloadHash;

        public ExtractionOperationJournalEntry(
            string operationId,
            string operationType,
            string state,
            string payloadHash)
        {
            OperationId = operationId;
            OperationType = operationType;
            State = state;
            PayloadHash = payloadHash;
        }

        public ExtractionOperationJournalEntry(
            string operationId,
            string operationType,
            ExtractionOperationState state,
            string payloadHash)
            : this(
                operationId,
                operationType,
                ExtractionOperationStateCodec.ToSerializedValue(state),
                payloadHash)
        {
        }

        public bool TryGetState(out ExtractionOperationState state)
        {
            return ExtractionOperationStateCodec.TryParse(State, out state);
        }
    }

    public static class ExtractionOperationJournalService
    {
        public static bool TryPrepare(
            ExtractionOperationJournal journal,
            string operationId,
            string operationType,
            string payloadHash,
            out ExtractionOperationJournalEntry entry,
            out ExtractionOperationReplayAction replayAction)
        {
            entry = null;
            replayAction = ExtractionOperationReplayAction.Conflict;
            if (journal == null
                || string.IsNullOrEmpty(operationId)
                || string.IsNullOrEmpty(operationType)
                || string.IsNullOrEmpty(payloadHash))
            {
                return false;
            }

            journal.EnsureInitialized();
            if (!TryFindUniqueEntry(journal, operationId, out entry))
                return false;

            if (entry == null)
            {
                entry = new ExtractionOperationJournalEntry(
                    operationId,
                    operationType,
                    ExtractionOperationState.Prepared,
                    payloadHash);
                journal.Entries.Add(entry);
                replayAction = ExtractionOperationReplayAction.ApplyDomain;
                return true;
            }

            if (!string.Equals(entry.OperationType, operationType, StringComparison.Ordinal)
                || !string.Equals(entry.PayloadHash, payloadHash, StringComparison.Ordinal)
                || !entry.TryGetState(out var state))
            {
                return false;
            }

            switch (state)
            {
                case ExtractionOperationState.Prepared:
                    replayAction = ExtractionOperationReplayAction.ApplyDomain;
                    return true;
                case ExtractionOperationState.Committed:
                    replayAction = ExtractionOperationReplayAction.ReplayPresentation;
                    return true;
                case ExtractionOperationState.Completed:
                case ExtractionOperationState.Compensated:
                    replayAction = ExtractionOperationReplayAction.NoOp;
                    return true;
                case ExtractionOperationState.CompensationPending:
                    replayAction = ExtractionOperationReplayAction.ResumeCompensation;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryMarkCommitted(ExtractionOperationJournalEntry entry)
        {
            if (entry == null || !entry.TryGetState(out var state)) return false;
            if (state == ExtractionOperationState.Compensated) return false;
            if (state == ExtractionOperationState.Committed || state == ExtractionOperationState.Completed)
                return true;
            if (state != ExtractionOperationState.Prepared) return false;

            entry.State = ExtractionOperationStateCodec.ToSerializedValue(ExtractionOperationState.Committed);
            return true;
        }

        public static bool TryMarkCompleted(ExtractionOperationJournalEntry entry)
        {
            if (entry == null || !entry.TryGetState(out var state)) return false;
            if (state == ExtractionOperationState.Completed) return true;
            if (state != ExtractionOperationState.Committed) return false;

            entry.State = ExtractionOperationStateCodec.ToSerializedValue(ExtractionOperationState.Completed);
            return true;
        }

        public static bool TryMarkCompensated(ExtractionOperationJournalEntry entry)
        {
            if (entry == null || !entry.TryGetState(out var state)) return false;
            if (state == ExtractionOperationState.Compensated) return true;
            if (state == ExtractionOperationState.Completed) return false;
            if (state != ExtractionOperationState.Prepared
                && state != ExtractionOperationState.Committed
                && state != ExtractionOperationState.CompensationPending)
                return false;

            entry.State = ExtractionOperationStateCodec.ToSerializedValue(ExtractionOperationState.Compensated);
            return true;
        }

        public static bool TryMarkCompensationPending(ExtractionOperationJournalEntry entry)
        {
            if (entry == null || !entry.TryGetState(out var state)) return false;
            if (state == ExtractionOperationState.CompensationPending) return true;
            if (state != ExtractionOperationState.Prepared && state != ExtractionOperationState.Committed)
                return false;

            entry.State = ExtractionOperationStateCodec.ToSerializedValue(ExtractionOperationState.CompensationPending);
            return true;
        }

        private static bool TryFindUniqueEntry(
            ExtractionOperationJournal journal,
            string operationId,
            out ExtractionOperationJournalEntry result)
        {
            result = null;
            foreach (var candidate in journal.Entries)
            {
                if (candidate == null
                    || !string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (result != null)
                {
                    result = null;
                    return false;
                }

                result = candidate;
            }

            return true;
        }
    }

    internal static class ExtractionOperationStateCodec
    {
        internal static string ToSerializedValue(ExtractionOperationState state)
        {
            switch (state)
            {
                case ExtractionOperationState.Prepared:
                    return "Prepared";
                case ExtractionOperationState.Committed:
                    return "Committed";
                case ExtractionOperationState.Completed:
                    return "Completed";
                case ExtractionOperationState.Compensated:
                    return "Compensated";
                case ExtractionOperationState.CompensationPending:
                    return "CompensationPending";
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        internal static bool TryParse(string value, out ExtractionOperationState state)
        {
            switch (value)
            {
                case "Prepared":
                    state = ExtractionOperationState.Prepared;
                    return true;
                case "Committed":
                    state = ExtractionOperationState.Committed;
                    return true;
                case "Completed":
                    state = ExtractionOperationState.Completed;
                    return true;
                case "Compensated":
                    state = ExtractionOperationState.Compensated;
                    return true;
                case "CompensationPending":
                    state = ExtractionOperationState.CompensationPending;
                    return true;
                default:
                    state = default;
                    return false;
            }
        }
    }
}
