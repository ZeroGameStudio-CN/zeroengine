using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using ZeroEngine.Render.Weather;

namespace ZeroEngine.Render.Urp.Weather
{
    internal sealed class SurfaceWeatherSceneRegistry : IDisposable
    {
        internal sealed class Entry
        {
            public MeshRenderer Renderer;
            public MeshFilter Filter;
            public Terrain Terrain;
            public Mesh Mesh;
            public bool OwnsMesh;
            public Matrix4x4 Matrix;
            public Bounds Bounds;
            public Material[] Before, Applied;
            public Material TerrainBefore, TerrainApplied;
            public bool Casts;
            public bool Active => Terrain != null ? Terrain.isActiveAndEnabled : Renderer != null && Renderer.gameObject.activeInHierarchy;
        }

        private readonly Dictionary<UnityEngine.Object, Entry> _entries = new();
        private readonly List<UnityEngine.Object> _retire = new();
        private readonly Queue<Transform> _pending = new();
        private readonly List<GameObject> _roots = new();
        private readonly List<Material> _materials = new();
        private readonly SurfaceWeatherSceneProfileSO _profile;
        private readonly SurfaceWeatherMaterialCache _cache;
        private readonly GameObject _persistentWitness;
        private float _nextSweep;
        public int Version { get; private set; }
        public int Count => _entries.Count;
        public int Pending => _pending.Count;
        public string Failure { get; private set; }
        public IEnumerable<Entry> Entries => _entries.Values;

        public SurfaceWeatherSceneRegistry(SurfaceWeatherSceneProfileSO profile, SurfaceWeatherMaterialCache cache)
        {
            _profile = profile;
            _cache = cache;
            if (Application.isPlaying)
            {
                _persistentWitness = new GameObject("ZeroSurfaceWeather_PersistentDiscovery") { hideFlags = HideFlags.DontSave };
                UnityEngine.Object.DontDestroyOnLoad(_persistentWitness);
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            SurfaceWeatherMaterialSources.RendererMaterialsChanged += OnRendererMaterialsChanged;
            TerrainCallbacks.heightmapChanged += OnTerrainChanged;
            BeginSweep();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => QueueScene(scene);
        private void OnRendererMaterialsChanged(Renderer renderer)
        {
            // New objects still use bounded discovery. Only an existing registration
            // may rebind synchronously before the changed materials are rendered.
            if (renderer is not MeshRenderer mesh || !_entries.Remove(mesh, out Entry previous)) return;
            Release(previous);
            Version++;
            if (Failure == null && mesh != null && mesh.gameObject.activeInHierarchy)
                DiscoverTransform(mesh.transform);
        }
        private void OnTerrainChanged(Terrain terrain, RectInt rect, bool synced)
        {
            if (terrain != null && _entries.TryGetValue(terrain, out Entry entry))
            {
                SurfaceWeatherMaterialCache.DestroyOwned(entry.Mesh);
                entry.Mesh = BuildTerrainMesh(terrain.terrainData);
                Version++;
            }
        }

        private void QueueScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            _roots.Clear(); scene.GetRootGameObjects(_roots);
            foreach (GameObject root in _roots) Enqueue(root.transform);
        }

        private void BeginSweep()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) QueueScene(SceneManager.GetSceneAt(i));
            if (_persistentWitness != null) QueueScene(_persistentWitness.scene);
            _nextSweep = Time.unscaledTime + _profile.DiscoveryInterval;
        }

        public void DiscoverNow()
        {
            if (_pending.Count == 0) BeginSweep();
            while (_pending.Count > 0) DiscoverOne();
            RefreshEntries();
        }

        public void Tick()
        {
            if (Failure != null) { RefreshEntries(); return; }
            if (_pending.Count == 0 && Time.unscaledTime >= _nextSweep) BeginSweep();
            for (int i = 0; i < _profile.DiscoveryBudget && _pending.Count > 0; i++) DiscoverOne();
            RefreshEntries();
        }

        private void DiscoverOne()
        {
            Transform node = _pending.Dequeue();
            if (node == null || !node.gameObject.activeInHierarchy) return;
            for (int i = 0; i < node.childCount; i++) Enqueue(node.GetChild(i));
            DiscoverTransform(node);
        }

