using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.Capture
{
    // Every borrowed camera/canvas property is restored in the same synchronous call.
    internal sealed class CameraFrameWriter : IDisposable
    {
        private readonly RenderTexture _target;
        private readonly Texture2D _pixels;

        public CameraFrameWriter(int width, int height)
        {
            _target = new RenderTexture(width, height, 24)
            { name = "ZE Capture Target", hideFlags = HideFlags.HideAndDontSave };
            _pixels = new Texture2D(width, height, TextureFormat.RGB24, false)
            { name = "ZE Capture Pixels", hideFlags = HideFlags.HideAndDontSave };
        }

        public void Write(Camera camera, Canvas[] overlays, string path)
        {
            if (camera == null) throw new InvalidOperationException("The capture camera is unavailable.");
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousAspect = camera.aspect;
            var states = new List<CanvasState>();
            try
            {
                if (overlays != null)
                    foreach (var canvas in overlays)
                    {
                        if (canvas == null || !canvas.isActiveAndEnabled || !canvas.isRootCanvas ||
                            canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                        states.Add(new CanvasState(canvas));
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.01f, 1f);
                    }
                camera.targetTexture = _target;
                camera.aspect = (float)_target.width / _target.height;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = _target;
                _pixels.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0, false);
                _pixels.Apply(false, false);
                var bytes = _pixels.EncodeToPNG();
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    stream.Write(bytes, 0, bytes.Length);
            }
            finally
            {
                foreach (var state in states) state.Restore();
                if (camera != null)
                {
                    camera.targetTexture = previousTarget;
                    camera.aspect = previousAspect;
                }
                RenderTexture.active = previousActive;
            }
        }

        public void Dispose()
        {
            if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
            if (_pixels != null) Object.DestroyImmediate(_pixels);
        }

        private readonly struct CanvasState
        {
            private readonly Canvas _canvas;
            private readonly Camera _camera;
            private readonly float _distance;
            public CanvasState(Canvas canvas)
            {
                _canvas = canvas; _camera = canvas.worldCamera; _distance = canvas.planeDistance;
            }
            public void Restore()
            {
                if (_canvas == null) return;
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.worldCamera = _camera;
                _canvas.planeDistance = _distance;
            }
        }
    }
}
