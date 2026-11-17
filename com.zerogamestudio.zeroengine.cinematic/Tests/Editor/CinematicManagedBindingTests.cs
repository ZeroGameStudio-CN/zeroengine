using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicManagedBindingTests
    {
        private GameObject _root, _binding;
        private PlayableDirector _director;
        private TimelineAsset _timeline;
        private ControlTrack _track;
        private CinematicSequenceDefinition _sequence;
        private CinematicPlayableDirectorAdapter _adapter;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Managed binding test director");
            _binding = new GameObject("Managed binding test owner");
            _director = _root.AddComponent<PlayableDirector>();
            _director.timeUpdateMode = DirectorUpdateMode.Manual;
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _track = _timeline.CreateTrack<ControlTrack>(null, "Native owner track");
            _sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            using var data = new SerializedObject(_sequence);
            data.FindProperty("_sequenceId").stringValue = "cinematic.managed_binding";
            data.FindProperty("_timelineAsset").objectReferenceValue = _timeline;
            var rows = data.FindProperty("_bindingRequirements"); rows.arraySize = 1;
            var row = rows.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("_bindingKey").stringValue = "camera.timeline.override";
            row.FindPropertyRelative("_track").objectReferenceValue = _track;
            row.FindPropertyRelative("_managedByProjectService").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            _adapter = new CinematicPlayableDirectorAdapter();
        }

        [TearDown]
        public void TearDown()
        {
            _adapter.Stop(_director, CinematicPlayStatus.Cancelled);
            _director.Stop(); _director.playableAsset = null;
            foreach (var track in _timeline.GetRootTracks().ToArray()) _timeline.DeleteTrack(track);
            Object.DestroyImmediate(_sequence); Object.DestroyImmediate(_timeline);
            Object.DestroyImmediate(_binding); Object.DestroyImmediate(_root);
        }

        [Test]
        public void ProjectManagedTrack_IsValidatedAndBoundByTheEnteredService()
        {
            Assert.That(new CinematicValidationKernel().ValidateSequence(_sequence)
                .Where(issue => issue.Severity == CinematicValidationSeverity.Error), Is.Empty);
            var service = new BindingService(_director, _track, _binding);
            var result = Play(new CinematicBindingRegistry(), service);
            Assert.That(result.Status, Is.EqualTo(CinematicPlayStatus.Started));
            Assert.That(_director.GetGenericBinding(_track), Is.SameAs(_binding));
            _adapter.Stop(_director, CinematicPlayStatus.Cancelled);
            Assert.That(service.Exits, Is.EqualTo(1));
            Assert.That(_director.GetGenericBinding(_track), Is.Null);
        }

        [Test]
        public void ProjectManagedTrack_MissingBindingAbortsEnteredServicesBeforePlayback()
        {
            var service = new BindingService(_director, _track, null);
            var result = Play(new CinematicBindingRegistry(), service);
            Assert.That(result.Status, Is.EqualTo(CinematicPlayStatus.BindingMissing));
            Assert.That(service.Exits, Is.EqualTo(1));
            Assert.That(_director.state, Is.Not.EqualTo(PlayState.Playing));
            Assert.That(_adapter.Stop(_director, CinematicPlayStatus.Cancelled).Status, Is.EqualTo(CinematicPlayStatus.None));
        }

        [Test]
        public void OrdinaryTrack_StillRequiresItsRegistryObject()
        {
            using (var data = new SerializedObject(_sequence))
            {
                data.FindProperty("_bindingRequirements").GetArrayElementAtIndex(0).FindPropertyRelative("_managedByProjectService").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var service = new BindingService(_director, _track, _binding);
            Assert.That(Play(new CinematicBindingRegistry(), service).Status, Is.EqualTo(CinematicPlayStatus.BindingMissing));
            Assert.That(service.Entered, Is.Zero);
            Assert.That(new CinematicBindingRequirement("ordinary", _track).ManagedByProjectService, Is.False);
        }

        [Test]
        public void NativePropertyName_ResolvesTheActualStableKeyAndItsLiveStack()
        {
            var registry = new CinematicBindingRegistry();
            registry.Register("camera.shot.rail", _root);
            registry.Register("camera.shot.rail", _binding);
            var key = new PropertyName("camera.shot.rail");
            Assert.That(registry.TryResolve(key, out var found), Is.True);
            Assert.That(found, Is.SameAs(_binding));
            registry.Unregister("camera.shot.rail", _binding);
            Assert.That(registry.TryResolve(key, out found), Is.True);
            Assert.That(found, Is.SameAs(_root));
            Assert.That(registry.TryResolve(default(PropertyName), out _), Is.False);
        }

        private CinematicPlayResult Play(CinematicBindingRegistry registry, BindingService service) =>
            _adapter.Play(CinematicPlayRequest.FromSequence(_sequence), _sequence, _director, registry,
                new CinematicProjectPlaybackServices(new ICinematicProjectPlaybackService[] { service }), null);

        private sealed class BindingService : ICinematicProjectPlaybackService
        {
            private readonly PlayableDirector _director;
            private readonly TrackAsset _track;
            private readonly Object _binding;
            public int Entered, Exits;
            public BindingService(PlayableDirector director, TrackAsset track, Object binding) { _director = director; _track = track; _binding = binding; }
            public void EnterCinematic(CinematicPlaybackContext context) { Entered++; if (_binding != null) _director.SetGenericBinding(_track, _binding); }
            public void ExitCinematic(CinematicPlaybackContext context, CinematicPlayResult result) { Exits++; _director.ClearGenericBinding(_track); }
        }
    }
}
