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
        [UnityEngine.Scripting.Preserve]
        public bool LocalRosterUpdatesEnabled { get; set; } = true;
        [UnityEngine.Scripting.Preserve]
        public long RegisteredOrderEntries => registeredRoster?.OrderEntries ?? 0;
        [UnityEngine.Scripting.Preserve]
        public long RegisteredFastRendererUpdates { get; private set; }
        [UnityEngine.Scripting.Preserve]
        public long RegisteredSnapshotEntries => registeredRoster?.SnapshotEntries ?? 0;
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
                internal bool Active;
                internal Transform Root;
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
                internal readonly HashSet<Binding> Members = new();
                internal readonly List<Binding> Renderers = new(), Canvases = new();
                internal readonly List<Binding> ActiveRenderers = new(), ActiveCanvases = new();
                internal bool OrderDirty = true, ActiveDirty = true;
            }
            private readonly SceneVisibility2D owner;
            private readonly NativeVisibilityChanges changes;
            private readonly Dictionary<EntityId, Binding> bindings = new();
            private readonly Dictionary<Transform, Ancestor> descendants = new();
            private readonly HashSet<Binding> ancestryDirty = new();
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
            private int layers = int.MinValue;
            private bool orderDirty = true, disposed, assemblyChanged;
            private bool localUpdates;
            internal bool Active => !disposed && Failure == null;
            internal int Count => bindings.Count;
            internal string Failure { get; private set; }
            internal long BootstrapQueries { get; private set; }
            internal long BindingResolutions { get; private set; }
            internal long OrderEntries { get; private set; }
            internal long SnapshotEntries { get; private set; }

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

            private void RefreshActive(Binding binding)
            {
                bool active = binding.GameObject && binding.GameObject.activeInHierarchy;
                if (binding.Active == active) return;
                binding.Active = active;
                if (binding.Root && buckets.TryGetValue(binding.Root, out var bucket)) bucket.ActiveDirty = true;
            }

            private void RefreshRoot(Binding binding)
            {
                var root = binding.Transform ? binding.Transform.root : null;
                if (ReferenceEquals(root, binding.Root)) return;
                if (!ReferenceEquals(binding.Root, null) && buckets.TryGetValue(binding.Root, out var previous))
                {
                    previous.Members.Remove(binding);
                    previous.OrderDirty = previous.ActiveDirty = true;
                    if (previous.Members.Count == 0) buckets.Remove(binding.Root);
                }
                binding.Root = root;
                if (!root) return;
                if (!buckets.TryGetValue(root, out var bucket)) buckets.Add(root, bucket = new Bucket());
                bucket.Members.Add(binding);
                bucket.OrderDirty = bucket.ActiveDirty = true;
            }

            private void MarkOrder(Binding binding)
            {
                orderDirty = true;
                if (binding.Root && buckets.TryGetValue(binding.Root, out var bucket)) bucket.OrderDirty = true;
            }

            private void Register(Object graphic)
            {
                if (!graphic || graphic is not Component component) return;
                var gameObject = component.gameObject;
                if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded || graphic is TilemapRenderer) return;
                if (graphic is not Renderer && graphic is not Canvas) return;
                var identity = graphic.GetEntityId();
                if (bindings.TryGetValue(identity, out var existing)) { RefreshActive(existing); return; }
                var binding = new Binding { Graphic = graphic, Renderer = graphic as Renderer, Canvas = graphic as Canvas,
                    Transform = component.transform, GameObject = gameObject };
                bindings.Add(identity, binding);
                // Index inactive pooled descendants too, so an ancestor activation is
                // published before a later root's snapshot without scanning the pool.
                Index(binding); RefreshRoot(binding); RefreshActive(binding);
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
                    if (reparented) foreach (var binding in ancestor.Graphics)
                    { binding.Dirty = true; ancestryDirty.Add(binding); RefreshActive(binding); }
                    if (reparented || ancestor.Sibling != sibling)
                        foreach (var binding in ancestor.Graphics) MarkOrder(binding);
                    ancestor.Parent = parent; ancestor.Sibling = sibling;
                    return;
                }
                if (item is GameObject gameObject && descendants.TryGetValue(gameObject.transform, out var affected))
                    foreach (var binding in affected.Graphics) { binding.Dirty = true; RefreshActive(binding); }
            }

            private void Destroyed(EntityId identity)
            {
                if (!bindings.TryGetValue(identity, out var binding)) return;
                Unlink(binding); bindings.Remove(identity); MarkOrder(binding);
                if (!ReferenceEquals(binding.Root, null) && buckets.TryGetValue(binding.Root, out var bucket))
                {
                    bucket.Members.Remove(binding); bucket.ActiveDirty = true;
                    if (bucket.Members.Count == 0) buckets.Remove(binding.Root);
                }
            }

            private bool Sync()
            {
                using var sample = owner.DiscoveryMarker.Auto();
                using var syncSample = owner.RegistrationSyncMarker.Auto();
                if (assemblyChanged) { Failure = "Engine assembly set changed; rebuild through synchronous discovery."; return false; }
                try
                {
                    changes.Drain(changed, destroyed);
                    foreach (var binding in ancestryDirty) if (binding.Graphic) { Index(binding); RefreshRoot(binding); }
                    ancestryDirty.Clear();
                }
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

            private void Index(Binding binding)
            {
                Unlink(binding);
                for (var current = binding.Transform; current; current = current.parent)
                {
                    binding.Ancestors.Add(current);
                    if (!descendants.TryGetValue(current, out var ancestor))
                        descendants.Add(current, ancestor = new Ancestor { Parent = current.parent, Sibling = current.GetSiblingIndex() });
                    ancestor.Graphics.Add(binding);
                }
            }

            private void Resolve(Binding binding)
            {
                BindingResolutions++;
                Component primary = null, secondary = null;
                bool content = false, actualPlayer = false;
                foreach (var current in binding.Ancestors)
                {
                    if (!current) continue;
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

            private void Order(Transform requestedRoot)
            {
                if (localUpdates)
                {
                    if (!buckets.TryGetValue(requestedRoot, out var bucket)) return;
                    if (bucket.OrderDirty)
                    {
                        using var discovery = owner.DiscoveryMarker.Auto();
                        using var timing = owner.RegistrationOrderMarker.Auto();
                        bucket.Renderers.Clear(); bucket.Canvases.Clear();
                        foreach (var binding in bucket.Members)
                        {
                            if (!binding.Graphic || !binding.Transform) continue;
                            PrepareOrder(binding, requestedRoot);
                            (binding.Renderer ? bucket.Renderers : bucket.Canvases).Add(binding);
                        }
                        bucket.Renderers.Sort(compare); bucket.Canvases.Sort(compare);
                        bucket.OrderDirty = false; bucket.ActiveDirty = true;
                    }
                    if (bucket.ActiveDirty)
                    {
                        bucket.ActiveRenderers.Clear(); bucket.ActiveCanvases.Clear();
                        foreach (var binding in bucket.Renderers) if (binding.Active) bucket.ActiveRenderers.Add(binding);
                        foreach (var binding in bucket.Canvases) if (binding.Active) bucket.ActiveCanvases.Add(binding);
                        bucket.ActiveDirty = false;
                    }
                    return;
                }
                if (!orderDirty) return;
                using var sample = owner.DiscoveryMarker.Auto();
                using var orderSample = owner.RegistrationOrderMarker.Auto();
                foreach (var bucket in buckets.Values) { bucket.Renderers.Clear(); bucket.Canvases.Clear(); }
                foreach (var binding in bindings.Values)
                {
                    if (!binding.Graphic || !binding.Transform) continue;
                    var root = binding.Transform.root;
                    if (!rootRanks.TryGetValue(root, out binding.RootRank)) continue;
                    PrepareOrder(binding, root);
                    if (!buckets.TryGetValue(root, out var bucket)) buckets.Add(root, bucket = new Bucket());
                    (binding.Renderer ? bucket.Renderers : bucket.Canvases).Add(binding);
                }
                foreach (var bucket in buckets.Values) { bucket.Renderers.Sort(compare); bucket.Canvases.Sort(compare); }
                orderDirty = false;
            }

            private void PrepareOrder(Binding binding, Transform root)
            {
                OrderEntries++;
                binding.Order.Clear();
                for (var item = binding.Transform; item && item != root; item = item.parent) binding.Order.Add(item.GetSiblingIndex());
                binding.Order.Reverse();
                members.Clear(); binding.Transform.GetComponents(members);
                binding.ComponentRank = members.IndexOf((Component)binding.Graphic);
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
                using var sample = owner.DiscoveryMarker.Auto();
                using var snapshotSample = owner.RegistrationSnapshotMarker.Auto();
                snapshot.Clear();
                // Match the original active-only per-root native query snapshot. A
                // callback may activate another child later in this same root.
                foreach (var binding in graphics)
                    {
                    SnapshotEntries++;
                    if (!binding.Active || !binding.Graphic) continue;
                    // A stable non-content graphic cannot acquire a fog override. Dirty
                    // structure/membership and owned releases always retain evaluation.
                    if (!binding.Dirty && !binding.Owner.HasOwner && !binding.Owner.Content
                        && !(binding.Renderer ? owner.hiddenRenderers.Contains(binding.Renderer) : owner.hiddenCanvases.Contains(binding.Canvas))) continue;
                    snapshot.Add(binding);
                }
            }

            internal bool Apply(IVisibilityQuery2D grid, Transform player)
            {
                this.grid = grid; ownerVisibility.Clear();
                if (localUpdates != owner.LocalRosterUpdatesEnabled)
                {
                    localUpdates = owner.LocalRosterUpdatesEnabled;
                    orderDirty = true;
                    foreach (var bucket in buckets.Values) bucket.OrderDirty = bucket.ActiveDirty = true;
                }
                ulong currentRevision = owner.policy.OwnerCreationRevision;
                if (this.player != player || revision != currentRevision || layers != owner.contentLayers)
                {
                    this.player = player; revision = currentRevision; layers = owner.contentLayers;
                    foreach (var binding in bindings.Values) binding.Dirty = true;
                }
                RefreshRoots();
                if (!Sync()) return false;
                foreach (var root in roots)
                {
                    if (!root || !root.gameObject.activeInHierarchy) continue;
                    Order(root);
                    if (!buckets.TryGetValue(root, out var bucket)) continue;
                    Snapshot(localUpdates ? bucket.ActiveRenderers : bucket.Renderers);
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
                    Order(root);
                    if (!buckets.TryGetValue(root, out var bucket)) continue;
                    Snapshot(localUpdates ? bucket.ActiveCanvases : bucket.Canvases);
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
                changes.Dispose(); bindings.Clear(); descendants.Clear(); ancestryDirty.Clear(); ownerVisibility.Clear(); buckets.Clear();
                roots.Clear(); previousRoots.Clear(); snapshot.Clear(); members.Clear();
            }
        }
    }
}
