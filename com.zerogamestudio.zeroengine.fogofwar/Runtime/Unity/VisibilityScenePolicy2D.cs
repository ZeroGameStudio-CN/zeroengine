using System;
using UnityEngine;

namespace ZeroEngine.FogOfWar.Unity
{
    public interface IVisibilityQuery2D { bool IsVisible(Vector2 position); }

    [Flags]
    public enum VisibilityComponentKind { None = 0, PrimaryOwner = 1, SecondaryOwner = 2, Content = 4, CanvasScope = 8 }

    /// <summary>Game-specific classification. Queries run on the main thread and must be side-effect free.</summary>
    public abstract class VisibilityScenePolicy2D
    {
        // Increment whenever a new owner or canvas scope is constructed, even while inactive.
        public abstract ulong OwnerCreationRevision { get; }
        public abstract int ContentLayers { get; }
        public virtual GameObject PersistentSceneAnchor => null;
        public abstract Component FindPrimaryOwner(Transform item);
        public abstract Component FindSecondaryOwner(Transform item);
        public abstract bool IsAlwaysVisible(Component owner);
        public abstract Vector2 GetOwnerPosition(Component owner);
        public abstract bool HasContentComponent(Transform item);
        public abstract VisibilityComponentKind Classify(Component component);
        public virtual Component FindCanvasScope(Transform item) => null;
        public virtual bool IsCanvasExempt(Component scope, Canvas canvas) => false;
    }
}
