using System;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Preview.Editor
{
    /// <summary>One owner for transition fencing and cleanup of registered previews.</summary>
    public sealed class PreviewEditorLifetime : IDisposable
    {
        private readonly PreviewController controller;
        private bool disposed;
        public bool IsTransitioning { get; private set; }

        public PreviewEditorLifetime(PreviewController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            IsTransitioning = EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying;
            EditorApplication.playModeStateChanged += OnPlay;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }

        private void OnPlay(PlayModeStateChange state)
        {
            IsTransitioning = state is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.ExitingPlayMode;
            if (state == PlayModeStateChange.ExitingPlayMode)
                Reset(PreviewResetReason.PlayModeExit);
        }

        private void BeforeReload()
        {
            IsTransitioning = true;
            Reset(PreviewResetReason.AssemblyReload);
        }

        private void Reset(PreviewResetReason reason)
        {
            try { controller.Reset(reason); }
            catch (Exception error) { Debug.LogException(error); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.playModeStateChanged -= OnPlay;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            controller.Dispose();
        }
    }
}
