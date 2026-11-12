using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class BodyVisualBoundsCacheTests
    {
        private GameObject root;
        private Texture2D texture;
        private Sprite first, second;
        private SpriteRenderer renderer;
        [SetUp] public void Setup()
        {
            root = new GameObject("BodyBoundsCacheTest");
            texture = new Texture2D(16, 16);
            first = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.2f, .35f), 16, 0, SpriteMeshType.FullRect);
            second = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.7f, .15f), 24, 0, SpriteMeshType.FullRect);
            renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = first;
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(first);
            Object.DestroyImmediate(second); Object.DestroyImmediate(texture);
        }
        private static void EqualBits(Vector3 expected, Vector3 actual)
        {
            Assert.That(BitConverter.ToInt32(BitConverter.GetBytes(actual.x), 0), Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes(expected.x), 0)));
            Assert.That(BitConverter.ToInt32(BitConverter.GetBytes(actual.y), 0), Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes(expected.y), 0)));
            Assert.That(BitConverter.ToInt32(BitConverter.GetBytes(actual.z), 0), Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes(expected.z), 0)));
        }
        [Test] public void EligibleSprite_ReusesExactNativeCenterWithinOnePassOnly()
        {
            var cache = new BodyVisualBoundsCache(); cache.SetEligibility(_ => true);
            var expected = renderer.bounds.center;
            for (int i = 0; i < 8; i++) EqualBits(expected, cache.GetCenter(renderer));
            Assert.That(cache.NativeReadsThisPass, Is.EqualTo(1));
            cache.BeginPass(); EqualBits(renderer.bounds.center, cache.GetCenter(renderer));
            Assert.That(cache.NativeReadsThisPass, Is.EqualTo(1));
        }
        [Test] public void TransformSpriteFlipSizeAndLocalBoundsChanges_InvalidateImmediately()
        {
            var cache = new BodyVisualBoundsCache(); cache.SetEligibility(_ => true);
            cache.GetCenter(renderer); int reads = 1;
            Action[] changes = {
                () => root.transform.localPosition = new Vector3(3, 5, 1),
                () => root.transform.localRotation = Quaternion.Euler(0, 0, 39),
                () => root.transform.localScale = new Vector3(-2f, .6f, 1),
                () => renderer.flipX = !renderer.flipX,
                () => renderer.flipY = !renderer.flipY,
                () => renderer.sprite = second,
                () => renderer.drawMode = SpriteDrawMode.Sliced,
                () => renderer.size = new Vector2(3, 4),
                () => renderer.localBounds = new Bounds(new Vector3(.2f, .7f, 0), new Vector3(2, 3, .1f))
            };
            foreach (var change in changes)
            {
                change(); var actual = cache.GetCenter(renderer);
                var expected = renderer.bounds.center;
                EqualBits(expected, actual); reads++;
                Assert.That(cache.NativeReadsThisPass, Is.EqualTo(reads));
                EqualBits(expected, cache.GetCenter(renderer));
                Assert.That(cache.NativeReadsThisPass, Is.EqualTo(reads));
            }
        }
        [Test] public void DefaultAndLiveOptOut_PreserveCustomWorldBoundsOnEveryRead()
        {
            var cache = new BodyVisualBoundsCache();
            renderer.bounds = new Bounds(new Vector3(10, 20, 30), Vector3.one);
            EqualBits(renderer.bounds.center, cache.GetCenter(renderer));
            renderer.bounds = new Bounds(new Vector3(4, 5, 6), Vector3.one);
            EqualBits(renderer.bounds.center, cache.GetCenter(renderer));
            Assert.That(cache.NativeReadsThisPass, Is.EqualTo(2));
            bool eligible = true; renderer.ResetBounds(); cache.SetEligibility(_ => eligible);
            cache.GetCenter(renderer); eligible = false;
            renderer.bounds = new Bounds(new Vector3(7, 8, 9), Vector3.one);
            EqualBits(renderer.bounds.center, cache.GetCenter(renderer));
            Assert.That(cache.NativeReadsThisPass, Is.EqualTo(2));
        }
        [Test] public void DifferentRendererAndPolicyChange_CannotReuseAnotherInput()
        {
            var child = new GameObject("Other"); child.transform.SetParent(root.transform, false);
            var other = child.AddComponent<SpriteRenderer>(); other.sprite = second;
            var cache = new BodyVisualBoundsCache(); cache.SetEligibility(_ => true);
            cache.GetCenter(renderer); EqualBits(other.bounds.center, cache.GetCenter(other));
            Assert.That(cache.NativeReadsThisPass, Is.EqualTo(2));
            cache.SetEligibility(null); cache.GetCenter(other); cache.GetCenter(other);
            Assert.That(cache.NativeReadsThisPass, Is.EqualTo(2));
        }
        [Test] public void NonSpriteRenderer_NeverUsesTheSpriteCacheEvenWhenOptedIn()
        {
            var child = new GameObject("MeshBody"); child.transform.SetParent(root.transform, false);
            var body = child.AddComponent<MeshRenderer>();
            var cache = new BodyVisualBoundsCache(); cache.SetEligibility(_ => true);
            body.bounds = new Bounds(new Vector3(1, 2, 3), Vector3.one);
            EqualBits(body.bounds.center, cache.GetCenter(body));
            body.bounds = new Bounds(new Vector3(7, 8, 9), Vector3.one);
            EqualBits(body.bounds.center, cache.GetCenter(body));
            Assert.That(cache.NativeReadsThisPass, Is.EqualTo(2));
        }
    }

    public sealed class BodyVisualBoundsGeometryPlayModeTests
    {
        [UnitySetUp] public IEnumerator EnterRuntime() { yield return new EnterPlayMode(); }
        [UnityTearDown] public IEnumerator ExitRuntime()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator InPlaceGeometryWithUnchangedBounds_KeepsTheExactNativeCenter()
        {
            var root = new GameObject("BodyBoundsGeometryTest");
            var texture = new Texture2D(64, 64);
            Sprite sprite = null;
            try
            {
                var colors = new Color[64 * 64];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                texture.SetPixels(colors); texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * .5f,
                    64, 0, SpriteMeshType.Tight);
                var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                yield return null;
                // Geometry overrides require the player loop and pixel positions
                // in the sprite rect, rather than Sprite.vertices' local units.
                var vertices = new[] { Vector2.zero, new Vector2(64, 0), new Vector2(64, 64),
                    new Vector2(0, 64), new Vector2(32, 32) };
                var triangles = new ushort[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 };
                sprite.OverrideGeometry(vertices, triangles);
                Assert.That(sprite.vertices.Length, Is.EqualTo(5));
                var originalBounds = sprite.bounds;
                var originalVertex = sprite.vertices[4];
                var cache = new BodyVisualBoundsCache(); cache.SetEligibility(_ => true);
                cache.GetCenter(renderer);
                vertices[4] = new Vector2(36, 28);
                sprite.OverrideGeometry(vertices, triangles);
                Assert.That(sprite.vertices[4], Is.Not.EqualTo(originalVertex));
                Assert.That(sprite.bounds, Is.EqualTo(originalBounds));
                var expected = renderer.bounds.center;
                var actual = cache.GetCenter(renderer);
                for (int i = 0; i < 3; i++)
                    Assert.That(BitConverter.ToInt32(BitConverter.GetBytes(actual[i]), 0),
                        Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes(expected[i]), 0)));
                Assert.That(cache.NativeReadsThisPass, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (sprite) Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
