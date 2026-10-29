using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Samples
{
    public sealed class PlaygroundDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private CanvasGroup group;
        private Vector3 before;
        public void OnBeginDrag(PointerEventData eventData)
        { before = transform.position; group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; }
        public void OnDrag(PointerEventData eventData) => transform.position = eventData.position;
        public void OnEndDrag(PointerEventData eventData) { transform.position = before; if (group) group.blocksRaycasts = true; }
    }
}
