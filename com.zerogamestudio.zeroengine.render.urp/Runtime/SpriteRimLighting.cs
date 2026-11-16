using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Render.Urp
{
    /// <summary>Explicit actor registration; scene lights and visible targets are resolved once per camera.</summary>
    [MovedFrom(true, "POB", "POB.Rendering", "SpriteRimLighting")]
    [DisallowMultipleComponent, AddComponentMenu("ZeroEngine/Rendering/像素边缘受光")]
    public sealed class SpriteRimLighting : MonoBehaviour
    {
        [SerializeField, InspectorName("受光对象")] private Renderer[] targets = Array.Empty<Renderer>();
        [SerializeField, InspectorName("共享受光样式")] private SpriteRimLightingProfile profile;
        [SerializeField, InspectorName("使用标记场景灯光")] private bool useSceneLights = true;
        [SerializeField, InspectorName("显式二维灯光")] private SpriteRimLight[] lights = Array.Empty<SpriteRimLight>();
        [Tooltip("Original mode only. Ignore these SpriteRimLight/native Light components or their GameObjects for direction selection; keep their field colors.")]
        public UnityEngine.Object[] OriginalIgnoredLights = Array.Empty<UnityEngine.Object>();
        private readonly List<SpriteRimDraw> draws = new();
        private readonly Dictionary<int, SpriteRimOriginalGroup> originalGroups = new();
        private static SpriteRimLightingProfile defaultProfile;

        public int TargetCount => draws.Count;
        public int ActiveTargetCount { get; private set; }
        public static SpriteRimLightingProfile DefaultProfile => defaultProfile ? defaultProfile :
            defaultProfile = Resources.Load<SpriteRimLightingProfile>("ZeroEngine/SpriteRimLightingDefault");

        public void Configure(Renderer[] renderers, SpriteRimLightingProfile style = null)
        {
            Restore(); targets = renderers; profile = style; useSceneLights = true; lights = Array.Empty<SpriteRimLight>();
            if (isActiveAndEnabled) BindTargets();
        }

        public void Configure(Renderer[] renderers, SpriteRimLight[] sources, SpriteRimLightingProfile style)
        {
            Restore(); targets = renderers; profile = style; useSceneLights = false;
            lights = sources != null ? (SpriteRimLight[])sources.Clone() : Array.Empty<SpriteRimLight>();
            if (isActiveAndEnabled) UpdateLighting();
        }

        private void OnEnable() { SpriteRimRegistry.Register(this); BindTargets(); }
        private void OnDisable() { SpriteRimRegistry.Unregister(this); Restore(); }
        private void OnDestroy() { SpriteRimRegistry.Unregister(this); Restore(); }

        private void BindTargets()
        {
            if (targets == null) return;
            foreach (var target in targets)
            {
                if (!target || !target.sharedMaterial) continue;
                bool alreadyBound = false;
                foreach (var draw in draws) if (draw.Renderer == target) { alreadyBound = true; break; }
                if (!alreadyBound) draws.Add(new SpriteRimDraw(target));
            }
        }

        /// <summary>Explicit-light previews can update immediately. Scene lights update during URP camera preparation.</summary>
        public void UpdateLighting()
        {
            if (!isActiveAndEnabled) return;
            BindTargets(); ActiveTargetCount = 0;
            if (useSceneLights) return;
            var style = profile ? profile : DefaultProfile;
            if (style && style.UseOriginalMechanism)
            {
                CollectOriginal(style, null, null, null, null);
                return;
            }
            if (!style || style.Strength <= 0) return;
            foreach (var draw in draws)
                if (draw.SelectLights(lights) && draw.Update(style)) ActiveTargetCount++;
        }

        internal void Collect(Camera camera, Plane[] planes, List<SpriteRimNativeSource> sceneLights, List<SpriteRimDraw> output,
            List<SpriteRimOriginalGroup> originalOutput)
        {
            ActiveTargetCount = 0;
            BindTargets();
            var style = profile ? profile : DefaultProfile;
            if (style && style.UseOriginalMechanism)
            {
                CollectOriginal(style, camera, planes, useSceneLights ? sceneLights : null, originalOutput);
                return;
            }
            if (useSceneLights && sceneLights.Count == 0) return;
            if (!style || style.Strength <= 0) return;
            foreach (var draw in draws)
            {
                var target = draw.Renderer;
                if (!target || !target.enabled || !target.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << target.gameObject.layer)) == 0 || !GeometryUtility.TestPlanesAABB(planes, target.bounds)) continue;
                bool lit = useSceneLights ? draw.SelectLights(sceneLights) : draw.SelectLights(lights);
                if (lit && draw.Update(style)) { output.Add(draw); ActiveTargetCount++; }
            }
        }

        private void CollectOriginal(SpriteRimLightingProfile style, Camera camera, Plane[] planes,
            List<SpriteRimNativeSource> sceneLights, List<SpriteRimOriginalGroup> output)
        {
            if (targets == null) return;
            foreach (var target in targets)
                if (target && !originalGroups.ContainsKey(target.gameObject.layer))
                    originalGroups.Add(target.gameObject.layer, new SpriteRimOriginalGroup(target.gameObject.layer));
            foreach (var group in originalGroups.Values)
            {
                int count = group.Prepare(targets, useSceneLights ? null : lights, sceneLights, OriginalIgnoredLights, style.Original, camera, planes);
                if (count > 0) { ActiveTargetCount += count; output?.Add(group); }
            }
        }

        public Vector2 GetPixelStep(Renderer renderer)
        {
            foreach (var draw in draws)
                if (draw.Renderer == renderer && draw.Silhouette.PixelStep.x > 0) return draw.Silhouette.PixelStep;
            foreach (var group in originalGroups.Values)
                foreach (var draw in group.Draws) if (draw.Renderer == renderer) return draw.Silhouette.PixelStep;
            return Vector2.zero;
        }

        private void Restore()
        {
            foreach (var draw in draws) draw.Dispose();
            draws.Clear(); ActiveTargetCount = 0;
            foreach (var group in originalGroups.Values) group.Dispose();
            originalGroups.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlay() => defaultProfile = null;
    }

    internal static class SpriteRimRegistry
    {
        private static readonly HashSet<SpriteRimLighting> Members = new();
        internal static void Register(SpriteRimLighting value) => Members.Add(value);
        internal static void Unregister(SpriteRimLighting value) => Members.Remove(value);
        internal static void Collect(Camera camera, Plane[] planes, List<SpriteRimNativeSource> lights, List<SpriteRimDraw> draws,
            List<SpriteRimOriginalGroup> original)
        {
            draws.Clear(); original.Clear();
            foreach (var member in Members)
                if (member && member.isActiveAndEnabled) member.Collect(camera, planes, lights, draws, original);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlay() => Members.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RebindSceneMembers()
        {
            foreach (var member in UnityEngine.Object.FindObjectsByType<SpriteRimLighting>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (member.isActiveAndEnabled) Register(member);
        }
    }
}
