using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public interface IAssetInspector
    {
        bool CanInspect(Object asset);
        void SetTarget(Object asset);
        void Draw();
        void Dispose();
    }
}
