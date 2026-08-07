using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionOwnershipLedger
    {
        public List<ExtractionOwnershipEntry> Entries = new();

        public int Count => Entries.Count;

        public bool Register(string itemInstanceId, ExtractionInventoryContainerType container)
        {
            return Register(itemInstanceId, container, null, null);
        }

        public bool Register(
            string itemInstanceId,
            ExtractionInventoryContainerType container,
            string locationSubtype,
            string locationId)
        {
            if (string.IsNullOrEmpty(itemInstanceId)) return false;
            if (FindEntry(itemInstanceId) != null) return false;
            Entries.Add(new ExtractionOwnershipEntry(itemInstanceId, container, locationSubtype, locationId));
            return true;
        }

        public bool TryGetContainer(string itemInstanceId, out ExtractionInventoryContainerType container)
        {
            var entry = FindEntry(itemInstanceId);
            if (entry == null)
            {
                container = default;
                return false;
            }

            container = entry.Container;
            return true;
        }

        public ExtractionInventoryContainerType GetRequiredContainer(string itemInstanceId)
        {
            if (!TryGetContainer(itemInstanceId, out var container))
                throw new InvalidOperationException($"Extraction item '{itemInstanceId}' is not tracked.");
            return container;
        }

        public bool TryMove(
            string itemInstanceId,
            ExtractionInventoryContainerType expected,
            ExtractionInventoryContainerType target)
        {
            return TryMove(itemInstanceId, expected, target, null, null);
        }

        public bool TryMove(
            string itemInstanceId,
            ExtractionInventoryContainerType expected,
            ExtractionInventoryContainerType target,
            string targetLocationSubtype,
            string targetLocationId)
        {
            var entry = FindEntry(itemInstanceId);
            if (entry == null) return false;
            if (entry.Container != expected) return false;
            entry.Container = target;
            entry.LocationSubtype = targetLocationSubtype;
            entry.LocationId = targetLocationId;
            return true;
        }

        public bool TryRemove(string itemInstanceId)
        {
            if (string.IsNullOrEmpty(itemInstanceId)) return false;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].ItemInstanceId != itemInstanceId) continue;
                Entries.RemoveAt(i);
                return true;
            }

            return false;
        }

        private ExtractionOwnershipEntry FindEntry(string itemInstanceId)
        {
            foreach (var entry in Entries)
            {
                if (entry.ItemInstanceId == itemInstanceId) return entry;
            }

            return null;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionOwnershipEntry
    {
        public string ItemInstanceId;
        public ExtractionInventoryContainerType Container;
        public string LocationSubtype;
        public string LocationId;

        public ExtractionOwnershipEntry(string itemInstanceId, ExtractionInventoryContainerType container)
            : this(itemInstanceId, container, null, null)
        {
        }

        public ExtractionOwnershipEntry(
            string itemInstanceId,
            ExtractionInventoryContainerType container,
            string locationSubtype,
            string locationId)
        {
            ItemInstanceId = itemInstanceId;
            Container = container;
            LocationSubtype = locationSubtype;
            LocationId = locationId;
        }
    }
}
