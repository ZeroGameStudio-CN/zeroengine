using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Guidance
{
    /// <summary>Connects a world target's native pointer event to its project's actual operation.</summary>
    public sealed class TutorialInteractionRelay : MonoBehaviour, IPointerClickHandler
    {
        public UnityEvent OnActivated = new UnityEvent();
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) OnActivated.Invoke();
        }
    }
}
