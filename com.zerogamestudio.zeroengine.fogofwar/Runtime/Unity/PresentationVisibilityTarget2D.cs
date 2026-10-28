using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.FogOfWar.Unity
{
    /// <summary>
    /// An explicitly supplied presentation group for VisibilityWorld. It never discovers a scene,
    /// disables an actor, or changes authored Renderer.enabled state. Use one owner per graphic.
    /// Replace registration when the graphic roster changes; moving the anchor needs no notification.
    /// </summary>
    public sealed class PresentationVisibilityTarget2D : IFogVisibilityTarget, IDisposable
    {
        private readonly Transform anchor;
        private readonly Vector2 offset;
        private readonly Renderer[] renderers;
        private readonly Canvas[] canvases;
        private readonly HashSet<Renderer> hiddenRenderers = new();
        private readonly HashSet<Canvas> hiddenCanvases = new();
        private uint releaseRevision;

        public PresentationVisibilityTarget2D(Transform anchor, IReadOnlyList<Renderer> renderers,
            IReadOnlyList<Canvas> canvases = null, Vector2 offset = default)
        {
            this.anchor = anchor ? anchor : throw new ArgumentNullException(nameof(anchor));
            this.offset = offset;
            this.renderers = Copy(renderers);
            this.canvases = Copy(canvases);
        }

        private static T[] Copy<T>(IReadOnlyList<T> input)
        {
            if (input == null || input.Count == 0) return Array.Empty<T>();
            var result = new T[input.Count];
            for (int i = 0; i < result.Length; i++) result[i] = input[i];
            return result;
        }

        public bool TryGetPosition(out FogPosition position)
        {
            position = default;
            if (!anchor || !anchor.gameObject.activeInHierarchy) return false;
            var world = (Vector2)anchor.position + offset;
            position = new FogPosition(world.x, world.y);
            return true;
        }

        public void ApplyVisibility(bool visible)
        {
            if (visible) { ReleaseVisibility(); return; }
            uint revision = releaseRevision;
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true;
                hiddenRenderers.Add(renderer);
            }
            foreach (var canvas in canvases)
            {
                if (!canvas || !canvas.gameObject.activeInHierarchy || !canvas.enabled) continue;
                // Record custody before the setter: Canvas callbacks can synchronously release it.
                hiddenCanvases.Add(canvas);
                canvas.enabled = false;
                if (releaseRevision != revision) return;
            }
        }

        public void ReleaseVisibility()
        {
            unchecked { releaseRevision++; }
            foreach (var renderer in hiddenRenderers) if (renderer) renderer.forceRenderingOff = false;
            hiddenRenderers.Clear();
            // Clear custody before callbacks. Copying is unnecessary: remove one owned entry first.
            while (hiddenCanvases.Count != 0)
            {
                Canvas owned = null;
                foreach (var canvas in hiddenCanvases) { owned = canvas; break; }
                hiddenCanvases.Remove(owned);
                if (owned) owned.enabled = true;
            }
        }

        public void Dispose() => ReleaseVisibility();
    }

    public sealed class VisibilityQuery2DBridge : IFogVisibilityQuery
    {
        private readonly IVisibilityQuery2D query;
        public VisibilityQuery2DBridge(IVisibilityQuery2D query) => this.query = query ?? throw new ArgumentNullException(nameof(query));
        public bool IsVisible(FogPosition position) => query.IsVisible(new Vector2(position.X, position.Y));
    }
}
