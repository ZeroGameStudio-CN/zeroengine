using System;
using System.Linq;
using System.Reflection;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public sealed class OdinReflectionAssetInspector : IAssetInspector
    {
        private static readonly Type PropertyTreeType =
            Type.GetType("Sirenix.OdinInspector.Editor.PropertyTree, Sirenix.OdinInspector.Editor");

        private static readonly MethodInfo CreateMethod = PropertyTreeType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "Create" && method.GetParameters().Length == 1);

        private static readonly MethodInfo DrawMethod = PropertyTreeType?.GetMethod("Draw", new[] { typeof(bool) });

        private object propertyTree;
        private Object target;

        public bool CanInspect(Object asset)
        {
            return asset != null && PropertyTreeType != null && CreateMethod != null && DrawMethod != null;
        }

        public void SetTarget(Object asset)
        {
            if (target == asset)
            {
                return;
            }

            Dispose();
            target = asset;
            if (asset != null && CanInspect(asset))
            {
                propertyTree = CreateMethod.Invoke(null, new object[] { asset });
            }
        }

        public void Draw()
        {
            if (propertyTree == null)
            {
                return;
            }

            DrawMethod.Invoke(propertyTree, new object[] { true });
        }

        public void Dispose()
        {
            if (propertyTree is IDisposable disposable)
            {
                disposable.Dispose();
            }

            propertyTree = null;
            target = null;
        }
    }
}
