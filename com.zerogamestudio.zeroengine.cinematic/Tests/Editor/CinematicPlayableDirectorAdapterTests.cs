using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicPlayableDirectorAdapterTests
    {
        [Test]
        public void MissingSequence_ReturnsSequenceMissingAndDoesNotPlay()
        {
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(null, director, new CinematicBindingRegistry());

            Assert.AreEqual(CinematicPlayStatus.SequenceMissing, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            Object.DestroyImmediate(directorObject);
        }

        [Test]
        public void Play_WhenRequestSequenceIdDiffersFromSequence_ReturnsFailedWithoutStartingPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.sequence");
            SetObject(sequence, "_timelineAsset", timeline);
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events)
                });
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(
                CinematicPlayRequest.ForSequence("cinematic.request"),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services);

            Assert.AreEqual(CinematicPlayStatus.Failed, result.Status);
            Assert.AreEqual("cinematic.request", result.SequenceId);
            Assert.IsNull(director.playableAsset);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            Assert.IsEmpty(events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void MissingBinding_ReturnsBindingMissingAndDoesNotStartPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var track = timeline.CreateTrack<ActivationTrack>(null, "actor.storyteller");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(sequence, new[] { new BindingRequirementSetup("actor.storyteller", track) });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(sequence, director, new CinematicBindingRegistry());

            Assert.AreEqual(CinematicPlayStatus.BindingMissing, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void RegisteredBinding_AppliesBindingAndStartsPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var track = timeline.CreateTrack<ActivationTrack>(null, "actor.storyteller");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(sequence, new[] { new BindingRequirementSetup("actor.storyteller", track) });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var binding = new GameObject("Storyteller");
            var registry = new CinematicBindingRegistry();
            registry.Register("actor.storyteller", binding);
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(sequence, director, registry);

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            Assert.AreSame(timeline, director.playableAsset);
            Assert.AreSame(binding, director.GetGenericBinding(track));
            director.Stop();
            Object.DestroyImmediate(binding);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Stop_WhenNoActivePlayback_ReturnsNoneAndDoesNotStopUnownedDirector()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.Play();
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Stop(director, CinematicPlayStatus.Completed);

            Assert.AreEqual(CinematicPlayStatus.None, result.Status);
            Assert.AreEqual(PlayState.Playing, director.state);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_InvokesLifecycleServicesInOrderBeforeReportingStarted()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events),
                    new RecordingPlaybackService("camera", events)
                });
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services);

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test", "enter:camera:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_ExecutesStartCommandsAfterLifecycleEnterBeforePlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnStart, "fact.set", "intro") });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events)
                });
            var commands = new RecordingCommandExecutor(events);
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services,
                commands);

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "command:OnStart:fact.set:intro" },
                events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_NormalizesCommandIdBeforeExecution()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnStart, "  fact.set  ", "intro") });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var commands = new RecordingCommandExecutor(events);
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                CinematicProjectPlaybackServices.None,
                commands);

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            CollectionAssert.AreEqual(
                new[] { "command:OnStart:fact.set:intro" },
                events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_WhenStartCommandFails_ExitsLifecycleAndDoesNotPlay()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnStart, "fact.set", "intro") });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events)
                });
            var commands = new RecordingCommandExecutor(events, "fact.set");
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services,
                commands);

            Assert.AreEqual(CinematicPlayStatus.Failed, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "command:OnStart:fact.set:intro",
                    "exit:input:Failed"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_WhenStartCommandThrows_RunsAbortCommandsAndExitsLifecycle()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(
                sequence,
                new[]
                {
                    new CommandSetup(CinematicCommandPhase.OnStart, "fact.set", "intro"),
                    new CommandSetup(CinematicCommandPhase.OnAbort, "fact.clear", "intro")
                });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events)
                });
            var commands = new ThrowingCommandExecutor(events, "fact.set");
            var adapter = new CinematicPlayableDirectorAdapter();
            CinematicPlayResult result = default;

            Assert.DoesNotThrow(
                () => result = adapter.Play(
                    CinematicPlayRequest.FromSequence(sequence),
                    sequence,
                    director,
                    new CinematicBindingRegistry(),
                    services,
                    commands));
            Assert.AreEqual(CinematicPlayStatus.Failed, result.Status);
            Assert.IsTrue(result.RequiresAbortCleanup);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "command:OnStart:fact.set:intro",
                    "command:OnAbort:fact.clear:intro",
                    "exit:input:Failed"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_WhenLifecycleEnterFails_ReturnsFailedAndExitsEnteredServices()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events),
                    new ThrowingEnterPlaybackService("camera", events)
                });
            var adapter = new CinematicPlayableDirectorAdapter();
            CinematicPlayResult result = default;

            Assert.DoesNotThrow(
                () => result = adapter.Play(
                    CinematicPlayRequest.FromSequence(sequence),
                    sequence,
                    director,
                    new CinematicBindingRegistry(),
                    services));
            Assert.AreEqual(CinematicPlayStatus.Failed, result.Status);
            Assert.IsTrue(result.RequiresAbortCleanup);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "enter:camera:cinematic.test",
                    "exit:input:Failed"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_WhenLifecycleEnterFails_DoesNotMutateDirectorAssetOrBindings()
        {
            var previousTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var previousTrack = previousTimeline.CreateTrack<ActivationTrack>(null, "previous.actor");
            var nextTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var nextTrack = nextTimeline.CreateTrack<ActivationTrack>(null, "next.actor");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", nextTimeline);
            SetBindingRequirements(sequence, new[] { new BindingRequirementSetup("actor.storyteller", nextTrack) });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var previousBinding = new GameObject("PreviousBinding");
            var nextBinding = new GameObject("NextBinding");
            director.playableAsset = previousTimeline;
            director.SetGenericBinding(previousTrack, previousBinding);
            var registry = new CinematicBindingRegistry();
            registry.Register("actor.storyteller", nextBinding);
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new ThrowingEnterPlaybackService("camera", events)
                });
            var adapter = new CinematicPlayableDirectorAdapter();

            var result = adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                registry,
                services);

            Assert.AreEqual(CinematicPlayStatus.Failed, result.Status);
            Assert.AreSame(previousTimeline, director.playableAsset);
            Assert.AreSame(previousBinding, director.GetGenericBinding(previousTrack));
            Assert.IsNull(director.GetGenericBinding(nextTrack));
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(new[] { "enter:camera:cinematic.test" }, events);
            Object.DestroyImmediate(nextBinding);
            Object.DestroyImmediate(previousBinding);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(nextTimeline);
            Object.DestroyImmediate(previousTimeline);
        }

        [Test]
        public void Stop_StopsDirectorAndInvokesLifecycleServicesInReverseOrder()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnComplete, "fact.set", "done") });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events),
                    new RecordingPlaybackService("camera", events)
                });
            var commands = new RecordingCommandExecutor(events);
            var adapter = new CinematicPlayableDirectorAdapter();
            adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services,
                commands);

            var result = adapter.Stop(director, CinematicPlayStatus.Completed);

            Assert.AreEqual(CinematicPlayStatus.Completed, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "enter:camera:cinematic.test",
                    "command:OnComplete:fact.set:done",
                    "exit:camera:Completed",
                    "exit:input:Completed"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Stop_WhenSkippedWithoutSkippedCommands_RunsCompleteCommands()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnComplete, "fact.set", "done") });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events)
                });
            var commands = new RecordingCommandExecutor(events);
            var adapter = new CinematicPlayableDirectorAdapter();
            adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services,
                commands);

            var result = adapter.Stop(director, CinematicPlayStatus.SkippedCompleted);

            Assert.AreEqual(CinematicPlayStatus.SkippedCompleted, result.Status);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "command:OnComplete:fact.set:done",
                    "exit:input:SkippedCompleted"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Stop_WhenCompleteCommandFails_RunsAbortCommandsBeforeLifecycleExit()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(
                sequence,
                new[]
                {
                    new CommandSetup(CinematicCommandPhase.OnComplete, "fact.set", "done"),
                    new CommandSetup(CinematicCommandPhase.OnAbort, "fact.clear", "done")
                });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events)
                });
            var commands = new RecordingCommandExecutor(events, "fact.set");
            var adapter = new CinematicPlayableDirectorAdapter();
            adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services,
                commands);

            var result = adapter.Stop(director, CinematicPlayStatus.Completed);

            Assert.AreEqual(CinematicPlayStatus.Failed, result.Status);
            Assert.IsTrue(result.RequiresAbortCleanup);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "command:OnComplete:fact.set:done",
                    "command:OnAbort:fact.clear:done",
                    "exit:input:Failed"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void EvaluateTimeout_WhenWatchdogTrips_StopsDirectorAndRequestsAbortCleanup()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_maxPlaybackSeconds", 0.1f);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnAbort, "fact.clear", "intro") });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var services = new CinematicProjectPlaybackServices(
                new ICinematicProjectPlaybackService[]
                {
                    new RecordingPlaybackService("input", events)
                });
            var commands = new RecordingCommandExecutor(events);
            var adapter = new CinematicPlayableDirectorAdapter();
            adapter.Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                new CinematicBindingRegistry(),
                services,
                commands);

            var result = adapter.EvaluateTimeout(director, 0.25f);

            Assert.AreEqual(CinematicPlayStatus.TimedOut, result.Status);
            Assert.IsTrue(result.RequiresAbortCleanup);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "command:OnAbort:fact.clear:intro",
                    "exit:input:TimedOut"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        private readonly struct BindingRequirementSetup
        {
            public BindingRequirementSetup(string bindingKey, TrackAsset track)
            {
                BindingKey = bindingKey;
                Track = track;
            }

            public string BindingKey { get; }

            public TrackAsset Track { get; }
        }

        private readonly struct CommandSetup
        {
            public CommandSetup(CinematicCommandPhase phase, string commandId, string payload)
            {
                Phase = phase;
                CommandId = commandId;
                Payload = payload;
            }

            public CinematicCommandPhase Phase { get; }

            public string CommandId { get; }

            public string Payload { get; }
        }

        private static void SetObject(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBindingRequirements(CinematicSequenceDefinition sequence, BindingRequirementSetup[] values)
        {
            var serialized = new SerializedObject(sequence);
            var array = serialized.FindProperty("_bindingRequirements");
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_bindingKey").stringValue = values[i].BindingKey;
                element.FindPropertyRelative("_track").objectReferenceValue = values[i].Track;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetCommands(CinematicSequenceDefinition sequence, CommandSetup[] values)
        {
            var serialized = new SerializedObject(sequence);
            var array = serialized.FindProperty("_commands");
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_phase").enumValueIndex = (int)values[i].Phase;
                element.FindPropertyRelative("_commandId").stringValue = values[i].CommandId;
                element.FindPropertyRelative("_payload").stringValue = values[i].Payload;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class RecordingPlaybackService : ICinematicProjectPlaybackService
        {
            private readonly string _name;
            private readonly List<string> _events;

            public RecordingPlaybackService(string name, List<string> events)
            {
                _name = name;
                _events = events;
            }

            public void EnterCinematic(CinematicPlaybackContext context)
            {
                _events.Add($"enter:{_name}:{context.Request.SequenceId}");
            }

            public void ExitCinematic(CinematicPlaybackContext context, CinematicPlayResult result)
            {
                _events.Add($"exit:{_name}:{result.Status}");
            }
        }

        private sealed class ThrowingEnterPlaybackService : ICinematicProjectPlaybackService
        {
            private readonly string _name;
            private readonly List<string> _events;

            public ThrowingEnterPlaybackService(string name, List<string> events)
            {
                _name = name;
                _events = events;
            }

            public void EnterCinematic(CinematicPlaybackContext context)
            {
                _events.Add($"enter:{_name}:{context.Request.SequenceId}");
                throw new System.InvalidOperationException($"{_name} enter failed.");
            }

            public void ExitCinematic(CinematicPlaybackContext context, CinematicPlayResult result)
            {
                _events.Add($"exit:{_name}:{result.Status}");
            }
        }

        private sealed class RecordingCommandExecutor : ICinematicCommandExecutor
        {
            private readonly List<string> _events;
            private readonly string _failingCommandId;

            public RecordingCommandExecutor(List<string> events, string failingCommandId = "")
            {
                _events = events;
                _failingCommandId = failingCommandId;
            }

            public CinematicCommandResult Execute(
                CinematicCommandDefinition command,
                CinematicPlaybackContext context)
            {
                _events.Add($"command:{command.Phase}:{command.CommandId}:{command.Payload}");
                if (command.CommandId == _failingCommandId)
                {
                    return CinematicCommandResult.Fail($"Command '{command.CommandId}' failed.");
                }

                return CinematicCommandResult.Success();
            }
        }

        private sealed class ThrowingCommandExecutor : ICinematicCommandExecutor
        {
            private readonly List<string> _events;
            private readonly string _throwingCommandId;

            public ThrowingCommandExecutor(List<string> events, string throwingCommandId)
            {
                _events = events;
                _throwingCommandId = throwingCommandId;
            }

            public CinematicCommandResult Execute(
                CinematicCommandDefinition command,
                CinematicPlaybackContext context)
            {
                _events.Add($"command:{command.Phase}:{command.CommandId}:{command.Payload}");
                if (command.CommandId == _throwingCommandId)
                {
                    throw new System.InvalidOperationException($"Command '{command.CommandId}' threw.");
                }

                return CinematicCommandResult.Success();
            }
        }
    }
}
