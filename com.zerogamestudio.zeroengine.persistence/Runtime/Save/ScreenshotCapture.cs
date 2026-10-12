using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using ZeroEngine.Utils;

namespace ZeroEngine.Save
{
    /// <summary>
    /// 截图捕获组件 (v1.8.0 性能优化版)
    /// 用于存档预览截图
    ///
    /// 优化点:
    /// - GPU 缩放替代 CPU GetPixel (100x 提速)
    /// - 纹理复用，减少 GC 分配
    /// - Camera 缓存，避免每次查找
    /// </summary>
    public class ScreenshotCapture : MonoBehaviour
    {
        private int _width = 320;
        private int _height = 180;
        private int _quality = 75;
        private bool _initialized;

        // 复用纹理
        private RenderTexture _renderTexture;
        private RenderTexture _scaleRT;
        private Texture2D _captureTexture;
        private Texture2D _screenTexture;
        private RenderTexture _screenRT;
        private Texture2D _readbackTexture;
        private readonly WaitForEndOfFrame _endOfFrame = new WaitForEndOfFrame();
        private readonly System.Collections.Generic.Queue<Action<byte[]>> _callbacks =
            new System.Collections.Generic.Queue<Action<byte[]>>();
        private Coroutine _captureCoroutine;
        private bool _readbackPending;
        private bool _destroyed;
        private int _readbackQuality;

        // Camera 缓存
        private Camera _cachedCamera;

        /// <summary>
        /// 初始化截图配置
        /// </summary>
        public void Initialize(int width, int height, int quality)
        {
            _width = Mathf.Clamp(width, 64, 1920);
            _height = Mathf.Clamp(height, 64, 1080);
            _quality = Mathf.Clamp(quality, 1, 100);
            _initialized = true;

            // 预创建纹理
            if (!_readbackPending) EnsureTextures();
        }

        /// <summary>
        /// 刷新 Camera 缓存 (场景切换后调用)
        /// </summary>
        public void RefreshCameraCache()
        {
            _cachedCamera = Camera.main;
            if (_cachedCamera == null)
            {
                _cachedCamera = FindFirstObjectByType<Camera>();
            }
        }

        /// <summary>
        /// 异步捕获截图
        /// </summary>
        public void CaptureAsync(Action<byte[]> callback)
        {
            if (!_initialized)
            {
                callback?.Invoke(null);
                return;
            }

            if (_destroyed || !isActiveAndEnabled)
            {
                callback?.Invoke(null);
                return;
            }

            _callbacks.Enqueue(callback);
            if (_captureCoroutine == null && !_readbackPending)
                _captureCoroutine = StartCoroutine(CaptureCoroutine());
        }

        /// <summary>
        /// 同步捕获截图 (会阻塞一帧)
        /// </summary>
        public byte[] CaptureSync()
        {
            if (!_initialized || _destroyed) return null;

            var previousRT = RenderTexture.active;
            RenderTexture originalCameraRT = null;
            Camera camera = null;

            try
            {
                EnsureTextures();

                // 使用缓存的 Camera
                if (_cachedCamera == null)
                {
                    RefreshCameraCache();
                }

                if (_cachedCamera == null)
                {
                    ZeroLog.Warning(ZeroLog.Modules.Save, "No camera found.");
                    return null;
                }

                // 保存原始设置
                camera = _cachedCamera;
                originalCameraRT = camera.targetTexture;

                // 渲染到 RenderTexture
                _cachedCamera.targetTexture = _renderTexture;
                _cachedCamera.Render();

                // 读取像素
                RenderTexture.active = _renderTexture;
                _captureTexture.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                _captureTexture.Apply();

                // 恢复设置
                return _captureTexture.EncodeToJPG(_quality);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScreenshotCapture] Capture failed: {e.Message}");
                return null;
            }
            finally
            {
                if (camera != null) camera.targetTexture = originalCameraRT;
                RenderTexture.active = previousRT;
            }
        }