        private void DiscoverTransform(Transform node)
        {
            if ((_profile.EnvironmentLayers & (1 << node.gameObject.layer)) == 0) return;
            bool unregisteredMesh = node.TryGetComponent(out MeshRenderer pendingRenderer) && !_entries.ContainsKey(pendingRenderer);
            bool unregisteredTerrain = node.TryGetComponent(out Terrain pendingTerrain) && !_entries.ContainsKey(pendingTerrain);
            if ((unregisteredMesh || unregisteredTerrain) && _entries.Count >= _profile.MaximumRegisteredObjects)
            {
                Failure = $"Automatic scene weather object budget exhausted ({_profile.MaximumRegisteredObjects}): {node.name}.";
                _pending.Clear(); return;
            }
            if (node.TryGetComponent(out MeshRenderer renderer) && node.TryGetComponent(out MeshFilter filter)
                && filter.sharedMesh != null && !_entries.ContainsKey(renderer))
            {
                var before = renderer.sharedMaterials;
                for (int i = 0; i < before.Length; i++) before[i] = SurfaceWeatherMaterialSources.Resolve(before[i]);
                var applied = new Material[before.Length];
                for (int i = 0; i < before.Length; i++)
                    applied[i] = renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly ? before[i] : _cache.Acquire(before[i]);
                renderer.sharedMaterials = applied;
                _entries.Add(renderer, new Entry { Renderer = renderer, Filter = filter,
                    Mesh = filter.sharedMesh, Before = before, Applied = applied, Matrix = renderer.localToWorldMatrix, Bounds = renderer.bounds });
                _entries[renderer].Casts = IsCaster(renderer, before);
                Version++;
            }
            if (node.TryGetComponent(out Terrain terrain) && terrain.terrainData != null && !_entries.ContainsKey(terrain))
            {
                Material original = SurfaceWeatherMaterialSources.Resolve(terrain.materialTemplate);
                bool known = original == null || original.shader.name == "Universal Render Pipeline/Terrain/Lit";
                if (!known && !_profile.IsNonReceiver(original)) _cache.ReportUnsupported(original);
                Material material = known ? (original != null ? new Material(original) : new Material(_profile.TerrainShader)) : null;
                if (material != null)
                {
                    material.shader = _profile.TerrainShader; material.hideFlags = HideFlags.DontSave;
                    material.SetFloat("_ZeroSceneWeatherReceiver", 1f); terrain.materialTemplate = material;
                    SurfaceWeatherMaterialSources.Register(material, original);
                }
                _entries.Add(terrain, new Entry { Terrain = terrain, Mesh = BuildTerrainMesh(terrain.terrainData), OwnsMesh = true,
                    Matrix = terrain.transform.localToWorldMatrix, Bounds = TerrainBounds(terrain), TerrainBefore = original, TerrainApplied = material, Casts = true });
                Version++;
            }
        }

        private void RefreshEntries()
        {
            _retire.Clear();
            foreach (var pair in _entries)
            {
                Entry entry = pair.Value;
                if (!entry.Active) { _retire.Add(pair.Key); continue; }
                Matrix4x4 matrix = entry.Terrain != null ? entry.Terrain.transform.localToWorldMatrix : entry.Renderer.localToWorldMatrix;
                Bounds bounds = entry.Terrain != null ? TerrainBounds(entry.Terrain) : entry.Renderer.bounds;
                if (entry.Matrix != matrix || entry.Bounds != bounds)
                { entry.Matrix = matrix; entry.Bounds = bounds; if (entry.Casts) Version++; }
                if (entry.Filter != null && entry.Mesh != entry.Filter.sharedMesh)
                { entry.Mesh = entry.Filter.sharedMesh; Version++; }
                if (entry.Renderer != null)
                {
                    bool casts = IsCaster(entry.Renderer, entry.Before);
                    if (casts != entry.Casts) { entry.Casts = casts; Version++; }
                    _materials.Clear(); entry.Renderer.GetSharedMaterials(_materials);
                    bool same = _materials.Count == entry.Applied.Length;
                    for (int i = 0; same && i < _materials.Count; i++) same = _materials[i] == entry.Applied[i];
                    if (!same) _retire.Add(pair.Key); // A later legitimate writer keeps its materials; discover its new source next sweep.
                }
            }
            foreach (UnityEngine.Object id in _retire) { Release(_entries[id]); _entries.Remove(id); Version++; }
        }

        public bool TryGetCasterBounds(out Bounds bounds)
        {
            bounds = default; bool found = false;
            foreach (Entry entry in _entries.Values)
            {
                if (!entry.Active || !entry.Casts || entry.Mesh == null) continue;
                if (!found) { bounds = entry.Bounds; found = true; }
                else bounds.Encapsulate(entry.Bounds);
            }
            return found;
        }

