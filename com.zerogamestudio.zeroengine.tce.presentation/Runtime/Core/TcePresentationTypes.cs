using System;
using UnityEngine;
using ZeroEngine.TCE;

namespace ZeroEngine.TCE.Presentation
{
    public enum TcePresentationStyle
    {
        StaticSnapshot = 0,
        MeshSnapshot = 1,
        LayeredSpriteSnapshot = 2,
        SoulGhost = 3
    }

    public readonly struct TceVisualSnapshotRequest
    {
        public TceVisualSnapshotRequest(Vector3 offset, Vector3 direction, bool copyMainTexture)
        {
            Offset = offset;
            Direction = direction;
            CopyMainTexture = copyMainTexture;
        }

        public Vector3 Offset { get; }
        public Vector3 Direction { get; }
        public bool CopyMainTexture { get; }
    }

    [Serializable]
    public sealed class TcePresentationPlaybackSettings
    {
        [TceFieldDoc("Presentation style used to play the captured snapshot.")]
        public TcePresentationStyle Style = TcePresentationStyle.StaticSnapshot;

        [TceFieldDoc("Tint color applied to the visual snapshot.")]
        public Color Tint = new(0.55f, 0.85f, 1f, 0.55f);

        [TceFieldDoc("Duration in seconds measured by the supplied TCE clock.")]
        public float Duration = 0.35f;

        [TceFieldDoc("Delay before alpha fade starts, measured in seconds by the supplied TCE clock.")]
        public float FadeDelay;

        [TceFieldDoc("Alpha fade duration in seconds. Values less than or equal to zero use the remaining playback duration.")]
        public float FadeDuration;

        [TceFieldDoc("Optional alpha multiplier curve evaluated over normalized fade progress.")]
        public AnimationCurve AlphaEase;

        [TceFieldDoc("Optional material used by sprite and mesh playback instead of the package fallback material.")]
        public Material MaterialOverride;

        [TceFieldDoc("Shader property name used for renderer tint playback.")]
        public string TintPropertyName = "_Color";

        [TceFieldDoc("Shader property name used for mesh main texture playback.")]
        public string MainTexturePropertyName = "_MainTex";

        [TceFieldDoc("Whether sprite playback should also write SpriteRenderer.color in addition to the tint shader property.")]
        public bool ApplySpriteRendererColor = true;

        [TceFieldDoc("Whether sprite playback should override captured sorting values.")]
        public bool OverrideSorting;

        [TceFieldDoc("Sorting layer id used when OverrideSorting is true.")]
        public int SortingLayerId;

        [TceFieldDoc("Sorting order used when OverrideSorting is true.")]
        public int SortingOrder;

        [TceFieldDoc("World-space offset applied during capture or playback.")]
        public Vector3 Offset;

        [TceFieldDoc("World-space direction used by soul ghost motion.")]
        public Vector3 Direction = Vector3.right;

        [TceFieldDoc("Whether playback should copy the source main texture when available.")]
        public bool CopyMainTexture = true;
    }

    public interface ITcePresentationSource
    {
        bool TryCaptureSnapshot(TceVisualSnapshotRequest request, out TceVisualSnapshot snapshot);
    }

    public interface ITcePresentationPlayer
    {
        TcePresentationHandle Play(TceVisualSnapshot snapshot, TcePresentationPlaybackSettings settings, ITceClock clock);
    }

    public sealed class TcePresentationHandle : IDisposable
    {
        private Action dispose;

        public TcePresentationHandle(Action dispose)
        {
            this.dispose = dispose;
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            dispose?.Invoke();
            dispose = null;
        }
    }
}