        private IEnumerator CaptureCoroutine()
        {
            while (_callbacks.Count > 0)
            {
                yield return _endOfFrame;

                bool asynchronous = false;
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    try
                    {
                        EnsureAsyncTextures();
                        int srcX, srcY, srcWidth, srcHeight;
                        CalculateCaptureRegion(out srcX, out srcY, out srcWidth, out srcHeight);
                        var previousRT = RenderTexture.active;
                        try
                        {
                            ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenRT);
                            var scale = new Vector2((float)srcWidth / Screen.width, (float)srcHeight / Screen.height);
                            var offset = new Vector2((float)srcX / Screen.width, (float)srcY / Screen.height);
                            // Readback rows must retain ReadPixels' bottom-up image convention.
                            if (SystemInfo.graphicsUVStartsAtTop)
                            {
                                scale.y = -scale.y;
                                offset.y = 1f - offset.y;
                            }
                            Graphics.Blit(_screenRT, _scaleRT, scale, offset);
                        }
                        finally { RenderTexture.active = previousRT; }

                        // Keep both render targets alive until the request completes.
                        _readbackQuality = _quality;
                        _readbackPending = true;
                        AsyncGPUReadback.Request(_scaleRT, 0, TextureFormat.RGBA32, CompleteReadback);
                        asynchronous = true;
                    }
                    catch (Exception e)
                    {
                        _readbackPending = false;
                        ZeroLog.Warning(ZeroLog.Modules.Save, $"Async screenshot unavailable: {e.Message}");
                    }
                }

