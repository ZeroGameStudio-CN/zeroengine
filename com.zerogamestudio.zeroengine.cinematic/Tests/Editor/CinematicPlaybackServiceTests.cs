using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicPlaybackServiceTests
    {
        [Test]
        public void Play_MissingSequence_ReturnsMissingWithoutEnteringLifecycle()
        {
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);

            var result = service.Play("cinematic.missing");

            Assert.AreEqual(CinematicPlayStatus.SequenceMissing, result.Status);
            Assert.AreEqual("cinematic.missing", result.SequenceId);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            Assert.IsEmpty(events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void Play_ResolvedSequence_StartsDirectorAndEntersLifecycle()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);

            var result = service.Play("cinematic.test");

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            Assert.AreEqual("cinematic.test", result.SequenceId);
            Assert.AreSame(timeline, director.playableAsset);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_WhileActiveMissingSequence_ReturnsAlreadyPlayingAndKeepsCurrentPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Play("cinematic.missing");

            Assert.AreEqual(CinematicPlayStatus.AlreadyPlaying, result.Status);
            Assert.AreSame(timeline, director.playableAsset);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_AllowInterruptMissingSequence_ReturnsMissingAndKeepsCurrentPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Play(
                CinematicPlayRequest.ForSequence(
                    "cinematic.missing",
                    allowInterrupt: true));

            Assert.AreEqual(CinematicPlayStatus.SequenceMissing, result.Status);
            Assert.AreEqual("cinematic.missing", result.SequenceId);
            Assert.AreSame(timeline, director.playableAsset);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_AllowInterruptResolvedSequence_CancelsActiveBeforeStartingNext()
        {
            var firstTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var secondTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var firstSequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            var secondSequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(firstSequence, "_sequenceId", "cinematic.first");
            SetObject(firstSequence, "_timelineAsset", firstTimeline);
            SetString(secondSequence, "_sequenceId", "cinematic.second");
            SetObject(secondSequence, "_timelineAsset", secondTimeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { firstSequence, secondSequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.first");

            var result = service.Play(
                CinematicPlayRequest.FromSequence(
                    secondSequence,
                    allowInterrupt: true));

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            Assert.AreEqual("cinematic.second", result.SequenceId);
            Assert.AreSame(secondTimeline, director.playableAsset);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.first",
                    "exit:input:Cancelled",
                    "enter:input:cinematic.second"
                },
                events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(firstSequence);
            Object.DestroyImmediate(secondSequence);
            Object.DestroyImmediate(firstTimeline);
            Object.DestroyImmediate(secondTimeline);
        }

        [Test]
        public void Play_CustomResolver_StartsDirectorWithoutConcreteCatalog()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var resolver = new SingleSequenceResolver(sequence);
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                resolver,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);

            var result = service.Play("cinematic.test");

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            Assert.AreEqual("cinematic.test", resolver.LastRequestedSequenceId);
            Assert.AreSame(timeline, director.playableAsset);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_RequestMetadata_ReachesProjectLifecycleServices()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RequestMetadataPlaybackService(events)
                    }),
                null);
            var request = CinematicPlayRequest.FromSequence(
                sequence,
                "npc.storyteller",
                allowInterrupt: true);

            var result = service.Play(request);

            Assert.AreEqual(CinematicPlayStatus.Started, result.Status);
            CollectionAssert.AreEqual(
                new[] { "enter:cinematic.test:npc.storyteller:True" },
                events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Play_RequestById_UsesResolvedSequencePolicies()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.Disabled);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);

            var playResult = service.Play(CinematicPlayRequest.ForSequence("cinematic.test", "npc.storyteller"));
            var skipResult = service.Skip(10f);

            Assert.AreEqual(CinematicPlayStatus.Started, playResult.Status);
            Assert.AreEqual(CinematicPlayStatus.SkipNotAllowed, skipResult.Status);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Stop_Completed_StopsDirectorAndExitsLifecycle()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Stop(CinematicPlayStatus.Completed);

            Assert.AreEqual(CinematicPlayStatus.Completed, result.Status);
            Assert.AreEqual("cinematic.test", result.SequenceId);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:Completed" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Stop_WhenLifecycleExitThrows_StillExitsRemainingServicesAndClearsPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events),
                        new ThrowingExitPlaybackService("camera", events)
                    }),
                null);
            service.Play("cinematic.test");
            CinematicPlayResult stopResult = default;

            Assert.DoesNotThrow(() => stopResult = service.Stop(CinematicPlayStatus.Completed));
            Assert.AreEqual(CinematicPlayStatus.Failed, stopResult.Status);
            Assert.IsTrue(stopResult.RequiresAbortCleanup);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "enter:camera:cinematic.test",
                    "exit:camera:Completed",
                    "exit:input:Failed"
                },
                events);

            events.Clear();
            var replayResult = service.Play("cinematic.test");

            Assert.AreEqual(CinematicPlayStatus.Started, replayResult.Status);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "enter:camera:cinematic.test" },
                events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Cancel_ActivePlayback_StopsDirectorRunsAbortCommandsAndExitsLifecycle()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnAbort, "fact.clear", "intro") });
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                new RecordingCommandExecutor(events));
            service.Play("cinematic.test");

            var result = service.Cancel();

            Assert.AreEqual(CinematicPlayStatus.Cancelled, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "command:OnAbort:fact.clear:intro",
                    "exit:input:Cancelled"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Abort_ActivePlayback_StopsDirectorAndExitsLifecycleAsAborted()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Abort();

            Assert.AreEqual(CinematicPlayStatus.Aborted, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:Aborted" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void EvaluateTimeout_WhenDirectorStillPlaying_StopsAndReturnsTimedOut()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_maxPlaybackSeconds", 0.1f);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.EvaluateTimeout(0.25f);

            Assert.AreEqual(CinematicPlayStatus.TimedOut, result.Status);
            Assert.IsTrue(result.RequiresAbortCleanup);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:TimedOut" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Tick_AccumulatedElapsedPastMaxSeconds_StopsAndReturnsTimedOut()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_maxPlaybackSeconds", 0.5f);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var firstTick = service.Tick(0.25f);
            var timeout = service.Tick(0.26f);

            Assert.AreEqual(CinematicPlayStatus.None, firstTick.Status);
            Assert.AreEqual(CinematicPlayStatus.TimedOut, timeout.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:TimedOut" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Tick_WhenDirectorStoppedAfterStart_CompletesOnceAndClearsPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_maxPlaybackSeconds", 10f);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            director.Stop();
            var completed = service.Tick(0.1f);
            var afterCompletion = service.Tick(0.1f);

            Assert.AreEqual(CinematicPlayStatus.Completed, completed.Status);
            Assert.AreEqual(CinematicPlayStatus.None, afterCompletion.Status);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:Completed" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Tick_WhenDirectorTimeReachesDuration_CompletesPlayback()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var track = timeline.CreateTrack<ActivationTrack>(null, "duration.track");
            var clip = track.CreateDefaultClip();
            clip.start = 0d;
            clip.duration = 1d;
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_maxPlaybackSeconds", 10f);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            director.time = director.duration;
            var completed = service.Tick(0.1f);

            Assert.AreEqual(CinematicPlayStatus.Completed, completed.Status);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:Completed" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Tick_WhenDirectorStoppedBeforeMinimumPlayback_DoesNotCompleteUntilMinimumElapsed()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_minimumPlaybackSeconds", 2f);
            SetFloat(sequence, "_maxPlaybackSeconds", 10f);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            director.Stop();
            var beforeMinimum = service.Tick(0.5f);
            var afterMinimum = service.Tick(1.5f);

            Assert.AreEqual(CinematicPlayStatus.None, beforeMinimum.Status);
            Assert.AreEqual(CinematicPlayStatus.Completed, afterMinimum.Status);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:Completed" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Tick_WhenMinimumPlaybackSecondsIsNegative_StillAppliesSameFrameCompletionFloor()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_minimumPlaybackSeconds", -1f);
            SetFloat(sequence, "_maxPlaybackSeconds", 10f);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            director.Stop();
            var sameFrame = service.Tick(0f);
            var afterFloor = service.Tick(0.01f);

            Assert.AreEqual(CinematicPlayStatus.None, sameFrame.Status);
            Assert.AreEqual(CinematicPlayStatus.Completed, afterFloor.Status);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:Completed" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Skip_WhenPolicyDisabled_ReturnsSkipNotAllowedAndKeepsPlaying()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.Disabled);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Skip(10f);

            Assert.AreEqual(CinematicPlayStatus.SkipNotAllowed, result.Status);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Skip_BeforeMinimumPlayback_ReturnsSkipNotAllowedAndKeepsPlaying()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_minimumPlaybackSeconds", 2f);
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.AllowAfterMinimumPlayback);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Skip(1f);

            Assert.AreEqual(CinematicPlayStatus.SkipNotAllowed, result.Status);
            Assert.AreEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(new[] { "enter:input:cinematic.test" }, events);
            director.Stop();
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Skip_WithoutExplicitElapsed_UsesAccumulatedPlaybackTime()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_minimumPlaybackSeconds", 2f);
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.AllowAfterMinimumPlayback);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            service.Tick(1f);
            var earlySkip = service.Skip();
            service.Tick(1f);
            var allowedSkip = service.Skip();

            Assert.AreEqual(CinematicPlayStatus.SkipNotAllowed, earlySkip.Status);
            Assert.AreEqual(CinematicPlayStatus.SkippedCompleted, allowedSkip.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:SkippedCompleted" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Skip_WhenAllowed_StopsAsSkippedCompletedAndExitsLifecycle()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_minimumPlaybackSeconds", 2f);
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.AllowAfterMinimumPlayback);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Skip(2f);

            Assert.AreEqual(CinematicPlayStatus.SkippedCompleted, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:SkippedCompleted" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Skip_WhenPolicyAlways_IgnoresMinimumPlaybackAndSkipsImmediately()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetFloat(sequence, "_minimumPlaybackSeconds", 2f);
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.Always);
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                null);
            service.Play("cinematic.test");

            var result = service.Skip(0f);

            Assert.AreEqual(CinematicPlayStatus.SkippedCompleted, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[] { "enter:input:cinematic.test", "exit:input:SkippedCompleted" },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void Skip_WhenPolicyAbort_StopsAsAbortedRunsAbortCommandsAndExitsLifecycle()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetObject(sequence, "_timelineAsset", timeline);
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.Abort);
            SetCommands(sequence, new[] { new CommandSetup(CinematicCommandPhase.OnAbort, "fact.clear", "intro") });
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });
            var directorObject = new GameObject("Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            var events = new List<string>();
            var service = new CinematicPlaybackService(
                catalog,
                director,
                new CinematicBindingRegistry(),
                new CinematicProjectPlaybackServices(
                    new ICinematicProjectPlaybackService[]
                    {
                        new RecordingPlaybackService("input", events)
                    }),
                new RecordingCommandExecutor(events));
            service.Play("cinematic.test");

            var result = service.Skip(0f);

            Assert.IsTrue(System.Enum.IsDefined(typeof(CinematicSkipPolicy), "Abort"));
            Assert.AreEqual(CinematicPlayStatus.Aborted, result.Status);
            Assert.AreNotEqual(PlayState.Playing, director.state);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enter:input:cinematic.test",
                    "command:OnAbort:fact.clear:intro",
                    "exit:input:Aborted"
                },
                events);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
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

        private static void SetEnum(Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(Object target, string propertyName, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var array = serialized.FindProperty(propertyName);
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
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

        private sealed class RequestMetadataPlaybackService : ICinematicProjectPlaybackService
        {
            private readonly List<string> _events;

            public RequestMetadataPlaybackService(List<string> events)
            {
                _events = events;
            }

            public void EnterCinematic(CinematicPlaybackContext context)
            {
                _events.Add(
                    $"enter:{context.Request.SequenceId}:{context.Request.SourceId}:{context.Request.AllowInterrupt}");
            }

            public void ExitCinematic(CinematicPlaybackContext context, CinematicPlayResult result)
            {
            }
        }

        private sealed class ThrowingExitPlaybackService : ICinematicProjectPlaybackService
        {
            private readonly string _name;
            private readonly List<string> _events;

            public ThrowingExitPlaybackService(string name, List<string> events)
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
                throw new System.InvalidOperationException($"{_name} exit failed.");
            }
        }

        private sealed class SingleSequenceResolver : ICinematicSequenceResolver
        {
            private readonly CinematicSequenceDefinition _sequence;

            public SingleSequenceResolver(CinematicSequenceDefinition sequence)
            {
                _sequence = sequence;
            }

            public string LastRequestedSequenceId { get; private set; }

            public bool TryResolve(string sequenceId, out CinematicSequenceDefinition sequence)
            {
                LastRequestedSequenceId = sequenceId;
                if (_sequence != null && _sequence.SequenceId == sequenceId)
                {
                    sequence = _sequence;
                    return true;
                }

                sequence = null;
                return false;
            }
        }

        private sealed class RecordingCommandExecutor : ICinematicCommandExecutor
        {
            private readonly List<string> _events;

            public RecordingCommandExecutor(List<string> events)
            {
                _events = events;
            }

            public CinematicCommandResult Execute(
                CinematicCommandDefinition command,
                CinematicPlaybackContext context)
            {
                _events.Add($"command:{command.Phase}:{command.CommandId}:{command.Payload}");
                return CinematicCommandResult.Success();
            }
        }
    }
}
