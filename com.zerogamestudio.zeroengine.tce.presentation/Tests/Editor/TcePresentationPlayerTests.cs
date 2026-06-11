using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.TCE;
using ZeroEngine.TCE.Presentation;

namespace ZeroEngine.TCE.Presentation.Tests.Editor
{
    [TestFixture]
    public sealed class TcePresentationPlayerTests
    {
        private const string PackagePath = "Packages/com.zerogamestudio.zeroengine.tce.presentation";

        [Test]
        public void Play_NullSnapshot_ReturnsDisposedHandle()
        {
            var runnerObject = new GameObject(nameof(Play_NullSnapshot_ReturnsDisposedHandle));

            try
            {
                var runner = runnerObject.AddComponent<TcePresentationRunner>();

                var handle = runner.Play(null, new TcePresentationPlaybackSettings(), new FakeClock(0f));

                Assert.IsTrue(handle.IsDisposed);
            }
            finally
            {
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Play_MeshSnapshot_DisposeCleansSnapshot()
        {
            var runnerObject = new GameObject(nameof(Play_MeshSnapshot_DisposeCleansSnapshot));
            var mesh = CreateTriangleMesh("PlayableSnapshotMesh");

            try
            {
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceMeshSnapshot(Matrix4x4.identity, 0, mesh, ownsMesh: true);

                var handle = runner.Play(snapshot, new TcePresentationPlaybackSettings { Duration = 1f }, new FakeClock(0f));
                handle.Dispose();

                Assert.IsTrue(mesh == null);
            }
            finally
            {
                if (mesh) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Play_SpriteLayerSnapshot_CreatesTemporaryLayersAndCleansSnapshot()
        {
            var runnerObject = new GameObject(nameof(Play_SpriteLayerSnapshot_CreatesTemporaryLayersAndCleansSnapshot));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);

            try
            {
                int before = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteLayerSnapshot(
                    Matrix4x4.identity,
                    0,
                    new[]
                    {
                        new TceSpriteLayerFrame(sprite, true, 0, 12, Vector3.zero, Quaternion.identity, Vector3.one)
                    });

                var handle = runner.Play(snapshot, new TcePresentationPlaybackSettings { Duration = 1f }, new FakeClock(0f));
                int during = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                Assert.AreEqual(before + 1, during);

                handle.Dispose();

                int after = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                Assert.AreEqual(before, after);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_UsesClockDeltaWithoutDotweenDependency()
        {
            foreach (string file in Directory.GetFiles($"{PackagePath}/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                StringAssert.DoesNotContain("DG.Tweening", source, file);
                StringAssert.DoesNotContain("DOTween", source, file);
            }

            var runnerObject = new GameObject(nameof(Runner_UsesClockDeltaWithoutDotweenDependency));
            var mesh = CreateTriangleMesh("ClockDrivenSnapshotMesh");

            try
            {
                var clock = new FakeClock(10f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                runner.Play(new TceMeshSnapshot(Matrix4x4.identity, 0, mesh, ownsMesh: true), new TcePresentationPlaybackSettings { Duration = 1f }, clock);

                InvokeLateUpdate(runner);
                Assert.IsFalse(mesh == null);

                clock.Now = 11.1f;
                InvokeLateUpdate(runner);
                Assert.IsTrue(mesh == null);
            }
            finally
            {
                if (mesh) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SoulGhostSpriteSnapshot_MovesAlongDirection()
        {
            var runnerObject = new GameObject(nameof(Runner_SoulGhostSpriteSnapshot_MovesAlongDirection));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);

            try
            {
                var clock = new FakeClock(0f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 0);
                var handle = runner.Play(
                    snapshot,
                    new TcePresentationPlaybackSettings
                    {
                        Style = TcePresentationStyle.SoulGhost,
                        Direction = new Vector3(2f, 0f, 0f),
                        Duration = 1f
                    },
                    clock);

                Transform root = FindTransform("Tce Sprite Snapshot");
                Assert.IsNotNull(root);
                Assert.AreEqual(Vector3.zero, root.position);

                clock.Now = 0.5f;
                InvokeLateUpdate(runner);

                Assert.AreEqual(new Vector3(1f, 0f, 0f), root.position);
                handle.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SpriteSnapshot_UsesConfiguredAlphaCurveAndFadeWindow()
        {
            var runnerObject = new GameObject(nameof(Runner_SpriteSnapshot_UsesConfiguredAlphaCurveAndFadeWindow));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            TcePresentationHandle handle = null;

            try
            {
                var clock = new FakeClock(0f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 0);
                handle = runner.Play(
                    snapshot,
                    new TcePresentationPlaybackSettings
                    {
                        Tint = Color.white,
                        Duration = 1f,
                        FadeDelay = 0.25f,
                        FadeDuration = 0.5f,
                        AlphaEase = AnimationCurve.Linear(0f, 1f, 1f, 0f)
                    },
                    clock);

                var renderer = FindSpriteRenderer(sprite);
                Assert.IsNotNull(renderer);

                clock.Now = 0.125f;
                InvokeLateUpdate(runner);
                Assert.AreEqual(1f, renderer.color.a, 0.0001f);

                clock.Now = 0.5f;
                InvokeLateUpdate(runner);
                Assert.AreEqual(0.5f, renderer.color.a, 0.0001f);
            }
            finally
            {
                handle?.Dispose();
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SpriteSnapshot_AppliesSortingAndMaterialOverrides()
        {
            var runnerObject = new GameObject(nameof(Runner_SpriteSnapshot_AppliesSortingAndMaterialOverrides));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            Material material = null;
            TcePresentationHandle handle = null;

            try
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                Assert.IsNotNull(shader);
                material = new Material(shader) { name = "TcePresentationOverrideMaterial" };

                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 17);
                handle = runner.Play(
                    snapshot,
                    new TcePresentationPlaybackSettings
                    {
                        Duration = 1f,
                        OverrideSorting = true,
                        SortingLayerId = 0,
                        SortingOrder = 44,
                        MaterialOverride = material
                    },
                    new FakeClock(0f));

                var renderer = FindSpriteRenderer(sprite);
                Assert.IsNotNull(renderer);
                Assert.AreEqual(44, renderer.sortingOrder);
                Assert.AreSame(material, renderer.sharedMaterial);
            }
            finally
            {
                handle?.Dispose();
                if (material) Object.DestroyImmediate(material);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SpriteSnapshot_UsesTintShaderPropertyName()
        {
            var runnerObject = new GameObject(nameof(Runner_SpriteSnapshot_UsesTintShaderPropertyName));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            TcePresentationHandle handle = null;

            try
            {
                var tint = new Color(0.25f, 0.5f, 0.75f, 0.9f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 17);
                handle = runner.Play(
                    snapshot,
                    new TcePresentationPlaybackSettings
                    {
                        Tint = tint,
                        Duration = 1f,
                        TintPropertyName = "_BaseColor"
                    },
                    new FakeClock(0f));

                var renderer = FindSpriteRenderer(sprite);
                Assert.IsNotNull(renderer);

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                AssertColor(tint, block.GetColor(Shader.PropertyToID("_BaseColor")));
            }
            finally
            {
                handle?.Dispose();
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SpriteSnapshot_CanDriveShaderTintWithoutSpriteRendererColor()
        {
            var runnerObject = new GameObject(nameof(Runner_SpriteSnapshot_CanDriveShaderTintWithoutSpriteRendererColor));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            TcePresentationHandle handle = null;

            try
            {
                var colorField = typeof(TcePresentationPlaybackSettings).GetField("ApplySpriteRendererColor");
                Assert.IsNotNull(colorField);

                var tint = new Color(0.25f, 0.5f, 0.75f, 0.9f);
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 17);
                var settings = new TcePresentationPlaybackSettings
                {
                    Tint = tint,
                    Duration = 1f,
                    TintPropertyName = "_BaseColor"
                };
                colorField.SetValue(settings, false);

                handle = runner.Play(snapshot, settings, new FakeClock(0f));

                var renderer = FindSpriteRenderer(sprite);
                Assert.IsNotNull(renderer);
                AssertColor(Color.white, renderer.color);

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                AssertColor(tint, block.GetColor(Shader.PropertyToID("_BaseColor")));
            }
            finally
            {
                handle?.Dispose();
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SpriteSnapshot_CopiesSpriteTextureToConfiguredShaderProperty()
        {
            var runnerObject = new GameObject(nameof(Runner_SpriteSnapshot_CopiesSpriteTextureToConfiguredShaderProperty));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            TcePresentationHandle handle = null;

            try
            {
                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 17);
                handle = runner.Play(
                    snapshot,
                    new TcePresentationPlaybackSettings
                    {
                        Duration = 1f,
                        CopyMainTexture = true,
                        MainTexturePropertyName = "_MainTex"
                    },
                    new FakeClock(0f));

                var renderer = FindSpriteRenderer(sprite);
                Assert.IsNotNull(renderer);

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Assert.AreSame(texture, block.GetTexture(Shader.PropertyToID("_MainTex")));
            }
            finally
            {
                handle?.Dispose();
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_SpriteSnapshot_CanFadeTintRgbWithAlpha()
        {
            var runnerObject = new GameObject(nameof(Runner_SpriteSnapshot_CanFadeTintRgbWithAlpha));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            TcePresentationHandle handle = null;

            try
            {
                var rgbFadeField = typeof(TcePresentationPlaybackSettings).GetField("FadeTintRgbWithAlpha");
                Assert.IsNotNull(rgbFadeField);

                var runner = runnerObject.AddComponent<TcePresentationRunner>();
                var snapshot = new TceSpriteSnapshot(Matrix4x4.identity, 0, sprite, 0, 17);
                var settings = new TcePresentationPlaybackSettings
                {
                    Tint = new Color(2f, 4f, 6f, 0.8f),
                    Duration = 1f,
                    FadeDuration = 1f,
                    TintPropertyName = "_BaseColor",
                    AlphaEase = AnimationCurve.Linear(0f, 1f, 1f, 0f)
                };
                rgbFadeField.SetValue(settings, true);

                var clock = new FakeClock(0f);
                handle = runner.Play(snapshot, settings, clock);

                var renderer = FindSpriteRenderer(sprite);
                Assert.IsNotNull(renderer);

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                AssertColor(new Color(2f, 4f, 6f, 0.8f), block.GetColor(Shader.PropertyToID("_BaseColor")));

                InvokeLateUpdate(runner);
                renderer.GetPropertyBlock(block);
                AssertColor(new Color(2f, 4f, 6f, 0.8f), block.GetColor(Shader.PropertyToID("_BaseColor")));

                clock.Now = 0.5f;
                InvokeLateUpdate(runner);
                renderer.GetPropertyBlock(block);
                AssertColor(new Color(1f, 2f, 3f, 0.4f), block.GetColor(Shader.PropertyToID("_BaseColor")));
            }
            finally
            {
                handle?.Dispose();
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void Runner_MeshSnapshot_UsesMaterialOverrideAndConfigurableShaderProperties()
        {
            string source = File.ReadAllText($"{PackagePath}/Runtime/Playback/TcePresentationRunner.cs");

            StringAssert.Contains("settings.MaterialOverride ? settings.MaterialOverride : GetFallbackMaterial()", source);
            StringAssert.Contains("settings.TintPropertyName", source);
            StringAssert.Contains("settings.MainTexturePropertyName", source);
        }

        private static void InvokeLateUpdate(TcePresentationRunner runner)
        {
            typeof(TcePresentationRunner)
                .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(runner, null);
        }

        private static Transform FindTransform(string name)
        {
            foreach (Transform transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (transform.name == name)
                    return transform;
            }

            return null;
        }

        private static SpriteRenderer FindSpriteRenderer(Sprite sprite)
        {
            foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer.sprite == sprite)
                    return renderer;
            }

            return null;
        }

        private static void AssertColor(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 0.0001f);
            Assert.AreEqual(expected.g, actual.g, 0.0001f);
            Assert.AreEqual(expected.b, actual.b, 0.0001f);
            Assert.AreEqual(expected.a, actual.a, 0.0001f);
        }

        private static Mesh CreateTriangleMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class FakeClock : ITceClock
        {
            public FakeClock(float now)
            {
                Now = now;
            }

            public float Now { get; set; }
        }
    }
}