                if (asynchronous)
                {
                    while (_readbackPending) yield return null;
                }
                else
                {
                    CompleteCapture(CaptureScreenSync());
                }
            }
            _captureCoroutine = null;
        }

        private void CompleteReadback(AsyncGPUReadbackRequest request)
        {
            byte[] result = null;
            try
            {
                if (!_destroyed && !request.hasError)
                {
                    _readbackTexture.LoadRawTextureData(request.GetData<byte>());
                    // Encoding uses CPU pixels; uploading them back to the GPU is unnecessary.
                    result = _readbackTexture.EncodeToJPG(_readbackQuality);
                }
                else if (!_destroyed)
                {
                    ZeroLog.Warning(ZeroLog.Modules.Save, "Async screenshot readback failed; keeping prior preview.");
                }
            }
            catch (Exception e)
            {
                ZeroLog.Warning(ZeroLog.Modules.Save, $"Async screenshot encoding failed: {e.Message}");
            }
            finally
            {
                _readbackPending = false;
                if (_destroyed) ReleaseAsyncTextures();
            }
            if (!_destroyed) CompleteCapture(result);
            if (!_destroyed)
            {
                if (!isActiveAndEnabled)
                    while (_callbacks.Count > 0) CompleteCapture(null);
                else if (_captureCoroutine == null && _callbacks.Count > 0)
                    _captureCoroutine = StartCoroutine(CaptureCoroutine());
            }
        }

        private void CompleteCapture(byte[] bytes)
        {
            if (_callbacks.Count == 0) return;
            var callback = _callbacks.Dequeue();
            try { callback?.Invoke(bytes); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private byte[] CaptureScreenSync()
        {
            var previousRT = RenderTexture.active;
            try
            {
                EnsureCaptureTexture();
                int srcX, srcY, srcWidth, srcHeight;
                CalculateCaptureRegion(out srcX, out srcY, out srcWidth, out srcHeight);
                EnsureScreenTexture(srcWidth, srcHeight);
                RenderTexture.active = null;
                _screenTexture.ReadPixels(new Rect(srcX, srcY, srcWidth, srcHeight), 0, 0);
                _screenTexture.Apply();
                ScaleTextureGPU(_screenTexture, _captureTexture);
                return _captureTexture.EncodeToJPG(_quality);
            }
            catch (Exception e)
            {
                ZeroLog.Warning(ZeroLog.Modules.Save, $"Screenshot capture failed: {e.Message}");
                return null;
            }
            finally { RenderTexture.active = previousRT; }
        }

        private void EnsureAsyncTextures()
        {
            if (_screenRT == null || _screenRT.width != Screen.width || _screenRT.height != Screen.height)
            {
                ReleaseRenderTexture(ref _screenRT);
                _screenRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _screenRT.Create();
            }
            if (_scaleRT == null || _scaleRT.width != _width || _scaleRT.height != _height)
            {
                ReleaseRenderTexture(ref _scaleRT);
                _scaleRT = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32);
                _scaleRT.filterMode = FilterMode.Bilinear;
                _scaleRT.Create();
            }
            if (_readbackTexture == null || _readbackTexture.width != _width || _readbackTexture.height != _height)
            {
                if (_readbackTexture != null) Destroy(_readbackTexture);
                _readbackTexture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            }
        }

        private void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        private void ReleaseAsyncTextures()
        {
            ReleaseRenderTexture(ref _screenRT);
            ReleaseRenderTexture(ref _scaleRT);
            if (_readbackTexture != null) Destroy(_readbackTexture);
            _readbackTexture = null;
        }

        private void CalculateCaptureRegion(out int srcX, out int srcY, out int srcWidth, out int srcHeight)
        {
            float screenAspect = (float)Screen.width / Screen.height;
            float targetAspect = (float)_width / _height;

            if (screenAspect > targetAspect)
            {
                // 屏幕更宽，从两侧裁剪
                srcHeight = Screen.height;
                srcWidth = Mathf.RoundToInt(srcHeight * targetAspect);
                srcX = (Screen.width - srcWidth) / 2;
                srcY = 0;
            }
            else
            {
                // 屏幕更高，从上下裁剪
                srcWidth = Screen.width;
                srcHeight = Mathf.RoundToInt(srcWidth / targetAspect);
                srcX = 0;
                srcY = (Screen.height - srcHeight) / 2;
            }
        }

        /// <summary>
        /// GPU 加速缩放 (使用 Graphics.Blit)
        /// </summary>
        private void ScaleTextureGPU(Texture2D source, Texture2D destination)
        {
            // 确保缩放用 RenderTexture 存在
            if (_scaleRT == null || _scaleRT.width != destination.width || _scaleRT.height != destination.height)
            {
                if (_scaleRT != null)
                {
                    _scaleRT.Release();
                    Destroy(_scaleRT);
                }
                _scaleRT = new RenderTexture(destination.width, destination.height, 0, RenderTextureFormat.ARGB32);
                _scaleRT.filterMode = FilterMode.Bilinear;
            }

            // GPU blit 带双线性过滤
            var prevRT = RenderTexture.active;
            Graphics.Blit(source, _scaleRT);

            // 读取结果
            RenderTexture.active = _scaleRT;
            destination.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
            destination.Apply();
            RenderTexture.active = prevRT;
        }

        private void EnsureTextures()
        {
            if (_renderTexture == null || _renderTexture.width != _width || _renderTexture.height != _height)
            {
                if (_renderTexture != null)
                {
                    _renderTexture.Release();
                    Destroy(_renderTexture);
                }
                _renderTexture = new RenderTexture(_width, _height, 24);
            }

            EnsureCaptureTexture();
        }

        private void EnsureCaptureTexture()
        {
            if (_captureTexture == null || _captureTexture.width != _width || _captureTexture.height != _height)
            {
                if (_captureTexture != null)
                {
                    Destroy(_captureTexture);
                }
                _captureTexture = new Texture2D(_width, _height, TextureFormat.RGB24, false);
            }
        }

        private void EnsureScreenTexture(int width, int height)
        {
            if (_screenTexture == null || _screenTexture.width != width || _screenTexture.height != height)
            {
                if (_screenTexture != null)
                {
                    Destroy(_screenTexture);
                }
                _screenTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            }
        }

        private void OnDestroy()
        {
            _destroyed = true;
            StopAllCoroutines();
            _captureCoroutine = null;
            while (_callbacks.Count > 0) CompleteCapture(null);
            // The native completion callback owns destruction of any in-flight target.
            if (!_readbackPending) ReleaseAsyncTextures();
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            if (_captureTexture != null)
            {
                Destroy(_captureTexture);
            }

            if (_screenTexture != null)
            {
                Destroy(_screenTexture);
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            _captureCoroutine = null;
            if (!_readbackPending)
                while (_callbacks.Count > 0) CompleteCapture(null);
        }
    }
}
