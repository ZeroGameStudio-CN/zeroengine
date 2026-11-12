using System;
using UnityEngine;
using UnityEngine.EventSystems;
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

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
#if ZE_CONSOLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            lease.owned.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            lease.owned.AddComponent<StandaloneInputModule>();
#endif
            return lease;
        }
        public void Dispose()
        {
            if (owned) { owned.SetActive(false); UnityEngine.Object.Destroy(owned); }
            owned = null;
        }
    }
}
