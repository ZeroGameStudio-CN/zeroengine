using UnityEngine;
namespace ZeroEngine.Render
{
    /// <summary>Explicit sprite/mesh visual layers for any entity; never scans UI or gameplay children.</summary>
    [DisallowMultipleComponent]
    public sealed class RenderPoseSource : MonoBehaviour, IRenderPoseSource
    {
        [SerializeField] private Renderer[] layers;

        public void Configure(params Renderer[] renderers)
        {
            layers = renderers != null ? (Renderer[])renderers.Clone() : null;
        }

        public bool TryCapture(RenderPoseSnapshot snapshot, Transform origin)
        {
            int sortingOrigin = 0;
            if (layers != null)
                foreach (var layer in layers)
                    if (layer) { sortingOrigin = layer.sortingOrder; break; }
            snapshot.Clear(sortingOrigin);
            if (!origin) return false;
            if (!isActiveAndEnabled || layers == null) return true;
            for (int i = 0; i < layers.Length; i++)
            {
                if (!layers[i]) continue;
                bool duplicate = false;
                for (int j = 0; j < i; j++)
                    if (layers[j] == layers[i]) { duplicate = true; break; }
                if (!duplicate && !snapshot.TryCapture(layers[i], origin))
                {
                    snapshot.Clear();
                    return false;
                }
            }
            return true;
        }
    }
}
