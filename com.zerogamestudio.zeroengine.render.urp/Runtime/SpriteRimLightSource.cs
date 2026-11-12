using UnityEngine;
using UnityEngine.Rendering;

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

        [SerializeField, InspectorName("无实时阴影时的挡光网格")]
        [Tooltip("Optional opaque door or wall. Its oriented mesh bounds block this light's rim when URP cannot render its shadow.")]
        private MeshRenderer fallbackOccluder;

        public MeshRenderer FallbackOccluder
        {
            get => fallbackOccluder;
            set => fallbackOccluder = value;
        }

        public float DarkEdgeFill
        {
            get => ClampFill(darkEdgeFill);
            set => darkEdgeFill = ClampFill(value);
        }

        internal bool TryGetFallbackBox(out Matrix4x4 worldToBox)
        {
            worldToBox = default;
            var occluder = fallbackOccluder;
            if (!occluder || !occluder.enabled || occluder.forceRenderingOff || !occluder.gameObject.activeInHierarchy ||
                occluder.shadowCastingMode == ShadowCastingMode.Off) return false;

            var bounds = occluder.localBounds;
            var extents = Vector3.Max(bounds.extents, Vector3.one * .0001f);
            var worldToLocal = occluder.transform.worldToLocalMatrix;
            float determinant = worldToLocal.determinant;
            if (!float.IsFinite(bounds.center.sqrMagnitude) || !float.IsFinite(extents.sqrMagnitude) ||
                !float.IsFinite(determinant) || determinant == 0) return false;

            var reciprocalExtents = new Vector3(1 / extents.x, 1 / extents.y, 1 / extents.z);
            worldToBox = Matrix4x4.Scale(reciprocalExtents) * Matrix4x4.Translate(-bounds.center) * worldToLocal;
            return true;
        }

        private static float ClampFill(float value) => float.IsFinite(value) ? Mathf.Clamp(value, 0f, .25f) : 0f;
    }
}
