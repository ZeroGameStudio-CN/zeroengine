using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.UI.Toast
{
    public sealed class ToastRootPresenter : MonoBehaviour, IToastPresenter
    {
        [SerializeField] private ToastSettings settings;
        [SerializeField] private ToastContainer[] containers;

        private readonly Dictionary<ToastAnchor, ToastContainer> byAnchor = new Dictionary<ToastAnchor, ToastContainer>();

        private void Awake()
        {
            RebuildLookup();
            Toast.Configure(settings, null, this);
        }

        public void Configure(ToastSettings toastSettings, ToastContainer[] toastContainers)
        {
            settings = toastSettings;
            containers = toastContainers;
            RebuildLookup();
            Toast.Configure(settings, null, this);
        }

        private void OnDisable()
        {
            Toast.ClearAll();
        }

        private void Update()
        {
            Toast.Runtime.Tick(Time.unscaledTime);
        }

        public void RebuildLookup()
        {
            byAnchor.Clear();
            if (containers == null) return;

            for (int i = 0; i < containers.Length; i++)
            {
                var container = containers[i];
                if (container == null) continue;
                byAnchor[container.Anchor] = container;
            }
        }

        public void ShowToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
        {
            if (handle == null) return;
            if (TryGetContainer(handle.Request.Anchor, out var container))
                container.ShowToast(handle, resolvedText, style, timings);
        }

        public void RefreshToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
        {
            if (handle == null) return;
            if (!TryGetContainer(handle.Request.Anchor, out var target)) return;

            if (target.HasToast(handle))
            {
                target.RefreshToast(handle, resolvedText, style, timings);
                return;
            }

            foreach (var container in byAnchor.Values)
            {
                if (container != null && container.HasToast(handle))
                    container.DismissToast(handle, ToastDismissReason.Replaced);
            }

            target.ShowToast(handle, resolvedText, style, timings);
        }

        public void DismissToast(ToastHandle handle, ToastDismissReason reason)
        {
            if (handle == null) return;
            foreach (var container in byAnchor.Values)
                container?.DismissToast(handle, reason);
        }

        public void RepositionToast(ToastHandle handle, int index, float spacing)
        {
            if (handle == null) return;
            if (TryGetContainer(handle.Request.Anchor, out var container))
                container.RepositionToast(handle, index, spacing);
        }

        public void ClearAll()
        {
            foreach (var container in byAnchor.Values)
                container?.ClearAll();
        }

        private bool TryGetContainer(ToastAnchor anchor, out ToastContainer container)
        {
            if (byAnchor.TryGetValue(anchor, out container) && container != null) return true;
            if (byAnchor.TryGetValue(ToastAnchor.TopRight, out container) && container != null) return true;
            foreach (var pair in byAnchor)
            {
                if (pair.Value == null) continue;
                container = pair.Value;
                return true;
            }

            container = null;
            return false;
        }
    }
}
