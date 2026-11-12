using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Tests.Runtime
{
    public sealed class TutorialNativeDragProbe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int Begins, Drags, Ends;
        public void OnBeginDrag(PointerEventData eventData) => Begins++;
        public void OnDrag(PointerEventData eventData) => Drags++;
        public void OnEndDrag(PointerEventData eventData) => Ends++;
    }
}
