using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    public enum ExtractionRecoveryItemState
    {
        Recoverable,
        Recovered,
        Destroyed,
        Expired
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionRecoveryLedger
    {
        public List<ExtractionRecoveryEntry> Entries = new();

        public void RegisterLostItem(string snapshotId, string itemInstanceId)
        {
            if (string.IsNullOrEmpty(snapshotId) || string.IsNullOrEmpty(itemInstanceId)) return;
            if (FindEntry(itemInstanceId) != null) return;
            Entries.Add(new ExtractionRecoveryEntry(snapshotId, itemInstanceId, ExtractionRecoveryItemState.Recoverable));
        }

        public ExtractionRecoveryItemState GetState(string itemInstanceId)
        {
            var entry = FindEntry(itemInstanceId);
            if (entry == null)
                throw new InvalidOperationException($"Extraction recovery item '{itemInstanceId}' is not tracked.");
            return entry.State;
        }

        public bool TryGetState(string itemInstanceId, out ExtractionRecoveryItemState state)
        {
            var entry = FindEntry(itemInstanceId);
            if (entry == null)
            {
                state = default;
                return false;
            }

            state = entry.State;
            return true;
        }

        public bool TryMarkRecovered(string itemInstanceId)
        {
            var entry = FindEntry(itemInstanceId);
            if (entry == null) return false;
            if (entry.State != ExtractionRecoveryItemState.Recoverable) return false;
            entry.State = ExtractionRecoveryItemState.Recovered;
            return true;
        }

        public bool TryMarkDestroyed(string itemInstanceId)
        {
            return TrySetTerminalState(itemInstanceId, ExtractionRecoveryItemState.Destroyed);
        }

        public bool TryMarkExpired(string itemInstanceId)
        {
            return TrySetTerminalState(itemInstanceId, ExtractionRecoveryItemState.Expired);
        }

        public bool TryGetRecoverableItems(string snapshotId, List<string> results)
        {
            if (results == null) return false;
            results.Clear();
            if (string.IsNullOrEmpty(snapshotId)) return false;

            foreach (var entry in Entries)
            {
                if (entry == null) continue;
                if (entry.SnapshotId != snapshotId) continue;
                if (entry.State != ExtractionRecoveryItemState.Recoverable) continue;
                results.Add(entry.ItemInstanceId);
            }

            return results.Count > 0;
        }

        private bool TrySetTerminalState(
            string itemInstanceId,
            ExtractionRecoveryItemState terminalState)
        {
            var entry = FindEntry(itemInstanceId);
            if (entry == null) return false;
            if (entry.State != ExtractionRecoveryItemState.Recoverable) return false;
            entry.State = terminalState;
            return true;
        }

        private ExtractionRecoveryEntry FindEntry(string itemInstanceId)
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
    public class ExtractionRecoveryEntry
    {
        public string SnapshotId;
        public string ItemInstanceId;
        public ExtractionRecoveryItemState State;

        public ExtractionRecoveryEntry(
            string snapshotId,
            string itemInstanceId,
            ExtractionRecoveryItemState state)
        {
            SnapshotId = snapshotId;
            ItemInstanceId = itemInstanceId;
            State = state;
        }
    }
}
