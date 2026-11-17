using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CM = Unity.Cinemachine;
using Object = UnityEngine.Object;

namespace ZeroEngine.Cinematic.Cinemachine
{
    /// <summary>
    /// Binds native Cinemachine shots to a Brain without taking over the Timeline clock.
    /// The caller retains its rig, subjects, camera authority and final pose-restoration policy.
    /// </summary>
    public sealed class CinemachineTimelineScope : IDisposable
    {
        private readonly struct TrackBinding
        {
            public readonly CM.CinemachineTrack Track;
            public readonly Object Previous;
            public TrackBinding(CM.CinemachineTrack track, Object previous) { Track = track; Previous = previous; }
        }
        private readonly struct ReferenceBinding
        {
            public readonly PropertyName Name;
            public readonly Object Previous;
            public readonly bool WasValid;
            public readonly CM.CinemachineVirtualCameraBase Current;
            public ReferenceBinding(PropertyName name, Object previous, bool valid, CM.CinemachineVirtualCameraBase current)
            { Name = name; Previous = previous; WasValid = valid; Current = current; }
        }
        private readonly struct CameraBinding
        {
            public readonly CM.CinemachineVirtualCameraBase Camera;
            public readonly bool WasEnabled;
            public CameraBinding(CM.CinemachineVirtualCameraBase camera) { Camera = camera; WasEnabled = camera.enabled; }
        }

        private readonly List<TrackBinding> _tracks = new();
        private readonly List<ReferenceBinding> _references = new();
        private readonly List<CameraBinding> _cameras = new();
        private readonly CM.CinemachineCore.BrainEvent _updatedEvent;
        private readonly Func<bool> _ownsBindings;
        private readonly Func<bool> _cameraIsCurrent;
        private readonly PlayableDirector _director;
        private readonly TimelineAsset _timeline;
        private readonly CM.CinemachineBrain _brain;
        private readonly Camera _camera;
        private readonly Transform _cameraParent;
        private readonly bool _cameraEnabled;
        private CinemachineTimelineOverrideProxy _proxy;
        private bool _retired;
        private bool _disposed;

        public bool HasAppliedPose { get; private set; }
        public Pose LastAppliedPose { get; private set; }
        public float LastAppliedFieldOfView { get; private set; }
        public bool LastAppliedOrthographic { get; private set; }
        public int AppliedFrameCount { get; private set; }
        public int OwnedOverrideCount => _proxy != null ? _proxy.OwnedOverrideCount : 0;
        public bool IsRetired => _retired || _disposed;

        public CinemachineTimelineScope(PlayableDirector director, TimelineAsset timeline, CM.CinemachineBrain brain,
            CinematicBindingRegistry bindings, Func<bool> ownsBindings = null, Func<bool> cameraIsCurrent = null)
        {
            if (director == null || timeline == null || bindings == null || brain != null && brain.OutputCamera == null)
                throw new ArgumentException("Native camera playback needs its director, Timeline, Brain output and rig bindings.");
            _director = director;
            _timeline = timeline;
            _brain = brain;
            _camera = brain != null ? brain.OutputCamera : null;
            _cameraParent = _camera != null ? _camera.transform.parent : null;
            _cameraEnabled = _camera != null && _camera.enabled;
            _ownsBindings = ownsBindings;
            _cameraIsCurrent = cameraIsCurrent;
            _updatedEvent = CM.CinemachineCore.CameraUpdatedEvent;

            // Resolve every shot before borrowing anything. Native clips may share a reference key.
            var seenReferences = new HashSet<PropertyName>();
            var seenCameras = new HashSet<CM.CinemachineVirtualCameraBase>();
            foreach (var output in timeline.GetOutputTracks())
            {
                if (output is not CM.CinemachineTrack track || track.mutedInHierarchy) continue;
                _tracks.Add(new TrackBinding(track, director.GetGenericBinding(track)));
                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is not CM.CinemachineShot shot) continue;
                    var name = shot.VirtualCamera.exposedName;
                    if (PropertyName.IsNullOrEmpty(name) || !bindings.TryResolve(name, out var binding)
                        || binding is not CM.CinemachineVirtualCameraBase camera)
                        throw new InvalidOperationException($"Native shot '{clip.displayName}' has no camera binding '{name}'.");
                    if (seenReferences.Add(name))
                    {
                        var previous = director.GetReferenceValue(name, out bool valid);
                        _references.Add(new ReferenceBinding(name, previous, valid, camera));
                    }
                    if (seenCameras.Add(camera)) _cameras.Add(new CameraBinding(camera));
                }
            }
            if (_tracks.Count == 0 || _references.Count == 0)
                throw new InvalidOperationException("The selected Timeline has no bound native Cinemachine shots.");
            if (_ownsBindings?.Invoke() == false || _cameraIsCurrent?.Invoke() == false)
                throw new OperationCanceledException("The native camera owner retired before binding.");

