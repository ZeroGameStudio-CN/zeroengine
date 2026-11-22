using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroEngine.GameFeel.Unity
{
    /// <summary>A render-only camera impulse compatible with cameras whose poses are owned by another system.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraImpactShake : MonoBehaviour
    {
        private Camera view;
        private double started;
        private float duration, amplitude;
        private Vector2 screenDirection;
        private Vector3 home, applied;
        private bool borrowing;

        public bool IsPlaying => duration > 0f && Time.unscaledTimeAsDouble - started < duration;

        public bool Play(Vector2 direction, float worldAmplitude, float seconds)
        {
            if (!isActiveAndEnabled || !float.IsFinite(seconds) || seconds <= 0f ||
                !float.IsFinite(worldAmplitude) || worldAmplitude <= 0f ||
                !float.IsFinite(direction.x) || !float.IsFinite(direction.y)) return false;
            if (IsPlaying && worldAmplitude < amplitude) return false;

            started = Time.unscaledTimeAsDouble;
            duration = seconds;
            amplitude = worldAmplitude;
            screenDirection = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
            return true;
        }

        public void Stop()
        {
            duration = 0f;
            RestorePose();
        }

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeginSrp;
            RenderPipelineManager.endCameraRendering += EndSrp;
            Camera.onPreCull += BeginBuiltIn;
            Camera.onPostRender += EndBuiltIn;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginSrp;
            RenderPipelineManager.endCameraRendering -= EndSrp;
            Camera.onPreCull -= BeginBuiltIn;
            Camera.onPostRender -= EndBuiltIn;
            Stop();
        }

        private void BeginSrp(ScriptableRenderContext context, Camera camera) => BeginRender(camera);
        private void EndSrp(ScriptableRenderContext context, Camera camera) => EndRender(camera);
        private void BeginBuiltIn(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) BeginRender(camera);
        }

        private void EndBuiltIn(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) EndRender(camera);
        }

        private void BeginRender(Camera camera)
        {
            if (camera != view) return;
            RestorePose();
            if (!IsPlaying) return;
            float progress = (float)((Time.unscaledTimeAsDouble - started) / duration);
            float amount = amplitude * ImpactEnvelopes.CameraImpulse(progress, 1.6f);
            var pose = view.transform;
            home = pose.position;
            applied = home + (pose.right * screenDirection.x + pose.up * screenDirection.y) * amount;
            pose.position = applied;
            borrowing = true;
        }

        private void EndRender(Camera camera)
        {
            if (camera == view) RestorePose();
        }

        private void RestorePose()
        {
            if (!borrowing) return;
            borrowing = false;
            if (view && (view.transform.position - applied).sqrMagnitude < .0000001f)
                view.transform.position = home;
        }
    }
}
