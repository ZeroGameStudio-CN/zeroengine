using UnityEngine;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Explicit opt-in for a native local light. The component checkbox controls rim participation only.</summary>
    [RequireComponent(typeof(Light)), DisallowMultipleComponent]
    [AddComponentMenu("ZeroEngine/Rendering/参与角色边缘受光")]
    public sealed class SpriteRimLightSource : MonoBehaviour
    {
    }
}
