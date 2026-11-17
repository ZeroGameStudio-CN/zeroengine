using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Splines;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace ZeroEngine.Cinematic.Cinemachine.Tests
{
    public sealed class CinemachineTimelineScopeTests
    {
        private readonly List<Object> _owned = new();
        private PlayableDirector _director;
        private TimelineAsset _timeline;
        private CinemachineTrack _track;
        private CinemachineBrain _brain;
        private Camera _output;
        private CinematicBindingRegistry _bindings;
        private CinemachineCamera _first, _second;
        private CinemachineTimelineScope _scope;

        [SetUp]
        public void SetUp()
        {
            _output = Go("Output").AddComponent<Camera>();
            _brain = _output.gameObject.AddComponent<CinemachineBrain>();
            _brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            _brain.ChannelMask = (OutputChannels)(1 << 30);
            _brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            _director = Go("Director").AddComponent<PlayableDirector>();
            _director.timeUpdateMode = DirectorUpdateMode.Manual;
            _timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            _timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            _timeline.fixedDuration = 3d;
            _track = _timeline.CreateTrack<CinemachineTrack>(null, "Native shots");
            _track.TrackPriority = 100;
            _director.playableAsset = _timeline;
            _bindings = new CinematicBindingRegistry();
            _first = Camera("first", Vector3.zero);
            _second = Camera("second", new Vector3(10f, 0f, 0f));
            Shot("first", 0d, 2d);
            Shot("second", 1d, 2d);
        }

        [TearDown]
        public void TearDown()
        {
            _director?.Stop();
            _scope?.Dispose();
            _scope = null;
            if (_director != null) _director.playableAsset = null;
            if (_timeline != null)
                foreach (var track in _timeline.GetRootTracks().ToArray()) _timeline.DeleteTrack(track);
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void Overlap_UsesNativeBlendAndRestoresBorrowedBindings()
        {
            _director.SetGenericBinding(_track, _brain);
            _scope = new CinemachineTimelineScope(_director, _timeline, _brain, _bindings);
            Sample(.5d);
            float first = _output.transform.position.x;
            Sample(1.5d);
            float middle = _output.transform.position.x;
            Sample(2.5d);
            float last = _output.transform.position.x;
            Assert.That(first, Is.EqualTo(0f).Within(.02f));
            Assert.That(last, Is.EqualTo(10f).Within(.02f));
            Assert.That(middle, Is.GreaterThan(1f).And.LessThan(9f));
            Assert.That(_scope.OwnedOverrideCount, Is.EqualTo(1));
            Assert.That(_scope.AppliedFrameCount, Is.GreaterThanOrEqualTo(3));
            _director.Stop();
            _scope.Dispose();
            _scope.Dispose();
            Assert.That(_director.GetGenericBinding(_track), Is.SameAs(_brain));
            Assert.That(_first.enabled || _second.enabled, Is.False);
            Assert.That(_scope.OwnedOverrideCount, Is.Zero);
        }

        [Test]
        public void AnimationTrack_DrivesNativeSplineDollyOnTheSameClock()
        {
            foreach (var clip in _track.GetClips().ToArray()) _track.DeleteClip(clip);
            Shot("first", 0d, 3d);
            var spline = Go("Rail").AddComponent<SplineContainer>();
            spline.Spline = new Spline(new[] { new BezierKnot(new float3(0f, 0f, -5f)), new BezierKnot(new float3(8f, 0f, -5f)) });
            var dolly = _first.gameObject.AddComponent<CinemachineSplineDolly>();
            dolly.Spline = spline;
            dolly.PositionUnits = PathIndexUnit.Normalized;
            var animator = _first.gameObject.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var animation = Own(new AnimationClip());
            AnimationUtility.SetEditorCurve(animation,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(CinemachineSplineDolly), "m_SplineSettings.Position"),
                AnimationCurve.Linear(0f, 0f, 3f, 1f));
            var track = _timeline.CreateTrack<AnimationTrack>(null, "Native rail position");
            var clipAsset = track.CreateClip<AnimationPlayableAsset>();
            ((AnimationPlayableAsset)clipAsset.asset).clip = animation;
            clipAsset.duration = 3d;
            _director.SetGenericBinding(track, animator);
            _scope = new CinemachineTimelineScope(_director, _timeline, _brain, _bindings);
            Sample(.3d);
            float start = _output.transform.position.x;
            Sample(2.7d);
            Assert.That(dolly.CameraPosition, Is.EqualTo(.9f).Within(.03f));
            Assert.That(_output.transform.position.x - start, Is.GreaterThan(4f));
        }

        [Test]
        public void MissingShot_PreflightLeavesExistingOwnerUntouched()
        {
            _director.SetGenericBinding(_track, _brain);
            _bindings.Unregister("second", _second);
            Assert.Throws<InvalidOperationException>(() => new CinemachineTimelineScope(_director, _timeline, _brain, _bindings));
            Assert.That(_director.GetGenericBinding(_track), Is.SameAs(_brain));
            Assert.That(_first.enabled || _second.enabled, Is.False);
            Assert.That(_director.GetReferenceValue(new PropertyName("first"), out bool valid), Is.Null);
            Assert.That(valid, Is.False);
        }

        [Test]
        public void Retire_ReleasesOnlyItsOverride_WhileOtherClientKeepsCamera()
        {
            _scope = new CinemachineTimelineScope(_director, _timeline, _brain, _bindings);
            Sample(.5d);
            var foreign = Camera("foreign", new Vector3(23f, 0f, 0f));
            foreign.enabled = true;
            int id = _brain.SetCameraOverride(-1, 1000, null, foreign, 1f, -1f);
            try
            {
                _scope.Retire();
                Sample(1.5d);
                Assert.That(_scope.IsRetired, Is.True);
                Assert.That(_scope.OwnedOverrideCount, Is.Zero);
                Assert.That(_output.transform.position.x, Is.EqualTo(23f).Within(.02f));
                Assert.That(_first.enabled || _second.enabled, Is.False);
            }
            finally { _brain.ReleaseCameraOverride(id); }
        }

        [Test]
        public void OwnerReplacement_PreservesNewBindingsAndCameraState()
        {
            bool owns = true;
            _scope = new CinemachineTimelineScope(_director, _timeline, _brain, _bindings, () => owns);
            Sample(.5d);
            owns = false;
            _director.SetGenericBinding(_track, _brain);
            _director.SetReferenceValue(new PropertyName("first"), _second);
            _scope.Dispose();
            Assert.That(_director.GetGenericBinding(_track), Is.SameAs(_brain));
            Assert.That(_director.GetReferenceValue(new PropertyName("first"), out bool valid), Is.SameAs(_second));
            Assert.That(valid, Is.True);
            Assert.That(_first.enabled && _second.enabled, Is.True);
            Assert.That(_scope.OwnedOverrideCount, Is.Zero);
        }

        [Test]
        public void CameraAuthorityLoss_RetiresOldMixer_BeforeSubsequentSample()
        {
            bool current = true;
            _scope = new CinemachineTimelineScope(_director, _timeline, _brain, _bindings, cameraIsCurrent: () => current);
            Sample(.5d);
            current = false;
            Sample(1.5d);
            Assert.That(_scope.IsRetired, Is.True);
            Assert.That(_scope.OwnedOverrideCount, Is.Zero);
            Assert.That(_first.enabled || _second.enabled, Is.False);
        }

        [Test]
        public void CueOnlyPlayback_BindsAnInertNativeTrackWithoutEnablingRigCameras()
        {
            _scope = new CinemachineTimelineScope(_director, _timeline, null, _bindings);
            _director.Play();
            _director.time = 1.5d;
            _director.Evaluate();
            Assert.That(_director.GetGenericBinding(_track), Is.InstanceOf<ICameraOverrideStack>());
            Assert.That(_scope.IsRetired, Is.True);
            Assert.That(_scope.OwnedOverrideCount, Is.Zero);
            Assert.That(_scope.AppliedFrameCount, Is.Zero);
            Assert.That(_first.enabled || _second.enabled, Is.False);
        }

        [Test]
        public void SequentialReplay_HasFreshOverridesAndNoStaleBindings()
        {
            for (int i = 0; i < 2; i++)
            {
                _scope = new CinemachineTimelineScope(_director, _timeline, _brain, _bindings);
                Sample(.5d);
                Assert.That(_scope.OwnedOverrideCount, Is.EqualTo(1));
                _director.Stop();
                _scope.Dispose();
                Assert.That(_director.GetGenericBinding(_track), Is.Null);
                Assert.That(_director.GetReferenceValue(new PropertyName("first"), out bool valid), Is.Null);
                Assert.That(valid, Is.False);
            }
        }

        private GameObject Go(string name) => Own(new GameObject("ZE camera scope test " + name));
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }
        private CinemachineCamera Camera(string key, Vector3 position)
        {
            var camera = Go(key).AddComponent<CinemachineCamera>();
            camera.OutputChannel = (OutputChannels)(1 << 30);
            camera.transform.position = position;
            camera.Priority = -10000;
            camera.enabled = false;
            _bindings.Register(key, camera);
            return camera;
        }
        private void Shot(string key, double start, double duration)
        {
            var clip = _track.CreateClip<CinemachineShot>();
            clip.start = start;
            clip.duration = duration;
            ((CinemachineShot)clip.asset).VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = new PropertyName(key) };
        }
        private void Sample(double time)
        {
            if (!_director.playableGraph.IsValid()) _director.Play();
            _director.time = time;
            _director.Evaluate();
            _brain.ManualUpdate();
        }
    }
}
