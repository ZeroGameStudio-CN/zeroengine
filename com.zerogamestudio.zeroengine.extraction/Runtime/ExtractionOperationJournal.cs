using System;
using System.Collections.Generic;

namespace POB.Extraction
{
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
    }
}
