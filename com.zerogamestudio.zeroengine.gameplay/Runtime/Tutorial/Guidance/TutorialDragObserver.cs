using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Guidance
{
    public sealed class TutorialDragObserver : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal TutorialFocusView Owner;
        internal int TargetIndex;
        public void OnBeginDrag(PointerEventData data) => Owner?.ObserveBeginDrag(TargetIndex, data);
        public void OnDrag(PointerEventData data) { }
        public void OnEndDrag(PointerEventData data) => Owner?.ObserveEndDrag(TargetIndex);
    }
}
