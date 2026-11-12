using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Samples
{
    public sealed class PlaygroundDropTarget : MonoBehaviour, IDropHandler
    {
        public void OnDrop(PointerEventData eventData) => GetComponent<UnityEngine.UI.Image>().color = Color.green;
    }
}