            try
            {
                _proxy = ScriptableObject.CreateInstance<CinemachineTimelineOverrideProxy>();
                _proxy.hideFlags = HideFlags.HideAndDontSave;
                if (brain != null) _proxy.Initialize(brain, CanSampleOrRetire);
                else { _proxy.Retire(); _retired = true; } // Explicit cue-only playback still supplies a valid native track binding.
                foreach (var track in _tracks) director.SetGenericBinding(track.Track, _proxy);
                foreach (var reference in _references) director.SetReferenceValue(reference.Name, reference.Current);
                foreach (var camera in _cameras)
                {
                    if (brain == null) continue;
                    camera.Camera.PreviousStateIsValid = false;
                    camera.Camera.enabled = true;
                }
                if (brain != null) _updatedEvent.AddListener(OnCameraUpdated);
            }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
                throw;
            }
        }

        private bool CanSample()
        {
            return !_disposed && !_retired && _director != null && _director.playableAsset == _timeline
                && _brain != null && _brain.isActiveAndEnabled && _brain.OutputCamera == _camera
                && _camera != null && _camera.gameObject.activeInHierarchy
                && _camera.enabled == _cameraEnabled && _camera.transform.parent == _cameraParent
                && _ownsBindings?.Invoke() != false && _cameraIsCurrent?.Invoke() != false;
        }

        private void OnCameraUpdated(CM.CinemachineBrain brain)
        {
            if (brain != _brain || _disposed || _retired) return;
            if (!CanSample()) { Retire(); return; }
            LastAppliedPose = new Pose(_camera.transform.position, _camera.transform.rotation);
            LastAppliedFieldOfView = _camera.fieldOfView;
            LastAppliedOrthographic = _camera.orthographic;
            HasAppliedPose = true;
            AppliedFrameCount++;
        }

        private bool CanSampleOrRetire()
        {
            if (CanSample()) return true;
            Retire();
            return false;
        }

        /// <summary>Immediately releases this Timeline's override IDs while other cue tracks can continue.</summary>
        public void Retire()
        {
            if (_retired) return;
            _retired = true;
            _updatedEvent?.RemoveListener(OnCameraUpdated);
            try { _proxy?.Retire(); }
            finally
            {
                // Disabled rig cameras must not become automatic priority fallbacks
                // after their Timeline override is retired.
                if (_ownsBindings?.Invoke() != false)
                    foreach (var camera in _cameras)
                        if (camera.Camera != null && camera.Camera.enabled && !camera.WasEnabled)
                            camera.Camera.enabled = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _updatedEvent?.RemoveListener(OnCameraUpdated);
            var errors = new List<Exception>();
            try { _proxy?.Retire(); } catch (Exception error) { errors.Add(error); }
            bool owns = false;
            try { owns = _ownsBindings?.Invoke() != false; } catch (Exception error) { errors.Add(error); }
            if (owns)
            {
                for (int i = _references.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var reference = _references[i];
                        if (_director == null) continue;
                        var current = _director.GetReferenceValue(reference.Name, out bool valid);
                        if (!valid || !ReferenceEquals(current, reference.Current)) continue;
                        if (reference.WasValid && reference.Previous != null)
                            _director.SetReferenceValue(reference.Name, reference.Previous);
                        else _director.ClearReferenceValue(reference.Name);
                    }
                    catch (Exception error) { errors.Add(error); }
                }
                for (int i = _tracks.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var track = _tracks[i];
                        if (_director == null || !ReferenceEquals(_director.GetGenericBinding(track.Track), _proxy)) continue;
                        if (track.Previous != null) _director.SetGenericBinding(track.Track, track.Previous);
                        else _director.ClearGenericBinding(track.Track);
                    }
                    catch (Exception error) { errors.Add(error); }
                }
                foreach (var camera in _cameras)
                {
                    try { if (camera.Camera != null && camera.Camera.enabled) camera.Camera.enabled = camera.WasEnabled; }
                    catch (Exception error) { errors.Add(error); }
                }
            }
            if (_proxy != null)
            {
                if (Application.isPlaying) Object.Destroy(_proxy);
                else Object.DestroyImmediate(_proxy);
                _proxy = null;
            }
            _tracks.Clear();
            _references.Clear();
            _cameras.Clear();
            if (errors.Count != 0) throw new AggregateException("Native camera bindings could not all be restored.", errors);
        }
    }
}
