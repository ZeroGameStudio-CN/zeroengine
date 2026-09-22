using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
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
            var layers = new Dictionary<GameObject, int>();
            var graphics = new List<Graphic>();
            try
            {
                if (overlays != null)
                    foreach (var canvas in overlays)
                    {
                        if (canvas == null || !canvas.isActiveAndEnabled || !canvas.isRootCanvas ||
                            canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                        // Borrow an already visible layer, never widen the world's visibility.
                        // Only the supplied UI renderers/canvases participate; no scene search.
                        var renderers = canvas.GetComponentsInChildren<CanvasRenderer>(true);
                        if (renderers.Length > 0)
                        {
                            int layer = FindVisibleLayer(camera.cullingMask);
                            foreach (var nested in canvas.GetComponentsInChildren<Canvas>(true))
                                BorrowLayer(nested.gameObject, layer, layers);
                            foreach (var renderer in renderers)
                                BorrowLayer(renderer.gameObject, layer, layers);
                        }
                        states.Add(new CanvasState(canvas));
                        graphics.AddRange(canvas.GetComponentsInChildren<Graphic>(true));
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.01f, 1f);
                    }
                camera.targetTexture = _target;
                camera.aspect = (float)_target.width / _target.height;
                InvalidateGraphics(graphics);
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
                foreach (var pair in layers)
                    if (pair.Key != null) pair.Key.layer = pair.Value;
                if (camera != null)
                {
                    camera.targetTexture = previousTarget;
                    camera.aspect = previousAspect;
                }
                RenderTexture.active = previousActive;
                // Scale-sensitive geometry (for example SDF text) must also be rebuilt
                // for the normal Game View after the borrowed camera-space capture.
                InvalidateGraphics(graphics);
                if (graphics.Count > 0) Canvas.ForceUpdateCanvases();
            }
        }

        private static void InvalidateGraphics(List<Graphic> graphics)
        {
            // Equal logical RectTransform sizes can hide a change in pixel scale from
            // uGUI's layout invalidation. Explicitly rebuild only the supplied UI trees.
            foreach (var graphic in graphics)
                if (graphic != null) graphic.SetAllDirty();
        }

        private static int FindVisibleLayer(int mask)
        {
            for (int layer = 0; layer < 32; layer++)
                if ((mask & (1 << layer)) != 0) return layer;
            throw new InvalidOperationException(
                "Overlay capture requires at least one visible camera layer. " +
                "A zero culling mask cannot render UI without exposing excluded world content.");
        }

        private static void BorrowLayer(GameObject target, int layer, Dictionary<GameObject, int> layers)
        {
            if (target.layer == layer || layers.ContainsKey(target)) return;
            if (target.GetComponent<Renderer>() != null)
                throw new InvalidOperationException("Overlay UI and a world Renderer must not share a GameObject.");
            layers.Add(target, target.layer);
            target.layer = layer;
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
