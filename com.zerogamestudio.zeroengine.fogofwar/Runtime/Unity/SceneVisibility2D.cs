using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace ZeroEngine.FogOfWar.Unity
{
    /// <summary>Hides scene contents independently of the translucent atmosphere over the terrain.</summary>
    public partial class SceneVisibility2D : IDisposable
    {
        private readonly ProfilerMarker DiscoveryMarker, RendererMarker, CanvasMarker;
        private readonly ProfilerMarker RegistrationSyncMarker, RegistrationOrderMarker, RegistrationSnapshotMarker;
        private readonly VisibilityScenePolicy2D policy;
        private int contentLayers;
        public SceneVisibility2D(VisibilityScenePolicy2D policy, string profilerPrefix = "ZeroEngine.FogOfWar.Visibility")
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            DiscoveryMarker = new ProfilerMarker(profilerPrefix + ".Discovery");
            RendererMarker = new ProfilerMarker(profilerPrefix + ".Renderers");
            CanvasMarker = new ProfilerMarker(profilerPrefix + ".Canvases");
            RegistrationSyncMarker = new ProfilerMarker(profilerPrefix + ".Registration.Sync");
            RegistrationOrderMarker = new ProfilerMarker(profilerPrefix + ".Registration.Order");
            RegistrationSnapshotMarker = new ProfilerMarker(profilerPrefix + ".Registration.Snapshot");
        }
        // Only record overrides we actually own. Authored disabled/hidden state is never enabled.
        private readonly HashSet<Renderer> hiddenRenderers = new();
        private readonly HashSet<Canvas> hiddenCanvases = new();
        private readonly List<Renderer> releasedRenderers = new();
        private readonly List<Canvas> releasedCanvases = new();
        // One synchronous Apply owns these answers. Pool reuse, reparenting, layer changes and
        // late owner binding are rediscovered on every call, including calls in the same frame.
        private readonly Dictionary<Transform, ContentOwner> contentOwners = new();
        private readonly Dictionary<Vector2, bool> visiblePositions = new();
        private readonly List<Transform> ownerTrail = new();
        // Cache only components on this exact Transform. Ancestors, layers and positions remain
        // synchronous Apply results; creation revisions invalidate cached component absences.
        private readonly Dictionary<Transform, ContentOwner> localOwners = new();
        // Exact-Transform entries stay valid through reparenting and pool reuse. Bound their
        // lifetime by capacity instead of walking every cached ancestor on every Apply.
        private const int MaximumCachedLocalOwners = 4096;
        private bool boundedCache = true;
        [UnityEngine.Scripting.Preserve]
        public int LocalOwnerCacheCount => localOwners.Count;
        [UnityEngine.Scripting.Preserve]
        public int LocalOwnerCapacityClearCount { get; private set; }
        // Diagnostic per-world override; production uses the optimized path by default.
        [UnityEngine.Scripting.Preserve]
        public bool QueryChecksEnabled { get; set; } = true;
        private readonly HashSet<Transform> visitedOwners = new();
        private readonly List<Transform> obsoleteLocalOwners = new();
        public void SetOptimizationMode(bool enabled)
        {
            if (boundedCache == enabled) return;
            boundedCache = enabled;
            localOwners.Clear(); visitedOwners.Clear(); obsoleteLocalOwners.Clear();
        }
        private ulong ownerCreationRevision;
        private readonly List<GameObject> sceneRoots = new();
        private readonly List<GameObject> queriedSceneRoots = new();
#if UNITY_6000_0_OR_NEWER
        private readonly HashSet<ulong> queriedScenes = new();
#else
        private readonly HashSet<int> queriedScenes = new();
#endif
        [UnityEngine.Scripting.Preserve]
        public bool NativeRendererDiscoveryEnabled { get; set; }
        private readonly List<Renderer> queriedRenderers = new();
        private readonly List<Canvas> queriedCanvases = new();
        // A suppressed component we do not own cannot receive an override in this Apply.
        // Read its current state each time so an external release is observed immediately.
        [UnityEngine.Scripting.Preserve]
        public bool SkipExternallySuppressedContent { get; set; }
        [UnityEngine.Scripting.Preserve]
        public int SkippedExternalRenderers { get; private set; }
        [UnityEngine.Scripting.Preserve]
        public int SkippedExternalCanvases { get; private set; }

        [Serializable] private struct QueryDiagnostics
        {
            public long applies, rootQueries, renderersFound, canvasesFound, renderersProcessed, canvasesProcessed;
            public long ownerRequests, ownerHits, ancestorSteps, localOwnerQueries, localOwnerHits, componentClassifications;
            public long sightRequests, sightHits, presentationInvalidations, creationInvalidations, ownerTicks, sightTicks;
            public long hierarchyTransforms, hierarchyMembershipHits, hierarchyMembershipMisses, hierarchyComponentChecks;
        }
        [Serializable] private sealed class RootDiagnostic
        {
            public string name, scene;
            public int renderers, canvases, transforms;
        }
        [Serializable] private sealed class DiagnosticReport
        {
            public long stopwatchFrequency = System.Diagnostics.Stopwatch.Frequency;
            public bool enabled, fusedHierarchy;
            public int hierarchyQueries, hierarchyCacheEntries, hierarchyPreparedNodes;
            public long hierarchyResolvedGraphics, hierarchyFallbacks, inheritedMembershipReuses, hierarchySizeBypasses;
            public QueryDiagnostics counters;
            public RootDiagnostic[] roots;
        }
        private bool detailedDiagnostics;
        private QueryDiagnostics diagnostics;
        public bool DetailedDiagnosticsEnabled
        {
            get => detailedDiagnostics;
            set { if (detailedDiagnostics == value) return; detailedDiagnostics = value; if (value) diagnostics = default; }
        }
        // Snapshot-only allocations and native census happen at excluded diagnostic boundaries.
        public string DescribeDiagnostics()
        {
            var roots = new List<RootDiagnostic>();
            foreach (var root in sceneRoots)
                if (root) roots.Add(new RootDiagnostic { name = root.name, scene = root.scene.name,
                    transforms = root.GetComponentsInChildren<Transform>(false).Length,
                    renderers = root.GetComponentsInChildren<Renderer>(false).Length,
                    canvases = root.GetComponentsInChildren<Canvas>(false).Length });
            return JsonUtility.ToJson(new DiagnosticReport { enabled = detailedDiagnostics, fusedHierarchy = FusedHierarchyEnabled,
                hierarchyQueries = FusedHierarchyQueries, hierarchyCacheEntries = hierarchyCache.Count, hierarchyPreparedNodes = hierarchyNodes.Count,
                hierarchyResolvedGraphics = FusedHierarchyResolvedGraphics, hierarchyFallbacks = hierarchyFallbacks, inheritedMembershipReuses = inheritedMembershipReuses, hierarchySizeBypasses = FusedHierarchySizeBypasses, counters = diagnostics, roots = roots.ToArray() });
        }

        // Fresh typed Renderer/Canvas queries retain discovery order on every Apply.
        // Only their current ancestor union needs membership and ownership classification.
        // This joins discovery and ownership; it never caches visibility across Apply calls.
        [UnityEngine.Scripting.Preserve]
        public bool FusedHierarchyEnabled { get; set; } = true;
        [UnityEngine.Scripting.Preserve]
        public int FusedHierarchyQueries { get; private set; }
        [UnityEngine.Scripting.Preserve]
        public int FusedHierarchyNodeCount => hierarchyNodes.Count;
        [UnityEngine.Scripting.Preserve]
        public long FusedHierarchyResolvedGraphics { get; private set; }
        [UnityEngine.Scripting.Preserve]
        public long FusedHierarchySizeBypasses { get; private set; }
        // Bound setup work by the current native roster, independently for every scene root.
        // Dense roots keep the original synchronous owner path without building an ancestor graph.
        public const int MaximumFusedRootRenderers = 256;
        private long hierarchyFallbacks, inheritedMembershipReuses;
        private const int MaximumHierarchyNodes = 8192, MaximumMembershipComponents = 64;
        private sealed class HierarchyNode
        {
            public Transform Transform;
            public GameObject GameObject;
            public readonly List<Component> Members = new();
            public Component PrimaryOwner;
            public Component SecondaryOwner;
            public Component CanvasScope, NearestCanvasScope;
            public bool ContentComponent, ActualPlayerSubtree, OwnerReady;
            public HierarchyNode OwningNode;
            public int Index;
            public uint Epoch;
            public ulong OwnerRevision;
            public ContentOwner Owner;
        }
        private readonly struct HierarchyRenderer
        {
            public readonly Renderer Renderer;
            public readonly HierarchyNode Node;
            public HierarchyRenderer(Renderer renderer, HierarchyNode node) { Renderer = renderer; Node = node; }
        }
        private readonly struct HierarchyCanvas
        {
            public readonly Canvas Canvas;
            public readonly HierarchyNode Node;
            public HierarchyCanvas(Canvas canvas, HierarchyNode node) { Canvas = canvas; Node = node; }
        }
        private readonly Dictionary<Transform, HierarchyNode> hierarchyCache = new();
        private readonly List<Transform> hierarchyTrail = new();
        private readonly List<HierarchyNode> hierarchyNodes = new();
        private readonly List<HierarchyRenderer> hierarchyRenderers = new();
        private readonly List<HierarchyCanvas> hierarchyCanvases = new();
        private readonly Dictionary<GameObject, Vector2Int> hierarchyCanvasRanges = new();
        private bool hierarchySnapshotValid, hierarchyCacheFull;
        private HierarchyNode currentHierarchyNode;
        private uint hierarchyEpoch;

        private void BeginHierarchyApply(bool enabled)
        {
            hierarchySnapshotValid = enabled;
            currentHierarchyNode = null;
            hierarchyNodes.Clear();
            hierarchyCanvases.Clear();
            hierarchyCanvasRanges.Clear();
            if (hierarchyCacheFull || ++hierarchyEpoch == 0)
            { hierarchyCache.Clear(); hierarchyCacheFull = false; }
        }

        private void ReadHierarchyMembership(HierarchyNode node, bool inheritedContent)
        {
            // Under inherited content, only a nearer owner/door can change ownership.
            // Their existing creation revisions and every cached positive identity stay fresh.
            // A lost inherited answer always returns to complete native membership validation.
            if (inheritedContent && node.Members.Count != 0
                && node.OwnerRevision == policy.OwnerCreationRevision
                && (ReferenceEquals(node.PrimaryOwner, null) || node.PrimaryOwner) && (ReferenceEquals(node.SecondaryOwner, null) || node.SecondaryOwner)
                && (ReferenceEquals(node.CanvasScope, null) || node.CanvasScope))
            {
                inheritedMembershipReuses++;
                return;
            }
            node.OwnerRevision = policy.OwnerCreationRevision;
            int count = node.GameObject.GetComponentCount();
            bool current = count <= MaximumMembershipComponents && count == node.Members.Count;
            if (current)
                for (int i = 0; i < count; i++)
                {
                    if (detailedDiagnostics) diagnostics.hierarchyComponentChecks++;
                    if (!node.Members[i]) { current = false; break; }
                }
            if (current)
            {
                if (detailedDiagnostics) diagnostics.hierarchyMembershipHits++;
                return;
            }
            if (detailedDiagnostics) diagnostics.hierarchyMembershipMisses++;
            node.GameObject.GetComponents(node.Members);
            node.PrimaryOwner = null; node.SecondaryOwner = null; node.CanvasScope = null; node.ContentComponent = false;
            foreach (var component in node.Members)
            {
                var kind = component ? policy.Classify(component) : VisibilityComponentKind.None;
                if ((kind & VisibilityComponentKind.CanvasScope) != 0 && ReferenceEquals(node.CanvasScope, null)) node.CanvasScope = component;
                if ((kind & VisibilityComponentKind.PrimaryOwner) != 0 && ReferenceEquals(node.PrimaryOwner, null)) node.PrimaryOwner = component;
                else if ((kind & VisibilityComponentKind.SecondaryOwner) != 0 && ReferenceEquals(node.SecondaryOwner, null)) node.SecondaryOwner = component;
                else if ((kind & VisibilityComponentKind.Content) != 0) node.ContentComponent = true;
            }
        }

        private bool PrepareHierarchyRoot(GameObject root, IVisibilityQuery2D grid, Transform player)
        {
            int first = hierarchyNodes.Count, firstCanvas = hierarchyCanvases.Count;
            hierarchyRenderers.Clear();
            root.GetComponentsInChildren(false, queriedCanvases);
            FusedHierarchyQueries++;
            if (detailedDiagnostics) diagnostics.rootQueries++;
            foreach (var renderer in queriedRenderers)
            {
                if (!renderer) continue;
                var node = PrepareHierarchyAncestors(renderer.transform, first, player);
                if (node == null) return false;
                hierarchyRenderers.Add(new HierarchyRenderer(renderer, node));
            }
            foreach (var canvas in queriedCanvases)
            {
                if (!canvas) continue;
                var node = PrepareHierarchyAncestors(canvas.transform, first, player);
                if (node == null) return false;
                hierarchyCanvases.Add(new HierarchyCanvas(canvas, node));
            }
            hierarchyCanvasRanges[root] = new Vector2Int(firstCanvas, hierarchyCanvases.Count - firstCanvas);
            if (detailedDiagnostics)
            {
                diagnostics.hierarchyTransforms += hierarchyNodes.Count - first;
                diagnostics.canvasesFound += hierarchyCanvases.Count - firstCanvas;
            }
            return true;
        }

        private HierarchyNode PrepareHierarchyAncestors(Transform item, int first, Transform player)
        {
            hierarchyTrail.Clear();
            HierarchyNode parent = null;
            for (var current = item; current; current = current.parent)
            {
                if (hierarchyCache.TryGetValue(current, out var ready) && ready.Epoch == hierarchyEpoch)
                {
                    // Distinct scene roots cannot share ancestry before presentation writes.
                    if (ready.Index < first) return null;
                    parent = ready;
                    break;
                }
                if (hierarchyNodes.Count + hierarchyTrail.Count >= MaximumHierarchyNodes) return null;
                hierarchyTrail.Add(current);
            }
            // The current native parent chain is walked once per needed node in this Apply.
            // Unvisited descendants cannot affect a graphic's content inheritance.
            for (int i = hierarchyTrail.Count - 1; i >= 0; i--)
            {
                var current = hierarchyTrail[i];
                if (!hierarchyCache.TryGetValue(current, out var node))
                {
                    if (hierarchyCache.Count >= MaximumHierarchyNodes) { hierarchyCacheFull = true; return null; }
                    node = new HierarchyNode { Transform = current, GameObject = current.gameObject };
                    hierarchyCache.Add(current, node);
                }
                node.Epoch = hierarchyEpoch; node.Index = hierarchyNodes.Count;
                node.OwnerReady = false; node.OwningNode = null;
                hierarchyNodes.Add(node);
                ReadHierarchyMembership(node, parent != null
                    && (parent.Owner.HasOwner || parent.Owner.Content || parent.ActualPlayerSubtree));
                node.NearestCanvasScope = node.CanvasScope ?? parent?.NearestCanvasScope;
                node.ActualPlayerSubtree = node.Transform == player || parent != null && parent.ActualPlayerSubtree;
                if (node.PrimaryOwner || node.SecondaryOwner)
                {
                    bool playerHierarchy = node.ActualPlayerSubtree || node.PrimaryOwner && policy.IsAlwaysVisible(node.PrimaryOwner);
                    node.Owner = new ContentOwner(node.PrimaryOwner, node.PrimaryOwner ? null : node.SecondaryOwner, false, playerHierarchy: playerHierarchy);
                    node.OwningNode = node;
                }
                else if (node.ActualPlayerSubtree) node.Owner = new ContentOwner(null, null, false, playerHierarchy: true);
                else
                {
                    var owner = parent == null ? default : parent.Owner;
                    node.Owner = owner.HasOwner || owner.Content ? owner
                        : new ContentOwner(null, null, node.ContentComponent || (contentLayers & (1 << node.GameObject.layer)) != 0);
                    node.OwningNode = parent?.OwningNode;
                }
                parent = node;
            }
            return parent;
        }

        private ContentOwner ResolveHierarchyOwner(HierarchyNode node, IVisibilityQuery2D grid)
        {
            FusedHierarchyResolvedGraphics++;
            if (node.ActualPlayerSubtree || node.OwningNode == null) return node.Owner;
            var anchor = node.OwningNode;
            if (!anchor.OwnerReady)
            {
                var owner = anchor.Owner;
                bool hidden = !owner.PlayerHierarchy && !IsVisible(grid,
                    policy.GetOwnerPosition(owner.PrimaryOwner ? owner.PrimaryOwner : owner.SecondaryOwner));
                anchor.Owner = new ContentOwner(owner.PrimaryOwner, owner.SecondaryOwner, false, hidden, owner.PlayerHierarchy);
                anchor.OwnerReady = true;
            }
            return anchor.Owner;
        }

        private void ClearHierarchy()
        {
            hierarchySnapshotValid = false; currentHierarchyNode = null;
            hierarchyCache.Clear(); hierarchyTrail.Clear(); hierarchyNodes.Clear();
            hierarchyRenderers.Clear(); hierarchyCanvases.Clear(); hierarchyCanvasRanges.Clear();
        }

        private readonly struct ContentOwner
        {
            public readonly Component PrimaryOwner;
            public readonly Component SecondaryOwner;
            public readonly bool Content;
            public readonly bool HasOwner;
            public readonly bool Hidden;
            public readonly bool PlayerHierarchy;

            public ContentOwner(Component primaryOwner, Component secondaryOwner, bool content, bool hidden = false,
                bool playerHierarchy = false)
            {
                PrimaryOwner = primaryOwner;
                SecondaryOwner = secondaryOwner;
                Content = content;
                HasOwner = !ReferenceEquals(primaryOwner, null) || !ReferenceEquals(secondaryOwner, null);
                Hidden = hidden;
                PlayerHierarchy = playerHierarchy;
            }
        }

        public void Apply(IVisibilityQuery2D grid, Transform player)
        {
            if (detailedDiagnostics) diagnostics.applies++;
            contentOwners.Clear();
            visiblePositions.Clear();
            SkippedExternalRenderers = SkippedExternalCanvases = 0;
            if (!boundedCache) visitedOwners.Clear();
            if (!Application.isPlaying || ownerCreationRevision != policy.OwnerCreationRevision)
            {
                if (detailedDiagnostics) diagnostics.creationInvalidations++;
                localOwners.Clear();
                ownerCreationRevision = policy.OwnerCreationRevision;
            }
            BeginHierarchyApply(FusedHierarchyEnabled && Application.isPlaying && player && !NativeRendererDiscoveryEnabled);
            // The caller resolves project layers on the main thread.
            contentLayers = policy.ContentLayers;
            if (ApplyRegistered(grid, player))
            {
                ReleaseInactiveOverrides();
                return;
            }
            // Keep synchronous discovery, including pooled/reparented content. During gameplay
            // reusable scene/component lists replace the two fresh native Object[] results.
            // EditMode and a missing player retain the original global discovery contract.
            if (Application.isPlaying && player)
            {
                sceneRoots.Clear();
                queriedScenes.Clear();
                for (int i = 0; i < SceneManager.sceneCount; i++) AppendSceneRoots(SceneManager.GetSceneAt(i));
                AppendSceneRoots(player.gameObject.scene);
                if (policy.PersistentSceneAnchor) AppendSceneRoots(policy.PersistentSceneAnchor.scene);
                if (NativeRendererDiscoveryEnabled)
                {
                    Renderer[] renderers;
                    using (DiscoveryMarker.Auto()) renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    using (RendererMarker.Auto())
                        foreach (var renderer in renderers)
                            if ((!QueryChecksEnabled || renderer is not TilemapRenderer) && renderer
                                && renderer.gameObject.activeInHierarchy && (renderer.hideFlags & HideFlags.DontSave) == 0)
                                ApplyRenderer(renderer, grid, player);
                }
                else foreach (var root in sceneRoots)
                {
                    if (!root || !root.activeInHierarchy) continue;
                    using (DiscoveryMarker.Auto()) root.GetComponentsInChildren(false, queriedRenderers);
                    if (detailedDiagnostics) { diagnostics.rootQueries++; diagnostics.renderersFound += queriedRenderers.Count; }
                    if (hierarchySnapshotValid) FusedHierarchyQueries++;
                    if (hierarchySnapshotValid && queriedRenderers.Count <= MaximumFusedRootRenderers)
                    {
                        bool prepared;
                        using (DiscoveryMarker.Auto()) prepared = PrepareHierarchyRoot(root, grid, player);
                        if (prepared)
                        {
                            using (RendererMarker.Auto())
                                foreach (var entry in hierarchyRenderers)
                                {
                                    var renderer = entry.Renderer;
                                    if ((!QueryChecksEnabled || renderer is not TilemapRenderer) && renderer
                                        && (hierarchySnapshotValid || renderer.gameObject.activeInHierarchy) && (renderer.hideFlags & HideFlags.DontSave) == 0)
                                    {
                                        currentHierarchyNode = entry.Node;
                                        ApplyRenderer(renderer, grid, player);
                                    }
                                }
                            currentHierarchyNode = null;
                            continue;
                        }
                        hierarchyFallbacks++;
                        hierarchySnapshotValid = false;
                    }
                    else if (hierarchySnapshotValid) FusedHierarchySizeBypasses++;
                    using (RendererMarker.Auto())
                        foreach (var renderer in queriedRenderers)
                            if ((!QueryChecksEnabled || renderer is not TilemapRenderer) && renderer && renderer.gameObject.activeInHierarchy && (renderer.hideFlags & HideFlags.DontSave) == 0)
                                ApplyRenderer(renderer, grid, player);
                }
                // As before, renderer discovery/overrides precede canvas discovery/overrides.
                foreach (var root in sceneRoots)
                {
                    if (!root || !root.activeInHierarchy) continue;
                    if (hierarchySnapshotValid && hierarchyCanvasRanges.TryGetValue(root, out var range))
                    {
                        using (CanvasMarker.Auto())
                            for (int i = range.x; i < range.x + range.y; i++)
                            {
                                var entry = hierarchyCanvases[i];
                                var canvas = entry.Canvas;
                                if (canvas && (hierarchySnapshotValid || canvas.gameObject.activeInHierarchy) && (canvas.hideFlags & HideFlags.DontSave) == 0)
                                {
                                    currentHierarchyNode = entry.Node;
                                    ApplyCanvas(canvas, grid, player);
                                }
                            }
                        currentHierarchyNode = null;
                        continue;
                    }
                    using (DiscoveryMarker.Auto()) root.GetComponentsInChildren(false, queriedCanvases);
                    if (detailedDiagnostics) { diagnostics.rootQueries++; diagnostics.canvasesFound += queriedCanvases.Count; }
                    using (CanvasMarker.Auto())
                        foreach (var canvas in queriedCanvases)
                            if (canvas && canvas.gameObject.activeInHierarchy && (canvas.hideFlags & HideFlags.DontSave) == 0)
                                ApplyCanvas(canvas, grid, player);
                }
            }
            else
            {
                foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType(typeof(Renderer), FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    ApplyRenderer(renderer, grid, player);
                foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType(typeof(Canvas), FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    ApplyCanvas(canvas, grid, player);
            }
            ReleaseInactiveOverrides();
            if (!boundedCache)
            {
                // Legacy mode is retained as the per-world opt-out and same-revision audit arm.
                obsoleteLocalOwners.Clear();
                foreach (var pair in localOwners)
                    if (!visitedOwners.Contains(pair.Key)) obsoleteLocalOwners.Add(pair.Key);
                foreach (var item in obsoleteLocalOwners) localOwners.Remove(item);
            }
        }

        private void ReleaseInactiveOverrides()
        {
            // Return overrides promptly on despawn, even before a pooled object is reused.
            releasedRenderers.Clear();
            foreach (var renderer in hiddenRenderers)
                if (!renderer || !renderer.gameObject.activeInHierarchy) releasedRenderers.Add(renderer);
            foreach (var renderer in releasedRenderers)
            {
                if (renderer) renderer.forceRenderingOff = false;
                hiddenRenderers.Remove(renderer);
            }
            releasedCanvases.Clear();
            foreach (var canvas in hiddenCanvases)
                if (!canvas || !canvas.gameObject.activeInHierarchy) releasedCanvases.Add(canvas);
            foreach (var canvas in releasedCanvases)
            {
                if (canvas) canvas.enabled = true;
                hiddenCanvases.Remove(canvas);
            }
        }

        private void AppendSceneRoots(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
#if UNITY_6000_0_OR_NEWER
            if (!queriedScenes.Add(scene.handle.GetRawData())) return;
#else
            if (!queriedScenes.Add(scene.handle)) return;
#endif
            // Unity's List overload needs capacity greater than rootCount to avoid an public array.
            if (queriedSceneRoots.Capacity <= scene.rootCount) queriedSceneRoots.Capacity = scene.rootCount + 1;
            scene.GetRootGameObjects(queriedSceneRoots);
            sceneRoots.AddRange(queriedSceneRoots);
        }

        private void ApplyRenderer(Renderer renderer, IVisibilityQuery2D grid, Transform player)
        {
            if (detailedDiagnostics) diagnostics.renderersProcessed++;
            if (SkipExternallySuppressedContent && renderer.forceRenderingOff && !hiddenRenderers.Contains(renderer))
            {
                SkippedExternalRenderers++;
                return;
            }
            bool hide = renderer is not TilemapRenderer && ShouldHide(hierarchySnapshotValid && currentHierarchyNode != null ? currentHierarchyNode.Transform : renderer.transform, renderer, null, grid, player);
            if (hide && !renderer.forceRenderingOff)
            {
                renderer.forceRenderingOff = true;
                hiddenRenderers.Add(renderer);
                InvalidateAfterPresentationChange(mayInvokeCallbacks: false);
            }
            else if (!hide && hiddenRenderers.Remove(renderer))
            {
                renderer.forceRenderingOff = false;
                InvalidateAfterPresentationChange(mayInvokeCallbacks: false);
            }
        }

        private void ApplyCanvas(Canvas canvas, IVisibilityQuery2D grid, Transform player)
        {
            if (detailedDiagnostics) diagnostics.canvasesProcessed++;
            if (SkipExternallySuppressedContent && !canvas.enabled && !hiddenCanvases.Contains(canvas))
            {
                SkippedExternalCanvases++;
                return;
            }
            bool hide;
            if (QueryChecksEnabled)
            {
                hide = ShouldHide(canvas.transform, null, canvas, grid, player);
                if (hide)
                {
                    // Only hidden contents need the focused-door exception. Door ancestry
                    // remains fresh, independently of the nearest owner.
                    var door = currentRegisteredBinding != null ? registeredRoster.CanvasScope(currentRegisteredBinding)
                        : hierarchySnapshotValid && currentHierarchyNode != null
                        ? currentHierarchyNode.NearestCanvasScope : policy.FindCanvasScope(canvas.transform);
                    hide = !(door && policy.IsCanvasExempt(door, canvas));
                }
            }
            else
            {
                var door = currentRegisteredBinding != null ? registeredRoster.CanvasScope(currentRegisteredBinding)
                        : hierarchySnapshotValid && currentHierarchyNode != null
                        ? currentHierarchyNode.NearestCanvasScope : policy.FindCanvasScope(canvas.transform);
                hide = !(door && policy.IsCanvasExempt(door, canvas))
                    && ShouldHide(canvas.transform, null, canvas, grid, player);
            }
            if (hide && canvas.enabled)
            {
                canvas.enabled = false;
                hiddenCanvases.Add(canvas);
                InvalidateAfterPresentationChange();
            }
            else if (!hide && hiddenCanvases.Remove(canvas))
            {
                canvas.enabled = true;
                InvalidateAfterPresentationChange();
            }
        }

        private void InvalidateAfterPresentationChange(bool mayInvokeCallbacks = true)
        {
            // forceRenderingOff only changes this renderer's culling override. Policy
            // queries are pure; unlike enabling a Canvas, this write has no synchronous
            // lifecycle callback. Consume its notifications at the next phase boundary.
            if (!mayInvokeCallbacks && LocalRosterUpdatesEnabled && RegisteredVisibilityActive)
            { RegisteredFastRendererUpdates++; return; }
            if (detailedDiagnostics) diagnostics.presentationInvalidations++;
            hierarchySnapshotValid = false;
            // Presentation callbacks can reparent later contents or change their bindings.
            // Only exact-Transform components persist; their creation revisions still invalidate absences.
            contentOwners.Clear();
            visiblePositions.Clear();
            registeredRoster?.PresentationChanged();
            if (ownerCreationRevision != policy.OwnerCreationRevision)
            {
                if (detailedDiagnostics) diagnostics.creationInvalidations++;
                localOwners.Clear();
                ownerCreationRevision = policy.OwnerCreationRevision;
            }
        }

        private bool ShouldHide(Transform item, Renderer renderer, Canvas canvas, IVisibilityQuery2D grid, Transform player)
        {
            if (currentRegisteredBinding != null) return registeredRoster.ShouldHide(currentRegisteredBinding);
            var owner = hierarchySnapshotValid && currentHierarchyNode != null
                ? ResolveHierarchyOwner(currentHierarchyNode, grid)
                : ResolveContentOwner(item, grid, player, player, renderer is SpriteRenderer || canvas);
            if (owner.PlayerHierarchy) return false;
            if (owner.HasOwner) return owner.Hidden;
            return owner.Content && !IsVisible(grid, renderer ? renderer.bounds.center : canvas.transform.position);
        }

        private ContentOwner ResolveContentOwner(Transform item, IVisibilityQuery2D grid,
            Transform player, bool hasPlayer, bool hasSimpleGraphic)
        {
            long started = detailedDiagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            if (detailedDiagnostics) diagnostics.ownerRequests++;
            try
            {
                if (contentOwners.TryGetValue(item, out var owner))
                { if (detailedDiagnostics) diagnostics.ownerHits++; return owner; }
                ownerTrail.Clear();
                for (var ancestor = item; ancestor; ancestor = ancestor.parent)
                {
                    if (detailedDiagnostics) diagnostics.ancestorSteps++;
                    // Reaching the actual player wins over any content owner above it.
                    if (hasPlayer && ancestor == player)
                    {
                        owner = new ContentOwner(null, null, false, playerHierarchy: true);
                        contentOwners[ancestor] = owner;
                        break;
                    }
                    if (contentOwners.TryGetValue(ancestor, out owner)) break;
                    owner = ResolveLocalOwner(ancestor);
                    if (owner.HasOwner)
                    {
                        // All graphics of this owner share one ancestry answer in this Apply.
                        // A nested owner can still be inside the player's subtree.
                        bool playerHierarchy = owner.PrimaryOwner && policy.IsAlwaysVisible(owner.PrimaryOwner)
                            || hasPlayer && ancestor.IsChildOf(player);
                        bool hidden = !playerHierarchy && !IsVisible(grid,
                            policy.GetOwnerPosition(owner.PrimaryOwner ? owner.PrimaryOwner : owner.SecondaryOwner));
                        owner = new ContentOwner(owner.PrimaryOwner, owner.SecondaryOwner, false, hidden, playerHierarchy);
                        contentOwners.Add(ancestor, owner);
                        break;
                    }
                    ownerTrail.Add(ancestor);
                }
                for (int i = ownerTrail.Count - 1; i >= 0; i--)
                {
                    var ancestor = ownerTrail[i];
                    if (!owner.HasOwner && !owner.Content && !owner.PlayerHierarchy)
                    {
                        var node = ancestor.gameObject;
                        bool content = (contentLayers & (1 << node.layer)) != 0
                            || HasContentComponent(ancestor, node, hasSimpleGraphic && boundedCache && QueryChecksEnabled ? item : null);
                        owner = new ContentOwner(null, null, content);
                    }
                    contentOwners.Add(ancestor, owner);
                }
                return owner;
            }
            finally { if (detailedDiagnostics) diagnostics.ownerTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started; }
        }

        private bool HasContentComponent(Transform item, GameObject node, Transform graphicLeaf)
        {
            if (detailedDiagnostics) diagnostics.componentClassifications++;
            if (graphicLeaf)
            {
                // Recheck native structure on every Apply: Transform-only ancestors and
                // Transform + the known SpriteRenderer/Canvas cannot also own text/fragments.
                // Added/replaced components immediately fall back to the full query.
                int count = node.GetComponentCount();
                if (count == 1 || item == graphicLeaf && count == 2) return false;
            }
            return policy.HasContentComponent(item);
        }

        private ContentOwner ResolveLocalOwner(Transform item)
        {
            if (detailedDiagnostics) diagnostics.localOwnerQueries++;
            if (!boundedCache) visitedOwners.Add(item);
            if (Application.isPlaying && localOwners.TryGetValue(item, out var owner)
                && (ReferenceEquals(owner.PrimaryOwner, null) || owner.PrimaryOwner)
                && (ReferenceEquals(owner.SecondaryOwner, null) || owner.SecondaryOwner))
            { if (detailedDiagnostics) diagnostics.localOwnerHits++; return owner; }
            // A destroyed first component must be queried again: another component may remain.
            var primary = policy.FindPrimaryOwner(item);
            var secondary = primary ? null : policy.FindSecondaryOwner(item);
            owner = new ContentOwner(primary, secondary, false);
            if (Application.isPlaying)
            {
                if (boundedCache && localOwners.Count >= MaximumCachedLocalOwners)
                {
                    localOwners.Clear();
                    LocalOwnerCapacityClearCount++;
                }
                localOwners[item] = owner;
            }
            return owner;
        }

        private bool IsVisible(IVisibilityQuery2D grid, Vector2 position)
        {
            // Exact world positions preserve thin-wall occlusion inside a single exploration cell.
            if (detailedDiagnostics) diagnostics.sightRequests++;
            if (visiblePositions.TryGetValue(position, out bool visible))
            { if (detailedDiagnostics) diagnostics.sightHits++; return visible; }
            long started = detailedDiagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            visible = grid.IsVisible(position);
            if (detailedDiagnostics) diagnostics.sightTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            visiblePositions.Add(position, visible);
            return visible;
        }

        public void Dispose()
        {
            registeredRoster?.Dispose(); registeredRoster = null; currentRegisteredBinding = null;
            foreach (var renderer in hiddenRenderers) if (renderer) renderer.forceRenderingOff = false;
            foreach (var canvas in hiddenCanvases) if (canvas) canvas.enabled = true;
            hiddenRenderers.Clear();
            hiddenCanvases.Clear();
            releasedRenderers.Clear();
            releasedCanvases.Clear();
            contentOwners.Clear();
            visiblePositions.Clear();
            ownerTrail.Clear();
            localOwners.Clear();
            visitedOwners.Clear(); obsoleteLocalOwners.Clear();
            sceneRoots.Clear();
            queriedSceneRoots.Clear();
            queriedScenes.Clear();
            queriedRenderers.Clear();
            queriedCanvases.Clear();
            ClearHierarchy();
        }
    }
}