        public bool TryGetSceneBounds(Scene scene, out Bounds bounds)
        {
            bounds = default; bool found = false;
            foreach (Entry entry in _entries.Values)
            {
                if (!entry.Active) continue;
                // Actor shadow proxies do not define an environment's spatial domain.
                if (entry.Renderer != null && entry.Renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if (entry.Renderer != null)
                {
                    bool surface = false;
                    foreach (Material material in entry.Before) if (!_profile.IsNonReceiver(material)) { surface = true; break; }
                    if (!surface) continue;
                }
                Scene owner = entry.Terrain != null ? entry.Terrain.gameObject.scene : entry.Renderer.gameObject.scene;
                if (owner != scene) continue;
                if (!found) { bounds = entry.Bounds; found = true; }
                else bounds.Encapsulate(entry.Bounds);
            }
            return found;
        }

        private static Bounds TerrainBounds(Terrain terrain)
        {
            Vector3 size = terrain.terrainData.size;
            return new Bounds(terrain.transform.position + size * .5f, size);
        }

        private bool IsCaster(MeshRenderer renderer, Material[] materials)
        {
            // Counting actor shadow proxies as roofs would shelter a character's own feet.
            // Physical cutaway surfaces remain present even with lighting shadows disabled.
            if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            foreach (Material material in materials) if (_profile.CanOcclude(material)) return true;
            return false;
        }

        private void Enqueue(Transform node)
        {
            if (Failure != null) return;
            if (_pending.Count >= _profile.MaximumPendingTransforms)
            { Failure = "Automatic scene weather discovery queue budget exhausted."; _pending.Clear(); return; }
            _pending.Enqueue(node);
        }

        private static Mesh BuildTerrainMesh(TerrainData data)
        {
            int size = Mathf.Min(129, data.heightmapResolution);
            var vertices = new Vector3[size * size]; var uv = new Vector2[vertices.Length];
            var triangles = new List<int>((size - 1) * (size - 1) * 6);
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1), v = z / (float)(size - 1); int i = z * size + x;
                vertices[i] = new Vector3(u * data.size.x, data.GetInterpolatedHeight(u, v), v * data.size.z); uv[i] = new Vector2(u, v);
                if (x == size - 1 || z == size - 1) continue;
                int hx = Mathf.Min(data.holesResolution - 1, Mathf.FloorToInt(u * data.holesResolution));
                int hz = Mathf.Min(data.holesResolution - 1, Mathf.FloorToInt(v * data.holesResolution));
                if (data.IsHole(hx, hz)) continue;
                triangles.Add(i); triangles.Add(i + size); triangles.Add(i + 1);
                triangles.Add(i + 1); triangles.Add(i + size); triangles.Add(i + size + 1);
            }
            var mesh = new Mesh { name = "ZeroSurfaceWeather_TerrainExposure", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        private void Release(Entry entry)
        {
            if (entry.Renderer != null)
            {
                _materials.Clear(); entry.Renderer.GetSharedMaterials(_materials);
                bool same = _materials.Count == entry.Applied.Length;
                for (int i = 0; same && i < _materials.Count; i++) same = _materials[i] == entry.Applied[i];
                if (same) entry.Renderer.sharedMaterials = entry.Before;
            }
            for (int i = 0; i < (entry.Before?.Length ?? 0); i++)
                if (entry.Applied[i] != entry.Before[i]) _cache.Release(entry.Before[i]);
            if (entry.Terrain != null && entry.Terrain.materialTemplate == entry.TerrainApplied) entry.Terrain.materialTemplate = entry.TerrainBefore;
            SurfaceWeatherMaterialSources.Unregister(entry.TerrainApplied);
            SurfaceWeatherMaterialCache.DestroyOwned(entry.TerrainApplied);
            if (entry.OwnsMesh) SurfaceWeatherMaterialCache.DestroyOwned(entry.Mesh);
        }

        public void Dispose()
        {
            SurfaceWeatherMaterialSources.RendererMaterialsChanged -= OnRendererMaterialsChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            TerrainCallbacks.heightmapChanged -= OnTerrainChanged;
            foreach (Entry entry in _entries.Values) Release(entry);
            _entries.Clear(); _pending.Clear();
            SurfaceWeatherMaterialCache.DestroyOwned(_persistentWitness);
        }
    }
}
