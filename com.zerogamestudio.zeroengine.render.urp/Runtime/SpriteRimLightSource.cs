using UnityEngine;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Explicit opt-in for a native local light. The component checkbox controls rim participation only.</summary>
    [RequireComponent(typeof(Light)), DisallowMultipleComponent]
    [AddComponentMenu("ZeroEngine/Rendering/参与角色边缘受光")]
    public sealed class SpriteRimLightSource : MonoBehaviour
    {
        [SerializeField, Range(0f, .25f), InspectorName("暗边补光")]
        [Tooltip("Adds bounded light color to dark contour cells. Zero keeps the existing rim response.")]
        private float darkEdgeFill;

        public float DarkEdgeFill
        {
            get => ClampFill(darkEdgeFill);
            set => darkEdgeFill = ClampFill(value);
        }

        private static float ClampFill(float value) => float.IsFinite(value) ? Mathf.Clamp(value, 0f, .25f) : 0f;
    }
}
