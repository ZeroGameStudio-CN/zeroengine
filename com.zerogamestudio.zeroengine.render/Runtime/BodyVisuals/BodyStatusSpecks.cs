using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.Render
{
    /// <summary>Posed-alpha snow, embers and brief blood spurts; caller-clocked in world space.</summary>
    internal sealed class BodyStatusSpecks : IDisposable
    {
        private const int Capacity = BodyVisual.MaximumSmallParticles;
        private const float BloodMinimumHeight = .2f, BloodMaximumHeight = .8f;
        private readonly Transform origin;
        private readonly bool snow;
        private readonly bool solidIce;
        private readonly bool blood;
        private readonly float speed;
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Material material;
        private readonly List<Vector2> anchors = new(128);
        private readonly List<Vector2> fallbackAnchors = new(128);
        private readonly List<Vector2> bloodAnchors = new(4);
        private readonly List<Speck> specks = new(Capacity);
        private readonly List<Vector3> vertices = new(Capacity * 4);
        private readonly List<Vector2> uvs = new(Capacity * 4);
        private readonly List<Color> colors = new(Capacity * 4);
        private readonly List<int> indices = new(Capacity * 6);
        private AsyncGPUReadbackRequest maskRequest;
        private bool pendingMask, disposed;
        private float lastClock = -1f, lastMaskClock = -1f, nextBirth;
        private int serial, bloodSite;
        private bool wasEmitting = true;
        private bool pendingIceEntry, pendingIceExit;
        public void SetPalette(VisualPalette palette) => palette.Apply(material);

        public bool HasParticles => specks.Count > 0;
        private struct Speck
        {
            public Vector3 Position, Velocity, Acceleration;
            public float Born, Lifetime, Size, Seed, RetiredAt, Angle, Floor;
            public bool Jet, Crystal;
        }

        public BodyStatusSpecks(Transform origin, Shader shader, BodyVisualSettings settings)
        {
            this.origin = origin;
            snow = settings.IsIce;
            solidIce = settings.Kind == BodyVisualKind.FrozenSolid;
            pendingIceEntry = solidIce;
            blood = settings.Kind == BodyVisualKind.Bleeding;
            speed = settings.MotionSpeed;
            root = new GameObject(snow ? "BodySnowflakes" : blood ? "BodyBloodDrops" : "BodyEmbers") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(origin, false);
            mesh = new Mesh { name = "BodyStatusSpecks", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            material = new Material(shader) { name = "BodySpecksRuntime", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_Snow", snow ? 1f : 0f);
            material.SetFloat("_Blood", blood ? 1f : 0f);
            material.SetColor("_Tint", blood ? settings.Color : snow ? new Color(0.66f, 0.84f, 0.94f, settings.Color.a * 0.9f) : new Color(1f, 0.44f, 0.08f, settings.Color.a * 0.95f));
            material.SetFloat("_Intensity", Mathf.Min(settings.Intensity, 1.4f));
            renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
        }

        public void Hide() { if (renderer) renderer.enabled = false; }
        public void Clear() { specks.Clear(); pendingIceEntry = pendingIceExit = false; Hide(); }

        public void Update(float clock, Renderer body, Vector4 bounds, float pixelSize, RenderTexture mask,
            float opacity, int sortingOffset, int budget, bool emitting = true, float presence = 1f)
        {
            if (disposed) return;
            budget = Mathf.Clamp(budget, 0, Capacity);
            while (specks.Count > budget) specks.RemoveAt(0);
            float delta = lastClock < 0f ? 0f : Mathf.Max(0f, clock - lastClock);
            if (lastClock < 0f || clock < lastClock)
            {
                specks.Clear(); nextBirth = clock; delta = 0f;
                pendingIceEntry = solidIce && emitting;
                pendingIceExit = false;
            }
            lastClock = clock;
            bool releaseCrystals = wasEmitting && !emitting && snow && presence > .05f;
            if (solidIce)
            {
                if (!wasEmitting && emitting) { pendingIceEntry = true; pendingIceExit = false; }
                if (releaseCrystals) { pendingIceEntry = false; pendingIceExit = true; }
                if (!emitting) pendingIceEntry = false;
            }
            wasEmitting = emitting;
            if (!emitting)
            {
                nextBirth = clock;
                for (int i = 0; i < specks.Count; i++)
                {
                    var speck = specks[i];
                    if (speck.RetiredAt < 0f) { speck.RetiredAt = clock; specks[i] = speck; }
                }
            }
            if (emitting && !pendingMask && (lastMaskClock < 0f || clock - lastMaskClock >= 0.25f))
            {
                pendingMask = true; lastMaskClock = clock;
                int width = mask.width, height = mask.height;
                maskRequest = AsyncGPUReadback.Request(mask, 0, TextureFormat.RGBA32, request =>
                {
                    pendingMask = false;
                    if (disposed || request.hasError) return;
                    var data = request.GetData<Color32>();
                    anchors.Clear();
                    fallbackAnchors.Clear();
                    for (int y = 1; y < 16; y++)
                        for (int x = 1; x < 16; x++)
                        {
                            int px = x * width / 16, py = y * height / 16;
                            if (data[py * width + px].a < 160) continue;
                            var point = new Vector2((px + 0.5f) / width, (py + 0.5f) / height);
                            if (blood && (point.y < BloodMinimumHeight || point.y > BloodMaximumHeight)) continue;
                            fallbackAnchors.Add(point);
                            int stepX = Mathf.Max(1, width / 32), stepY = Mathf.Max(1, height / 32);
                            // Emit from the posed edge, rather than covering the face and torso.
                            if (data[py * width + px - stepX].a < 160 || data[py * width + px + stepX].a < 160 ||
                                data[(py - stepY) * width + px].a < 160 || data[(py + stepY) * width + px].a < 160)
                                anchors.Add(point);
                        }
                    if (anchors.Count == 0) anchors.AddRange(fallbackAnchors);
                });
            }
            float worldPixel = BodyVisual.EmissionPixelSize(pixelSize) * Mathf.Max(0.00001f,
                (origin.TransformVector(Vector3.right).magnitude + origin.TransformVector(Vector3.up).magnitude) * 0.5f);
            float coverage = Mathf.Clamp((1f / bounds.z + 1f / bounds.w) / (pixelSize * 72f), 0.6f, 2f);
            float rate = (snow ? 4.5f : 7f) * coverage * speed * presence;
            // These small, silhouette-born chips belong to the coat transition, not the
            // gameplay Buff's removal. The separate hit feedback owns the big shatter.
            if (solidIce && delta > 0f && anchors.Count > 0 && budget > 0 && speed > 0f && presence > .05f &&
                (pendingIceEntry || pendingIceExit))
            {
                EmitIceChips(clock, body, bounds, worldPixel, budget, pendingIceEntry);
                pendingIceEntry = pendingIceExit = false;
            }
            if (blood && emitting && delta > 0f && rate > 0f && anchors.Count > 0 && clock >= nextBirth)
            {
                EmitBloodSpurt(clock, body, bounds, worldPixel, budget);
                // One fresh burst only: a hidden/full emitter must never repay missed bursts.
                nextBirth = clock + (.28f + Hash(serial + 19) * .08f) / Mathf.Max(speed, .05f);
            }
            if (!blood && (emitting && delta > 0f && rate > 0f || releaseCrystals && !solidIce) && anchors.Count > 0)
            {
                int births = 0;
                while ((releaseCrystals || clock >= nextBirth) && specks.Count < budget && births++ < 2)
                {
                    float seed = Hash(++serial);
                    var anchor = anchors[Mathf.Min(anchors.Count - 1, (int)(Hash(serial * 29 + 7) * anchors.Count))];
                    var position = origin.TransformPoint(new Vector3(bounds.x + (anchor.x - 0.5f) / bounds.z,
                        bounds.y + (anchor.y - 0.5f) / bounds.w, 0f));
                    specks.Add(new Speck
                    {
                        Position = position, Born = clock, Seed = seed, RetiredAt = emitting ? -1f : clock,
                        Lifetime = (snow ? 1.1f + seed * 0.6f : 0.55f + seed * 0.55f) / Mathf.Max(speed, 0.05f),
                        Size = worldPixel * (snow ? 2.3f + seed * 0.9f : 0.7f + seed * 0.7f) * (releaseCrystals ? .7f : 1f),
                        Velocity = new Vector3((seed - 0.5f) * worldPixel * 9f,
                            worldPixel * (snow ? -5f - seed * 4f : 12f + seed * 10f), 0f) * speed
                    });
                    if (rate > 0f) nextBirth += 1f / rate;
                }
                // A hidden body or full budget must never build a catch-up burst.
                nextBirth = Mathf.Max(nextBirth, clock);
            }
            float step = Mathf.Min(delta, 0.12f);
            for (int i = specks.Count - 1; i >= 0; i--)
            {
                var speck = specks[i];
                if (clock - speck.Born >= speck.Lifetime || speck.RetiredAt >= 0f && clock - speck.RetiredAt >= .65f)
                { specks.RemoveAt(i); continue; }
                float age = Mathf.Clamp01((clock - speck.Born) / speck.Lifetime);
                if (blood || speck.Crystal)
                {
                    speck.Position += speck.Velocity * step + speck.Acceleration * (.5f * step * step);
                    speck.Velocity += speck.Acceleration * step;
                    // Droplets disappear at the release silhouette's feet, never through the floor.
                    if (blood && !speck.Jet && speck.Position.y <= speck.Floor) { specks.RemoveAt(i); continue; }
                }
                else speck.Position += speck.Velocity * step * (snow ? 1f : 1f - age * 0.55f);
                float drift = Mathf.Sin((clock - speck.Born) * (snow ? 3f : 5f) * speed + speck.Seed * 6f);
                speck.Position.x += drift * worldPixel * speed * step * (blood ? 0f : snow ? 1.2f : 0.65f);
                specks[i] = speck;
            }
            root.layer = body.gameObject.layer;
            renderer.sortingLayerID = body.sortingLayerID;
            renderer.sortingOrder = body.sortingOrder + 3 + sortingOffset;
            material.SetFloat("_Opacity", opacity);
            Rebuild(clock);
            renderer.enabled = specks.Count > 0;
        }

        private void EmitIceChips(float clock, Renderer body, Vector4 bounds, float worldPixel, int budget, bool entering)
        {
            int count = Mathf.Min(entering ? 6 : 8, budget);
            // Reserve a little of the existing shared budget, even if old snow filled it.
            while (specks.Count > budget - count) specks.RemoveAt(0);
            for (int i = 0; i < count; i++)
            {
                float seed = Hash(++serial);
                var anchor = anchors[Mathf.Min(anchors.Count - 1, i * anchors.Count / count)];
                var position = origin.TransformPoint(new Vector3(bounds.x + (anchor.x - .5f) / bounds.z,
                    bounds.y + (anchor.y - .5f) / bounds.w, 0f));
                specks.Add(new Speck
                {
                    Position = position, Born = clock, Seed = seed, Crystal = true,
                    RetiredAt = entering ? -1f : clock,
                    Lifetime = (.48f + seed * .17f) / Mathf.Max(speed, .05f),
                    Size = worldPixel * (entering ? 3f : 4f) * (.75f + seed * .5f),
                    Velocity = new Vector3((anchor.x - .5f) * 2.2f, (entering ? .65f : .25f) + seed * .5f, 0f) * (worldPixel * 32f * speed),
                    Acceleration = Vector3.down * (worldPixel * 112f * speed * speed),
                    Angle = (seed - .5f) * Mathf.PI
                });
            }
        }

        private void EmitBloodSpurt(float clock, Renderer body, Vector4 bounds, float worldPixel, int budget)
        {
            int available = budget - specks.Count;
            if (available <= 0) return;
            int sites = Mathf.Min(2, Mathf.Max(1, available / 2));
            bloodAnchors.Clear();
            for (int site = 0; site < sites; site++)
            {
                // Alternate sides across the middle body, keeping feet/head out of birth selection.
                float heightBand = .26f + .24f * ((bloodSite * 2) % 3);
                var target = new Vector2(bloodSite % 2 == 0 ? .18f : .82f, heightBand);
                bloodSite = (bloodSite + 1) % 6;
                var anchor = anchors[0];
                float closest = float.MaxValue;
                foreach (var candidate in anchors)
                {
                    if (candidate.y < BloodMinimumHeight || candidate.y > BloodMaximumHeight) continue;
                    float distance = Vector2.Scale(candidate - target, new Vector2(1f, 1.4f)).sqrMagnitude;
                    if (bloodAnchors.Contains(candidate)) distance += 4f;
                    if (distance >= closest) continue;
                    closest = distance; anchor = candidate;
                }
                if (closest == float.MaxValue) return;
                bloodAnchors.Add(anchor);
                int count = Mathf.Min(3, (budget - specks.Count) / (sites - site));
                float seed = Hash(++serial);
                var position = origin.TransformPoint(new Vector3(bounds.x + (anchor.x - .5f) / bounds.z,
                    bounds.y + (anchor.y - .5f) / bounds.w, 0f));
                float side = position.x < body.bounds.center.x ? -1f : 1f;
                var direction = new Vector3(side, .25f + seed * .3f, 0f).normalized;
                // Size is independent of the animated silhouette; only birth positions follow the pose.
                float jetSize = worldPixel * 9f;
                float travel = Mathf.Max(worldPixel, jetSize / 8f);
                float motion = Mathf.Max(speed, .05f);
                specks.Add(new Speck
                {
                    Position = position + direction * jetSize * .42f,
                    Velocity = direction * travel * (4f * speed),
                    Born = clock, Lifetime = .38f / motion, Size = jetSize, Seed = seed, RetiredAt = -1f,
                    Angle = Mathf.Atan2(-direction.x, direction.y), Jet = true
                });
                for (int i = 1; i < count; i++)
                {
                    float fragment = Hash(serial * 11 + i * 7);
                    specks.Add(new Speck
                    {
                        Position = position + direction * jetSize * .5f,
                        Velocity = new Vector3(side * (14f + fragment * 13f), 3f + i * 4f, 0f) * (travel * speed),
                        Acceleration = Vector3.down * (travel * 48f * speed * speed),
                        Born = clock, Lifetime = (.64f + fragment * .12f) / motion,
                        Size = worldPixel * 3f,
                        Seed = fragment, RetiredAt = -1f,
                        Floor = body.bounds.min.y + worldPixel, Angle = (fragment - .5f) * .9f
                    });
                }
            }
        }

        private void Rebuild(float clock)
        {
            vertices.Clear(); uvs.Clear(); colors.Clear(); indices.Clear();
            foreach (var speck in specks)
            {
                float age = Mathf.Clamp01((clock - speck.Born) / speck.Lifetime);
                float alpha = Mathf.Min(age * 10f, 1f) * Mathf.Clamp01((1f - age) * 3f);
                if (speck.RetiredAt >= 0f) alpha *= 1f - Mathf.SmoothStep(0f, 1f, (clock - speck.RetiredAt) / .65f);
                int start = vertices.Count;
                foreach (var corner in Corners)
                {
                    if (blood)
                    {
                        // Axis-aligned cells stay square instead of rotating a sampled pixel mask.
                        float pixel = speck.Size / (speck.Jet ? 9f : 3f);
                        var dimensions = speck.Jet ? new Vector2(9f, 7f) : new Vector2(3f, 4f);
                        var size = dimensions * pixel;
                        var lower = speck.Position - new Vector3(size.x, size.y, 0f) * .5f;
                        lower.x = Mathf.Round(lower.x / pixel) * pixel;
                        lower.y = Mathf.Round(lower.y / pixel) * pixel;
                        var world = lower + new Vector3(corner.x * size.x, corner.y * size.y, 0f);
                        vertices.Add(origin.InverseTransformPoint(world));
                        bool mirror = speck.Jet ? speck.Angle > 0f : speck.Seed > .5f;
                        uvs.Add(new Vector2(mirror ? 1f - corner.x : corner.x, corner.y));
                    }
                    else
                    {
                        var offset = new Vector3(corner.x - .5f, (corner.y - .5f) * (snow ? 1f : 1.7f), 0f) * speck.Size;
                        if (speck.Crystal)
                        {
                            float cosine = Mathf.Cos(speck.Angle), sine = Mathf.Sin(speck.Angle);
                            offset = new Vector3(offset.x * cosine - offset.y * sine, offset.x * sine + offset.y * cosine, 0f);
                        }
                        vertices.Add(origin.InverseTransformPoint(speck.Position + offset));
                        uvs.Add(corner);
                    }
                    colors.Add(blood || snow ? new Color(speck.Seed, age, blood && speck.Jet ? 1f : 0f, alpha) : new Color(1f, 1f, 1f, alpha));
                }
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
                indices.Add(start + 2); indices.Add(start + 3); indices.Add(start + 1);
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
        }

        private static readonly Vector2[] Corners = { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        private static float Hash(int value) => Mathf.Repeat(Mathf.Sin(value * 12.9898f) * 43758.5453f, 1f);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (pendingMask) maskRequest.WaitForCompletion();
            Hide(); specks.Clear(); anchors.Clear(); fallbackAnchors.Clear();
            root.GetComponent<MeshFilter>().sharedMesh = null;
            BodyVisual.DestroyOwned(root); BodyVisual.DestroyOwned(mesh); BodyVisual.DestroyOwned(material);
        }
    }
}
