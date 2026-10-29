using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Guidance
{
    public sealed class TutorialDropObserver : MonoBehaviour, IDropHandler
    {
        internal TutorialFocusView Owner;
        internal int TargetIndex;
        public void OnDrop(PointerEventData data) => Owner?.ObserveDrop(TargetIndex, data);
    }
}
