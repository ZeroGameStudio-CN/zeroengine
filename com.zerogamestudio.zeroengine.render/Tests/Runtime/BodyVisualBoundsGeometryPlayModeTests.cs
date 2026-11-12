using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests.Runtime
{
    public sealed class BodyVisualBoundsGeometryPlayModeTests
    {
        [UnityTest]
        public IEnumerator InPlaceGeometryWithUnchangedBounds_KeepsTheExactNativeCenter()
        {
            bool background = Application.runInBackground;
            Application.runInBackground = true;
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
                var driver = root.AddComponent<BodyVisualBoundsGeometryDriver>();
                driver.Mutation = () =>
                {
                    // OverrideGeometry requires an actual player-loop callback;
                    // an EditMode coroutine remains on EditorApplication.update after EnterPlayMode.
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
                };
                float deadline = Time.realtimeSinceStartup + 10;
                while (!driver.Completed && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(driver.Completed, Is.True, "The geometry mutation did not reach Update.");
                if (driver.Failure != null) ExceptionDispatchInfo.Capture(driver.Failure).Throw();
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (sprite) Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Application.runInBackground = background;
            }
        }
    }
}
