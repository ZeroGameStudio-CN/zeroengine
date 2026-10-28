using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests
{
    public sealed class BodyVisualTests
    {
        private static BodyVisual CreateVisual(Transform origin, IRenderPoseSource source, Shader shader,
            BodyVisualSettings settings, Material appearanceTemplate = null)
        {
            var resources = ZeroEngine.Render.Urp.BodyVisualResourcesLoader.Load();
            return appearanceTemplate ? new BodyVisual(origin,source,shader,settings,appearanceTemplate,resources)
                : new BodyVisual(origin,source,shader,settings,resources);
        }

        // Permanent regression: ice transitions are readable under dense art, share
        // the particle budget, pause on the caller clock and cannot repeat after removal.
        [TestCase(1f, 1f / 16f, 24)]
        [TestCase(4f, 1f / 128f, 6)]
        public void SolidIce_EntryAndExitChips_RespectClockBudgetAndRetirement(float scale, float pixelSize, int budget)
        {
            var origin = new GameObject("SyntheticIceTransitions");
            var texture = new Texture2D(16, 32);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), Vector2.one * .5f, 16f);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite;
            origin.transform.localScale = Vector3.one * scale;
            var type = typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusSpecks", true);
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            IDisposable emitter = null;
            try
            {
                emitter = (IDisposable)Activator.CreateInstance(type, origin.transform,
                    Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks"), BodyVisualSettings.For(BodyVisualKind.FrozenSolid));
                type.GetField("lastMaskClock", fields).SetValue(emitter, 1000f);
                var anchors = (System.Collections.Generic.List<Vector2>)type.GetField("anchors", fields).GetValue(emitter);
                anchors.AddRange(new[] { new Vector2(.2f,.25f), new Vector2(.8f,.25f),
                    new Vector2(.2f,.5f), new Vector2(.8f,.5f), new Vector2(.2f,.75f), new Vector2(.8f,.75f) });
                var update = type.GetMethod("Update");
                var mesh = (Mesh)type.GetField("mesh", fields).GetValue(emitter);
                void Tick(float clock, bool emitting = true) => update.Invoke(emitter,
                    new object[] { clock, body, new Vector4(0, 0, 1, .5f), pixelSize, null, 1f, 0, budget, emitting, 1f });
                Tick(0f); Tick(0f);
                Assert.That(mesh.vertexCount, Is.Zero, "A stopped clock must not emit the entry burst.");
                Tick(.01f); Tick(.06f);
                Assert.That(mesh.vertexCount, Is.GreaterThan(8), "Entry should read as a small burst, not a single falling speck.");
                Assert.That(mesh.vertexCount, Is.LessThanOrEqualTo(budget * 4));
                float width = origin.transform.TransformVector(mesh.vertices[1] - mesh.vertices[0]).magnitude;
                Assert.That(width / (Mathf.Min(pixelSize, 1f / 16f) * scale), Is.InRange(2f, 5f),
                    "Ice chips stay a few source pixels rather than expanding with the animated bounds.");
                var entry = mesh.vertices;
                Tick(.06f);
                Assert.That(mesh.vertices, Is.EqualTo(entry), "Refreshing at the same clock cannot replay or move the entry.");
                Tick(.06f, false);
                Assert.That(mesh.vertices, Is.EqualTo(entry), "Removing while paused must defer new exit particles.");
                Tick(.08f, false);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0).And.LessThanOrEqualTo(budget * 4));
                var exit = mesh.vertices;
                Tick(.08f, false);
                Assert.That(mesh.vertices, Is.EqualTo(exit));
                Tick(.8f, false); Tick(2f, false);
                Assert.That(mesh.vertexCount, Is.Zero, "A retired source must finish and stay silent.");
                Tick(2.01f);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0).And.LessThanOrEqualTo(budget * 4),
                    "A genuinely new application may play entry again.");
                type.GetMethod("Clear").Invoke(emitter, null);
                Assert.That((bool)type.GetProperty("HasParticles").GetValue(emitter), Is.False);
            }
            finally
            {
                emitter?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        // Permanent regression: a caller-clocked burst remains bounded, pauses completely and retires without new births.
        [TestCase(1f)]
        [TestCase(.2f)]
        public void BloodSpurts_RespectCallerClock_Budget_AndRetirement(float speed)
        {
            var origin = new GameObject("SyntheticBloodSpurt");
            var texture = new Texture2D(16, 32);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), Vector2.one * .5f, 16f);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite;
            var shader = Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks");
            Assert.That(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), Is.True);
            var type = typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusSpecks", true);
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var settings = BodyVisualSettings.For(BodyVisualKind.Bleeding); settings.MotionSpeed = speed;
            IDisposable emitter = null;
            try
            {
                emitter = (IDisposable)Activator.CreateInstance(type, origin.transform, shader, settings);
                // Synthetic anchors avoid asynchronous GPU timing in a pure emission/lifecycle regression.
                type.GetField("lastMaskClock", fields).SetValue(emitter, 1000f);
                var anchors = (System.Collections.Generic.List<Vector2>)type.GetField("anchors", fields).GetValue(emitter);
                anchors.Add(new Vector2(.25f, .6f));
                var update = type.GetMethod("Update");
                var mesh = (Mesh)type.GetField("mesh", fields).GetValue(emitter);
                void Tick(float clock, bool emitting = true) => update.Invoke(emitter,
                    new object[] { clock, body, new Vector4(0, 0, 1, .5f), 1f / 16f, null, 1f, 0, 4, emitting, 1f });
                Tick(0f); Tick(.01f);
                Assert.That(mesh.vertexCount, Is.EqualTo(16), "A burst shares its budget between heads and fragments.");
                var first = mesh.vertices;
                Tick(.01f);
                Assert.That(mesh.vertices, Is.EqualTo(first), "A paused caller must freeze births and positions.");
                Tick(.1f);
                Assert.That(mesh.vertexCount, Is.EqualTo(16), "Emission is intermittent rather than a continuous shower.");
                Assert.That(mesh.vertices, Is.Not.EqualTo(first), "Both normal and slow callers must advance the spurt.");
                for (int i = 2; i <= 20; i++)
                {
                    Tick(i * .1f);
                    Assert.That(mesh.vertexCount, Is.LessThanOrEqualTo(16));
                }
                Tick(2f, false); Tick(2.7f, false);
                Assert.That(mesh.vertexCount, Is.Zero, "Stopping a source must retire every head and fragment.");
                Tick(3f, false);
                Assert.That(mesh.vertexCount, Is.Zero, "A retired emitter cannot produce another burst.");
            }
            finally
            {
                emitter?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        // Permanent regression: blood follows body coverage while its dimensions stay in source pixels.
        [TestCase(1f, 1f / 16f, 24)]
        [TestCase(4f, 1f / 128f, 24)]
        [TestCase(4f, 1f / 128f, 6)]
        public void BloodSpurts_DistributeAcrossBody_AndKeepPixelSize(float scale, float pixelSize, int budget)
        {
            var origin = new GameObject("SyntheticBodyWideBlood");
            var texture = new Texture2D(16, 32);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), Vector2.one * .5f, 16f);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite;
            origin.transform.localScale = Vector3.one * scale;
            var shader = Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks");
            var type = typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusSpecks", true);
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            IDisposable emitter = null;
            try
            {
                emitter = (IDisposable)Activator.CreateInstance(type, origin.transform, shader, BodyVisualSettings.For(BodyVisualKind.Bleeding));
                type.GetField("lastMaskClock", fields).SetValue(emitter, 1000f);
                var anchors = (System.Collections.Generic.List<Vector2>)type.GetField("anchors", fields).GetValue(emitter);
                anchors.AddRange(new[] { new Vector2(.22f,.25f), new Vector2(.8f,.5f), new Vector2(.22f,.75f),
                    new Vector2(.8f,.25f), new Vector2(.22f,.5f), new Vector2(.8f,.75f) });
                var update = type.GetMethod("Update");
                void Tick(float clock) => update.Invoke(emitter,
                    new object[] { clock, body, new Vector4(0, 0, 1, .5f), pixelSize, null, 1f, 0, budget, true, 1f });
                Tick(0f); Tick(.01f);
                var mesh = (Mesh)type.GetField("mesh", fields).GetValue(emitter);
                var points = mesh.vertices; var colors = mesh.colors;
                var centres = new System.Collections.Generic.List<Vector3>();
                for (int i = 0; i < points.Length; i += 4)
                {
                    if (colors[i].b < .5f) continue;
                    centres.Add(origin.transform.TransformPoint((points[i] + points[i+1] + points[i+2] + points[i+3]) * .25f));
                    float size = Vector3.Distance(origin.transform.TransformPoint(points[i]), origin.transform.TransformPoint(points[i+1]));
                    Assert.That(size / (Mathf.Min(pixelSize, 1f / 16f) * scale), Is.InRange(4f, 10f),
                        "Body coverage cannot enlarge individual blood jets.");
                }
                Assert.That(centres.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(centres.Min(point => point.x), Is.LessThan(body.bounds.center.x));
                Assert.That(centres.Max(point => point.x), Is.GreaterThan(body.bounds.center.x));
                Assert.That(centres.Max(point => point.y) - centres.Min(point => point.y), Is.GreaterThan(body.bounds.size.y * .25f));
                Assert.That(mesh.vertexCount, Is.LessThanOrEqualTo(budget * 4));
            }
            finally
            {
                emitter?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        // Permanent regression: body-wide blood excludes the head/feet and never falls back to excluded anchors.
        [TestCase(true)]
        [TestCase(false)]
        public void BloodSpurts_OnlyUseMiddleBody_AndSkipMissingCoverage(bool visibleMiddle)
        {
            var origin = new GameObject("SyntheticBloodBirthBand");
            var texture = new Texture2D(16, 32);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), Vector2.one * .5f, 16f);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite;
            var type = typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusSpecks", true);
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            IDisposable emitter = null;
            try
            {
                emitter = (IDisposable)Activator.CreateInstance(type, origin.transform, Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks"), BodyVisualSettings.For(BodyVisualKind.Bleeding));
                type.GetField("lastMaskClock", fields).SetValue(emitter, 1000f);
                var anchors = (System.Collections.Generic.List<Vector2>)type.GetField("anchors", fields).GetValue(emitter);
                anchors.AddRange(new[] { new Vector2(.2f,.08f), new Vector2(.8f,.19f), new Vector2(.2f,.81f), new Vector2(.8f,.95f) });
                if (visibleMiddle) anchors.AddRange(new[] { new Vector2(.35f,.35f), new Vector2(.65f,.65f) });
                var update = type.GetMethod("Update");
                void Tick(float clock) => update.Invoke(emitter,
                    new object[] { clock, body, new Vector4(0,0,1,.5f), 1f / 16f, null, 1f, 0, 12, true, 1f });
                Tick(0f); Tick(.01f);
                var selected = (System.Collections.Generic.List<Vector2>)type.GetField("bloodAnchors", fields).GetValue(emitter);
                var mesh = (Mesh)type.GetField("mesh", fields).GetValue(emitter);
                if (visibleMiddle)
                {
                    Assert.That(selected.Count, Is.GreaterThanOrEqualTo(2));
                    Assert.That(selected.All(point => point.y >= .2f && point.y <= .8f), Is.True);
                    Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                }
                else
                {
                    Assert.That(selected, Is.Empty);
                    Assert.That(mesh.vertexCount, Is.Zero, "Missing middle coverage must skip emission, not use feet/head.");
                }
            }
            finally
            {
                emitter?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        // Permanent regression: creating the effect during an expanded attack pose must not enlarge particles.
        [TestCase(BodyVisualKind.Bleeding)]
        [TestCase(BodyVisualKind.FrozenSolid)]
        [TestCase(BodyVisualKind.Frozen)]
        [TestCase(BodyVisualKind.Burning)]
        public void Specks_KeepDimensionsWhenCreatedDuringExpandedPose(BodyVisualKind kind)
        {
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var root = new GameObject("SyntheticPoseIndependentSpecks");
            root.transform.localScale = new Vector3(2f, .7f, 1f);
            var body = root.AddComponent<MeshRenderer>();
            var type = typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusSpecks", true);
            var settings = BodyVisualSettings.For(kind);
            var owned = new System.Collections.Generic.List<IDisposable>();
            try
            {
                float Sample(float height)
                {
                    body.localBounds = new Bounds(Vector3.zero, new Vector3(height, height, .01f));
                    var emitter = (IDisposable)Activator.CreateInstance(type, root.transform,
                        Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks"), settings);
                    owned.Add(emitter);
                    type.GetField("lastMaskClock", fields).SetValue(emitter, 1000f);
                    var anchors = (System.Collections.Generic.List<Vector2>)type.GetField("anchors", fields).GetValue(emitter);
                    anchors.AddRange(new[] { new Vector2(.2f,.25f), new Vector2(.8f,.5f), new Vector2(.2f,.75f) });
                    var update = type.GetMethod("Update");
                    void Tick(float clock) => update.Invoke(emitter, new object[] {
                        clock, body, new Vector4(0, 0, 1f / height, 1f / height), 1f / 32f, null, 1f, 0, 24, true, 1f });
                    Tick(0f); Tick(.01f);
                    var mesh = (Mesh)type.GetField("mesh", fields).GetValue(emitter);
                    Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(4));
                    var vertices = mesh.vertices;
                    return root.transform.TransformVector(vertices[1] - vertices[0]).magnitude;
                }
                float idle = Sample(2f);
                Assert.That(Sample(20f), Is.EqualTo(idle).Within(.00001f),
                    "Animated body bounds determine emission positions, not blood/ice/ember dimensions.");
            }
            finally { foreach (var item in owned) item.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void ParticleFlames_KeepDimensionsAndIgnorePoseCenterMotion()
        {
            const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var root = new GameObject("SyntheticPoseIndependentFlames");
            var body = root.AddComponent<MeshRenderer>();
            var template = new Material(Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodyStatusSample"));
            var type = typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusParticles", true);
            var owned = new System.Collections.Generic.List<IDisposable>();
            try
            {
                (Vector3 size, Vector3 velocity) Sample(float height, bool movePose)
                {
                    body.localBounds = new Bounds(Vector3.zero, new Vector3(height, height, .01f));
                    var emitter = (IDisposable)Activator.CreateInstance(type, root.transform, template);
                    owned.Add(emitter);
                    type.GetField("lastMaskClock", fields).SetValue(emitter, 1000f);
                    ((System.Collections.Generic.List<Vector2>)type.GetField("anchors", fields).GetValue(emitter)).Add(new Vector2(.5f,.5f));
                    var update = type.GetMethod("Update");
                    void Tick(float clock) => update.Invoke(emitter, new object[] {
                        clock, body, new Vector4(0, 0, 1f / height, 1f / height), 1f / 32f, null });
                    Tick(0f);
                    if (movePose) body.localBounds = new Bounds(Vector3.one * 10f, body.localBounds.size);
                    Tick(.01f);
                    var mesh = (Mesh)type.GetField("mesh", fields).GetValue(emitter);
                    Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(4));
                    var points = mesh.vertices;
                    var particle = ((System.Collections.IList)type.GetField("particles", fields).GetValue(emitter))[0];
                    var velocity = (Vector3)particle.GetType().GetField("velocity").GetValue(particle);
                    return (new Vector3(Vector3.Distance(points[0],points[1]), Vector3.Distance(points[0],points[2])), velocity);
                }
                var idle = Sample(2f, false);
                var attack = Sample(20f, true);
                Assert.That(Vector3.Distance(attack.size, idle.size), Is.LessThan(.00001f));
                Assert.That(Vector3.Distance(attack.velocity, idle.velocity), Is.LessThan(.00001f),
                    "A moving pose center must not fling released flames as though the actor moved.");
            }
            finally { foreach (var item in owned) item.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(template); }
        }

        [Test]
        public void LocalizedFire_KeepDimensionsWhenCreatedDuringExpandedPose()
        {
            var root = new GameObject("SyntheticPoseIndependentLocalizedFire");
            var body = root.AddComponent<MeshRenderer>();
            var template = new Material(Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodyStatusFinal"));
            var type = typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusFire", true);
            var owned = new System.Collections.Generic.List<IDisposable>();
            try
            {
                Vector3[] Sample(float height)
                {
                    body.localBounds = new Bounds(Vector3.zero, new Vector3(height, height, .01f));
                    var emitter = (IDisposable)Activator.CreateInstance(type, root.transform, template); owned.Add(emitter);
                    var systems = root.GetComponentsInChildren<ParticleSystem>();
                    var mine = systems.Skip(systems.Length - 3).ToArray();
                    var update = type.GetMethod("Update");
                    var bounds = new Vector4(0, 0, 1f / height, 1f / height);
                    update.Invoke(emitter, new object[] { 0f, body, bounds, 1f / 32f });
                    update.Invoke(emitter, new object[] { .3f, body, bounds, 1f / 32f });
                    var particles = new ParticleSystem.Particle[16];
                    return mine.Select(system => { Assert.That(system.GetParticles(particles), Is.GreaterThan(0)); return particles[0].startSize3D; }).ToArray();
                }
                var idle = Sample(2f); var attack = Sample(20f);
                for (int i = 0; i < idle.Length; i++)
                    Assert.That(Vector3.Distance(attack[i], idle[i]), Is.LessThan(.00001f), "Both localized flames and world embers keep their dimensions.");
            }
            finally { foreach (var item in owned) item.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(template); }
        }

        // Permanent regression: blood cells remain square and world-grid aligned under rotated/nonuniform owners.
        [Test]
        public void BloodCells_KeepSquareWorldPixels_ThroughOwnerFlipAndMotion()
        {
            var root=new GameObject("SyntheticBloodGrid"); var texture=new Texture2D(16,32);
            var sprite=Sprite.Create(texture,new Rect(0,0,16,32),Vector2.one*.5f,16f);
            root.transform.position=new Vector3(.137f,.119f,0);root.transform.rotation=Quaternion.Euler(0,0,37);root.transform.localScale=new Vector3(1.7f,.65f,1);
            var body=root.AddComponent<SpriteRenderer>();body.sprite=sprite;
            var type=typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusSpecks",true);
            const System.Reflection.BindingFlags fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            IDisposable emitter=null;
            try
            {
                emitter=(IDisposable)Activator.CreateInstance(type,root.transform,Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks"),BodyVisualSettings.For(BodyVisualKind.Bleeding));
                type.GetField("lastMaskClock",fields).SetValue(emitter,1000f);
                ((System.Collections.Generic.List<Vector2>)type.GetField("anchors",fields).GetValue(emitter)).Add(new Vector2(.25f,.6f));
                var update=type.GetMethod("Update");
                void Tick(float clock)=>update.Invoke(emitter,new object[]{clock,body,new Vector4(0,0,1,.5f),1f/16f,null,1f,0,24,true,1f});
                Tick(0);Tick(.01f);Tick(.1f);
                var mesh=(Mesh)type.GetField("mesh",fields).GetValue(emitter);var colors=mesh.colors;
                Vector3[] World()=>mesh.vertices.Select(root.transform.TransformPoint).ToArray();
                var points=World();Assert.That(points.Length,Is.GreaterThan(4));
                for(int i=0;i<points.Length;i+=4)
                {
                    var x=points[i+1]-points[i];var y=points[i+2]-points[i];
                    Assert.That(Mathf.Abs(x.y)+Mathf.Abs(y.x),Is.LessThan(.0001f),"Cells cannot rotate into diagonal filtered strips.");
                    float pixel=x.x/(colors[i].b>.5f?9f:3f);
                    Assert.That(y.y/(colors[i].b>.5f?7f:4f),Is.EqualTo(pixel).Within(.0001f),"CPU geometry and glyph cells share one square pixel.");
                    Assert.That(points[i].x/pixel,Is.EqualTo(Mathf.Round(points[i].x/pixel)).Within(.0001f));
                    Assert.That(points[i].y/pixel,Is.EqualTo(Mathf.Round(points[i].y/pixel)).Within(.0001f));
                }
                root.transform.position+=new Vector3(1.37f,.43f,0);root.transform.localScale=new Vector3(-1.7f,.65f,1);
                Tick(.1f);var moved=World();Assert.That(moved.Length,Is.EqualTo(points.Length));
                for(int i=0;i<points.Length;i++)Assert.That(Vector3.Distance(points[i],moved[i]),Is.LessThan(.0001f),"A paused clock and owner flip cannot drag released pixels.");
            }
            finally{emitter?.Dispose();Object.DestroyImmediate(root);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }

        // Permanent regression: fading visibility cannot stretch the blood timer into multi-second gaps.
        [Test]
        public void BloodCadence_DoesNotWaitForPresenceFadeToFinish()
        {
            var root=new GameObject("SyntheticBloodPresence");var texture=new Texture2D(16,32);
            var sprite=Sprite.Create(texture,new Rect(0,0,16,32),Vector2.one*.5f,16f);var body=root.AddComponent<SpriteRenderer>();body.sprite=sprite;
            var type=typeof(BodyVisual).Assembly.GetType("ZeroEngine.Render.BodyStatusSpecks",true);
            const System.Reflection.BindingFlags fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            IDisposable emitter=null;
            try
            {
                var settings=BodyVisualSettings.For(BodyVisualKind.Bleeding);settings.MotionSpeed=1;
                emitter=(IDisposable)Activator.CreateInstance(type,root.transform,Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks"),settings);
                type.GetField("lastMaskClock",fields).SetValue(emitter,1000f);
                ((System.Collections.Generic.List<Vector2>)type.GetField("anchors",fields).GetValue(emitter)).Add(new Vector2(.25f,.6f));
                var update=type.GetMethod("Update");void Tick(float clock)=>update.Invoke(emitter,new object[]{clock,body,new Vector4(0,0,1,.5f),1f/16f,null,1f,0,24,true,.1f});
                Tick(0);Tick(.1f);int first=(int)type.GetField("serial",fields).GetValue(emitter);
                Assert.That(first,Is.GreaterThan(0));Tick(.5f);
                Assert.That((int)type.GetField("serial",fields).GetValue(emitter),Is.GreaterThan(first),"Opacity/presence belongs to rendering, not the repeat interval.");
            }
            finally{emitter?.Dispose();Object.DestroyImmediate(root);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }

        [Test]
        public void InkPalette_PreservesShaderSpaceCoefficients_AndBloodCoverage()
        {
            var root = new GameObject("Synthetic palette coefficients");
            var texture = new Texture2D(32, 64, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(Color.white, 2048).ToArray()); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 64), Vector2.one * .5f, 16);
            var body = root.AddComponent<SpriteRenderer>(); body.sprite = sprite;
            var source = root.AddComponent<RenderPoseSource>(); source.Configure(body);
            var resources = ZeroEngine.Render.Urp.BodyVisualResourcesLoader.Load();
            var settings = BodyVisualSettings.For(BodyVisualKind.Bleeding); settings.SmallParticles = false;
            var target = new RenderTexture(96, 192, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create();
            var reader = new Texture2D(96, 192, TextureFormat.RGBA32, false, true);
            var commands = new UnityEngine.Rendering.CommandBuffer(); var previous = RenderTexture.active;
            BodyVisual visual = null;
            try
            {
                visual = new BodyVisual(root.transform, source, resources.Surface, settings, resources);
                Color32[] Capture(VisualPalette palette)
                {
                    visual.SetPalette(palette); Assert.That(visual.Update(1.3f, body), Is.True);
                    commands.Clear(); commands.SetRenderTarget(target); commands.ClearRenderTarget(false, true, Color.clear);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-1.5f, 1.5f, -2.6f, 2.6f, -10, 10));
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>().Where(x => x != body && x.enabled).OrderBy(x => x.sortingOrder))
                        commands.DrawRenderer(renderer, renderer.sharedMaterial, 0, 0);
                    Graphics.ExecuteCommandBuffer(commands); RenderTexture.active = target;
                    reader.ReadPixels(new Rect(0, 0, 96, 192), 0, 0); reader.Apply(); return reader.GetPixels32();
                }
                var red = Capture(default); var ink = Capture(new VisualPalette(true, new Color(.2f, .22f, .25f, 1)));
                Assert.That(red.Count(x => x.a > 0), Is.GreaterThan(0));
                for (int i = 0; i < red.Length; i++)
                {
                    Assert.That(ink[i].a, Is.EqualTo(red[i].a));
                    float value = Mathf.Max(red[i].r, Mathf.Max(red[i].g, red[i].b));
                    Assert.That(ink[i].r, Is.EqualTo(value * .2f).Within(1f));
                    Assert.That(ink[i].g, Is.EqualTo(value * .22f).Within(1f));
                    Assert.That(ink[i].b, Is.EqualTo(value * .25f).Within(1f));
                }
            }
            finally
            {
                RenderTexture.active = previous; visual?.Dispose(); commands.Release(); target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(reader); Object.DestroyImmediate(root);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        // Permanent GPU contract: hard cells and discrete poses, not a continuously interpolated mask.
        [Test]
        public void BloodShader_UsesUniformHardCells_AndDiscretePoseFrames()
        {
            var shader=Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodySpecks");Assert.That(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),Is.True);
            var material=new Material(shader);material.SetFloat("_Snow",0);material.SetFloat("_Blood",1);material.SetColor("_Tint",new Color(.8f,.05f,.03f,1));
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,2,3,1};
            var target=new RenderTexture(72,56,0,RenderTextureFormat.ARGB32);target.Create();
            var reader=new Texture2D(72,56,TextureFormat.RGBA32,false);var commands=new UnityEngine.Rendering.CommandBuffer();var previous=RenderTexture.active;
            try
            {
                Color32[] Render(float age)
                {
                    mesh.colors=Enumerable.Repeat(new Color(.43f,age,1,1),4).ToArray();commands.Clear();commands.SetRenderTarget(target);commands.ClearRenderTarget(false,true,Color.clear);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(-.5f,.5f,-.5f,.5f,-1,1));commands.DrawMesh(mesh,Matrix4x4.identity,material);
                    Graphics.ExecuteCommandBuffer(commands);RenderTexture.active=target;reader.ReadPixels(new Rect(0,0,72,56),0,0);reader.Apply();return reader.GetPixels32();
                }
                var a=Render(.11f);var b=Render(.15f);Assert.That(b,Is.EqualTo(a),"A pixel pose stays unchanged inside its animation step.");
                Assert.That(a.Count(c=>c.a>0),Is.InRange(5*64,35*64),"Inspect visible glyph cells and transparent space, not a solid fallback shader.");
                for(int cy=0;cy<7;cy++)for(int cx=0;cx<9;cx++)
                {
                    var expected=a[(cy*8)*72+cx*8];
                    for(int y=0;y<8;y++)for(int x=0;x<8;x++)Assert.That(a[(cy*8+y)*72+cx*8+x],Is.EqualTo(expected),"No gradients or slanted subpixels inside a glyph cell.");
                }
            }
            finally{RenderTexture.active=previous;commands.Release();target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(reader);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
        }

        // Permanent regression: poison icons cross the torso without pose-mask flicker; the caller owns animation time.
        [Test]
        public void PoisonSkulls_CrossBodyWithoutPoseFlicker_AndFollowCallerClock()
        {
            var shader = Resources.Load<Shader>(ZeroEngine.Render.Urp.BodyVisualResourcesLoader.SurfacePath);
            Assert.That(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), Is.True);
            var material = new Material(shader);
            var fullMask = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var posedMask = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var fullPixels = new Color[64 * 64];
            var posedPixels = new Color[64 * 64];
            for (int y = 12; y < 52; y++)
                for (int x = 12; x < 52; x++)
                {
                    fullPixels[y * 64 + x] = Color.white;
                    if (x < 20 || x >= 44) posedPixels[y * 64 + x] = Color.white;
                }
            fullMask.SetPixels(fullPixels); fullMask.Apply();
            posedMask.SetPixels(posedPixels); posedMask.Apply();
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-.7f,-.7f,0), new Vector3(.7f,-.7f,0), new Vector3(-.7f,.7f,0), new Vector3(.7f,.7f,0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
            mesh.triangles = new[] { 0,2,1,2,3,1 };
            var target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32); target.Create();
            var reader = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var commands = new UnityEngine.Rendering.CommandBuffer();
            var previous = RenderTexture.active;
            try
            {
                material.SetFloat("_Mode", (int)BodyVisualKind.Poisoned);
                material.SetFloat("_BodyLayer", 1f); material.SetFloat("_BodyPresence", 1f);
                material.SetFloat("_Opacity", 1f); material.SetFloat("_Intensity", 1f);
                material.SetFloat("_BodyPixelSize", 1f / 64f);
                material.SetColor("_Tint", new Color(.72f, .24f, .95f, .78f));
                material.SetVector("_BodyBounds", new Vector4(0, 0, 1, 1));
                material.SetVector("_Flip", Vector4.one); material.SetColor("_RendererColor", Color.white);
                material.SetMatrix("_BodyWorldToLocal", Matrix4x4.identity);
                Color32[] Draw(Texture mask, float elapsed, float speed = 1f)
                {
                    material.SetTexture("_BodyMask", mask);
                    material.SetFloat("_BodyClock", elapsed); material.SetFloat("_MotionSpeed", speed);
                    commands.Clear(); commands.SetRenderTarget(target); commands.ClearRenderTarget(false, true, Color.clear);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity,
                        GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-.7f, .7f, -.7f, .7f, -1f, 1f), true));
                    commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                    Graphics.ExecuteCommandBuffer(commands);
                    RenderTexture.active = target;
                    reader.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); reader.Apply();
                    return reader.GetPixels32();
                }
                var before = Draw(fullMask, .8f);
                Assert.That(before.Where((pixel, index) => index % 128 > 46 && index % 128 < 82 &&
                    index / 128 > 42 && index / 128 < 86).Count(pixel => pixel.a > 8), Is.GreaterThan(0),
                    "Poison skulls must be allowed in the body area, rather than only outside its silhouette.");
                Assert.That(Draw(posedMask, .8f), Is.EqualTo(before), "Pose alpha changes must not switch a skull on or off.");
                Assert.That(Draw(fullMask, .8f), Is.EqualTo(before), "A frozen owner clock must freeze the complete image.");
                Assert.That(Draw(fullMask, 4f, .2f), Is.EqualTo(before), "Slow presentation uses the same caller-clock phase.");
                Assert.That(Draw(fullMask, 1.2f), Is.Not.EqualTo(before), "Skulls must actually float as the caller clock advances.");
                material.SetFloat("_BodyPresence", 0f);
                Assert.That(Draw(fullMask, 1.2f).All(pixel => pixel.a == 0), Is.True, "Retirement must finish without residue.");
            }
            finally
            {
                RenderTexture.active = previous;
                commands.Dispose(); target.Release();
                Object.DestroyImmediate(material); Object.DestroyImmediate(fullMask); Object.DestroyImmediate(posedMask);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(reader); Object.DestroyImmediate(target);
            }
        }

        // Regression: rotated actors must not collapse the local silhouette, and Spine submeshes use their own atlas region.
        [Test]
        public void RotatedMesh_UsesLocalExtentsAndSubmeshAtlasMapping_WithoutChangingSource()
        {
            var origin = new GameObject("RotatedBodyContract");
            origin.transform.rotation = Quaternion.Euler(0, 0, 45);
            origin.transform.localScale = new Vector3(2f, 0.5f, 1f);
            var texture = new Texture2D(32, 16);
            texture.SetPixels(Enumerable.Repeat(Color.white, 512).ToArray()); texture.Apply();
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0) };
            mesh.uv = new[] { new Vector2(0.5f,0), new Vector2(1,0), new Vector2(0.5f,1), new Vector2(1,1) };
            mesh.triangles = new[] { 0,2,1,2,3,1 }; mesh.RecalculateBounds();
            origin.AddComponent<MeshFilter>().sharedMesh = mesh;
            var body = origin.AddComponent<MeshRenderer>();
            var authored = new Material(Shader.Find("Sprites/Default")); authored.mainTexture = texture; body.sharedMaterial = authored;
            var source = origin.AddComponent<RenderPoseSource>(); source.Configure(body);
            BodyVisual visual = null;
            try
            {
                visual = CreateVisual(origin.transform, source, Resources.Load<Shader>(ZeroEngine.Render.Urp.BodyVisualResourcesLoader.SurfacePath),
                    BodyVisualSettings.For(BodyVisualKind.Poisoned));
                visual.SetComposition(0.5f, 8, 12);
                Assert.That(visual.Update(1f, body), Is.True);
                var surface = origin.GetComponentsInChildren<MeshRenderer>().Single(r => r != body && r.sharedMaterial.name == "BodyVisualRuntime");
                var block = new MaterialPropertyBlock(); surface.GetPropertyBlock(block, 0);
                Assert.That(block.GetVector("_BodySourceUVRect"), Is.EqualTo(new Vector4(0.5f,0,0.5f,1)));
                var bounds = surface.sharedMaterial.GetVector("_BodyBounds");
                Assert.That(bounds.z, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(bounds.w, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(surface.sortingOrder, Is.EqualTo(body.sortingOrder + 9));
                var atmosphere = origin.GetComponentsInChildren<MeshRenderer>().Single(r => r.sharedMaterial.name == "BodyVisualAtmosphere");
                Assert.That(atmosphere.sharedMaterial.GetFloat("_Opacity"), Is.InRange(0.5f, 1f),
                    "Edge accents remain readable when the body surface is reduced.");
                Assert.That(body.sharedMaterial, Is.SameAs(authored));
                Assert.That(origin.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            }
            finally
            {
                visual?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(authored); Object.DestroyImmediate(texture);
            }
        }

        // Core contract: optional looks preserve source/atlas ownership and share the existing caller clock.
        [TestCase(BodyVisualKind.ShadowPulse)]
        [TestCase(BodyVisualKind.Glow)]
        [TestCase(BodyVisualKind.Frozen)]
        [TestCase(BodyVisualKind.Burning)]
        [TestCase(BodyVisualKind.Poisoned)]
        [TestCase(BodyVisualKind.Electrified)]
        [TestCase(BodyVisualKind.Bleeding)]
        [TestCase(BodyVisualKind.Weakened)]
        [TestCase(BodyVisualKind.Resonating)]
        [TestCase(BodyVisualKind.FrozenSolid)]
        public void SurfaceTemplate_UsesPrivateFrameMappingAndCallerClock_WithoutParticleOrSourceMutation(BodyVisualKind kind)
        {
            var origin = new GameObject("SurfaceFamilyContract");
            var texture = new Texture2D(32, 16);
            texture.SetPixels(Enumerable.Repeat(Color.white, 32 * 16).ToArray()); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(16, 0, 16, 16), Vector2.one * 0.5f, 16f, 0, SpriteMeshType.FullRect);
            var shader = Resources.Load<Shader>(ZeroEngine.Render.Urp.BodyVisualResourcesLoader.SurfacePath);
            Assert.That(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), Is.True);
            var template = new Material(shader); template.SetFloat("_BodyClock", 99f);
            template.SetTexture("_FrostMask", texture);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite; body.flipX = true;
            var authored = new MaterialPropertyBlock(); authored.SetFloat("_SourceSentinel", 0.37f); body.SetPropertyBlock(authored);
            var source = origin.AddComponent<RenderPoseSource>(); source.Configure(body);
            var settings = BodyVisualSettings.For(kind); settings.AppearanceTemplate = template;
            BodyVisual visual = null;
            try
            {
                visual = CreateVisual(origin.transform, source, Resources.Load<Shader>(ZeroEngine.Render.Urp.BodyVisualResourcesLoader.BodyPath), settings);
                settings.Color = Color.clear; settings.AppearanceTemplate = null;
                Assert.That(visual.Update(0.08f, body), Is.True);
                var surface = origin.GetComponentsInChildren<SpriteRenderer>().Single(value => value != body && value.enabled);
                var block = new MaterialPropertyBlock(); surface.GetPropertyBlock(block);
                Assert.That(block.GetVector("_BodySourceUVRect"), Is.EqualTo(new Vector4(0.5f, 0, 0.5f, 1)));
                Assert.That(surface.flipX, Is.True);
                Assert.That(surface.sharedMaterial.shader, Is.SameAs(shader));
                Assert.That(surface.sharedMaterial.GetColor("_Tint").a, Is.GreaterThan(0));
                Assert.That(surface.sharedMaterial.GetTexture("_FrostMask"), Is.SameAs(texture));
                var owned = origin.GetComponentsInChildren<Renderer>().Where(value => value != body && value.enabled)
                    .Select(value => value.sharedMaterial).Distinct().ToArray();
                Assert.That(owned.Length, Is.EqualTo((int)kind >= 2 ? 2 : 1));
                foreach (float elapsed in new[] { 0.08f, 0.10f, 0.20f })
                {
                    Assert.That(visual.Update(elapsed, body), Is.True);
                    Assert.That(owned.All(value => Mathf.Abs(value.GetFloat("_BodyClock") - elapsed) < 0.0001f), Is.True);
                }
                Assert.That(origin.GetComponentsInChildren<ParticleSystem>(), Is.Empty);
                block.Clear(); body.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_SourceSentinel"), Is.EqualTo(0.37f));
                Assert.That(block.GetVector("_BodySourceUVRect"), Is.EqualTo(Vector4.zero));
                Assert.That(template.GetFloat("_BodyClock"), Is.EqualTo(99f));
                Assert.That(body.transform.localScale, Is.EqualTo(Vector3.one));
                visual.Dispose();
                Assert.That(owned.All(value => !value), Is.True);
                Assert.That(template && texture && sprite, Is.True);
            }
            finally
            {
                visual?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(template);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void LocalizedFire_UsesCallerClock_LayersAndBorrowedAssets()
        {
            var origin = new GameObject("LocalizedFireContract");
            var texture = new Texture2D(16, 32);
            var pixels = Enumerable.Repeat(Color.white, 16 * 32).ToArray();
            texture.SetPixels(pixels); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), Vector2.one * 0.5f, 16f);
            var shader = Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodyStatusFinal");
            var template = new Material(shader); template.SetTexture("_FlameAtlas", texture);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite; body.sortingOrder = 20;
            var source = origin.AddComponent<RenderPoseSource>(); source.Configure(body);
            BodyVisual visual = null;
            try
            {
                visual = CreateVisual(origin.transform, source, shader, BodyVisualSettings.For(BodyVisualKind.Burning), template);
                Assert.That(visual.Update(0f, body), Is.True);
                Assert.That(visual.Update(0.1f, body), Is.True);
                Assert.That(visual.Update(0.4f, body), Is.True);
                var systems = origin.GetComponentsInChildren<ParticleSystem>();
                Assert.That(systems.Length, Is.EqualTo(3));
                var back = systems.Single(value => value.name == "BodyFireBack");
                var front = systems.Single(value => value.name == "BodyFireFront");
                var embers = systems.Single(value => value.name == "BodyFireEmbers");
                Assert.That(back.GetComponent<Renderer>().sortingOrder, Is.LessThan(body.sortingOrder));
                Assert.That(front.GetComponent<Renderer>().sortingOrder, Is.GreaterThan(body.sortingOrder));
                Assert.That(systems.All(value => !value.isPlaying), Is.True, "Native systems cannot free-run between owner clock updates.");
                var particles = new ParticleSystem.Particle[16];
                Assert.That(back.GetParticles(particles), Is.GreaterThan(0));
                float lifetime = particles[0].remainingLifetime;
                Assert.That(embers.GetParticles(particles), Is.GreaterThan(0));
                var worldPosition = particles[0].position;
                origin.transform.position = new Vector3(3, 1, 0);
                visual.Update(0.4f, body);
                back.GetParticles(particles);
                Assert.That(particles[0].remainingLifetime, Is.EqualTo(lifetime).Within(0.0001f));
                embers.GetParticles(particles);
                Assert.That(Vector3.Distance(particles[0].position, worldPosition), Is.LessThan(0.0001f));
                visual.Update(0.42f, body); back.GetParticles(particles);
                float slowAge = lifetime - particles[0].remainingLifetime;
                visual.Update(0.52f, body); back.GetParticles(particles);
                float normalAge = lifetime - slowAge - particles[0].remainingLifetime;
                Assert.That(slowAge / normalAge, Is.EqualTo(0.2f).Within(0.005f));
                var privateMaterials = systems.Select(value => value.GetComponent<Renderer>().sharedMaterial).ToArray();
                visual.Dispose();
                Assert.That(systems.All(value => !value), Is.True);
                Assert.That(privateMaterials.All(value => !value), Is.True);
                Assert.That(texture && sprite && template, Is.True);
                Assert.That(template.GetTexture("_FlameAtlas"), Is.SameAs(texture));
            }
            finally
            {
                visual?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(template);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void FrostTexture_RemovesSpriteAtlasOffset_FromPrivateOverlayOnly()
        {
            var origin = new GameObject("FrostAtlasContract");
            var texture = new Texture2D(32, 16);
            var sprite = Sprite.Create(texture, new Rect(16, 0, 16, 16), Vector2.one * 0.5f, 16f, 0, SpriteMeshType.FullRect);
            var shader = Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodyStatusFinal");
            var template = new Material(shader);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite;
            var source = origin.AddComponent<RenderPoseSource>(); source.Configure(body);
            BodyVisual visual = null;
            try
            {
                visual = CreateVisual(origin.transform, source, shader, BodyVisualSettings.For(BodyVisualKind.Frozen), template);
                var authored = new MaterialPropertyBlock();
                authored.SetFloat("_AuthoredBodySentinel", 0.31f);
                body.SetPropertyBlock(authored);
                visual.Update(1f, body);
                var surface = origin.GetComponentsInChildren<SpriteRenderer>().Single(value => value != body && value.enabled);
                var block = new MaterialPropertyBlock(); surface.GetPropertyBlock(block);
                Assert.That(block.GetVector("_BodySourceUVRect"), Is.EqualTo(new Vector4(0.5f, 0, 0.5f, 1)));
                block.Clear(); body.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_AuthoredBodySentinel"), Is.EqualTo(0.31f));
                Assert.That(block.GetVector("_BodySourceUVRect"), Is.EqualTo(Vector4.zero), "The private frost mapping must not be written to the authored body.");
                Assert.That(template.GetVector("_BodySourceUVRect"), Is.EqualTo(new Vector4(0, 0, 1, 1)));
            }
            finally
            {
                visual?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(template);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }


        [TestCase(16)]
        [TestCase(64)]
        public void AppearancePadding_PreservesSourcePixelMargin_ForSmallAndLargeArtwork(int artworkPixels)
        {
            // Lasting contract: configured ice thickness reserves source-pixel space independently of body size.
            var origin = new GameObject("AppearancePaddingContract");
            origin.transform.localScale = new Vector3(2, 2, 1);
            var texture = new Texture2D(artworkPixels, artworkPixels);
            var sprite = Sprite.Create(texture, new Rect(0, 0, artworkPixels, artworkPixels), Vector2.one * 0.5f, 16f);
            var shader = Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodyStatusSample");
            var template = new Material(shader);
            template.SetVector("_EffectPadding", new Vector4(5, 8, 3, 0));
            BodyVisual visual = null;
            try
            {
                var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite;
                var source = origin.AddComponent<RenderPoseSource>(); source.Configure(body);
                visual = CreateVisual(origin.transform, source, shader, BodyVisualSettings.For(BodyVisualKind.Frozen), template);
                Assert.That(visual.Update(0.4f, body), Is.True);
                var attached = origin.GetComponentsInChildren<MeshRenderer>().Single();
                Assert.That(attached.bounds.max.x - body.bounds.max.x, Is.EqualTo(5f * 2f / 16f).Within(0.0001f));
                Assert.That(attached.bounds.max.y - body.bounds.max.y, Is.EqualTo(8f * 2f / 16f).Within(0.0001f));
                Assert.That(body.bounds.min.y - attached.bounds.min.y, Is.EqualTo(3f * 2f / 16f).Within(0.0001f));
                Assert.That(attached.sharedMaterial.GetFloat("_BodyPixelSize"), Is.EqualTo(1f / 16f).Within(0.00001f));
                var surface = origin.GetComponentsInChildren<SpriteRenderer>().Single(value => value != body && value.enabled);
                var block = new MaterialPropertyBlock(); surface.GetPropertyBlock(block);
                Assert.That(block.GetVector("_BodySourceTexelSize").z, Is.EqualTo(artworkPixels), "Crystal UVs must use the actual source texture, not the template's default white texture.");
                Assert.That(template.GetVector("_EffectPadding"), Is.EqualTo(new Vector4(5, 8, 3, 0)));
            }
            finally
            {
                visual?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(template);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        [Category("ZeroEngine.Render.SlowEditMode")]
        [Category("ZeroEngine.Render.AsyncWait")]
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator IndependentFlames_UseOccupiedAlpha_FreezeWithCallerClock_AndReleasedVerticesStayInWorld()
        {
            // Permanent clock/ownership contract, using synthetic top-half alpha rather than designer data.
            Assert.That(SystemInfo.supportsAsyncGPUReadback, Is.True, "This Editor contract requires asynchronous GPU readback.");
            var origin = new GameObject("IndependentAtmosphereContract");
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color[256];
            for (int y = 8; y < 14; y++) for (int x = 3; x < 13; x++) pixels[y * 16 + x] = Color.white;
            texture.SetPixels(pixels); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 16f, 0, SpriteMeshType.FullRect);
            var shader = Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodyStatusSample");
            var template = new Material(shader); template.SetFloat("_UseParticles", 1); template.SetFloat("_Look", 1);
            var body = origin.AddComponent<SpriteRenderer>(); body.sprite = sprite;
            var source = origin.AddComponent<RenderPoseSource>(); source.Configure(body);
            BodyVisual visual = null;
            try
            {
                visual = CreateVisual(origin.transform, source, shader, BodyVisualSettings.For(BodyVisualKind.Burning), template);
                MeshRenderer particleRenderer = null;
                float clock = 0;
                for (int frame = 0; frame < 40; frame++)
                {
                    clock = frame * 0.1f;
                    Assert.That(visual.Update(clock, body), Is.True);
                    particleRenderer = origin.GetComponentsInChildren<MeshRenderer>().Single(value => value.name == "BodyStatusParticles");
                    yield return null;
                    if (particleRenderer.GetComponent<MeshFilter>().sharedMesh.vertexCount >= 24 && frame >= 10) break;
                }
                var mesh = particleRenderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(24), "Occupied body alpha must produce actual particle geometry.");
                var life = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(1, life);
                var local = mesh.vertices;
                bool checkedRoot = false;
                for (int i = 0; i < life.Count; i += 4)
                    if (life[i].z < 0.5f && life[i].x < 0.2f)
                    {
                        Assert.That(local[i].y, Is.GreaterThanOrEqualTo(-0.0001f), "No roots may spawn in the empty lower half.");
                        checkedRoot = true;
                    }
                Assert.That(checkedRoot, Is.True);
                var before = local.Select(origin.transform.TransformPoint).ToArray();
                origin.transform.position += new Vector3(3, 1, 0);
                Assert.That(visual.Update(clock, body), Is.True); // Same elapsed value is an owner pause.
                var after = mesh.vertices.Select(origin.transform.TransformPoint).ToArray();
                var pausedLife = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(1, pausedLife);
                Assert.That(pausedLife, Is.EqualTo(life), "A paused owner cannot emit, age, or fade particles.");
                bool checkedReleased = false;
                for (int i = 0; i < life.Count; i += 4)
                    if (life[i].x > 0.3f)
                    {
                        Assert.That(Vector3.Distance(before[i], after[i]), Is.LessThan(0.0001f), "Released flames stay in the world when the actor moves.");
                        checkedReleased = true;
                    }
                Assert.That(checkedReleased, Is.True);
                var identities = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(2, identities);
                int probe = Enumerable.Range(0, life.Count).First(i => i % 4 == 0 && life[i].x > 0.3f && life[i].x < 0.55f);
                float identity = identities[probe].w;
                (float age, float y) SampleReleasedParticle()
                {
                    var currentLife = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(1, currentLife);
                    var currentIdentities = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(2, currentIdentities);
                    int index = currentIdentities.FindIndex(value => value.w == identity);
                    Assert.That(index, Is.GreaterThanOrEqualTo(0), "The selected young released particle must survive both clock steps.");
                    return (currentLife[index].x, origin.transform.TransformPoint(mesh.vertices[index]).y);
                }
                var baseSample = SampleReleasedParticle();
                Assert.That(visual.Update(clock + 0.02f, body), Is.True);
                var slowSample = SampleReleasedParticle();
                Assert.That(visual.Update(clock + 0.12f, body), Is.True);
                var normalSample = SampleReleasedParticle();
                Assert.That((slowSample.age - baseSample.age) / (normalSample.age - slowSample.age), Is.EqualTo(0.2f).Within(0.005f), "Owner slow motion ages particles proportionally.");
                Assert.That((slowSample.y - baseSample.y) / (normalSample.y - slowSample.y), Is.EqualTo(0.2f).Within(0.005f), "Owner slow motion moves released particles proportionally.");
                var ownedMaterial = particleRenderer.sharedMaterial;
                visual.Dispose();
                Assert.That(!mesh && !ownedMaterial, Is.True);
                Assert.That(template && texture && sprite, Is.True);
            }
            finally
            {
                visual?.Dispose(); Object.DestroyImmediate(origin); Object.DestroyImmediate(template);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        [TestCase(BodyVisualKind.Frozen)]
        [TestCase(BodyVisualKind.Burning)]
        public void AppearanceTemplate_IsClonedForBothLayers_AndBorrowedTextureSurvivesDisposal(BodyVisualKind kind)
        {
            var origin = new GameObject("SyntheticAppearanceTemplate");
            var texture = new Texture2D(16, 16);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 16f);
            var shader = Resources.Load<Shader>("ZeroEngine/BodyVisuals/EntityBodyStatusSample");
            Assert.That(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), Is.True);
            var template = new Material(shader);
            template.SetTexture("_FlameAtlas", texture);
            template.SetFloat("_Mode", 17f);
            BodyVisual visual = null;
            try
            {
                var body = origin.AddComponent<SpriteRenderer>();
                body.sprite = sprite;
                var source = origin.AddComponent<RenderPoseSource>();
                source.Configure(body);
                visual = CreateVisual(origin.transform, source, shader, BodyVisualSettings.For(kind), template);
                Assert.That(visual.Update(0.4f, body), Is.True);
                var materials = origin.GetComponentsInChildren<Renderer>()
                    .Where(value => value != body && value.enabled).Select(value => value.sharedMaterial).ToArray();
                Assert.That(materials.Length, Is.EqualTo(2));
                foreach (var owned in materials)
                {
                    Assert.That(owned, Is.Not.SameAs(template));
                    Assert.That(owned.GetTexture("_FlameAtlas"), Is.SameAs(texture));
                    Assert.That(owned.GetFloat("_Mode"), Is.EqualTo((float)kind));
                    Assert.That(owned.GetFloat("_BodyClock"), Is.EqualTo(0.4f));
                }
                Assert.That(template.GetFloat("_Mode"), Is.EqualTo(17f));
                visual.Dispose();
                Assert.That(materials.All(value => !value), Is.True);
                Assert.That(template && texture, Is.True);
            }
            finally
            {
                visual?.Dispose();
                Object.DestroyImmediate(origin);
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        // Permanent contracts: caller-clocked pulse/rest, borrowed source isolation, geometry and cleanup.
        [Test]
        public void Pulse_ExpandsQuickly_Fades_ThenWaitsCompletelyHidden()
        {
            var settings = new BodyVisualSettings { PulseDuration = 0.4f, PulseInterval = 2f, Expansion = 0.5f };
            var early = settings.EvaluatePulse(0.08f);
            Assert.That(early.Visible, Is.True);
            Assert.That(early.Scale, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(settings.EvaluatePulse(0.35f).Opacity, Is.LessThan(early.Opacity));
            Assert.That(settings.EvaluatePulse(0.4f).Visible, Is.False);
            Assert.That(settings.EvaluatePulse(1.99f).Visible, Is.False);
            Assert.That(settings.EvaluatePulse(2f).Visible, Is.True);
            settings.Repeat = false;
            Assert.That(settings.EvaluatePulse(2f).Visible, Is.False);
        }

        [Test]
        public void CallerClock_FreezeAndSlowAdvance_DoNotDependOnWallTime()
        {
            var settings = new BodyVisualSettings { PulseDuration = 0.5f, PulseInterval = 2f };
            var normal = settings.EvaluatePulse(0.4f);
            var frozen = settings.EvaluatePulse(0.4f);
            Assert.That(frozen.Scale, Is.EqualTo(normal.Scale));
            Assert.That(frozen.Opacity, Is.EqualTo(normal.Opacity));
            // 0.5 wall seconds at owner scale 0.2 advances only 0.1 owner seconds.
            Assert.That(settings.EvaluatePulse(0.1f).Opacity, Is.GreaterThan(normal.Opacity));
        }

        [Test]
        public void InvalidParameters_AreRejected_AndConfigurationCopyIsIndependent()
        {
            var settings = new BodyVisualSettings { PulseDuration = 1f, PulseInterval = 1f };
            Assert.That(settings.IsValid, Is.False, "A pulse must have a silent interval.");
            settings.PulseInterval = 2f;
            settings.Expansion = float.NaN;
            Assert.That(settings.IsValid, Is.False);
            settings.Expansion = 0.3f;
            var copy = settings.Copy();
            copy.Expansion = 0.8f;
            Assert.That(settings.Expansion, Is.EqualTo(0.3f));
            Assert.That(settings.EvaluatePulse(float.PositiveInfinity).Visible, Is.False);
        }

        [TestCase(BodyVisualKind.ShadowPulse, false)]
        [TestCase(BodyVisualKind.Glow, false)]
        [TestCase(BodyVisualKind.Frozen, true)]
        [TestCase(BodyVisualKind.Burning, true)]
        public void View_HandlesSpriteAndMesh_WithoutChangingSource_AndReleasesOwnedObjects(BodyVisualKind kind, bool meshSource)
        {
            var origin = new GameObject("SyntheticEntity");
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * 0.5f, 8f);
            var mesh = new Mesh();
            var shader = Resources.Load<Shader>(ZeroEngine.Render.Urp.BodyVisualResourcesLoader.BodyPath);
            Assert.That(shader && shader.isSupported, Is.True);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            var authoredMaterial = new Material(shader);
            BodyVisual visual = null;
            try
            {
                var bodyObject = new GameObject("Body");
                bodyObject.transform.SetParent(origin.transform, false);
                bodyObject.transform.localPosition = new Vector3(0.1f, 0.7f, 0f);
                Renderer body;
                if (meshSource)
                {
                    mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0, 0.5f, 0f) };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one };
                    mesh.colors = Enumerable.Repeat(Color.white, 3).ToArray();
                    mesh.triangles = new[] { 0, 1, 2 };
                    mesh.RecalculateBounds();
                    bodyObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    body = bodyObject.AddComponent<MeshRenderer>();
                }
                else
                {
                    var spriteRenderer = bodyObject.AddComponent<SpriteRenderer>();
                    spriteRenderer.sprite = sprite;
                    spriteRenderer.flipX = true;
                    body = spriteRenderer;
                }
                body.sharedMaterial = authoredMaterial;
                authoredMaterial.SetTexture("_MainTex", texture);
                var block = new MaterialPropertyBlock();
                block.SetFloat("_UserProperty", 23f);
                body.SetPropertyBlock(block);
                var source = origin.AddComponent<RenderPoseSource>();
                source.Configure(body);
                var settings = BodyVisualSettings.For(kind);
                visual = CreateVisual(origin.transform, source, shader, settings);
                Assert.That(visual.Update(0.05f, body), Is.True);
                var copies = origin.GetComponentsInChildren<Renderer>().Where(value => value != body && value.enabled).ToArray();
                bool attached = kind == BodyVisualKind.Frozen || kind == BodyVisualKind.Burning;
                Assert.That(copies.Length, Is.EqualTo(attached ? 2 : 1));
                var copy = copies.Single(value => value.sharedMaterial.GetFloat("_BodyLayer") < 0.5f);
                Assert.That(copy.sharedMaterial, Is.Not.SameAs(authoredMaterial));
                Assert.That(copy.sortingOrder, Is.EqualTo(body.sortingOrder + (kind == BodyVisualKind.ShadowPulse ? -1 : 1)));
                if (meshSource) Assert.That(copy.GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs(mesh));
                else Assert.That(((SpriteRenderer)copy).flipX, Is.True);
                body.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_UserProperty"), Is.EqualTo(23f));
                Assert.That(body.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(body.sharedMaterial, Is.SameAs(authoredMaterial));
                Assert.That(origin.GetComponentsInChildren<Collider2D>(), Is.Empty);
                Assert.That(origin.GetComponentsInChildren<Animator>(), Is.Empty);
                visual.Dispose();
                visual.Dispose();
                Assert.That(origin.transform.childCount, Is.EqualTo(1));
                Assert.That(texture && sprite && mesh && authoredMaterial, Is.True);
            }
            finally
            {
                visual?.Dispose();
                Object.DestroyImmediate(origin);
                Object.DestroyImmediate(authoredMaterial);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PixelGrid_MatchesSourceTexels_AndKeepsItsScaleWhenPoseBoundsChange(bool meshSource)
        {
            // Permanent rendering contract: Sprite and mesh/Spine use their artwork pixel scale, not animated bounds.
            var origin = new GameObject("PixelGridContract");
            var texture = new Texture2D(16, 16);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 16f);
            var mesh = new Mesh();
            var shader = Resources.Load<Shader>(ZeroEngine.Render.Urp.BodyVisualResourcesLoader.BodyPath);
            var authored = new Material(shader);
            authored.mainTexture = texture;
            BodyVisual visual = null;
            try
            {
                Renderer body;
                if (meshSource)
                {
                    mesh.vertices = new[] { new Vector3(-0.5f,-0.5f,0), new Vector3(0.5f,-0.5f,0), new Vector3(-0.5f,0.5f,0), new Vector3(0.5f,0.5f,0) };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                    mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
                    mesh.triangles = new[] {0,2,1,2,3,1};
                    mesh.RecalculateBounds();
                    origin.AddComponent<MeshFilter>().sharedMesh = mesh;
                    body = origin.AddComponent<MeshRenderer>();
                }
                else
                {
                    var renderer = origin.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprite;
                    body = renderer;
                }
                body.sharedMaterial = authored;
                var source = origin.AddComponent<RenderPoseSource>();
                source.Configure(body);
                visual = CreateVisual(origin.transform, source, shader, BodyVisualSettings.For(BodyVisualKind.Burning));
                Assert.That(visual.Update(0, body), Is.True);
                var attached = origin.GetComponentsInChildren<MeshRenderer>().Single(r => r != body && r.sharedMaterial.GetFloat("_BodyLayer") > 0.5f);
                var material = attached.sharedMaterial;
                Assert.That(material.GetFloat("_BodyPixelSize"), Is.EqualTo(1f/16f).Within(0.00001f));
                Assert.That(attached.bounds.max.x - body.bounds.max.x, Is.GreaterThanOrEqualTo(1f/16f), "A narrow body must still have room for external pixels.");
                var mask = material.GetTexture("_BodyMask") as RenderTexture;
                Assert.That(mask.filterMode, Is.EqualTo(FilterMode.Point));
                if (meshSource)
                {
                    var vertices = mesh.vertices;
                    vertices[3] += Vector3.up * 0.125f; // A new pose must not change the scale of the frost/fire grid.
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                }
                else body.transform.localPosition = new Vector3(0.13f, 0.27f, 0);
                Assert.That(visual.Update(0.25f, body), Is.True);
                Assert.That(material.GetFloat("_BodyPixelSize"), Is.EqualTo(1f/16f).Within(0.00001f));
                Assert.That(body.sharedMaterial, Is.SameAs(authored));
            }
            finally
            {
                visual?.Dispose();
                Object.DestroyImmediate(origin);
                Object.DestroyImmediate(authored);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(BodyVisualKind.Frozen)]
        [TestCase(BodyVisualKind.Burning)]
        public void AttachedVisual_CapturesAsymmetricPose_PreservesRenderTarget_AndReleasesGpuResources(BodyVisualKind kind)
        {
            // Lasting contract: posed alpha union, external effect envelope and private GPU lifetime.
            var origin = new GameObject("SyntheticFrostFire");
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color[256];
            for (int y = 8; y < 14; y++)
                for (int x = 2; x < 14; x++) pixels[y * 16 + x] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 16f, 0, SpriteMeshType.FullRect);
            var source = origin.AddComponent<RenderPoseSource>();
            var body = origin.AddComponent<SpriteRenderer>();
            body.sprite = sprite;
            source.Configure(body);
            var callerTarget = new RenderTexture(16, 16, 0);
            callerTarget.Create();
            var previous = RenderTexture.active;
            BodyVisual visual = null;
            RenderTexture mask = null;
            Mesh attachedMesh = null;
            Material attachedMaterial = null;
            Texture2D readback = null;
            try
            {
                visual = CreateVisual(origin.transform, source, Resources.Load<Shader>(ZeroEngine.Render.Urp.BodyVisualResourcesLoader.BodyPath), BodyVisualSettings.For(kind));
                RenderTexture.active = callerTarget;
                Assert.That(visual.Update(0.2f, body), Is.True);
                Assert.That(RenderTexture.active, Is.SameAs(callerTarget));
                var attached = origin.GetComponentsInChildren<MeshRenderer>().Single();
                Assert.That(attached.bounds.max.y, Is.GreaterThan(body.bounds.max.y));
                attachedMaterial = attached.sharedMaterial;
                attachedMesh = attached.GetComponent<MeshFilter>().sharedMesh;
                mask = attachedMaterial.GetTexture("_BodyMask") as RenderTexture;
                Assert.That(mask && mask.IsCreated(), Is.True);
                RenderTexture.active = mask;
                readback = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, mask.width, mask.height), 0, 0);
                readback.Apply();
                Assert.That(readback.GetPixel(64, 192).a, Is.GreaterThan(0.9f), "The upper occupied part must remain above the empty lower half.");
                Assert.That(readback.GetPixel(64, 64).a, Is.LessThan(0.01f));
                RenderTexture.active = callerTarget;
                body.enabled = false;
                Assert.That(visual.Update(0.3f, body), Is.True);
                Assert.That(origin.GetComponentsInChildren<Renderer>().Any(value => value.enabled), Is.False);
                visual.Dispose();
                Assert.That(!mask && !attachedMesh && !attachedMaterial, Is.True);
                Assert.That(texture && sprite && callerTarget, Is.True);
            }
            finally
            {
                RenderTexture.active = previous;
                visual?.Dispose();
                callerTarget.Release();
                Object.DestroyImmediate(callerTarget);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(origin);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
