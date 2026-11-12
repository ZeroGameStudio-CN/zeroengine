using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.FogOfWar.Unity
{
    /// <summary>
    /// Synchronous scene membership and hierarchy notifications for presentation caches.
    /// Ordinary motion is excluded. Consumers retain their own policy and conservative fallback.
    /// </summary>
    public sealed class SceneStructureObserver2D : IDisposable
    {
        private NativeVisibilityChanges changes;
        private SceneStructureObserver2D(NativeVisibilityChanges changes) => this.changes = changes;

        public static bool TryCreate(out SceneStructureObserver2D observer, out string reason)
        {
            observer = null;
            if (!Application.isPlaying) { reason = "Scene structure tracking requires Play Mode."; return false; }
            if (!NativeVisibilityChanges.TryCreateStructure(out var changes, out reason)) return false;
            observer = new SceneStructureObserver2D(changes);
            return true;
        }

        public bool TryDrain(Action<Object> changed, Action<EntityId> destroyed, out string reason)
        {
            reason = null;
            if (changes == null) { reason = "Scene structure tracking has ended."; return false; }
            try { changes.Drain(changed, destroyed); return true; }
            catch (Exception error)
            {
                reason = (error.InnerException ?? error).Message;
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            var previous = changes;
            changes = null;
            previous?.Dispose();
        }
    }
}
