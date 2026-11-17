using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CM = Unity.Cinemachine;

namespace ZeroEngine.Cinematic.Cinemachine
{
    /// <summary>Gives native Timeline mixers a releasable subset of one Brain's override stack.</summary>
    public sealed class CinemachineTimelineOverrideProxy : ScriptableObject, CM.ICameraOverrideStack
    {
        private readonly HashSet<int> _owned = new();
        private CM.CinemachineBrain _brain;
        private Func<bool> _isCurrent;
        private bool _initialized;
        private bool _retired;

        public int OwnedOverrideCount => _owned.Count;
        public bool IsRetired => _retired;
        public Vector3 DefaultWorldUp => _brain != null ? _brain.DefaultWorldUp : Vector3.up;

        public void Initialize(CM.CinemachineBrain brain, Func<bool> isCurrent)
        {
            if (_initialized || brain == null) throw new InvalidOperationException("A camera override proxy requires one live Brain owner.");
            _initialized = true;
            _brain = brain;
            _isCurrent = isCurrent;
        }

        public int SetCameraOverride(int overrideId, int priority, CM.ICinemachineCamera camA,
            CM.ICinemachineCamera camB, float weightB, float deltaTime)
        {
            bool current;
            try { current = !_retired && _brain != null && _isCurrent?.Invoke() != false; }
            catch
            {
                Retire();
                throw;
            }
            if (!current)
            {
                Retire();
                return -1;
            }
            // A stale mixer ID cannot address another client's entry, even if an integer is reused.
            if (!_owned.Contains(overrideId)) overrideId = -1;
            int id = _brain.SetCameraOverride(overrideId, priority, camA, camB, weightB, deltaTime);
            if (id >= 0) _owned.Add(id);
            return id;
        }

        public void ReleaseCameraOverride(int overrideId)
        {
            if (!_owned.Remove(overrideId)) return;
            if (_brain != null) _brain.ReleaseCameraOverride(overrideId);
        }

        public void Retire()
        {
            if (_retired) return;
            _retired = true;
            var ids = _owned.ToArray();
            _owned.Clear();
            var errors = new List<Exception>();
            foreach (int id in ids)
            {
                try { if (_brain != null) _brain.ReleaseCameraOverride(id); }
                catch (Exception error) { errors.Add(error); }
            }
            _isCurrent = null;
            if (errors.Count != 0) throw new AggregateException("Owned Cinemachine overrides could not all be released.", errors);
        }

        private void OnDestroy() => Retire();
    }
}
