using System;
using UnityEngine;

namespace ZeroEngine.Cinematic
{
    /// <summary>
    /// Borrows a subject's world position until its owner or placement changes.
    /// The owner supplies a side-effect-free identity/lifetime predicate; normal
    /// playback cancellation should not revoke it before owned cleanup can run.
    /// Rotation, scale, parenting, animation and game rules remain with the caller.
    /// </summary>
    public sealed class CinematicPositionScope : IDisposable
    {
        private readonly Transform _subject;
        private readonly Transform _parent;
        private readonly Func<bool> _isCurrent;
        private Vector3 _lastPosition;
        private bool _retired;

        public Vector3 HomePosition { get; }

        public CinematicPositionScope(Transform subject, Vector3 homePosition, Func<bool> isCurrent)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (!IsFinite(homePosition)) throw new ArgumentException("A finite home position is required.", nameof(homePosition));
            _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            _subject = subject;
            _parent = subject.parent;
            _lastPosition = subject.position;
            HomePosition = homePosition;
            _retired = !subject.gameObject.activeInHierarchy || !IsFinite(_lastPosition);
        }

        /// <summary>Once lost, ownership cannot be reacquired by this scope.</summary>
        public bool HasOwnership
        {
            get
            {
                if (_retired) return false;
                bool current = false;
                try
                {
                    current = _isCurrent() && _subject != null && _subject.gameObject.activeInHierarchy
                        && _subject.parent == _parent
                        && (_subject.position - _lastPosition).sqrMagnitude < .000001f;
                    return current;
                }
                finally
                {
                    if (!current) _retired = true;
                }
            }
        }

        public bool TryGetPosition(out Vector3 position)
        {
            position = default;
            if (!HasOwnership) return false;
            position = _lastPosition;
            return true;
        }

        public bool TrySetPosition(Vector3 position)
        {
            if (!IsFinite(position)) throw new ArgumentException("A finite position is required.", nameof(position));
            if (!HasOwnership) return false;
            _subject.position = position;
            _lastPosition = position;
            return true;
        }

        /// <summary>Returns home without ending the scope, for Timeline sampling/replay.</summary>
        public bool TryRestore() => TrySetPosition(HomePosition);

        public void Dispose()
        {
            if (_retired) return;
            try { TryRestore(); }
            finally { _retired = true; }
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
