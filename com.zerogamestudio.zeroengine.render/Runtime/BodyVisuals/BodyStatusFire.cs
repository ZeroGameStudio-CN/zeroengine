using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render
{
    /// <summary>Small authored flipbook fires; only the caller's clock advances their simulation.</summary>
    internal sealed class BodyStatusFire : IDisposable
    {
        private readonly Transform origin;
        private readonly ParticleSystem[] systems = new ParticleSystem[3];
        private readonly ParticleSystemRenderer[] renderers = new ParticleSystemRenderer[3];
        private readonly Material[] materials = new Material[3];
        private readonly Vector4[] regions = new Vector4[2];
        private readonly float speed;
        private float previousClock = -1f;
        private float nextFlame;
        private float nextEmber;
        private uint serial;
        private bool disposed;

        public BodyStatusFire(Transform origin, Material template)
        {
            this.origin = origin;
            regions[0] = template.GetVector("_FireBackRegion");
            regions[1] = template.GetVector("_FireFrontRegion");
            speed = Mathf.Max(0.05f, template.GetFloat("_MotionSpeed"));
            for (int i = 0; i < systems.Length; i++)
            {
                bool ember = i == 2;
                var owner = new GameObject(ember ? "BodyFireEmbers" : i == 0 ? "BodyFireBack" : "BodyFireFront")
                    { hideFlags = HideFlags.DontSave };
                owner.transform.SetParent(origin, false);
                var system = owner.AddComponent<ParticleSystem>();
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 1000f;
                main.maxParticles = ember ? 12 : 4;
                main.startSpeed = 0f;
                main.startSize3D = true;
                main.simulationSpace = ember ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
                main.scalingMode = ember ? ParticleSystemScalingMode.Shape : ParticleSystemScalingMode.Hierarchy;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var emission = system.emission; emission.enabled = false;
                var shape = system.shape; shape.enabled = false;
                var overLife = system.colorOverLifetime;
                overLife.enabled = true;
                overLife.color = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                        new GradientAlphaKey(0.9f, 0.62f), new GradientAlphaKey(0f, 1f) }
                };
                if (!ember)
                {
                    var sheet = system.textureSheetAnimation;
                    sheet.enabled = true;
                    sheet.mode = ParticleSystemAnimationMode.Grid;
                    sheet.numTilesX = 4; sheet.numTilesY = 4;
                    sheet.animation = ParticleSystemAnimationType.WholeSheet;
                    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.999f));
                    sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.65f);
                    sheet.cycleCount = 1;
                    sheet.uvChannelMask = UVChannelFlags.UV0;
                }
                system.useAutoRandomSeed = false;
                system.randomSeed = (uint)(7301 + i * 97);
                system.Pause(false);
                var renderer = owner.GetComponent<ParticleSystemRenderer>();
                materials[i] = new Material(template) { name = owner.name + "Material", hideFlags = HideFlags.HideAndDontSave };
                materials[i].SetFloat("_BodyLayer", ember ? 3f : 2f);
                materials[i].SetVector("_Flip", Vector4.one);
                renderer.sharedMaterial = materials[i];
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                systems[i] = system; renderers[i] = renderer;
            }
        }

        public void SetPalette(VisualPalette palette) { foreach (var material in materials) palette.Apply(material); }

        public void Hide()
        {
            if (disposed) return;
            foreach (var renderer in renderers) renderer.enabled = false;
        }

        public void Update(float clock, Renderer body, Vector4 bounds, float pixelSize)
        {
            if (disposed) return;
            bool restart = previousClock < 0f || clock < previousClock;
            float delta = restart ? 0f : Mathf.Max(0f, clock - previousClock);
            if (restart)
            {
                foreach (var system in systems) system.Clear(false);
                nextFlame = clock; nextEmber = clock + 0.25f;
            }
            previousClock = clock;
            if (delta > 0f)
            {
                // Simulate pauses the native system afterwards. It never free-runs on Unity Time.
                foreach (var system in systems) system.Simulate(delta, false, false, false);
                if (clock >= nextFlame)
                {
                    EmitFlame(0, bounds, pixelSize);
                    EmitFlame(1, bounds, pixelSize);
                    nextFlame = clock + 0.48f / speed; // No catch-up burst after hidden/stalled frames.
                }
                if (clock >= nextEmber)
                {
                    var position = origin.TransformPoint(RegionPosition(regions[0], bounds));
                    float height = BodyVisual.EmissionPixelSize(pixelSize) * 32f *
                        Mathf.Max(origin.TransformVector(Vector3.up).magnitude, 0.00001f);
                    systems[2].Emit(new ParticleSystem.EmitParams
                    {
                        position = position,
                        velocity = new Vector3((serial % 2 == 0 ? 1f : -1f) * height * 0.08f, height * 0.5f * speed, 0f),
                        startLifetime = 0.65f / speed,
                        startSize3D = new Vector3(height * 0.012f, height * 0.022f, 1f),
                        startColor = new Color(0.9f, 0.34f, 0.025f, 0.75f),
                        randomSeed = ++serial
                    }, 1);
                    nextEmber = clock + 0.3f / speed;
                }
            }
            for (int i = 0; i < systems.Length; i++)
            {
                // Emit can resume a stopped system even though Simulate paused it.
                systems[i].Pause(false);
                systems[i].gameObject.layer = body.gameObject.layer;
                renderers[i].sortingLayerID = body.sortingLayerID;
                renderers[i].sortingOrder = body.sortingOrder + (i == 0 ? -1 : 2);
                renderers[i].enabled = true;
            }
        }

        private void EmitFlame(int index, Vector4 bounds, float pixelSize)
        {
            var region = regions[index];
            float bodyHeight = BodyVisual.EmissionPixelSize(pixelSize) * 32f;
            systems[index].Emit(new ParticleSystem.EmitParams
            {
                position = RegionPosition(region, bounds),
                velocity = Vector3.up * bodyHeight * 0.025f * speed,
                startLifetime = 1.1f / speed,
                startSize3D = new Vector3(bodyHeight * region.z, bodyHeight * region.w, 1f),
                startColor = new Color(1f, 1f, 1f, index == 0 ? 0.78f : 0.6f),
                randomSeed = ++serial
            }, 1);
        }

        private static Vector3 RegionPosition(Vector4 region, Vector4 bounds) =>
            new(bounds.x + (region.x - 0.5f) / bounds.z, bounds.y + (region.y - 0.5f) / bounds.w, 0f);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < systems.Length; i++)
            {
                renderers[i].enabled = false;
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                BodyVisual.DestroyOwned(systems[i].gameObject);
                BodyVisual.DestroyOwned(materials[i]);
            }
        }
    }
}
