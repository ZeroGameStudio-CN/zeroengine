using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Console.UI
{
    public sealed class ConsoleEventSystemLease : IDisposable
    {
        private GameObject owned;
        private ConsoleEventSystemLease() { }
        public static ConsoleEventSystemLease Acquire(Transform owner)
        {
            var lease = new ConsoleEventSystemLease();
            if (EventSystem.current) return lease;
            lease.owned = new GameObject("ZE Console EventSystem", typeof(EventSystem));
            lease.owned.transform.SetParent(owner, false);
            ConsoleInputBackend.AddInputModule(lease.owned);
            return lease;
        }
        public void Dispose()
        {
            if (owned) { owned.SetActive(false); UnityEngine.Object.Destroy(owned); }
            owned = null;
        }
    }
}
