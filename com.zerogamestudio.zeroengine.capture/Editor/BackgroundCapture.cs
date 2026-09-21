using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ZeroEngine.Capture
{
    /// <summary>One bounded Editor PlayMode session. Call only on Unity's main thread.</summary>
    public static class BackgroundCapture
    {
        public static CaptureSession Current { get; private set; }

        /// <summary>Capture a game camera once, without starting a timed recording or changing time.</summary>
        public static string Screenshot(CaptureOptions options, Camera camera, Canvas[] overlayCanvases = null)
        {
            if (Current != null) throw new InvalidOperationException("Finish the active recording before taking a separate screenshot.");
            var validated = (options ?? throw new ArgumentNullException(nameof(options))).ValidatedCopy();
            ValidateOutputRoot(validated.OutputRoot);
            var directory = Path.Combine(validated.OutputRoot, "screenshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, validated.Label + ".png");
            using (var writer = new CameraFrameWriter(validated.Width, validated.Height))
                writer.Write(camera, overlayCanvases, path);
            return path;
        }

        public static CaptureSession Start(CaptureOptions options, Func<Camera> camera,
            Func<Canvas[]> overlayCanvases = null)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Capture requires running, unpaused Editor PlayMode.");
            if (Current != null) throw new InvalidOperationException("A capture session is already active.");
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var validated = (options ?? throw new ArgumentNullException(nameof(options))).ValidatedCopy();
            ValidateOutputRoot(validated.OutputRoot);
            var session = new CaptureSession(validated, camera, overlayCanvases);
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                if (!Append(ref loop)) throw new InvalidOperationException("PostLateUpdate is unavailable.");
                Current = session;
                PlayerLoop.SetPlayerLoop(loop);
                AssemblyReloadEvents.beforeAssemblyReload += CancelForReload;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                EditorApplication.quitting += CancelForReload;
                return session;
            }
            catch
            {
                session.Cancel("start-failed");
                throw;
            }
        }

        private static void ValidateOutputRoot(string outputRoot)
        {
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var root = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(root, project, StringComparison.OrdinalIgnoreCase) ||
                root.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture output must be outside the Unity project.");
        }

        private static void Tick() => Current?.Tick();
        private static void CancelForReload() => Current?.Cancel("editor-reload-or-quit");
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) Current?.Cancel("playmode-exit");
        }

        internal static void Detach(CaptureSession session)
        {
            if (Current != session) return;
            Current = null;
            AssemblyReloadEvents.beforeAssemblyReload -= CancelForReload;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.quitting -= CancelForReload;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool Append(ref PlayerLoopSystem loop)
        {
            if (loop.type == typeof(PostLateUpdate))
            {
                var children = new List<PlayerLoopSystem>(loop.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                children.Add(new PlayerLoopSystem { type = typeof(BackgroundCapture), updateDelegate = Tick });
                loop.subSystemList = children.ToArray();
                return true;
            }
            if (loop.subSystemList == null) return false;
            for (var i = 0; i < loop.subSystemList.Length; i++)
                if (Append(ref loop.subSystemList[i])) return true;
            return false;
        }

        private static void Remove(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return;
            var children = new List<PlayerLoopSystem>();
            foreach (var original in loop.subSystemList)
            {
                if (original.type == typeof(BackgroundCapture)) continue;
                var child = original;
                Remove(ref child);
                children.Add(child);
            }
            loop.subSystemList = children.ToArray();
        }
    }
}
