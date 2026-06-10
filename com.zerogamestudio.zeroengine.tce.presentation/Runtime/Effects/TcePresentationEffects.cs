using System;
using UnityEngine;
using ZeroEngine.TCE;

namespace ZeroEngine.TCE.Presentation
{
    public interface ITcePresentationEffectSettings
    {
        TcePresentationPlaybackSettings Settings { get; }
    }

    public abstract class TcePresentationEffect<TData> : TceEffect<TData>
        where TData : TceEffectData, ITcePresentationEffectSettings
    {
        public override void Execute(ITceActor target, object source)
        {
            ITceActor resolved = ResolveTarget(target, source);
            ITcePresentationSource snapshotSource = ResolvePresentationSource(resolved, source);
            if (snapshotSource == null)
                return;

            TcePresentationPlaybackSettings settings = Data.Settings ?? new TcePresentationPlaybackSettings();
            var request = new TceVisualSnapshotRequest(settings.Offset, settings.Direction, settings.CopyMainTexture);
            if (!snapshotSource.TryCaptureSnapshot(request, out TceVisualSnapshot snapshot))
                return;

            new TcePresentationPlayer().Play(snapshot, settings, Context.Clock);
        }

        private static ITcePresentationSource ResolvePresentationSource(ITceActor resolved, object source)
        {
            if (TryResolvePresentationSource(resolved, out ITcePresentationSource snapshotSource))
                return snapshotSource;

            if (TryResolvePresentationSource(resolved?.NativeObject, out snapshotSource))
                return snapshotSource;

            if (TryResolvePresentationSource(source, out snapshotSource))
                return snapshotSource;

            if (source is ITceActor sourceActor && TryResolvePresentationSource(sourceActor.NativeObject, out snapshotSource))
                return snapshotSource;

            return null;
        }

        private static bool TryResolvePresentationSource(object candidate, out ITcePresentationSource snapshotSource)
        {
            snapshotSource = null;

            switch (candidate)
            {
                case ITcePresentationSource source:
                    snapshotSource = source;
                    return true;
                case GameObject gameObject when gameObject:
                    snapshotSource = new TceRendererSnapshotSource(gameObject);
                    return true;
                case Component component when component:
                    snapshotSource = new TceRendererSnapshotSource(component.gameObject);
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class SpawnSnapshotEffect : TcePresentationEffect<SpawnSnapshotEffectData>
    {
    }

    [Serializable]
    [TceComponentDoc(
        TceComponentDocCategory.Effect,
        "zeroengine.tce.presentation.effect.spawn_snapshot",
        "Spawn Snapshot",
        "Captures and plays a visual-only snapshot of the resolved target.",
        "Use this effect for generic afterimages and visual echoes. It never applies gameplay damage, spawns projectiles, changes stats, or dispatches gameplay events.")]
    public sealed class SpawnSnapshotEffectData : TceEffectData<SpawnSnapshotEffect>, ITcePresentationEffectSettings
    {
        [TceFieldDoc("Visual playback settings used by this presentation effect.")]
        public TcePresentationPlaybackSettings Settings = new();

        TcePresentationPlaybackSettings ITcePresentationEffectSettings.Settings => Settings;
    }

    public sealed class SpawnSoulGhostEffect : TcePresentationEffect<SpawnSoulGhostEffectData>
    {
    }

    [Serializable]
    [TceComponentDoc(
        TceComponentDocCategory.Effect,
        "zeroengine.tce.presentation.effect.spawn_soul_ghost",
        "Spawn Soul Ghost",
        "Captures and plays a soul-ghost style visual snapshot of the resolved target.",
        "Use this effect for hit ghosts and stylized mesh echoes. The effect is visual-only and keeps gameplay replay in project adapters.")]
    public sealed class SpawnSoulGhostEffectData : TceEffectData<SpawnSoulGhostEffect>, ITcePresentationEffectSettings
    {
        public SpawnSoulGhostEffectData()
        {
            Settings.Style = TcePresentationStyle.SoulGhost;
        }

        [TceFieldDoc("Visual playback settings used by this soul ghost presentation effect.")]
        public TcePresentationPlaybackSettings Settings = new();

        TcePresentationPlaybackSettings ITcePresentationEffectSettings.Settings => Settings;
    }
}
