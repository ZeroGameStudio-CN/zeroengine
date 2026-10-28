using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.FogOfWar.Unity;
using Object = UnityEngine.Object;

namespace ZeroEngine.FogOfWar.URP.Tests
{
    public sealed class SightMaskRendererTests
    {
        [TestCase(0f), TestCase(2f)]
        public void StandaloneMaskPreservesRawSightTerrainAndRecoversLostTargets(float softness)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.zerogamestudio.zeroengine.fogofwar.urp/Runtime/Shaders/SightMask.shader");
            Assert.That(shader && shader.isSupported, Is.True);
            var cameraObject = new GameObject("Synthetic standalone fog camera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(64, 48, 0);
            var terrain = new Mesh { hideFlags = HideFlags.DontSave,
                vertices = new[] { new Vector3(-2, -2), new Vector3(-1, -2), new Vector3(-1, -1), new Vector3(-2, -1) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            terrain.RecalculateBounds();
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = 4; camera.targetTexture = target;
                camera.transform.position = new Vector3(0, 0, -10);
                var sight = new SightPolygon2D();
                sight.Rebuild(Vector2.zero, new Vector2(3, 3), 1, 0,
                    new[] { new SightPolygon2D.Segment(new Vector2(1, -10), new Vector2(1, 10)) });
                var meshes = new[] { terrain };
                using var mask = new SightMaskRenderer2D(shader);
                Assert.That(mask.RenderIfChanged(camera, sight, meshes, 1, 0, softness), Is.True);
                var raw = Pixels(mask.Texture);
                bool sawSight = false, sawTerrain = false;
                foreach (var pixel in raw) { sawSight |= pixel.r != 0; sawTerrain |= pixel.g != 0; }
                Assert.That(sawSight && sawTerrain, Is.True);
                Assert.That(mask.RenderIfChanged(camera, sight, meshes, 1, 0, softness), Is.False);
                mask.Texture.Release();
                Assert.That(mask.RenderIfChanged(camera, sight, meshes, 1, 0, softness), Is.True);
                CollectionAssert.AreEqual(raw, Pixels(mask.Texture));
                Assert.That(mask.RenderIfChanged(camera, sight, meshes, 2, 0, softness), Is.True);
                CollectionAssert.AreEqual(raw, Pixels(mask.Texture));
                Assert.That(terrain && cameraObject, Is.True, "The renderer borrows caller geometry and camera.");
            }
            finally
            {
                Object.DestroyImmediate(terrain); Object.DestroyImmediate(cameraObject);
                target.Release(); Object.DestroyImmediate(target);
            }
        }

        private static Color32[] Pixels(RenderTexture texture)
        {
            var before = RenderTexture.active;
            var readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); readback.Apply();
                return readback.GetPixels32();
            }
            finally { RenderTexture.active = before; Object.DestroyImmediate(readback); }
        }
    }
}
