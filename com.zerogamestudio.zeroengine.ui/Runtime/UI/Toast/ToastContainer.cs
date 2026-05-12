using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.UI.Toast
{
    public sealed class ToastContainer : MonoBehaviour
    {
        [SerializeField] private ToastAnchor anchor = ToastAnchor.TopRight;
        [SerializeField] private RectTransform itemRoot;
        [SerializeField] private ToastItemView itemPrefab;

        private readonly Dictionary<int, ToastItemView> views = new Dictionary<int, ToastItemView>();

        public ToastAnchor Anchor => anchor;
        public int VisibleCount => views.Count;

        public void Configure(ToastAnchor targetAnchor, RectTransform targetItemRoot, ToastItemView targetItemPrefab)
        {
            anchor = targetAnchor;
            itemRoot = targetItemRoot;
            itemPrefab = targetItemPrefab;
        }

        private void Awake()
        {
            if (itemRoot == null) itemRoot = (RectTransform)transform;
        }

        public bool HasToast(ToastHandle handle)
        {
            return handle != null && views.ContainsKey(handle.Id);
        }

        public void ShowToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
        {
            if (handle == null || itemPrefab == null) return;
            if (itemRoot == null) itemRoot = (RectTransform)transform;

            var view = Instantiate(itemPrefab, itemRoot);
            views[handle.Id] = view;
            view.Show(handle, resolvedText, style, timings);
        }

        public void RefreshToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
        {
            if (handle == null) return;
            if (views.TryGetValue(handle.Id, out var view))
                view.Show(handle, resolvedText, style, timings);
        }

        public void DismissToast(ToastHandle handle, ToastDismissReason reason)
        {
            if (handle == null) return;
            if (!views.TryGetValue(handle.Id, out var view)) return;
            views.Remove(handle.Id);
            view.DismissImmediate();
        }

        public void RepositionToast(ToastHandle handle, int index, float spacing)
        {
            if (handle == null) return;
            if (views.TryGetValue(handle.Id, out var view))
                view.MoveToIndex(index, spacing);
        }

        public void ClearAll()
        {
            foreach (var view in views.Values)
            {
                if (view != null) view.DismissImmediate();
            }

            views.Clear();
        }
    }
}
