using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ZeroEngine.Cinematic.Tests
{
    internal static class TerminalReentryScenario
    {
        [Serializable]
        public sealed class Observation
        {
            public string mode, outer, nested, replayStart, replayEnd, message;
            public int exits, terminalCommands;
            public bool matchesContract, clockPlaying;
        }

        public static Observation Run(string mode)
        {
            GameObject root = null;
            TimelineAsset timeline = null;
            TrackAsset ownedTrack = null;
            CinematicSequenceDefinition first = null, second = null;
            CinematicSequenceCatalog catalog = null;
            try
            {
                root = new GameObject("Owned cinematic terminal probe") { hideFlags = HideFlags.HideAndDontSave };
                var director = root.AddComponent<PlayableDirector>();
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.hideFlags = HideFlags.HideAndDontSave;
                timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
                timeline.fixedDuration = 10d;
                ownedTrack = timeline.CreateTrack<ControlTrack>(null, "Owned clock");
                ownedTrack.hideFlags = HideFlags.HideAndDontSave;
                first = Sequence("cinematic.terminal.first", timeline);
                second = Sequence("cinematic.terminal.second", timeline);
                catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
                catalog.hideFlags = HideFlags.HideAndDontSave;
                Set(catalog, "_sequences", new[] { first, second });
                var callbacks = new CallbackService();
                var commands = new CallbackCommands();
                var services = new CinematicProjectPlaybackServices(new ICinematicProjectPlaybackService[] { callbacks });
                var adapter = new CinematicPlayableDirectorAdapter();
                var service = new CinematicPlaybackService(catalog, director, new CinematicBindingRegistry(), services, commands);
                var observed = new Observation { mode = mode, nested = CinematicPlayStatus.None.ToString() };
                bool invoked = false;
                Action cancel = () =>
                {
                    if (invoked) return;
                    invoked = true;
                    observed.nested = (mode == "adapter-exit-cancel"
                        ? adapter.Stop(director, CinematicPlayStatus.Cancelled)
                        : service.Cancel()).Status.ToString();
                };
                if (mode == "exit-replace")
                {
                    callbacks.OnExit = () =>
                    {
                        if (invoked) return;
                        invoked = true;
                        observed.nested = service.Play(CinematicPlayRequest.FromSequence(second,
                            "probe.reentrant", allowInterrupt: true)).Status.ToString();
                    };
                }
                else if (mode == "command-cancel")
                {
                    Set(first, "_commands", new[] { new CinematicCommandDefinition(CinematicCommandPhase.OnAbort, "probe.abort", "") });
                    commands.Callback = cancel;
                }
                else if (mode != "normal" && mode != "exit-failure") callbacks.OnExit = cancel;
                if (mode == "exit-failure") callbacks.OnExit = () => throw new InvalidOperationException("owned exit failure");

                var start = mode == "adapter-exit-cancel"
                    ? adapter.Play(CinematicPlayRequest.FromSequence(first), first, director, new CinematicBindingRegistry(), services)
                    : service.Play(first.SequenceId);
                if (start.Status != CinematicPlayStatus.Started) throw new InvalidOperationException("Owned clock did not start.");
                observed.clockPlaying = director.state == PlayState.Playing;
                CinematicPlayResult terminal;
                if (mode == "timeout")
                {
                    if (!observed.clockPlaying) throw new InvalidOperationException("Timeout probe needs an actually playing clock.");
                    terminal = service.EvaluateTimeout(2f);
                }
                else terminal = mode == "adapter-exit-cancel"
                    ? adapter.Stop(director, CinematicPlayStatus.Cancelled) : service.Cancel();
                observed.outer = terminal.Status.ToString();
                observed.message = terminal.Message;
                observed.exits = callbacks.Exits;
                observed.terminalCommands = commands.Count;
                callbacks.OnExit = null;
                commands.Callback = null;
                var replay = mode == "adapter-exit-cancel"
                    ? adapter.Play(CinematicPlayRequest.FromSequence(second), second, director, new CinematicBindingRegistry(), services)
                    : service.Play(second.SequenceId);
                observed.replayStart = replay.Status.ToString();
                observed.replayEnd = (mode == "adapter-exit-cancel"
                    ? adapter.Stop(director, CinematicPlayStatus.Cancelled) : service.Cancel()).Status.ToString();
                var expectedOuter = mode == "timeout" ? CinematicPlayStatus.TimedOut
                    : mode == "exit-failure" ? CinematicPlayStatus.Failed : CinematicPlayStatus.Cancelled;
                var expectedNested = mode == "exit-replace" ? CinematicPlayStatus.AlreadyPlaying : CinematicPlayStatus.None;
                observed.matchesContract = observed.exits == 1
                    && observed.outer == expectedOuter.ToString()
                    && observed.nested == expectedNested.ToString()
                    && observed.terminalCommands == (mode == "command-cancel" ? 1 : 0)
                    && observed.replayStart == CinematicPlayStatus.Started.ToString()
                    && observed.replayEnd == CinematicPlayStatus.Cancelled.ToString();
                return observed;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (catalog != null) UnityEngine.Object.DestroyImmediate(catalog);
                if (first != null) UnityEngine.Object.DestroyImmediate(first);
                if (second != null) UnityEngine.Object.DestroyImmediate(second);
                if (timeline != null && ownedTrack != null) timeline.DeleteTrack(ownedTrack);
                if (ownedTrack != null) UnityEngine.Object.DestroyImmediate(ownedTrack);
                if (timeline != null) UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        private static CinematicSequenceDefinition Sequence(string id, TimelineAsset timeline)
        {
            var result = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            result.hideFlags = HideFlags.HideAndDontSave;
            Set(result, "_sequenceId", id);
            Set(result, "_timelineAsset", timeline);
            Set(result, "_maxPlaybackSeconds", 1f);
            return result;
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private sealed class CallbackService : ICinematicProjectPlaybackService
        {
            public int Exits;
            public Action OnExit;
            public void EnterCinematic(CinematicPlaybackContext context) { }
            public void ExitCinematic(CinematicPlaybackContext context, CinematicPlayResult result)
            {
                Exits++;
                OnExit?.Invoke();
            }
        }

        private sealed class CallbackCommands : ICinematicCommandExecutor
        {
            public int Count;
            public Action Callback;
            public CinematicCommandResult Execute(CinematicCommandDefinition command, CinematicPlaybackContext context)
            {
                Count++;
                Callback?.Invoke();
                return CinematicCommandResult.Success();
            }
        }
    }
}
