using UnityEngine;

namespace ZeroEngine.Tutorial
{
    internal static class TutorialObjectLifetime
    {
        internal static void Destroy(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
