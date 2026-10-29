using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Guidance
{
    public sealed class TutorialPointerProxy : UnityEngine.UI.Selectable, IPointerClickHandler, ISubmitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IInitializePotentialDragHandler, ICancelHandler
    {
        internal TutorialFocusView Owner;
        internal int TargetIndex;
        public override void OnPointerDown(PointerEventData data) { base.OnPointerDown(data); Owner?.PointerDown(TargetIndex, data); }
        public override void OnPointerUp(PointerEventData data) { base.OnPointerUp(data); Owner?.PointerUp(TargetIndex, data); }
        public override void OnPointerExit(PointerEventData data) { base.OnPointerExit(data); Owner?.PointerExit(TargetIndex, data); }
        public void OnPointerClick(PointerEventData data) => Owner?.PointerClick(TargetIndex, data);
        public void OnInitializePotentialDrag(PointerEventData data) => Owner?.InitializeDrag(TargetIndex, data);
        public void OnBeginDrag(PointerEventData data) => Owner?.BeginDrag(TargetIndex, data);
        public void OnDrag(PointerEventData data) => Owner?.Drag(TargetIndex, data);
        public void OnEndDrag(PointerEventData data) => Owner?.EndDrag(TargetIndex, data);
        public void OnSubmit(BaseEventData data) { Owner?.Submit(TargetIndex); data.Use(); }
        public void OnCancel(BaseEventData data) { Owner?.TrySkip(); data.Use(); }
    }
}
