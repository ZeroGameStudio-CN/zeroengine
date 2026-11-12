using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZeroEngine.Capture.Tests
{
    public sealed class BackgroundCaptureTests
    {
        [UnityTest]
        public IEnumerator Recording_ProducesChangingFrames_AndRestoresState()
        {
            var cameraObject = new GameObject("ZE capture synthetic camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.red;
            var previousDelta = Time.captureDeltaTime;
            var previousBackground = Application.runInBackground;
            CaptureSession capture = null;
            try
            {
                capture = BackgroundCapture.Start(new CaptureOptions
                {
                    OutputRoot = Path.Combine(Path.GetTempPath(), "ZeroEngineCaptureTests"),
                    Width = 64, Height = 64, MaximumFrames = 120
                }, () => camera);
                Assert.Throws<InvalidOperationException>(() => BackgroundCapture.Start(
                    new CaptureOptions { OutputRoot = Path.GetTempPath() }, () => camera));
                var deadline = Time.realtimeSinceStartup + 15f;
                while (capture.FrameCount < 4 && capture.IsRecording && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.GreaterOrEqual(capture.FrameCount, 4, capture.Error);
                var redFrame = Path.Combine(capture.DirectoryPath, "recording-0000.png");
                camera.backgroundColor = Color.blue;
                var next = capture.FrameCount + 4;
                while (capture.FrameCount < next && capture.IsRecording && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.GreaterOrEqual(capture.FrameCount, next, capture.Error);
                capture.Complete();
                Assert.AreEqual("completed", capture.State, capture.Error);
                Assert.IsNull(BackgroundCapture.Current);
                Assert.AreEqual(previousDelta, Time.captureDeltaTime);
                Assert.AreEqual(previousBackground, Application.runInBackground);
                Assert.IsTrue(File.Exists(Path.Combine(capture.DirectoryPath, "capture.json")));
                AssertColor(redFrame, Color.red);
                AssertColor(Path.Combine(capture.DirectoryPath, "recording-" + (capture.FrameCount - 1).ToString("D4") + ".png"), Color.blue);
                Debug.Log("[ZE Capture Contract] " + capture.DirectoryPath);
            }
            finally
            {
                capture?.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator Cancel_RemovesFrameCallback_AndAllowsNextSession()
        {
            var cameraObject = new GameObject("ZE capture cancellation camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            var options = new CaptureOptions { OutputRoot = Path.GetTempPath(), Width = 32, Height = 32 };
            try
            {
                using (var first = BackgroundCapture.Start(options, () => camera))
                {
                    first.Cancel();
                    yield return null;
                    yield return null;
                    Assert.AreEqual(0, first.FrameCount);
                    Assert.IsFalse(File.Exists(Path.Combine(first.DirectoryPath, "capture.json")));
                }
                using (var second = BackgroundCapture.Start(options, () => camera))
                    second.Cancel();
                Assert.IsNull(BackgroundCapture.Current);
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }

        private static void AssertColor(string path, Color expected)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)));
                var actual = texture.GetPixel(texture.width / 2, texture.height / 2);
                Assert.Less(Mathf.Abs(actual.r - expected.r), 0.1f);
                Assert.Less(Mathf.Abs(actual.b - expected.b), 0.1f);
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
