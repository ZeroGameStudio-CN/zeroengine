using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.FogOfWar.Unity
{
    /// <summary>
    /// Caller-owned synchronous observations of explicitly tracked scene components.
    /// BeginObservation starts each read sequence; Invalidate ends it before presentation callbacks.
    /// Native structure/global-transform changes, rather than a frame TTL, invalidate observations.
    /// Unsupported engines or a failed drain return false so the caller retains live reads.
    /// </summary>
    public sealed class SceneObjectSnapshot2D : IDisposable
    {
        public readonly struct State
        {
            public readonly bool Alive, ActiveSelf, ActiveInHierarchy;
            public readonly Vector3 Position;
            internal State(bool alive, bool activeSelf, bool active, Vector3 position)
            { Alive = alive; ActiveSelf = activeSelf; ActiveInHierarchy = active; Position = position; }
        }

        public readonly struct ActivationState
        {
            public readonly bool Alive, ActiveSelf, ActiveInHierarchy;
            internal ActivationState(State value)
            { Alive = value.Alive; ActiveSelf = value.ActiveSelf; ActiveInHierarchy = value.ActiveInHierarchy; }
        }

        public sealed class Target
        {
            internal SceneObjectSnapshot2D Owner;
            internal Component Component;
            internal Transform Transform;
            internal GameObject Node;
            internal EntityId TransformId;
            internal readonly List<EntityId> Ancestors = new();
            internal State Value;
            internal bool HierarchyDirty;
        }

        private readonly NativeVisibilityChanges structure;
        private readonly object dispatcher;
        private readonly MethodInfo readTransforms, disableTransforms;
        private readonly object[] transformTypesArguments, readArguments;
        private readonly Type[] transformTypes = { typeof(Transform), typeof(RectTransform) };
        private readonly Dictionary<EntityId, HashSet<Target>> descendants = new();
        private readonly Dictionary<EntityId, HashSet<Target>> transforms = new();
        private readonly HashSet<Target> targets = new(), dirty = new();
        private readonly Action<Object> changed;
        private readonly Action<EntityId> destroyed;
        private bool synchronized, disposed;
        public bool IsActive => !disposed;
        public bool TracksMotion => !disposed && dispatcher != null;
        public string Failure { get; private set; }
        public int Count => targets.Count;
        public long StateReads { get; private set; }
        public long SnapshotHits { get; private set; }
        public long Synchronizations { get; private set; }

        private SceneObjectSnapshot2D(NativeVisibilityChanges structure, bool trackMotion)
        {
            this.structure = structure;
            changed = Changed; destroyed = Destroyed;
            if (!trackMotion) return;
            var type = typeof(Object).Assembly.GetType("UnityEngine.ObjectDispatcher", true);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo enable = null;
            foreach (var method in methods)
            {
                if (method.ContainsGenericParameters) continue;
                var parameters = method.GetParameters();
                if (method.Name == "EnableTransformTracking" && parameters.Length == 2) enable = method;
                else if (method.Name == "DisableTransformTracking" && parameters.Length == 2) disableTransforms = method;
                else if (method.Name == "GetTransformChangesAndClear" && parameters.Length == 3
                    && parameters[2].ParameterType == typeof(bool)) readTransforms = method;
            }
            if (enable == null || disableTransforms == null || readTransforms == null)
                throw new NotSupportedException("Synchronous global transform tracking is unavailable.");
            dispatcher = Activator.CreateInstance(type, true);
            try
            {
                var global = Enum.Parse(enable.GetParameters()[0].ParameterType, "GlobalTRS");
                transformTypesArguments = new object[] { global, transformTypes };
                readArguments = new object[] { typeof(Transform), global, false };
                enable.Invoke(dispatcher, transformTypesArguments);
            }
            catch { ((IDisposable)dispatcher).Dispose(); throw; }
        }

        public static bool TryCreate(out SceneObjectSnapshot2D result, out string reason) =>
            TryCreate(true, out result, out reason);

        /// <summary>Observe activation and hierarchy without a global motion queue. TryRead still returns live positions.</summary>
        public static bool TryCreateActivation(out SceneObjectSnapshot2D result, out string reason) =>
            TryCreate(false, out result, out reason);

        private static bool TryCreate(bool trackMotion, out SceneObjectSnapshot2D result, out string reason)
        {
            result = null;
            if (!NativeVisibilityChanges.TryCreateStructure(out var structure, out reason)) return false;
            try { result = new SceneObjectSnapshot2D(structure, trackMotion); return true; }
            catch (Exception error)
            { structure.Dispose(); reason = (error.InnerException ?? error).Message; return false; }
        }

        public Target Track(Component component)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SceneObjectSnapshot2D));
            if (!component) throw new ArgumentNullException(nameof(component));
            var target = new Target { Owner = this, Component = component, Transform = component.transform,
                Node = component.gameObject, TransformId = component.transform.GetEntityId(), HierarchyDirty = true };
            targets.Add(target);
            if (!transforms.TryGetValue(target.TransformId, out var set)) transforms.Add(target.TransformId, set = new HashSet<Target>());
            set.Add(target);
            dirty.Add(target);
            synchronized = false;
            return target;
        }

        public void Untrack(Target target)
        {
            if (target == null || !ReferenceEquals(target.Owner, this)) return;
            Unlink(target); dirty.Remove(target); targets.Remove(target);
            if (transforms.TryGetValue(target.TransformId, out var set))
            { set.Remove(target); if (set.Count == 0) transforms.Remove(target.TransformId); }
            target.Owner = null;
        }

        public void BeginObservation() => synchronized = false;
        public void Invalidate() => synchronized = false;

        public bool TryRead(Target target, out State state)
        {
            state = default;
            if (disposed || target == null || !ReferenceEquals(target.Owner, this) || !Synchronize()) return false;
            state = target.Value;
            if (dispatcher == null && state.Alive)
            { state = new State(state.Alive, state.ActiveSelf, state.ActiveInHierarchy, target.Transform.position); StateReads++; }
            SnapshotHits++;
            return true;
        }

        public bool TryReadActivation(Target target, out ActivationState state)
        {
            state = default;
            if (disposed || target == null || !ReferenceEquals(target.Owner, this) || !Synchronize()) return false;
            state = new ActivationState(target.Value); SnapshotHits++;
            return true;
        }

        private bool Synchronize()
        {
            if (synchronized) return true;
            try
            {
                structure.Drain(changed, destroyed);
                if (dispatcher != null) foreach (var type in transformTypes)
                {
                    readArguments[0] = type;
                    var moved = (Component[])readTransforms.Invoke(dispatcher, readArguments);
                    if (moved == null) continue;
                    foreach (var item in moved)
                        if (item && transforms.TryGetValue(item.GetEntityId(), out var set))
                            foreach (var target in set) dirty.Add(target);
                }
                foreach (var target in dirty) Refresh(target);
                dirty.Clear(); synchronized = true; Synchronizations++;
                return true;
            }
            catch (Exception error)
            {
                Failure = (error.InnerException ?? error).Message;
                Dispose();
                return false;
            }
        }

        private void Changed(Object item)
        {
            Transform transform = item is GameObject node ? node.transform : item as Transform;
            if (!transform || !descendants.TryGetValue(transform.GetEntityId(), out var set)) return;
            foreach (var target in set)
            { target.HierarchyDirty |= item is Transform; dirty.Add(target); }
        }

        private void Destroyed(EntityId identity)
        {
            if (!descendants.TryGetValue(identity, out var set)) return;
            foreach (var target in set) { target.HierarchyDirty = true; dirty.Add(target); }
        }

        private void Refresh(Target target)
        {
            StateReads++;
            if (!target.Component || !target.Node || !target.Transform)
            { target.Value = default; Unlink(target); return; }
            if (target.HierarchyDirty)
            {
                Unlink(target);
                for (var current = target.Transform; current; current = current.parent)
                {
                    var identity = current.GetEntityId(); target.Ancestors.Add(identity);
                    if (!descendants.TryGetValue(identity, out var set)) descendants.Add(identity, set = new HashSet<Target>());
                    set.Add(target);
                }
                target.HierarchyDirty = false;
            }
            target.Value = new State(true, target.Node.activeSelf, target.Node.activeInHierarchy, dispatcher != null ? target.Transform.position : default);
        }

        private void Unlink(Target target)
        {
            foreach (var ancestor in target.Ancestors)
                if (descendants.TryGetValue(ancestor, out var set))
                { set.Remove(target); if (set.Count == 0) descendants.Remove(ancestor); }
            target.Ancestors.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { structure.Dispose(); }
            finally
            {
                try { disableTransforms?.Invoke(dispatcher, transformTypesArguments); }
                finally
                {
                    ((IDisposable)dispatcher)?.Dispose();
                    foreach (var target in targets) target.Owner = null;
                    targets.Clear(); dirty.Clear(); descendants.Clear(); transforms.Clear();
                }
            }
        }
    }
}
