using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Character.Exploration
{
    /// <summary>zero.character.footstep_sample.v1. POB's distance sampling generalized to a caller-projected ground plane.</summary>
    public struct FootstepDistanceSampler
    {
        private Vector2 _previous;
        private Vector2 _direction;
        private float _remainder;
        private float _lastSpacing;
        private bool _hasPrevious;
        public void Reset() => this = default;

        public void Advance(Vector2 position, bool active, float spacing, List<Vector2> destination, float relocationDistance = 8f)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!active || !Finite(position.x) || !Finite(position.y) || !Finite(spacing)
                || !Finite(relocationDistance) || spacing < 0.05f || spacing > 10f || relocationDistance < spacing)
            { Reset(); return; }
            if (_lastSpacing != spacing) _remainder = 0f;
            _lastSpacing = spacing;
            if (!_hasPrevious) { _previous = position; _hasPrevious = true; return; }
            var delta = position - _previous;
            float distance = delta.magnitude;
            if (distance > relocationDistance) _remainder = 0f;
            else if (distance > 0.0001f)
            {
                var direction = delta / distance;
                if (Vector2.Dot(_direction, direction) < 0f) _remainder = 0f;
                float offset = spacing - _remainder;
                while (offset <= distance && destination.Count < 8)
                {
                    destination.Add(Vector2.Lerp(_previous, position, offset / distance));
                    offset += spacing;
                }
                // A bounded frame discards excess contacts instead of replaying a backlog next frame.
                _remainder = (_remainder + distance) % spacing;
                _direction = direction;
            }
            _previous = position;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
