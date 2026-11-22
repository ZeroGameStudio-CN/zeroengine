using System;
using UnityEngine;

namespace ZeroEngine.GameFeel.Unity
{
    /// <summary>A temporary renderer offset. It yields to a new transform owner or parent.</summary>
    public sealed class ImpactTransformOffset : IDisposable
    {
        private readonly Transform visual;
        private readonly Transform parent;
        private readonly Vector3 home;
        private Vector3 last;
        private bool released;

        public ImpactTransformOffset(Transform visual)
        {
            if (!visual) throw new ArgumentNullException(nameof(visual));
            this.visual = visual;
            parent = visual.parent;
            home = last = visual.localPosition;
        }

        private bool OwnsPlacement => !released && visual && visual.parent == parent &&
            (visual.localPosition - last).sqrMagnitude < .000001f;

        public bool TrySetWorldOffset(Vector3 offset)
        {
            if (!OwnsPlacement) return false;
            last = home + (parent ? parent.InverseTransformVector(offset) : offset);
            visual.localPosition = last;
            return true;
        }

        public void Dispose()
        {
            if (OwnsPlacement) visual.localPosition = home;
            released = true;
        }

        public static Vector3 LocalImpactDirection(Transform parent, Vector3 worldDirection) =>
            (parent ? parent.InverseTransformDirection(worldDirection) : worldDirection).normalized;

        public static float RecoilVariation(int seed) => Mathf.Lerp(.93f, 1.07f, (seed & 255) / 255f);
    }
}
