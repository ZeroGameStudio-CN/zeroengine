using UnityEngine;
namespace ZeroEngine.Tutorial.Samples
{
    public sealed class PlaygroundMaterialOwner : MonoBehaviour
    { public Material Owned; private void OnDestroy() { if (Owned) Destroy(Owned); } }
}
