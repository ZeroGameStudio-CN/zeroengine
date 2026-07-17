using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionItemRegistry
    {
        public List<ExtractionItemInstance> Entries = new();

        public bool Register(ExtractionItemInstance item)
        {
            if (item == null || string.IsNullOrEmpty(item.InstanceId) || string.IsNullOrEmpty(item.DefinitionId))
                return false;
            if (FindEntry(item.InstanceId) != null) return false;

            Entries.Add(new ExtractionItemInstance(item));
            return true;
        }

        public bool TryGet(string itemInstanceId, out ExtractionItemInstance item)
        {
            item = FindEntry(itemInstanceId);
            return item != null;
        }

        public bool TryRemove(string itemInstanceId)
        {
            if (string.IsNullOrEmpty(itemInstanceId)) return false;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].InstanceId != itemInstanceId) continue;
                Entries.RemoveAt(i);
                return true;
            }

            return false;
        }

        private ExtractionItemInstance FindEntry(string itemInstanceId)
        {
            foreach (var entry in Entries)
            {
                if (entry.InstanceId == itemInstanceId) return entry;
            }

            return null;
        }
    }
}
