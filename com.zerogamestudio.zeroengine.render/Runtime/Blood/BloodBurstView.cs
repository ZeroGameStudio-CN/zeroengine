using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace ZeroEngine.Render
{
    // Cosmetic mesh depth + swept XY terrain collision. Never creates a physics body/collider.
    // Attached to the existing pooled death effect; its existing lifetime lease owns the tail.
    [DisallowMultipleComponent]
    public class BloodBurstView : MonoBehaviour
    {
        private const int Count = 30;
        private const int DebrisCount = 6;
        private static readonly ProfilerMarker UpdateMarker = new(ProfilerCategory.Scripts, "ZeroEngine.DeathBlood.Update");
        private static readonly ProfilerMarker GroundMarker = new(ProfilerCategory.Scripts, "ZeroEngine.DeathBlood.Ground");
        private readonly Drop[] drops = new Drop[Count];
        private readonly Drop[] debris = new Drop[DebrisCount];
        private readonly RaycastHit2D[] hits = new RaycastHit2D[4];
        private readonly List<GroundEmitter> groundEmitters = new();
        private MaterialPropertyBlock properties;
        private Mesh strandMesh;
        private Transform strandRoot;
        private MeshRenderer strandRenderer;
        private readonly List<Vector3> strandVertices = new(640);
        private readonly List<int> strandTriangles = new(3200);
        private readonly List<Color> strandColors = new(640);
        private ContactFilter2D terrain;
        private IDisposable lifetime;
        private Transform visuals;
        private Camera viewCamera;
        private Color sourceTint;
        private Vector2 pixelWorldSize;
        private int scopeVersion;
        private BloodVisualContext context;
        private BloodVisualResources resources=>context.Resources;
        private bool playing;
        private float elapsed;
        public int LiveDrops { get; private set; }
        public int LandedDrops { get; private set; }
        public int Contacts { get; private set; }
        public int LiveStrands { get; private set; }
        public int GroundedAuthoredParticles { get; private set; }
        public int LiveDebris { get; private set; }
        public int LandedDebris { get; private set; }
        public int RetainedDebris { get; private set; }
        public bool Playing => playing;
        public Vector2 SprayDirection { get; private set; }
        public Transform VisualRoot => visuals;

        private struct Drop
        {
            public Transform Body, Rim;
            public MeshRenderer Renderer, RimRenderer;
            public MeshFilter Filter;
            public Vector3 Position, Velocity, Shape, AngularVelocity, SurfacePoint;
            public Quaternion Rotation;
            public Vector2 SurfaceNormal;
            public float Radius, Life, Shade, ImpactAt, StrandLife, StrandLimit, StrandWidth;
            public int Bounces, Variant;
            public byte Kind;
            public bool Settled, Volume;
            public bool Retained;
        }

        private sealed class GroundEmitter
        {
            public ParticleSystem System;
            public ParticleSystem.Particle[] Buffer;
            public readonly Dictionary<uint, GroundParticle> Landed = new();
        }

        private struct GroundParticle
        {
            public Vector3 Position, Size, Rotation, Surface;
            public Vector2 Normal;
        }

        public void Play(BloodVisualContext visualContext, IDisposable lease, uint seed, Vector2 sourcePixelSize, Vector2 direction = default)
        {
            Stop();
            context=visualContext??throw new ArgumentNullException(nameof(visualContext));
            lifetime=lease;context.PaletteChanged+=OnPaletteChanged;
            properties ??= new MaterialPropertyBlock();
            pixelWorldSize = sourcePixelSize;
            SprayDirection = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.zero;
            properties.SetVector("_PixelWorldSize", new Vector4(sourcePixelSize.x, sourcePixelSize.y, 0f, 0f));
            terrain = new ContactFilter2D { useTriggers = false };
            terrain.SetLayerMask(context.TerrainMask);
            var random = new System.Random(unchecked((int)(seed * 747796405u + 2891336453u)));
            float Next(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            sourceTint = ReadAuthoredTint();
            foreach (var particles in GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (!particles.collision.enabled || particles.collision.mode != ParticleSystemCollisionMode.Collision2D ||
                    !renderer || renderer.renderMode != ParticleSystemRenderMode.Billboard) continue;
                groundEmitters.Add(new GroundEmitter { System = particles,
                    Buffer = new ParticleSystem.Particle[particles.main.maxParticles] });
            }
            viewCamera = context.ViewCamera?.Invoke();
            // Authored spray keeps its own collision, visibility and lifetime throughout.
            // This detached mesh tail never truncates particles that are still flying.
            scopeVersion = context.ScopeVersion;
            elapsed = 0f;
            Contacts = LandedDrops = 0;
            LiveDebris = LandedDebris = RetainedDebris = 0;
            LiveDrops = Count;
            playing = true;
            context.ActiveBursts++;
            var jets = new Vector3[4];
            int depthSign = random.Next(2) == 0 ? -1 : 1;
            for (int i = 0; i < jets.Length; i++)
            {
                jets[i] = new Vector3((i - 1.5f + Next(-.45f, .45f)) * 4.2f + Next(-.6f, .6f), Next(5f, 8.2f),
                    depthSign * (i % 2 == 0 ? -1 : 1) * Next(2.7f, 5.2f));
                if (SprayDirection != Vector2.zero)
                {
                    var across = new Vector2(-SprayDirection.y, SprayDirection.x);
                    var forward = SprayDirection * Next(12.5f, 18f)
                        + across * ((i - 1.5f + Next(-.45f, .45f)) * Next(2.1f, 2.9f)) + Vector2.up * Next(4.4f, 6.4f);
                    jets[i].x = forward.x;
                    jets[i].y = forward.y;
                }
            }
            for (int i = 0; i < Count; i++)
            {
                ref var drop = ref drops[i];
                if (!drop.Body)
                {
                    drop.Body = MakeRenderer("Spatial blood", out drop.Renderer);
                    drop.Rim = MakeRenderer("Contact blood", out drop.RimRenderer);
                }
                drop.Variant = (i + random.Next(3)) % 3;
                drop.Kind = 0;
                int node = i % 6;
                drop.Volume = i < 24 && node == 0;
                drop.Filter = drop.Body.GetComponent<MeshFilter>();
                drop.Filter.sharedMesh = drop.Volume ? resources.VolumeMeshFor(drop.Variant)
                    : resources.DropMeshFor(drop.Variant);
                drop.Rim.GetComponent<MeshFilter>().sharedMesh = resources.PuddleMeshFor(drop.Variant);
                drop.Renderer.sharedMaterial = resources.DropMaterial;
                drop.RimRenderer.sharedMaterial = resources.ShadowMaterial;
                drop.Body.gameObject.SetActive(true);
                drop.Rim.gameObject.SetActive(false);
                drop.Radius = i < 24 ? node == 0 ? Next(.14f, .25f) : node < 3 ? Next(.06f, .11f) : Next(.025f, .058f) : Next(.025f, .05f);
                drop.Position = transform.position + new Vector3(Next(-.17f, .17f), Next(-.05f, .16f), Next(-.7f, .45f));
                drop.Velocity = i < 24
                    ? jets[i / 6] * (.78f + node * .11f + Next(-.06f, .06f)) + new Vector3(Next(-1f, 1f), Next(-.6f, .6f), Next(-.5f, .5f))
                    : new Vector3(Next(-7.5f, 7.5f), Next(3.5f, 7.5f), Next(-2.6f, 2.2f));
                if (drop.Volume && node == 0) drop.Velocity.z *= 1.5f;
                if (i < 24) drop.Position += jets[i / 6].normalized * node * Next(.025f, .06f);
                drop.Life = Next(5f, 6.2f);
                drop.Shade = drop.Volume ? Next(.76f, .94f) : node < 3 ? Next(.87f, 1f) : Next(.93f, 1f);
                drop.Shape = new Vector3(Next(.55f, 1.35f), Next(.7f, 1.55f), Next(.5f, 1.2f));
                drop.Rotation = drop.Volume ? Quaternion.Euler(Next(-70f,70f), Next(-150f,150f), Next(-90f,90f))
                    : Quaternion.Euler(Next(-8f,8f), Next(-12f,12f), Next(-20f,20f));
                drop.AngularVelocity = drop.Volume ? new Vector3(Next(-180f,180f), Next(-220f,220f), Next(-100f,100f))
                    : new Vector3(0f, 0f, Next(-35f, 35f));
                drop.StrandLife = Next(.32f, .56f);
                drop.StrandLimit = Next(1.5f, 2.3f);
                drop.StrandWidth = node > 0 && random.NextDouble() < .12 ? 0f : Next(.72f, 1.12f);
                drop.ImpactAt = -1f;
                drop.Bounces = 0;
                drop.Settled = false;
                Draw(ref drop, 0f);
            }
            if (context.DebrisEnabled) PrepareDebris(random);
            PrepareStrands();
            Advance(.04f);
        }

        private void PrepareDebris(System.Random random)
        {
            float Next(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            int side = random.Next(2) == 0 ? -1 : 1;
            for (int i = 0; i < DebrisCount; i++)
            {
                ref var piece = ref debris[i];
                bool bone = i >= 4;
                if (!piece.Body)
                {
                    if (BloodResidueStore.TryTake(context, out piece.Body, out piece.Renderer, out piece.Rim, out piece.RimRenderer))
                    { piece.Body.SetParent(visuals, false); piece.Rim.SetParent(visuals, false); }
                    else
                    {
                        piece.Body = MakeRenderer("Pixel flesh / bone", out piece.Renderer);
                        piece.Rim = MakeRenderer("Debris contact", out piece.RimRenderer);
                    }
                    piece.Filter = piece.Body.GetComponent<MeshFilter>();
                }
                piece.Kind = (byte)(bone ? 2 : 1);
                piece.Variant = random.Next(3);
                piece.Filter.sharedMesh = resources.DebrisMeshFor(piece.Variant, bone);
                piece.Rim.GetComponent<MeshFilter>().sharedMesh = resources.PuddleMeshFor(piece.Variant);
                piece.Renderer.sharedMaterial = resources.DropMaterial;
                piece.RimRenderer.sharedMaterial = resources.ShadowMaterial;
                piece.Body.gameObject.SetActive(true);
                piece.Rim.gameObject.SetActive(false);
                piece.Radius = bone ? Next(.12f, .19f) : Next(.14f, .23f);
                piece.Position = transform.position + new Vector3(Next(-.12f,.12f), Next(.02f,.18f), Next(-.3f,.3f));
                piece.Velocity = new Vector3(side * (i % 2 == 0 ? -1f : 1f) * Next(2.3f,6.8f) + Next(-1.1f,1.1f), Next(4.2f,8.4f), Next(-4f,4f));
                piece.Velocity += new Vector3(SprayDirection.x, SprayDirection.y, 0f) * 3.5f;
                piece.Shape = bone ? new Vector3(Next(1.15f,1.8f), Next(.75f,1.15f), Next(.8f,1.3f))
                    : new Vector3(Next(.7f,1.3f), Next(.7f,1.1f), Next(.75f,1.35f));
                piece.Rotation = Quaternion.Euler(Next(-70f,70f), Next(-180f,180f), Next(-100f,100f));
                piece.AngularVelocity = new Vector3(Next(-220f,220f), Next(-200f,200f), Next(-180f,180f));
                piece.Shade = Next(.9f,1f);
                piece.Life = 12f; // Flight-only safety bound; grounded pieces move to room ownership.
                piece.Volume = true;
                piece.ImpactAt = -1f;
                piece.Bounces = 0;
                piece.Settled = false;
                piece.Retained = false;
                LiveDebris++;
                Draw(ref piece, 0f);
            }
        }

        public void HideDebris()
        {
            for (int i = 0; i < DebrisCount; i++)
            {
                ref var piece = ref debris[i];
                piece.Life = 0f;
                if (piece.Body) piece.Body.gameObject.SetActive(false);
                if (piece.Rim) piece.Rim.gameObject.SetActive(false);
            }
            LiveDebris = 0;
        }

        private Color ReadAuthoredTint()
        {
            Color tint = Color.red;
            float best = 0f;
            // The generic root is a white collector. Its nested blood emitter owns the colour.
            foreach (var particles in GetComponentsInChildren<ParticleSystem>(true))
            {
                var color = particles.main.startColor.Evaluate(.5f, .5f);
                float red = color.r - Mathf.Max(color.g, color.b);
                if (red <= best) continue;
                best = red;
                tint = color;
            }
            tint.a = 1f;
            return tint;
        }

        private Transform MakeRenderer(string label, out MeshRenderer renderer)
        {
            // Sibling cosmetic tree has its own C# blood palette. The original prefab's
            // The source keeps ownership of its authored particle subtree/materials.
            if (!visuals) visuals = new GameObject("Spatial death blood") { hideFlags = HideFlags.DontSave }.transform;
            var child = new GameObject(label) { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
            child.transform.SetParent(visuals, false);
            child.AddComponent<MeshFilter>();
            renderer = child.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return child.transform;
        }

        private void Update()
        {
            if (!playing) return;
            if (!context.WorldEnabled || scopeVersion != context.ScopeVersion) { Stop(); return; }
            // The source actor can already be reused. The detached tail belongs to global gameplay time.
            using var scope = UpdateMarker.Auto();
            Advance(context.DeltaTime());
        }

        private void LateUpdate()
        {
            if (playing && context.DeltaTime() > 0f)
            {
                using var scope = GroundMarker.Auto();
                RefreshGroundParticles();
            }
        }

        // Adjust only stopped particles with a floor beneath them. Native flight, colour,
        // collision settings and remaining lifetime stay intact; no emitter/module is edited.
        public void RefreshGroundParticles()
        {
            GroundedAuthoredParticles = 0;
            foreach (var emitter in groundEmitters)
            {
                var particles = emitter.System;
                if (!particles) continue;
                var main = particles.main;
                var space = main.simulationSpace == ParticleSystemSimulationSpace.World ? null :
                    main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : particles.transform;
                int count = particles.GetParticles(emitter.Buffer);
                bool changed = false;
                for (int i = 0; i < count; i++)
                {
                    ref var particle = ref emitter.Buffer[i];
                    if (!emitter.Landed.TryGetValue(particle.randomSeed, out var landed))
                    {
                        var velocity = space ? space.TransformVector(particle.velocity) : particle.velocity;
                        if (velocity.sqrMagnitude > .04f || particle.startLifetime - particle.remainingLifetime < .08f) continue;
                        var position = space ? space.TransformPoint(particle.position) : particle.position;
                        float radius = particle.GetCurrentSize(particles) * .5f;
                        int contacts = Physics2D.Raycast(position, Vector2.down, terrain, hits, radius + pixelWorldSize.y);
                        RaycastHit2D floor = default;
                        for (int h = 0; h < contacts; h++)
                            if (hits[h].normal.y > .5f) { floor = hits[h]; break; }
                        if (!floor.collider) continue;
                        landed = new GroundParticle { Position = particle.position, Size = particle.startSize3D,
                            Rotation = particle.rotation3D, Surface = new Vector3(floor.point.x, floor.point.y, position.z),
                            Normal = floor.normal };
                        // A few source-texel depth offsets break up the dense billboard line.
                        uint lane = unchecked(particle.randomSeed * 747796405u + 2891336453u) >> 29;
                        landed.Surface.z += (lane / 7f - .5f) * pixelWorldSize.y * 12f;
                        emitter.Landed.Add(particle.randomSeed, landed);
                    }
                    // Compress size extremes on the floor, retaining irregular small/large patches.
                    float width = Mathf.Sqrt(landed.Size.x * .18f) * 1.25f;
                    float height = Mathf.Min(landed.Size.y * .52f, width * .38f);
                    particle.startSize3D = new Vector3(width, height, landed.Size.z);
                    particle.rotation3D = new Vector3(0f, 0f,
                        Mathf.Atan2(landed.Normal.y, landed.Normal.x) * Mathf.Rad2Deg - 90f);
                    var contact = landed.Surface + new Vector3(landed.Normal.x, landed.Normal.y, 0f) * (height * .5f + .004f);
                    particle.position = space ? space.InverseTransformPoint(contact) : contact;
                    GroundedAuthoredParticles++;
                    changed = true;
                }
                if (changed) particles.SetParticles(emitter.Buffer, count);
            }
        }

        private void RestoreGroundParticles()
        {
            foreach (var emitter in groundEmitters)
            {
                if (!emitter.System || emitter.Landed.Count == 0) continue;
                int count = emitter.System.GetParticles(emitter.Buffer);
                for (int i = 0; i < count; i++)
                {
                    ref var particle = ref emitter.Buffer[i];
                    if (!emitter.Landed.TryGetValue(particle.randomSeed, out var landed)) continue;
                    particle.position = landed.Position;
                    particle.startSize3D = landed.Size;
                    particle.rotation3D = landed.Rotation;
                }
                emitter.System.SetParticles(emitter.Buffer, count);
            }
            groundEmitters.Clear();
            GroundedAuthoredParticles = 0;
        }

        private void OnPaletteChanged()
        {
            if (!playing) return;
            for (int i = 0; i < Count; i++)
                if (elapsed < drops[i].Life) Draw(ref drops[i], elapsed);
            for (int i = 0; i < DebrisCount; i++)
                if (!debris[i].Retained && elapsed < debris[i].Life) Draw(ref debris[i], elapsed);
            DrawStrands();
        }

        public void Advance(float delta)
        {
            if (!playing || !float.IsFinite(delta) || delta <= 0f) return;
            elapsed += delta;
            LiveDrops = 0;
            int steps = Mathf.Clamp(Mathf.CeilToInt(delta / .025f), 1, 8);
            float step = delta / steps;
            for (int i = 0; i < Count; i++)
            {
                ref var drop = ref drops[i];
                if (elapsed >= drop.Life) { drop.Body.gameObject.SetActive(false); drop.Rim.gameObject.SetActive(false); continue; }
                LiveDrops++;
                for (int s = 0; s < steps && !drop.Settled; s++) Move(ref drop, step);
                if (NeedsRedraw(drop)) Draw(ref drop, elapsed);
            }
            LiveDebris = 0;
            for (int i = 0; i < DebrisCount; i++)
            {
                ref var piece = ref debris[i];
                if (piece.Retained) continue;
                if (!piece.Body) continue;
                if (elapsed >= piece.Life)
                { piece.Body.gameObject.SetActive(false); piece.Rim.gameObject.SetActive(false); continue; }
                LiveDebris++;
                for (int s = 0; s < steps && !piece.Settled; s++) Move(ref piece, step);
                if (NeedsRedraw(piece)) Draw(ref piece, elapsed);
                if (piece.Settled && elapsed - piece.ImpactAt >= .32f)
                {
                    Color rawTint = piece.Kind == 2 ? new Color(.68f, .61f, .51f, 1f) * piece.Shade : sourceTint * (piece.Shade * .55f);
                    rawTint.a = 1f;
                    if (BloodResidueStore.Retain(context, piece.Body, piece.Renderer, piece.Rim, piece.RimRenderer, rawTint, piece.Kind == 2))
                    {
                        piece.Retained = true; RetainedDebris++; LiveDebris--;
                        piece.Body = piece.Rim = null; piece.Renderer = piece.RimRenderer = null; piece.Filter = null;
                    }
                }
            }
            DrawStrands();
            if (LiveDrops == 0 && LiveDebris == 0) Finish();
        }

        // Once the landing spread is complete, world pose and full-alpha colour are
        // constant until fade. Palette changes already redraw explicitly in OnPaletteChanged.
        private bool NeedsRedraw(in Drop drop) => !drop.Settled || elapsed - drop.ImpactAt < .32f || elapsed >= drop.Life - 1.1f;

        private void Move(ref Drop drop, float delta)
        {
            drop.Rotation = Quaternion.Euler(drop.AngularVelocity * delta) * drop.Rotation;
            float drag = Mathf.Exp(-delta * 1.1f);
            drop.Velocity.x *= drag; drop.Velocity.z *= drag;
            drop.Velocity.y -= 16f * delta;
            var travel = drop.Velocity * delta;
            var xy = new Vector2(travel.x, travel.y);
            float distance = xy.magnitude;
            int count = distance > .00001f
                ? Physics2D.CircleCast(drop.Position, drop.Radius, xy / distance, terrain, hits, distance) : 0;
            RaycastHit2D surface = default;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance <= .00001f || Vector2.Dot(xy, hits[i].normal) >= 0f) continue;
                // One-way surfaces accept a falling droplet from above, just like the room's platforms.
                if (hits[i].collider.gameObject.layer == context.OneWayLayer &&
                    (hits[i].normal.y < .5f || xy.y >= 0f)) continue;
                surface = hits[i]; break;
            }
            if (!surface.collider) { drop.Position += travel; return; }
            Contacts++;
            context.CollisionCount++;
            drop.Bounces++;
            drop.ImpactAt = elapsed;
            var normal = surface.normal;
            drop.Position = new Vector3(surface.centroid.x + normal.x * .004f,
                surface.centroid.y + normal.y * .004f, drop.Position.z + travel.z * surface.fraction);
            Vector2 rebound = Vector2.Reflect(new Vector2(drop.Velocity.x, drop.Velocity.y), normal)
                * (drop.Kind == 2 ? .27f : drop.Kind == 1 ? .11f : .14f);
            drop.Velocity = new Vector3(rebound.x, rebound.y, drop.Velocity.z * .25f);
            if (drop.Kind != 0 && normal.y < .55f) return;
            if (drop.Bounces < 2 && drop.Radius > .12f && rebound.sqrMagnitude > .55f) return;
            drop.Settled = true;
            if (drop.Kind == 0) { LandedDrops++; context.SettledCount++; }
            else LandedDebris++;
            drop.SurfacePoint = new Vector3(surface.point.x, surface.point.y, drop.Position.z);
            drop.SurfaceNormal = normal;
            drop.Position = drop.SurfacePoint + new Vector3(normal.x, normal.y, 0f) * .006f;
            if (drop.Kind == 0) drop.Filter.sharedMesh = resources.PuddleMeshFor(drop.Variant);
            drop.Rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(normal.x, normal.y, 0f)) *
                Quaternion.Euler(drop.Kind == 2 ? 90f : 0f, drop.AngularVelocity.y, 0f);
        }

        private void Draw(ref Drop drop, float age)
        {
            float fade = drop.Kind == 0 ? FadeAt(drop.Life) : 1f;
            drop.Body.position = drop.Position;
            if (drop.Kind != 0)
            {
                var shape = drop.Shape;
                if (drop.Settled && drop.Kind == 1) shape.y *= .65f;
                drop.Body.localScale = shape * drop.Radius;
                drop.Body.rotation = drop.Rotation;
                if (drop.Settled)
                {
                    var extents = Vector3.Scale(drop.Filter.sharedMesh.bounds.extents, drop.Body.localScale);
                    var n = new Vector3(drop.SurfaceNormal.x, drop.SurfaceNormal.y, 0f);
                    float height = Mathf.Abs(Vector3.Dot(n, drop.Body.right)) * extents.x
                        + Mathf.Abs(Vector3.Dot(n, drop.Body.up)) * extents.y
                        + Mathf.Abs(Vector3.Dot(n, drop.Body.forward)) * extents.z;
                    drop.Body.position = drop.SurfacePoint + n * (height + .006f);
                }
            }
            else if (drop.Settled)
            {
                float spread = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - drop.ImpactAt) * 3.5f));
                float footprint = Mathf.Sqrt(drop.Radius * .10f);
                drop.Body.localScale = Vector3.Scale(drop.Shape,
                    new Vector3(Mathf.Lerp(drop.Radius * 1.1f, footprint * 2.9f, spread),
                        drop.Radius * Mathf.Lerp(2.4f, .45f, spread), Mathf.Lerp(drop.Radius * .85f, footprint * 2.55f, spread)));
                drop.Body.rotation = drop.Rotation;
            }
            else
            {
                float stretch = Mathf.Clamp(drop.Velocity.magnitude * .2f, 1f, 2.25f);
                float wobble = Mathf.Sin(age * 17f + drop.Variant * 2f) * .16f * Mathf.Exp(-age * 1.5f);
                drop.Body.localScale = Vector3.Scale(drop.Shape,
                    new Vector3(drop.Radius * (1f + wobble) / Mathf.Sqrt(stretch),
                        drop.Radius * stretch / (1f + wobble), drop.Radius * (1f - wobble * .5f)));
                var facing = viewCamera ? viewCamera.transform.rotation : Quaternion.identity;
                var direction = Quaternion.Inverse(facing) * drop.Velocity;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
                drop.Body.rotation = drop.Volume ? drop.Rotation : facing * Quaternion.Euler(0f, 0f, angle) * drop.Rotation;
            }
            var size = drop.Filter.sharedMesh.bounds.size;
            var scale = drop.Body.localScale;
            scale.x = Mathf.Max(scale.x, pixelWorldSize.x * 1.05f / Mathf.Max(.001f, size.x));
            if (drop.Settled)
                scale.z = Mathf.Max(scale.z, pixelWorldSize.y * 1.05f / Mathf.Max(.001f, size.z));
            else
                scale.y = Mathf.Max(scale.y, pixelWorldSize.y * 1.05f / Mathf.Max(.001f, size.y));
            drop.Body.localScale = scale;
            var red = sourceTint * drop.Shade;
            if (drop.Kind == 1) red = context.RemapBlood(red * .55f);
            else if (drop.Kind == 2) red = new Color(.68f, .61f, .51f, 1f) * drop.Shade;
            else red = context.RemapBlood(drop.Settled ? red * .92f : red);
            red.a = fade;
            properties.SetColor("_BaseColor", red);
            drop.Renderer.SetPropertyBlock(properties);
            DrawContactShadow(ref drop, fade);
        }

        private void DrawContactShadow(ref Drop drop, float fade)
        {
            Vector3 contact = drop.SurfacePoint;
            Vector2 normal = drop.SurfaceNormal;
            if (!drop.Settled)
            {
                int count = Physics2D.Raycast(drop.Position, Vector2.down, terrain, hits, 6f);
                bool found = false;
                for (int i = 0; i < count; i++)
                    if (hits[i].normal.y > .5f)
                    {
                        contact = new Vector3(hits[i].point.x, hits[i].point.y, drop.Position.z);
                        normal = hits[i].normal; found = true; break;
                    }
                if (!found) { drop.Rim.gameObject.SetActive(false); return; }
            }
            drop.Rim.gameObject.SetActive(true);
            float height = Mathf.Max(0f, drop.Position.y - contact.y);
            float size = drop.Settled ? Mathf.Sqrt(drop.Radius * .10f) * 2.65f : drop.Radius * (1.5f + height * .12f);
            if (drop.Kind != 0) size = drop.Radius * 1.5f;
            drop.Rim.position = contact + new Vector3(normal.x, normal.y, 0f) * .005f;
            drop.Rim.rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(normal.x, normal.y, 0f));
            drop.Rim.localScale = new Vector3(size, .008f, size * .8f);
            properties.SetColor("_BaseColor", new Color(.008f, .005f, .01f, fade * (drop.Settled ? .30f : .44f) / (1f + height)));
            drop.RimRenderer.SetPropertyBlock(properties);
        }

        public void Stop() => Finish();

        private void PrepareStrands()
        {
            if (!strandRoot) strandRoot = MakeRenderer("Viscous blood threads", out strandRenderer);
            if (!strandMesh)
            {
                strandMesh = new Mesh { name = "Pixel plasma threads", hideFlags = HideFlags.HideAndDontSave };
                strandMesh.MarkDynamic();
            }
            strandRoot.GetComponent<MeshFilter>().sharedMesh = strandMesh;
            strandRenderer.sharedMaterial = resources.DropMaterial;
        }

        private void DrawStrands()
        {
            if (!strandRoot || !playing) return;
            strandVertices.Clear(); strandTriangles.Clear(); strandColors.Clear(); LiveStrands = 0;
            const int sections = 6, sides = 4;
            for (int i = 0; i < 24; i++)
            {
                if (i % 6 == 5) continue;
                ref var a = ref drops[i]; ref var b = ref drops[i + 1];
                if (elapsed >= a.Life || elapsed >= b.Life || a.Settled && b.Settled || a.StrandWidth <= 0f) continue;
                float fade = Mathf.Min(FadeAt(a.Life), FadeAt(b.Life));
                if (fade < .015f) continue;
                float distance = Vector3.Distance(a.Position, b.Position);
                if (elapsed >= a.StrandLife || distance > a.StrandLimit) continue;
                float neck = Mathf.Pow(1f - elapsed / a.StrandLife, .65f);
                float radius = Mathf.Min(.08f, Mathf.Min(a.Radius, b.Radius) * .9f) * neck * a.StrandWidth;
                if (radius < .008f || distance < .02f) continue;
                // Retain a full source pixel until the thread breaks, rather than a finer wire.
                radius = Mathf.Max(radius, Mathf.Max(pixelWorldSize.x, pixelWorldSize.y) * .75f);
                var start = a.Position;
                var end = b.Position;
                var direction = (end - start).normalized;
                var normal = viewCamera ? viewCamera.transform.forward : Vector3.forward;
                var side = Vector3.Cross(direction, normal);
                if (side.sqrMagnitude < .001f) side = Vector3.Cross(direction, Vector3.up);
                side.Normalize(); normal = Vector3.Cross(side, direction).normalized;
                int first = strandVertices.Count;
                for (int section = 0; section <= sections; section++)
                {
                    float t = (float)section / sections;
                    var center = Vector3.Lerp(start, end, t);
                    center += Vector3.down * Mathf.Sin(t * Mathf.PI) * (distance * .13f + elapsed * .04f);
                    float width = radius * (.7f + .3f * Mathf.Abs(Mathf.Cos(t * Mathf.PI)));
                    for (int s = 0; s < sides; s++)
                    {
                        float angle = s * Mathf.PI * 2f / sides;
                        strandVertices.Add(center + side * Mathf.Cos(angle) * width + normal * Mathf.Sin(angle) * width * .15f);
                        strandColors.Add(new Color(1f, 1f, 1f, fade));
                    }
                    if (section == 0) continue;
                    int ring = first + section * sides, previous = ring - sides;
                    for (int s = 0; s < sides; s++)
                    {
                        int next = (s + 1) % sides;
                        strandTriangles.Add(previous + s); strandTriangles.Add(ring + s); strandTriangles.Add(ring + next);
                        strandTriangles.Add(previous + s); strandTriangles.Add(ring + next); strandTriangles.Add(previous + next);
                    }
                }
                LiveStrands++;
            }
            strandMesh.Clear();
            if (LiveStrands != 0)
            {
                strandMesh.SetVertices(strandVertices); strandMesh.SetColors(strandColors); strandMesh.SetTriangles(strandTriangles, 0);
                strandMesh.RecalculateNormals(); strandMesh.RecalculateBounds();
                properties.SetColor("_BaseColor", context.RemapBlood(sourceTint * new Color(.9f,.9f,.9f,1f)));
                strandRenderer.SetPropertyBlock(properties);
            }
            strandRoot.gameObject.SetActive(LiveStrands != 0);
        }

        private float FadeAt(float life) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(life - 1.1f, life, elapsed));

        private void Finish()
        {
            if(context!=null)context.PaletteChanged-=OnPaletteChanged;
            RestoreGroundParticles();
            if (playing)
            {
                context.ActiveBursts = Mathf.Max(0, context.ActiveBursts - 1);
            }
            playing = false;
            LiveDrops = 0;
            HideDebris();
            LiveStrands = 0;
            if (strandRoot) strandRoot.gameObject.SetActive(false);
            lifetime?.Dispose(); lifetime = null;
            foreach (var drop in drops)
            {
                if (drop.Body) drop.Body.gameObject.SetActive(false);
                if (drop.Rim) drop.Rim.gameObject.SetActive(false);
            }
        }

        protected virtual void OnDisable() => Stop();
        protected virtual void OnDestroy()
        {
            Stop();
            if (strandMesh) Destroy(strandMesh);
            if (visuals) Destroy(visuals.gameObject);
        }
    }
}
