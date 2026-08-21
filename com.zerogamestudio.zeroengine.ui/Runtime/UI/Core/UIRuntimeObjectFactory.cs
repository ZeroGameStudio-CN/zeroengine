using System;
using UnityEngine;

namespace ZeroEngine.UI
{
    /// <summary>
    /// Centralizes the Unity object creation used by the UI runtime.
    /// </summary>
    public static class UIRuntimeObjectFactory
    {
        public static T CreateChild<T>(T prefab, Transform parent) where T : UnityEngine.Object
        {
            return UnityEngine.Object.Instantiate(prefab, parent);
        }

        public static GameObject CreateFallbackObject(string name, Transform parent, params Type[] components)
        {
            var gameObject = new GameObject(name, components);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        public static RectTransform CreateFallbackRectChild(string name, Transform parent)
        {
            return CreateFallbackObject(name, parent, typeof(RectTransform)).GetComponent<RectTransform>();
        }
    }
}
