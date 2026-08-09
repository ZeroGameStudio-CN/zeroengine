using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionMedicalTreatmentLedger
    {
        public List<ExtractionMedicalTreatmentRecord> Entries = new();

        public int Count => Entries?.Count ?? 0;

        public bool Record(string treatmentId, string conditionId)
        {
            if (string.IsNullOrEmpty(treatmentId) || string.IsNullOrEmpty(conditionId)) return false;

            Entries ??= new List<ExtractionMedicalTreatmentRecord>();
            Entries.Add(new ExtractionMedicalTreatmentRecord(treatmentId, conditionId));
            return true;
        }

        public bool HasTreatedCondition(string conditionId)
        {
            if (string.IsNullOrEmpty(conditionId) || Entries == null) return false;
            foreach (var entry in Entries)
            {
                if (entry != null && entry.ConditionId == conditionId) return true;
            }
            return false;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionMedicalTreatmentRecord
    {
        public string TreatmentId;
        public string ConditionId;

        public ExtractionMedicalTreatmentRecord(string treatmentId, string conditionId)
        {
            TreatmentId = treatmentId;
            ConditionId = conditionId;
        }
    }
}
