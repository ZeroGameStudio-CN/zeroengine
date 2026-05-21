using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public interface IDataToolkitAssetInspectorProvider
    {
        int Order { get; }
        bool CanInspect(DataToolkitContext context, Object asset);
        IAssetInspector CreateInspector(DataToolkitContext context);
    }
}
