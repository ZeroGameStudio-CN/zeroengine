using UnityEngine.EventSystems;
using UnityEngine;

namespace ZeroEngine.Tutorial.Guidance
{
    /// <summary>Observes the original handler; it never forwards or invokes a game action.</summary>
    public sealed class TutorialPointerObserver : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        internal TutorialFocusView Owner;
        internal int TargetIndex;
        public void OnPointerClick(PointerEventData data) => Owner?.ObserveClick(TargetIndex);
        public void OnPointerDown(PointerEventData data) => Owner?.ObserveDown(TargetIndex, data);
        public void OnPointerUp(PointerEventData data) => Owner?.ObserveUp(TargetIndex, data);
        public void OnPointerExit(PointerEventData data) => Owner?.PointerExit(TargetIndex, data);
    }
}
