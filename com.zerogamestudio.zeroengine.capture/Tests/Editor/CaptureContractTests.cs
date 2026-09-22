using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.Capture.Tests
{
    // Permanent contracts: validation, pixel output, borrowed state and failure cleanup.
    public sealed class CaptureContractTests
    {
        [TestCase(30, 59)]
        [TestCase(0, 60)]
        [TestCase(9, 63)]
        [TestCase(61, 61)]
        public void Options_InvalidCadence_Rejects(int fps, int simulationFps)
        {
            Assert.Throws<ArgumentException>(() => new CaptureOptions
            {
                OutputRoot = Path.GetTempPath(), FramesPerSecond = fps,
                SimulationFramesPerSecond = simulationFps
            }.ValidatedCopy());
        }

        [Test]
        public void Options_Snapshot_IsIndependentOfCallerEdits()
        {
            var original = new CaptureOptions { OutputRoot = Path.GetTempPath() };
            var snapshot = original.ValidatedCopy();
            original.Width = 1;
            Assert.AreEqual(1280, snapshot.Width);
        }

        [Test]
        public void Start_OutsidePlayMode_RejectsWithoutSession()
        {
            Assert.Throws<InvalidOperationException>(() => BackgroundCapture.Start(
                new CaptureOptions { OutputRoot = Path.GetTempPath() }, () => null));
            Assert.IsNull(BackgroundCapture.Current);
        }

        [Test]
        public void FrameWriter_RendersPixelsAndRestoresCamera_OnSuccessAndIoFailure()
        {
            var root = Path.Combine(Path.GetTempPath(), "ze-capture-contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var cameraObject = new GameObject("Capture contract camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.red;
            camera.aspect = 1.25f;
            var previous = new RenderTexture(32, 32, 0);
            camera.targetTexture = previous;
            var activeBefore = RenderTexture.active;
            var png = Path.Combine(root, "frame.png");
            try
            {
                using (var writer = new CameraFrameWriter(64, 64))
                {
                    writer.Write(camera, null, png);
                    Assert.AreSame(previous, camera.targetTexture);
                    Assert.AreSame(activeBefore, RenderTexture.active);
                    Assert.AreEqual(1.25f, camera.aspect);
                    Assert.AreEqual(0, camera.cullingMask);
                    var pixels = new Texture2D(2, 2);
                    try
                    {
                        Assert.IsTrue(pixels.LoadImage(File.ReadAllBytes(png)));
                        Assert.AreEqual(64, pixels.width);
                        Assert.Greater(pixels.GetPixel(32, 32).r, 0.8f);
                        Assert.Less(pixels.GetPixel(32, 32).g, 0.1f);
                    }
                    finally { Object.DestroyImmediate(pixels); }
                    Assert.Throws<IOException>(() => writer.Write(camera, null, png));
                    Assert.AreSame(previous, camera.targetTexture);
                    Assert.AreSame(activeBefore, RenderTexture.active);
                    Assert.AreEqual(1.25f, camera.aspect);
                }
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(previous);
                Object.DestroyImmediate(cameraObject);
                // This exact generated directory owns only the synthetic test's frame.
                if (File.Exists(png)) File.Delete(png);
                Directory.Delete(root);
            }
        }

        [Test]
        public void Screenshot_WritesUniqueImage_AndPreservesOverlayAndTiming()
        {
            var root = Path.Combine(Path.GetTempPath(), "ze-screenshot-" + Guid.NewGuid().ToString("N"));
            var cameraObject = new GameObject("Screenshot contract camera");
            var canvasObject = new GameObject("Screenshot contract canvas", typeof(RectTransform), typeof(Canvas));
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var distance = canvas.planeDistance;
            var delta = Time.captureDeltaTime;
            var background = Application.runInBackground;
            string image = null;
            try
            {
                image = BackgroundCapture.Screenshot(new CaptureOptions
                { OutputRoot = root, Width = 64, Height = 64 }, camera, new[] { canvas });
                Assert.IsTrue(File.Exists(image));
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
                Assert.IsNull(canvas.worldCamera);
                Assert.AreEqual(distance, canvas.planeDistance);
                Assert.AreEqual(0, camera.cullingMask);
                Assert.IsNull(camera.targetTexture);
                Assert.AreEqual(delta, Time.captureDeltaTime);
                Assert.AreEqual(background, Application.runInBackground);
                Assert.IsNull(BackgroundCapture.Current);
                Assert.Throws<ArgumentException>(() => BackgroundCapture.Screenshot(new CaptureOptions
                { OutputRoot = Application.dataPath }, camera));
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(cameraObject);
                if (image != null)
                {
                    File.Delete(image);
                    Directory.Delete(Path.GetDirectoryName(image));
                }
                if (Directory.Exists(root)) Directory.Delete(root);
            }
        }

        [TestCase(0)]
        [TestCase(1 << 10)]
        [TestCase(1 << 31)]
        public void Screenshot_OverlayOnExcludedLayer_DoesNotRevealExcludedWorld(int cameraMask)
        {
            var root = Path.Combine(Path.GetTempPath(), "ze-capture-layer-contract-" + Guid.NewGuid().ToString("N"));
            var cameraObject = new GameObject("Layer contract camera");
            var canvasObject = new GameObject("Default layer overlay", typeof(RectTransform), typeof(Canvas));
            var imageObject = new GameObject("Opaque UI", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var excludedWorld = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            string path = null;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = cameraMask;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.blue;
                excludedWorld.name = "Excluded default-layer world";
                excludedWorld.layer = 0;
                excludedWorld.transform.position = new Vector3(0f, 0f, 5f);
                excludedWorld.transform.localScale = Vector3.one * 20f;
                material.color = Color.red;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.red);
                excludedWorld.GetComponent<Renderer>().sharedMaterial = material;

                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                imageObject.transform.SetParent(canvasObject.transform, false);
                var rect = imageObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                imageObject.GetComponent<UnityEngine.UI.Image>().color = Color.green;
                var options = new CaptureOptions { OutputRoot = root, Width = 64, Height = 64 };
                if (cameraMask == 0)
                {
                    var error = Assert.Throws<InvalidOperationException>(() =>
                        BackgroundCapture.Screenshot(options, camera, new[] { canvas }));
                    StringAssert.Contains("visible camera layer", error.Message);
                    Assert.IsEmpty(Directory.GetFiles(root, "*.png", SearchOption.AllDirectories));
                }
                else
                {
                    path = BackgroundCapture.Screenshot(options, camera, new[] { canvas });

                    var pixels = new Texture2D(2, 2);
                    try
                    {
                        Assert.IsTrue(pixels.LoadImage(File.ReadAllBytes(path)));
                        var ui = pixels.GetPixel(16, 32);
                        var world = pixels.GetPixel(48, 32);
                        Assert.Greater(ui.g, 0.8f, "The explicit overlay must be visible: " + path);
                        Assert.Less(ui.b, 0.1f);
                        Assert.Greater(world.b, 0.8f, "Excluded world content must remain absent: " + path);
                        Assert.Less(world.r, 0.1f);
                    }
                    finally { Object.DestroyImmediate(pixels); }
                    // IO failure after rendering must also restore every borrowed UI layer.
                    using (var writer = new CameraFrameWriter(64, 64))
                        Assert.Throws<IOException>(() => writer.Write(camera, new[] { canvas }, path));
                }
                Assert.AreEqual(cameraMask, camera.cullingMask);
                Assert.AreEqual(0, canvasObject.layer);
                Assert.AreEqual(0, imageObject.layer);
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
                Assert.IsNull(canvas.worldCamera);
            }
            finally
            {
                Object.DestroyImmediate(imageObject);
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(excludedWorld);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Screenshot_OverlaySorting_PreservesNestedUiOrderAboveSpritesAndRestores(bool reverseInputs)
        {
            var root = Path.Combine(Path.GetTempPath(), "ze-capture-sorting-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var cameraObject = new GameObject("Sorting contract camera");
            var worldObject = new GameObject("High-order world sprite", typeof(SpriteRenderer));
            var lowerObject = new GameObject("Lower overlay", typeof(RectTransform), typeof(Canvas));
            var upperObject = new GameObject("Upper overlay", typeof(RectTransform), typeof(Canvas));
            var nestedObject = new GameObject("Nested override", typeof(RectTransform), typeof(Canvas));
            var texture = new Texture2D(2, 2);
            Sprite sprite = null;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 2f;
                camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1f);
                var world = worldObject.GetComponent<SpriteRenderer>();
                world.sprite = sprite;
                world.color = Color.red;
                world.sortingOrder = 32760;
                foreach (var layer in SortingLayer.layers)
                    if (layer.value > SortingLayer.GetLayerValueFromID(world.sortingLayerID))
                        world.sortingLayerID = layer.id;
                worldObject.layer = 30;
                worldObject.transform.position = new Vector3(0f, 0f, 5f);
                worldObject.transform.localScale = Vector3.one * 10f;

                var lower = lowerObject.GetComponent<Canvas>();
                lower.renderMode = RenderMode.ScreenSpaceOverlay;
                lower.sortingOrder = -25;
                var upper = upperObject.GetComponent<Canvas>();
                upper.renderMode = RenderMode.ScreenSpaceOverlay;
                upper.sortingOrder = 17;
                nestedObject.transform.SetParent(lowerObject.transform, false);
                var nestedRect = nestedObject.GetComponent<RectTransform>();
                nestedRect.anchorMin = Vector2.zero;
                nestedRect.anchorMax = Vector2.one;
                nestedRect.offsetMin = nestedRect.offsetMax = Vector2.zero;
                var nested = nestedObject.GetComponent<Canvas>();
                nested.overrideSorting = true;
                nested.sortingOrder = 41;
                AddImage(lower.transform, Color.green, Vector2.zero, new Vector2(0.75f, 1f));
                AddImage(upper.transform, Color.blue, new Vector2(0.25f, 0f), new Vector2(0.75f, 1f));
                AddImage(nested.transform, Color.yellow, new Vector2(0.25f, 0.5f), new Vector2(0.75f, 1f));
                Canvas.ForceUpdateCanvases();
                var overlays = reverseInputs ? new[] { upper, lower, lower } : new[] { lower, upper, lower };
                var path = Path.Combine(root, "frame.png");
                using (var writer = new CameraFrameWriter(64, 64))
                {
                    writer.Write(camera, overlays, path);
                    var pixels = new Texture2D(2, 2);
                    try
                    {
                        Assert.IsTrue(pixels.LoadImage(File.ReadAllBytes(path)));
                        AssertPixel(pixels, 8, 16, Color.green, path);
                        AssertPixel(pixels, 32, 16, Color.blue, path);
                        AssertPixel(pixels, 32, 48, Color.yellow, path);
                        AssertPixel(pixels, 60, 32, Color.red, path);
                    }
                    finally { Object.DestroyImmediate(pixels); }
                    AssertSortingRestored(lower, upper, nested, camera);
                    Assert.Throws<IOException>(() => writer.Write(camera, overlays, path));
                    AssertSortingRestored(lower, upper, nested, camera);
                }
            }
            finally
            {
                Object.DestroyImmediate(nestedObject);
                Object.DestroyImmediate(lowerObject);
                Object.DestroyImmediate(upperObject);
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static void AddImage(Transform parent, Color color, Vector2 min, Vector2 max)
        {
            var image = new GameObject("Synthetic UI", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            image.transform.SetParent(parent, false);
            var rect = image.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            image.GetComponent<UnityEngine.UI.Image>().color = color;
        }

        private static void AssertPixel(Texture2D pixels, int x, int y, Color expected, string path)
        {
            Color actual = pixels.GetPixel(x, y);
            Assert.That(Mathf.Abs(actual.r - expected.r), Is.LessThan(0.1f), path);
            Assert.That(Mathf.Abs(actual.g - expected.g), Is.LessThan(0.1f), path);
            Assert.That(Mathf.Abs(actual.b - expected.b), Is.LessThan(0.1f), path);
        }

        private static void AssertSortingRestored(Canvas lower, Canvas upper, Canvas nested, Camera camera)
        {
            Assert.AreEqual(-25, lower.sortingOrder);
            Assert.AreEqual(17, upper.sortingOrder);
            Assert.AreEqual(41, nested.sortingOrder);
            Assert.IsTrue(nested.overrideSorting);
            foreach (var canvas in new[] { lower, upper, nested })
            {
                Assert.AreEqual(0, canvas.sortingLayerID);
                Assert.AreEqual(0, canvas.gameObject.layer);
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            }
            Assert.IsNull(lower.worldCamera);
            Assert.IsNull(upper.worldCamera);
            Assert.IsNull(camera.targetTexture);
            Assert.AreEqual(1 << 30, camera.cullingMask);
        }

        [Test]
        public void Cancel_RestoresTimingAndNeverPublishesSuccessManifest()
        {
            var delta = Time.captureDeltaTime;
            var background = Application.runInBackground;
            using (var session = new CaptureSession(new CaptureOptions
            {
                OutputRoot = Path.GetTempPath(), MaximumFrames = 10
            }.ValidatedCopy(), () => null, null))
            {
                session.Cancel("contract-test");
                Assert.AreEqual(delta, Time.captureDeltaTime);
                Assert.AreEqual(background, Application.runInBackground);
                Assert.AreEqual("cancelled", session.State);
                Assert.IsFalse(File.Exists(Path.Combine(session.DirectoryPath, "capture.json")));
                Assert.IsTrue(File.Exists(Path.Combine(session.DirectoryPath, "session.json")));
                File.Delete(Path.Combine(session.DirectoryPath, "session.json"));
                Directory.Delete(session.DirectoryPath);
            }
        }
    }
}
