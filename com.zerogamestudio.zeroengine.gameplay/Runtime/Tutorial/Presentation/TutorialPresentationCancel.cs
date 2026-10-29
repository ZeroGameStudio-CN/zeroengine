using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Presentation
{
    public sealed class TutorialPresentationCancel : MonoBehaviour, ICancelHandler
    {
        internal Action Cancel;
        public void OnCancel(BaseEventData eventData) { Cancel?.Invoke(); eventData.Use(); }
    }
}
