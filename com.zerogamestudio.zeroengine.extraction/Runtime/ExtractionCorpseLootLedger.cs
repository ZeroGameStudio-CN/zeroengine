using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionCorpseLootLedger
    {
        public List<ExtractionCorpseLootRecord> Records = new();

        public bool RegisterCorpse(string corpseId, ExtractionLostRunSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(corpseId) || snapshot == null) return false;
            if (string.IsNullOrEmpty(snapshot.SnapshotId)) return false;
            if (FindRecord(corpseId) != null) return false;

            var record = new ExtractionCorpseLootRecord(corpseId, snapshot.SnapshotId, snapshot.MapId)
            {
                DeathRegion = snapshot.DeathRegion,
                CorpseSeed = snapshot.CorpseSeed
            };
            if (snapshot.LostItemInstanceIds != null)
            {
                foreach (var itemInstanceId in snapshot.LostItemInstanceIds)
                {
                    if (string.IsNullOrEmpty(itemInstanceId)) continue;
                    if (Contains(record.ItemInstanceIds, itemInstanceId)) continue;
                    record.ItemInstanceIds.Add(itemInstanceId);
                }
            }

            Records.Add(record);
            return true;
        }

        public bool ContainsCorpse(string corpseId)
        {
            return FindRecord(corpseId) != null;
        }

        public bool TryGetRecord(string corpseId, out ExtractionCorpseLootRecord record)
        {
            record = FindRecord(corpseId);
            return record != null;
        }

        public bool TryGetAvailableItemIds(string corpseId, List<string> results)
        {
            if (results == null) return false;
            results.Clear();

            var record = FindRecord(corpseId);
            if (record == null) return false;

            foreach (var itemInstanceId in record.ItemInstanceIds)
            {
                if (Contains(record.LootedItemInstanceIds, itemInstanceId)) continue;
                results.Add(itemInstanceId);
            }

            return results.Count > 0;
        }

        public bool IsItemAvailable(string corpseId, string itemInstanceId)
        {
            var record = FindRecord(corpseId);
            if (record == null) return false;
            if (!Contains(record.ItemInstanceIds, itemInstanceId)) return false;
            return !Contains(record.LootedItemInstanceIds, itemInstanceId);
        }

        public bool TryMarkLooted(string corpseId, string itemInstanceId)
        {
            if (string.IsNullOrEmpty(itemInstanceId)) return false;

            var record = FindRecord(corpseId);
            if (record == null) return false;
            if (!Contains(record.ItemInstanceIds, itemInstanceId)) return false;
            if (Contains(record.LootedItemInstanceIds, itemInstanceId)) return false;

            record.LootedItemInstanceIds.Add(itemInstanceId);
            return true;
        }

        private ExtractionCorpseLootRecord FindRecord(string corpseId)
        {
            if (string.IsNullOrEmpty(corpseId)) return null;

            foreach (var record in Records)
            {
                if (record != null && record.CorpseId == corpseId) return record;
            }

            return null;
        }

        private static bool Contains(List<string> values, string value)
        {
            if (values == null || string.IsNullOrEmpty(value)) return false;

            foreach (var candidate in values)
            {
                if (candidate == value) return true;
            }

            return false;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionCorpseLootRecord
    {
        public string CorpseId;
        public string SnapshotId;
        public string MapId;
        public string DeathRegion;
        public int CorpseSeed;
        public List<string> ItemInstanceIds = new();
        public List<string> LootedItemInstanceIds = new();

        public ExtractionCorpseLootRecord(string corpseId, string snapshotId, string mapId)
        {
            CorpseId = corpseId;
            SnapshotId = snapshotId;
            MapId = mapId;
        }
    }
}
