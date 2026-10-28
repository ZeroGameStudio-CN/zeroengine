using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render
{
    /// <summary>Opt-in sample atmosphere: caller-clocked, posed-alpha roots and world-space release.</summary>
    internal sealed class BodyStatusParticles : IDisposable
    {
        private const int Capacity = 64;
        private readonly Transform origin;
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Material material;
        private readonly List<Vector2> anchors = new(128);
        private readonly List<Particle> particles = new(Capacity);
        private readonly List<Vector3> vertices = new(Capacity * 4);
        private readonly List<Vector2> uv = new(Capacity * 4);
        private readonly List<Vector4> lifeData = new(Capacity * 4);
        private readonly List<Vector4> rootData = new(Capacity * 4);
        private readonly List<int> triangles = new(Capacity * 6);
        private readonly int look;
        private bool disposed;
        private bool pendingMask;
        private AsyncGPUReadbackRequest pendingRequest;
        private float lastClock = -1f;
        private float lastMaskClock = -1f;
        private float nextBirth;
        private int serial;
        private Vector3 previousCenter;

        private struct Particle
        {
            public Vector2 anchor;
            public Vector3 position, velocity;
            public float born, lifetime, attachment, width, height, seed;
            public int kind, id;
            public bool released;
        }

        public BodyStatusParticles(Transform origin, Material template)
        {
            this.origin = origin;
            look = Mathf.Clamp(Mathf.RoundToInt(template.GetFloat("_Look")), 0, 2);
            root = new GameObject("BodyStatusParticles") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(origin, false);
            mesh = new Mesh { name = "BodyStatusParticles", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            material = new Material(template) { name = "BodyStatusParticleRuntime", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_BodyLayer", 2f);
            renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
        }

        public void SetPalette(VisualPalette palette) => palette.Apply(material);

        public void Hide() => renderer.enabled = false;
        public bool NeedsFallback => !pendingMask && lastMaskClock >= 0f && anchors.Count == 0;

        public void Update(float clock, Renderer body, Vector4 bounds, float pixelSize, RenderTexture mask)
        {
            if (disposed) return;
            float delta = lastClock < 0f ? 0f : Mathf.Max(0f, clock - lastClock);
            if (lastClock < 0f || clock < lastClock)
            {
                particles.Clear();
                nextBirth = clock;
                delta = 0f;
            }
            // No Unity Time reads: an unchanged owner clock creates no particles and ages none.
            float step = Mathf.Min(delta, 0.12f);
            float worldPixel = BodyVisual.EmissionPixelSize(pixelSize) * Mathf.Max((origin.TransformVector(Vector3.right).magnitude +
                origin.TransformVector(Vector3.up).magnitude) * 0.5f, 0.00001f);
            var center = origin.position;
            Vector3 motion = delta > 0.00001f ? Vector3.ClampMagnitude((center - previousCenter) / delta, 160f * worldPixel) : Vector3.zero;
            previousCenter = center;
            lastClock = clock;
            if (!pendingMask && SystemInfo.supportsAsyncGPUReadback &&
                (lastMaskClock < 0f || clock - lastMaskClock >= 0.4f))
            {
                pendingMask = true;
                lastMaskClock = clock;
                int width = mask.width, height = mask.height;
                // Read the private union, never the original atlas or its importer. Pending callbacks own no scene writes.
                pendingRequest = AsyncGPUReadback.Request(mask, 0, TextureFormat.RGBA32, request =>
                {
                    pendingMask = false;
                    if (disposed || request.hasError) return;
                    var data = request.GetData<Color32>();
                    anchors.Clear();
                    // Stratified occupied cells distribute roots over the actual posed body instead of four rectangle points.
                    for (int y = 2; y < 15; y++)
                        for (int x = 1; x < 15; x++)
                        {
                            int px = (x * width + width / 2) / 16;
                            int py = (y * height + height / 2) / 16;
                            if (data[py * width + px].a >= 160)
                                anchors.Add(new Vector2((px + 0.5f) / width, (py + 0.5f) / height));
                        }
                });
            }


            // Pose changes may redistribute roots, never resize flames or infer actor velocity.
            float coverage = Mathf.Clamp((1f / bounds.z + 1f / bounds.w) / (pixelSize * 72f), 0.7f, 2.8f);
            float speed = Mathf.Max(0.05f, material.GetFloat("_MotionSpeed"));
            float rate = (look == 0 ? 11f : look == 1 ? 19f : 25f) * coverage * speed;
            if (anchors.Count > 0 && delta > 0f)
            {
                int bornThisFrame = 0;
                while (clock >= nextBirth && particles.Count < Capacity && bornThisFrame++ < 5)
                {
                    serial++;
                    float seed = Hash(serial);
                    var anchor = anchors[Mathf.Min(anchors.Count - 1, Mathf.FloorToInt(Hash(serial * 29 + 7) * anchors.Count))];
                    int kind = look == 2 && serial % 5 == 0 ? 1 : serial % 7 == 0 ? 2 : 0;
                    float lifetime = (kind == 1 ? 1.7f : kind == 2 ? 1.15f : 0.8f + seed * 0.55f) / speed;
                    float width = worldPixel * (look == 0 ? 6f : look == 1 ? 8f : 10f) * (0.7f + seed * 0.5f);
                    float height = worldPixel * (look == 0 ? 13f : look == 1 ? 20f : 24f) * (0.75f + seed * 0.5f);
                    if (kind == 1) { width *= 1.4f; height = width * 1.1f; }
                    if (kind == 2) { width = worldPixel * 0.8f; height = worldPixel * 1.3f; }
                    particles.Add(new Particle
                    {
                        anchor = anchor, position = RootAt(anchor, bounds), born = clock, seed = seed, kind = kind, id = serial,
                        lifetime = lifetime, attachment = kind == 0 ? lifetime * 0.24f : 0f,
                        width = width, height = height,
                        velocity = new Vector3((seed - 0.5f) * height * 0.45f, height * (kind == 2 ? 1.9f : 0.7f) * speed, 0f) + motion * 0.18f
                    });
                    nextBirth += 1f / rate;
                }
                // Avoid a catch-up burst after a hidden body, stall, or full capacity.
                nextBirth = Mathf.Max(nextBirth, clock - 1f / rate);
            }

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var particle = particles[i];
                float age = clock - particle.born;
                if (age >= particle.lifetime) { particles.RemoveAt(i); continue; }
                if (age < particle.attachment) particle.position = RootAt(particle.anchor, bounds);
                else
                {
                    if (!particle.released)
                    {
                        particle.position = RootAt(particle.anchor, bounds);
                        particle.released = true;
                    }
                    particle.position += particle.velocity * step;
                }
                particles[i] = particle;
            }
            root.layer = body.gameObject.layer;
            renderer.sortingLayerID = body.sortingLayerID;
            renderer.sortingOrder = body.sortingOrder + 2;
            material.SetFloat("_BodyClock", clock);
            RebuildMesh(clock);
            renderer.enabled = particles.Count > 0;
        }

        private Vector3 RootAt(Vector2 anchor, Vector4 bounds) => origin.TransformPoint(
            new Vector3(bounds.x + (anchor.x - 0.5f) / bounds.z, bounds.y + (anchor.y - 0.5f) / bounds.w, 0f));

        private void RebuildMesh(float clock)
        {
            vertices.Clear(); uv.Clear(); lifeData.Clear(); rootData.Clear(); triangles.Clear();
            foreach (var particle in particles)
            {
                float age = Mathf.Clamp01((clock - particle.born) / particle.lifetime);
                float growth = particle.kind == 1 ? 0.6f + age * 1.5f : 0.42f + Mathf.Sin(age * Mathf.PI) * 0.65f;
                float alpha = Mathf.Min(age * 8f, 1f) * Mathf.Clamp01((1f - age) * 3f);
                float width = particle.width * growth, height = particle.height * growth;
                int start = vertices.Count;
                foreach (var corner in Corners)
                {
                    // Released positions remain in world space even when the entity moves, flips, scales or rotates.
                    var world = particle.position + new Vector3((corner.x - 0.5f) * width, corner.y * height, 0f);
                    vertices.Add(origin.InverseTransformPoint(world));
                    uv.Add(corner);
                    lifeData.Add(new Vector4(age, particle.seed, particle.kind, alpha));
                    rootData.Add(new Vector4(particle.anchor.x, particle.anchor.y, particle.released ? 0f : 1f, particle.id));
                }
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start + 2); triangles.Add(start + 3); triangles.Add(start + 1);
            }
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetUVs(1, lifeData);
            mesh.SetUVs(2, rootData);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static readonly Vector2[] Corners = { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        private static float Hash(int value) => Mathf.Repeat(Mathf.Sin(value * 12.9898f) * 43758.5453f, 1f);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (pendingMask) pendingRequest.WaitForCompletion(); // Finish this request before BodyVisual releases its private mask.
            particles.Clear(); anchors.Clear();
            renderer.enabled = false;
            root.GetComponent<MeshFilter>().sharedMesh = null;
            BodyVisual.DestroyOwned(root); BodyVisual.DestroyOwned(mesh); BodyVisual.DestroyOwned(material);
        }
    }
}
