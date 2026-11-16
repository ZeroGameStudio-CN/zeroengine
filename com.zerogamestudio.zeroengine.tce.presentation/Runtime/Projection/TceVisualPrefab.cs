using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace ZeroEngine.TCE.Presentation
{
    /// <summary>An admitted renderer/particle graph. It owns no gameplay, transport or source lifetime.</summary>
    public sealed class TceVisualPrefab
    {
        public const int MaximumComponents = 1024;
        public const int MaximumRenderers = 256;
        public const int MaximumParticleSystems = 256;
        private readonly GameObject prefab;
        private readonly TceVisualProjectionPolicy policy;

        private TceVisualPrefab(GameObject prefab, TceVisualProjectionPolicy policy)
        { this.prefab = prefab; this.policy = policy; }

        /// <summary>
        /// Project adapters may admit known source-only visual/lifetime behaviours.
        /// Every admitted behaviour is removed from the inactive copy before activation.
        /// Animation, audio and declared source physics are removed; unknown components are refused.
        /// </summary>
        public static bool TryCreate(GameObject prefab, TceVisualProjectionPolicy policy,
            out TceVisualPrefab recipe, out string reason)
        {
            recipe = null;
            reason = Validate(prefab, policy);
            if (reason != null) return false;
            recipe = new TceVisualPrefab(prefab, policy);
            return true;
        }

        private static string Validate(GameObject prefab, TceVisualProjectionPolicy policy)
        {
            if (!prefab) return "missing-prefab";
            var components = prefab.GetComponentsInChildren<Component>(true);
            if (components.Length > MaximumComponents) return "component-budget";
            int renderers = 0, systems = 0;
            foreach (var component in components)
            {
                if (!component) return "missing-script";
                if (component is Renderer) renderers++;
                if (component is ParticleSystem ps)
                {
                    systems++;
                    if (ps.main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                        return "custom-simulation-space";
                }
                if (component is Transform or ParticleSystem or ParticleSystemRenderer or SpriteRenderer
                    or MeshFilter or MeshRenderer or Light or TrailRenderer) continue;
                if(component is Animator or PlayableDirector or AudioSource)continue;
                if(component is MonoBehaviour data && policy?.RenderData?.Invoke(data)==true)continue;
                if(policy?.SourceOnly?.Invoke(component)==true)continue;
                return "unsupported-component:" + component.GetType().FullName;
            }
            if (renderers == 0) return "no-renderer";
            if (renderers > MaximumRenderers) return "renderer-budget";
            if (systems > MaximumParticleSystems) return "particle-system-budget";
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (!filter.sharedMesh && policy?.GeneratedMesh?.Invoke(filter)!=true) return "missing-mesh";
            return TceVisualDynamics.Validate(prefab);
        }

        /// <summary>
        /// Creates a fresh display graph. The caller owns its pose, clock, budget and destruction.
        /// The inactive parent prevents Awake/OnEnable/Start on source-only behaviours.
        /// </summary>
        public GameObject Instantiate(Transform parent = null)
        {
            if (!prefab) throw new InvalidOperationException("The admitted visual resource was unloaded.");
            string reason = Validate(prefab, policy);
            if (reason != null) throw new InvalidOperationException("Visual resource changed: " + reason);
            var staging = new GameObject("TCE Visual Staging") { hideFlags = HideFlags.HideAndDontSave };
            staging.SetActive(false);
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(prefab, staging.transform, false);
                instance.SetActive(false);
                var removed=instance.GetComponentsInChildren<Component>(true).Where(component=>
                    component is Animator or PlayableDirector or AudioSource
                        || component is MonoBehaviour behaviour && policy?.RenderData?.Invoke(behaviour)!=true
                        || component is not Transform && policy?.SourceOnly?.Invoke(component)==true).OrderBy(c=>c is MonoBehaviour?0:1).ToArray();
                foreach(var component in removed)Object.DestroyImmediate(component);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.forceRenderingOff = false;
                foreach (var system in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = system.main;
                    main.stopAction = ParticleSystemStopAction.None;
                }
                instance.transform.SetParent(parent, false);
                instance.hideFlags = HideFlags.DontSave;
                policy?.ConfigureDisplay?.Invoke(prefab,instance);
                return instance;
            }
            catch
            {
                if (instance) Destroy(instance);
                throw;
            }
            finally { Destroy(staging); }
        }

        public static void Destroy(GameObject instance)
        {
            if (!instance) return;
            instance.SetActive(false);
            if (Application.isPlaying) Object.Destroy(instance);
            else Object.DestroyImmediate(instance);
        }
    }
}
