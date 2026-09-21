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
