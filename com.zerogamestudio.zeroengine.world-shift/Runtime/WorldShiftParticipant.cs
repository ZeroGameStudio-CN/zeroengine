using UnityEngine;

namespace ZeroEngine.WorldShift
{
    [DisallowMultipleComponent]
    public sealed class WorldShiftParticipant : MonoBehaviour
    {
        [SerializeField] private WorldShiftLayer layer;
        [SerializeField] private Renderer[] renderers;
        [SerializeField] private bool includeInactiveChildren = true;

        public WorldShiftLayer Layer
        {
            get => layer;
            set => layer = value;
        }

        public Renderer[] Renderers => renderers;

        private void Reset()
        {
            RebuildRenderers();
        }

        private void Awake()
        {
            if (renderers == null || renderers.Length == 0)
            {
                RebuildRenderers();
            }
        }

        private void OnValidate()
        {
            if (renderers == null || renderers.Length == 0)
            {
                RebuildRenderers();
            }
        }

        public void RebuildRenderers()
        {
            renderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
        }

        public bool HasRenderable()
        {
            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        public void SetRenderersVisible(bool visible)
        {
            if (renderers == null || renderers.Length == 0)
            {
                RebuildRenderers();
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = visible;
                }
            }
        }
    }
}
