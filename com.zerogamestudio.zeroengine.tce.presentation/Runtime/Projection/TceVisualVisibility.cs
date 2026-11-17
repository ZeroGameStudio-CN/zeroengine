using System;
using UnityEngine;

namespace ZeroEngine.TCE.Presentation
{
    /// <summary>Pairs source suppression and restoration without changing authored enabled states.</summary>
    public sealed class TceVisualVisibility
    {
        private readonly Renderer[] renderers;
        private readonly Light[] lights;
        private readonly bool[] suppression;
        private readonly int[] lightMasks;
        private readonly bool preserveRendererSuppression;

        public TceVisualVisibility(GameObject root, bool preserveRendererSuppression = true)
            : this(root ? root.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>(),
                root ? root.GetComponentsInChildren<Light>(true) : Array.Empty<Light>(), preserveRendererSuppression) { }

        public TceVisualVisibility(Renderer[] renderers, Light[] lights, bool preserveRendererSuppression = true)
        {
            this.renderers = renderers == null ? Array.Empty<Renderer>() : (Renderer[])renderers.Clone();
            this.lights = lights == null ? Array.Empty<Light>() : (Light[])lights.Clone();
            this.preserveRendererSuppression = preserveRendererSuppression;
            suppression = new bool[this.renderers.Length];
            lightMasks = new int[this.lights.Length];
            for (int i=0;i<this.renderers.Length;i++) suppression[i] = this.renderers[i] && this.renderers[i].forceRenderingOff;
            for (int i=0;i<this.lights.Length;i++) lightMasks[i] = this.lights[i] ? this.lights[i].cullingMask : 0;
        }

        public void SetHidden(bool hidden) => Apply(hidden, preserveRendererSuppression);

        public void Restore() => Apply(false, true);

        private void Apply(bool hidden, bool preserveSuppression)
        {
            for (int i=0;i<renderers.Length;i++)
                if (renderers[i]) renderers[i].forceRenderingOff = hidden || preserveSuppression && suppression[i];
            // A particle light also copies its authored light's mask. Suppress illumination,
            // preserving enabled/playback state on the authoritative visual source.
            for (int i=0;i<lights.Length;i++)
                if (lights[i]) lights[i].cullingMask = hidden ? 0 : lightMasks[i];
        }
    }
}
