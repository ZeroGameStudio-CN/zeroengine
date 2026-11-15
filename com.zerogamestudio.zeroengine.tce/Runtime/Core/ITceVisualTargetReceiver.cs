using UnityEngine;

namespace ZeroEngine.TCE
{
    /// <summary>Optional feedback capability. Receives the explicit visual subject before the first rendered frame.</summary>
    public interface ITceVisualTargetReceiver
    {
        void BindTarget(Renderer renderer, Vector3 impactPosition);
    }
}
