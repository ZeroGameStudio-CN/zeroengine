using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Console.UI
{
    [DisallowMultipleComponent]
    internal sealed class ConsoleActionHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Message;
        internal Action<RectTransform, string> Show;
        internal Action<RectTransform> Hide;
        public void OnPointerEnter(PointerEventData eventData) => Show?.Invoke((RectTransform)transform, Message);
        public void OnPointerExit(PointerEventData eventData) => Hide?.Invoke((RectTransform)transform);
        private void OnDisable() => Hide?.Invoke((RectTransform)transform);
    }
}
