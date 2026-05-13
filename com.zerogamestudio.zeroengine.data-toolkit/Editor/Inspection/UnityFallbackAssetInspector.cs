using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class UnityFallbackAssetInspector : IAssetInspector
    {
        private UnityEditor.Editor editor;
        private Object target;

        public bool CanInspect(Object asset)
        {
            return asset != null;
        }

        public void SetTarget(Object asset)
        {
            if (target == asset)
            {
                return;
            }

            Dispose();
            target = asset;
            if (asset != null)
            {
                editor = UnityEditor.Editor.CreateEditor(asset);
            }
        }

        public void Draw()
        {
            editor?.OnInspectorGUI();
        }

        public void Dispose()
        {
            if (editor != null)
            {
                Object.DestroyImmediate(editor);
                editor = null;
            }

            target = null;
        }
    }
}
