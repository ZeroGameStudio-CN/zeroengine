using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.FogOfWar.Unity
{
    // Isolated, version-qualified bridge. No private engine type escapes this boundary.
    // Structural Transform/RectTransform type changes exclude ordinary motion and
    // observe sibling edits without TransformChangeDispatch listeners or managed arrays.
    internal sealed class NativeVisibilityChanges : IDisposable
    {
        private delegate void ReadTypes(Type type, List<Object> changed, out NativeArray<EntityId> changedIds,
            out NativeArray<EntityId> destroyedIds, Allocator allocator, bool sort);
        private readonly object dispatcher;
        private readonly ReadTypes readTypes;
        private readonly IDisposable lifetime;
        private readonly MethodInfo disable;
        private readonly Type[] trackedTypes;
        private readonly List<Object> changes = new();
        private bool tracking, disposed;

        private NativeVisibilityChanges(Type type)
        {
            dispatcher = Activator.CreateInstance(type, true);
            lifetime = (IDisposable)dispatcher;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                MethodInfo enable = null, drain = null;
                foreach (var method in type.GetMethods(flags))
                {
                    if (method.ContainsGenericParameters) continue;
                    int length = method.GetParameters().Length;
                    if (method.Name == "EnableTypeTracking" && length == 2) enable = method;
                    else if (method.Name == "DisableTypeTracking" && length == 1) disable = method;
                    else if (method.Name == "GetTypeChangesAndClear" && length == 6) drain = method;
                }
                if (enable == null || disable == null || drain == null)
                    throw new NotSupportedException("Synchronous visibility type-change bindings are unavailable.");
                readTypes = (ReadTypes)drain.CreateDelegate(typeof(ReadTypes), dispatcher);
                var types = new List<Type>();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                    Type[] candidates;
                    try { candidates = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException error) { candidates = error.Types; }
                    foreach (var candidate in candidates)
                        if (candidate != null && !candidate.IsAbstract && candidate.IsSubclassOf(typeof(Renderer)) && !types.Contains(candidate))
                            types.Add(candidate);
                }
                if (types.Count == 0) throw new NotSupportedException("No concrete engine renderer types found.");
                types.Add(typeof(Canvas)); types.Add(typeof(GameObject));
                types.Add(typeof(Transform)); types.Add(typeof(RectTransform));
                trackedTypes = types.ToArray();
                // Qualified TypeTrackingFlags.SceneObjects value; retain no asset/Editor roster.
                object sceneObjects = Enum.ToObject(enable.GetParameters()[0].ParameterType, 1);
                tracking = true;
                enable.Invoke(dispatcher, new object[] { sceneObjects, trackedTypes });
            }
            catch { Dispose(); throw; }
        }

        internal static bool TryCreate(out NativeVisibilityChanges result, out string reason)
        {
            result = null; reason = null;
            if (!Application.unityVersion.StartsWith("6000.6.", StringComparison.Ordinal))
            { reason = "Engine version is not qualified for synchronous change tracking."; return false; }
            try
            {
                var type = typeof(Object).Assembly.GetType("UnityEngine.ObjectDispatcher", false);
                if (type == null) throw new NotSupportedException("Engine change tracking was stripped or is unavailable.");
                result = new NativeVisibilityChanges(type);
                return true;
            }
            catch (Exception error) { reason = (error.InnerException ?? error).Message; return false; }
        }

        internal void Drain(Action<Object> changed, Action<EntityId> destroyed)
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeVisibilityChanges));
            foreach (var type in trackedTypes)
            {
                readTypes(type, changes, out var updated, out var removed, Allocator.Temp, false);
                try
                {
                    for (int i=0; i<changes.Count; i++) if (changes[i]) changed(changes[i]);
                    for (int i=0; i<removed.Length; i++) destroyed(removed[i]);
                }
                finally { changes.Clear(); if (updated.IsCreated) updated.Dispose(); if (removed.IsCreated) removed.Dispose(); }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (tracking) disable.Invoke(dispatcher, new object[] { trackedTypes }); }
            finally { tracking = false; lifetime.Dispose(); changes.Clear(); }
        }
    }
}
