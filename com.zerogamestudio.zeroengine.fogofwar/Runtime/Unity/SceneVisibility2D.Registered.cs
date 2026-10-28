using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace ZeroEngine.FogOfWar.Unity
{
    public partial class SceneVisibility2D
    {
        [UnityEngine.Scripting.Preserve]
        public bool RegisteredVisibilityEnabled { get; set; }
        [UnityEngine.Scripting.Preserve]
        public bool RegisteredVisibilityActive => registeredRoster != null && registeredRoster.Active;
        [UnityEngine.Scripting.Preserve]
        public int RegisteredGraphicCount => registeredRoster?.Count ?? 0;
        [UnityEngine.Scripting.Preserve]
        public long RegisteredBootstrapQueries => registeredRoster?.BootstrapQueries ?? 0;
        [UnityEngine.Scripting.Preserve]
        public long RegisteredBindingResolutions => registeredRoster?.BindingResolutions ?? 0;
        public string RegistrationUnavailableReason { get; private set; }
        private RegisteredRoster registeredRoster;
        private RegisteredRoster.Binding currentRegisteredBinding;
        private bool registrationUnavailable;

        private bool ApplyRegistered(IVisibilityQuery2D grid, Transform player)
        {
            if (!RegisteredVisibilityEnabled || !Application.isPlaying || !player || NativeRendererDiscoveryEnabled)
            {
                registeredRoster?.Dispose(); registeredRoster = null; currentRegisteredBinding = null;
                return false;
            }
            if (registrationUnavailable) return false;
            if (registeredRoster == null)
            {
                if (!NativeVisibilityChanges.TryCreate(out var changes, out string reason))
                { registrationUnavailable = true; RegistrationUnavailableReason = reason; return false; }
                try { registeredRoster = new RegisteredRoster(this, changes); }
                catch { changes.Dispose(); throw; }
            }
            if (registeredRoster.Apply(grid, player)) return true;
            RegistrationUnavailableReason = registeredRoster.Failure;
            registeredRoster.Dispose(); registeredRoster = null; currentRegisteredBinding = null;
            registrationUnavailable = true;
            return false;
        }

        private sealed class RegisteredRoster : IDisposable
        {
            internal sealed class Binding
            {
                internal Object Graphic;
                internal Renderer Renderer;
                internal Canvas Canvas;
                internal Transform Transform;
                internal GameObject GameObject;
                internal Component CanvasScope;
                internal ContentOwner Owner;
                internal bool Dirty = true;
                internal readonly List<Transform> Ancestors = new();
                internal readonly List<int> Order = new();
                internal int RootRank, ComponentRank;
            }
            private sealed class Ancestor
            {
                internal readonly HashSet<Binding> Graphics = new();
                internal Transform Parent;
                internal int Sibling;
            }
            private sealed class Bucket
            {
                internal readonly List<Binding> Renderers = new(), Canvases = new();
            }
            private readonly SceneVisibility2D owner;
            private readonly NativeVisibilityChanges changes;
            private readonly Dictionary<EntityId, Binding> bindings = new();
            private readonly Dictionary<Transform, Ancestor> descendants = new();
            private readonly Dictionary<Component, bool> ownerVisibility = new();
            private readonly Dictionary<Transform, Bucket> buckets = new();
            private readonly Dictionary<Transform, int> rootRanks = new();
            private readonly List<Transform> roots = new(), previousRoots = new();
            private readonly List<Binding> snapshot = new();
            private readonly List<Component> members = new();
            private readonly Action<Object> changed;
            private readonly Action<EntityId> destroyed;
            private readonly Comparison<Binding> compare;
            private Transform player;
            private IVisibilityQuery2D grid;
            private ulong revision;
            private bool orderDirty = true, disposed, assemblyChanged;
            internal bool Active => !disposed && Failure == null;
            internal int Count => bindings.Count;
            internal string Failure { get; private set; }
            internal long BootstrapQueries { get; private set; }
            internal long BindingResolutions { get; private set; }

            internal RegisteredRoster(SceneVisibility2D owner, NativeVisibilityChanges changes)
            {
                this.owner = owner; this.changes = changes;
                changed = Changed; destroyed = Destroyed;
                compare = Compare;
                // Include inactive pooled graphics once; active state is still read on each Apply.
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Register(renderer);
                foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Register(canvas);
                BootstrapQueries = 2;
                AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
            }

            private void AssemblyLoaded(object sender, AssemblyLoadEventArgs args)
            {
                if (args.LoadedAssembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal)) assemblyChanged = true;
            }

            private void Register(Object graphic)
            {
                if (!graphic) return;
                var component = graphic as Component;
                if (!component || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded) return;
                if (graphic is Renderer renderer && renderer is not TilemapRenderer)
                {
                    if (!bindings.ContainsKey(renderer.GetEntityId()))
                        bindings.Add(renderer.GetEntityId(), new Binding { Graphic = renderer, Renderer = renderer, Transform = renderer.transform, GameObject = renderer.gameObject });
                    else return;
                }
                else if (graphic is Canvas canvas)
                {
                    if (!bindings.ContainsKey(canvas.GetEntityId()))
                        bindings.Add(canvas.GetEntityId(), new Binding { Graphic = canvas, Canvas = canvas, Transform = canvas.transform, GameObject = canvas.gameObject });
                    else return;
                }
                else return;
                if (bindings.Count > 32768) { Failure = "Registered visibility graphic limit exceeded."; return; }
                orderDirty = true;
            }

            private void Changed(Object item)
            {
                if (item is Renderer or Canvas) { Register(item); return; }
                if (item is Transform transform)
                {
                    if (!descendants.TryGetValue(transform, out var ancestor)) return;
                    var parent = transform.parent;
                    int sibling = transform.GetSiblingIndex();
                    bool reparented = !ReferenceEquals(ancestor.Parent, parent);
                    if (reparented) foreach (var binding in ancestor.Graphics) binding.Dirty = true;
                    if (reparented || ancestor.Sibling != sibling) orderDirty = true;
                    ancestor.Parent = parent; ancestor.Sibling = sibling;
                    return;
                }
                if (item is GameObject gameObject && descendants.TryGetValue(gameObject.transform, out var affected))
                    foreach (var binding in affected.Graphics) binding.Dirty = true;
            }

            private void Destroyed(EntityId identity)
            {
                if (!bindings.TryGetValue(identity, out var binding)) return;
                Unlink(binding); bindings.Remove(identity); orderDirty = true;
            }

            private bool Sync()
            {
                using var sample = owner.DiscoveryMarker.Auto();
                if (assemblyChanged) { Failure = "Engine assembly set changed; rebuild through synchronous discovery."; return false; }
                try { changes.Drain(changed, destroyed); }
                catch (Exception error) { Failure = (error.InnerException ?? error).Message; return false; }
                return Failure == null;
            }

            internal void PresentationChanged()
            {
                // An enabled Canvas can synchronously move owners or publish new graphics.
                // Position/visibility answers never survive that boundary, even in the same frame.
                ownerVisibility.Clear();
                Sync();
            }

            private void Unlink(Binding binding)
            {
                foreach (var ancestor in binding.Ancestors)
                {
                    if (!descendants.TryGetValue(ancestor, out var set)) continue;
                    set.Graphics.Remove(binding);
                    if (set.Graphics.Count == 0) descendants.Remove(ancestor);
                }
                binding.Ancestors.Clear();
            }

            private void Resolve(Binding binding)
            {
                BindingResolutions++;
                Unlink(binding);
                Component primary = null, secondary = null;
                bool content = false, actualPlayer = false;
                for (var current = binding.Transform; current; current = current.parent)
                {
                    binding.Ancestors.Add(current);
                    if (!descendants.TryGetValue(current, out var set))
                    {
                        descendants.Add(current, set = new Ancestor { Parent = current.parent, Sibling = current.GetSiblingIndex() });
                    }
                    set.Graphics.Add(binding);
                    actualPlayer |= current == player;
                    if (!primary && !secondary)
                    {
                        primary = owner.policy.FindPrimaryOwner(current);
                        if (!primary) secondary = owner.policy.FindSecondaryOwner(current);
                    }
                    if (!content && !primary && !secondary)
                        content = (owner.contentLayers & (1 << current.gameObject.layer)) != 0 || owner.policy.HasContentComponent(current);
                }
                binding.Owner = new ContentOwner(primary, secondary, content, playerHierarchy: actualPlayer);
                binding.CanvasScope = binding.Canvas ? owner.policy.FindCanvasScope(binding.Transform) : null;
                binding.Dirty = false;
            }

            internal bool ShouldHide(Binding binding)
            {
                if (!binding.Graphic || !binding.Transform) return false;
                if (binding.Dirty || binding.Owner.HasOwner && !binding.Owner.PrimaryOwner && !binding.Owner.SecondaryOwner) Resolve(binding);
                var content = binding.Owner;
                if (content.PlayerHierarchy) return false;
                if (content.HasOwner)
                {
                    var anchor = content.PrimaryOwner ? content.PrimaryOwner : content.SecondaryOwner;
                    if (owner.policy.IsAlwaysVisible(anchor)) return false;
                    if (!ownerVisibility.TryGetValue(anchor, out bool hidden))
                    {
                        hidden = !owner.IsVisible(grid, owner.policy.GetOwnerPosition(anchor));
                        ownerVisibility.Add(anchor, hidden);
                    }
                    return hidden;
                }
                return content.Content && !owner.IsVisible(grid, binding.Renderer ? binding.Renderer.bounds.center : binding.Transform.position);
            }

            internal Component CanvasScope(Binding binding)
            {
                if (binding.Dirty) Resolve(binding);
                return binding.CanvasScope;
            }

            private void RefreshRoots()
            {
                using var sample = owner.DiscoveryMarker.Auto();
                owner.sceneRoots.Clear(); owner.queriedScenes.Clear();
                for (int i=0; i<SceneManager.sceneCount; i++) owner.AppendSceneRoots(SceneManager.GetSceneAt(i));
                owner.AppendSceneRoots(player.gameObject.scene);
                if (owner.policy.PersistentSceneAnchor) owner.AppendSceneRoots(owner.policy.PersistentSceneAnchor.scene);
                previousRoots.Clear(); previousRoots.AddRange(roots); roots.Clear(); rootRanks.Clear();
                foreach (var root in owner.sceneRoots)
                {
                    if (!root) continue;
                    var transform = root.transform;
                    rootRanks[transform] = roots.Count; roots.Add(transform);
                }
                if (roots.Count != previousRoots.Count) orderDirty = true;
                else for (int i=0; i<roots.Count; i++) if (roots[i] != previousRoots[i]) { orderDirty = true; break; }
            }

            private void Order()
            {
                if (!orderDirty) return;
                using var sample = owner.DiscoveryMarker.Auto();
                foreach (var bucket in buckets.Values) { bucket.Renderers.Clear(); bucket.Canvases.Clear(); }
                foreach (var binding in bindings.Values)
                {
                    if (!binding.Graphic || !binding.Transform) continue;
                    var root = binding.Transform.root;
                    if (!rootRanks.TryGetValue(root, out binding.RootRank)) continue;
                    binding.Order.Clear();
                    for (var item = binding.Transform; item && item != root; item = item.parent) binding.Order.Add(item.GetSiblingIndex());
                    binding.Order.Reverse();
                    members.Clear(); binding.Transform.GetComponents(members);
                    binding.ComponentRank = members.IndexOf((Component)binding.Graphic);
                    if (!buckets.TryGetValue(root, out var bucket)) buckets.Add(root, bucket = new Bucket());
                    (binding.Renderer ? bucket.Renderers : bucket.Canvases).Add(binding);
                }
                foreach (var bucket in buckets.Values) { bucket.Renderers.Sort(compare); bucket.Canvases.Sort(compare); }
                orderDirty = false;
            }

            private static int Compare(Binding left, Binding right)
            {
                int length = Math.Min(left.Order.Count, right.Order.Count);
                for (int i=0; i<length; i++) { int order = left.Order[i].CompareTo(right.Order[i]); if (order != 0) return order; }
                int depth = left.Order.Count.CompareTo(right.Order.Count);
                return depth != 0 ? depth : left.ComponentRank.CompareTo(right.ComponentRank);
            }

            private void Snapshot(List<Binding> graphics)
            {
                snapshot.Clear();
                // Match the original active-only per-root native query snapshot. A
                // callback may activate another child later in this same root.
                foreach (var binding in graphics)
                    if (binding.Graphic && binding.GameObject && binding.GameObject.activeInHierarchy) snapshot.Add(binding);
            }

            internal bool Apply(IVisibilityQuery2D grid, Transform player)
            {
                this.grid = grid; ownerVisibility.Clear();
                ulong currentRevision = owner.policy.OwnerCreationRevision;
                if (this.player != player || revision != currentRevision)
                {
                    this.player = player; revision = currentRevision;
                    foreach (var binding in bindings.Values) binding.Dirty = true;
                }
                RefreshRoots();
                if (!Sync()) return false;
                foreach (var root in roots)
                {
                    if (!root || !root.gameObject.activeInHierarchy) continue;
                    Order();
                    if (!buckets.TryGetValue(root, out var bucket)) continue;
                    Snapshot(bucket.Renderers);
                    using var sample = owner.RendererMarker.Auto();
                    foreach (var binding in snapshot)
                    {
                        var renderer = binding.Renderer;
                        if (!renderer || !binding.GameObject.activeInHierarchy || (renderer.hideFlags & HideFlags.DontSave) != 0) continue;
                        owner.currentRegisteredBinding = binding;
                        owner.ApplyRenderer(renderer, grid, player);
                        if (Failure != null) return false;
                    }
                }
                if (!Sync()) return false;
                foreach (var root in roots)
                {
                    if (!root || !root.gameObject.activeInHierarchy) continue;
                    Order();
                    if (!buckets.TryGetValue(root, out var bucket)) continue;
                    Snapshot(bucket.Canvases);
                    using var sample = owner.CanvasMarker.Auto();
                    foreach (var binding in snapshot)
                    {
                        var canvas = binding.Canvas;
                        if (!canvas || !binding.GameObject.activeInHierarchy || (canvas.hideFlags & HideFlags.DontSave) != 0) continue;
                        owner.currentRegisteredBinding = binding;
                        owner.ApplyCanvas(canvas, grid, player);
                        if (Failure != null) return false;
                    }
                }
                owner.currentRegisteredBinding = null;
                return true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true; AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
                changes.Dispose(); bindings.Clear(); descendants.Clear(); ownerVisibility.Clear(); buckets.Clear();
                roots.Clear(); previousRoots.Clear(); snapshot.Clear(); members.Clear();
            }
        }
    }
}
