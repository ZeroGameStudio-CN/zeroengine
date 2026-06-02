using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Timing
{
    public class TimeScaleModifierState
    {
        private readonly Dictionary<object, float> _modifiers = new();
        private bool _isRecovering;
        private float _recoveryStart;
        private float _recoveryTarget;
        private float _recoveryDuration;
        private float _recoveryElapsed;
        private Action<float> _recoveryOnChanged;

        public float Scale { get; private set; } = 1f;

        public void Set(object token, float speed, Action<float> onChanged)
        {
            if (token == null) return;

            _modifiers[token] = TimeScaleMath.Clamp(speed);
            Apply(GetTargetScale(), 0f, onChanged);
        }

        public void Clear(object token, float recoveryDuration, Action<float> onChanged)
        {
            if (token == null) return;

            _modifiers.Remove(token);
            Apply(GetTargetScale(), recoveryDuration, onChanged);
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!_isRecovering) return;

            if (_recoveryDuration <= 0f)
            {
                CompleteRecovery();
                return;
            }

            _recoveryElapsed += Mathf.Max(0f, unscaledDeltaTime);
            float t = Mathf.Clamp01(_recoveryElapsed / _recoveryDuration);
            SetScale(Mathf.Lerp(_recoveryStart, _recoveryTarget, t), _recoveryOnChanged);

            if (t >= 1f)
            {
                _isRecovering = false;
                _recoveryOnChanged = null;
            }
        }

        public void Reset(Action<float> onChanged = null)
        {
            _modifiers.Clear();
            _isRecovering = false;
            _recoveryOnChanged = null;
            SetScale(1f, onChanged);
        }

        private void Apply(float targetScale, float recoveryDuration, Action<float> onChanged)
        {
            if (recoveryDuration > 0f && targetScale > Scale)
            {
                _isRecovering = true;
                _recoveryStart = Scale;
                _recoveryTarget = targetScale;
                _recoveryDuration = recoveryDuration;
                _recoveryElapsed = 0f;
                _recoveryOnChanged = onChanged;
                return;
            }

            _isRecovering = false;
            _recoveryOnChanged = null;
            SetScale(targetScale, onChanged);
        }

        private void CompleteRecovery()
        {
            SetScale(_recoveryTarget, _recoveryOnChanged);
            _isRecovering = false;
            _recoveryOnChanged = null;
        }

        private void SetScale(float scale, Action<float> onChanged)
        {
            scale = TimeScaleMath.Clamp(scale);
            if (Mathf.Approximately(Scale, scale)) return;

            Scale = scale;
            onChanged?.Invoke(Scale);
        }

        private float GetTargetScale()
        {
            if (_modifiers.Count == 0) return 1f;

            float target = float.PositiveInfinity;
            foreach (float value in _modifiers.Values)
            {
                target = Mathf.Min(target, value);
            }

            return float.IsPositiveInfinity(target) ? 1f : target;
        }
    }
}
