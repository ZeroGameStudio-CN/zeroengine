using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Tutorial.Tests.Runtime
{
    public sealed class TutorialNativeDropProbe : MonoBehaviour, IDropHandler
    {
        public int Drops;
        public void OnDrop(PointerEventData eventData) => Drops++;
    }
}
