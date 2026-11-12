using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    // Progression is persisted independently from legacy character abilities and item receipts.
    // Node definitions, purchase rules and effect projection belong to the consumer adapter.
    [Serializable]
    public class ExtractionTalentProgressionState
    {
        public List<ExtractionTalentNodeLevel> NodeLevels = new();
        public string TargetNodeId = string.Empty;
        public List<ExtractionTalentPurchaseReceipt> PurchaseReceipts = new();

        internal void EnsureInitialized()
        {
            NodeLevels ??= new List<ExtractionTalentNodeLevel>();
            TargetNodeId ??= string.Empty;
            PurchaseReceipts ??= new List<ExtractionTalentPurchaseReceipt>();
        }
    }

    [Serializable]
    public class ExtractionTalentNodeLevel
    {
        public string NodeId;
        public int Level;
    }

    [Serializable]
    public class ExtractionTalentPurchaseReceipt
    {
        public string OperationId;
        public string NodeId;
        public int ExpectedLevel;
        public int TargetLevel;
        public string ConfigVersion;
    }
}
