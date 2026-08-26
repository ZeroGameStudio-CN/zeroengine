using System;

namespace ZeroEngine.Haptics
{
    /// <summary>
    /// Bounded pulse admission. Callers supply an unscaled monotonic timestamp in seconds.
    /// </summary>
    public sealed class HapticArbiter
    {
        public const double CooldownSeconds = 0.060;
        public const float OverlapGuardFraction = 0.60f;
        public const float StrongerReplacementDelta = 0.04f;
        public const float EnergyCapacity = 0.064f;
        public const float EnergyRefillPerSecond = 0.032f;

        private bool _initialized;
        private double _budgetTimestamp;
        private double _lastStartTimestamp;
        private float _lastDuration;
        private float _lastStrength;
        private float _availableEnergy;

        public HapticArbiter()
        {
            Reset();
        }

        public float AvailableEnergy => _availableEnergy;

        public void Reset()
        {
            _initialized = false;
            _budgetTimestamp = 0d;
            _lastStartTimestamp = double.NegativeInfinity;
            _lastDuration = 0f;
            _lastStrength = 0f;
            _availableEnergy = EnergyCapacity;
        }

        public bool TryAccept(
            HapticResolvedPulse requested,
            double timestamp,
            out HapticResolvedPulse accepted)
        {
            accepted = default;
            if (!IsFinite(timestamp) || !IsValid(requested)) return false;

            if (_initialized && timestamp < _budgetTimestamp)
                Reset();

            Refill(timestamp);

            float scale = 1f;
            if (requested.Energy > _availableEnergy)
            {
                if (_availableEnergy <= 0f) return false;
                scale = (float)Math.Sqrt(_availableEnergy / requested.Energy);
            }

            HapticResolvedPulse candidate = requested.Scale(scale);
            if (!IsValid(candidate)
                || candidate.Strength < HapticResponseResolver.MinimumPlayableStrength)
            {
                return false;
            }

            bool withinCooldown = timestamp - _lastStartTimestamp < CooldownSeconds;
            bool withinOverlap = timestamp
                                 < _lastStartTimestamp
                                 + _lastDuration * OverlapGuardFraction;
            bool stronger = candidate.Strength - _lastStrength
                            >= StrongerReplacementDelta;
            if ((withinCooldown || withinOverlap) && !stronger)
                return false;

            _availableEnergy = Math.Max(0f, _availableEnergy - candidate.Energy);
            _lastStartTimestamp = timestamp;
            _lastDuration = candidate.Duration;
            _lastStrength = candidate.Strength;
            accepted = candidate;
            return true;
        }

        private void Refill(double timestamp)
        {
            if (!_initialized)
            {
                _initialized = true;
                _budgetTimestamp = timestamp;
                return;
            }

            double elapsed = timestamp - _budgetTimestamp;
            if (elapsed > 0d)
            {
                double refill = Math.Min(
                    EnergyCapacity,
                    elapsed * EnergyRefillPerSecond);
                _availableEnergy = Math.Min(
                    EnergyCapacity,
                    _availableEnergy + (float)refill);
            }

            _budgetTimestamp = timestamp;
        }

        private static bool IsValid(HapticResolvedPulse pulse)
        {
            return pulse.IsPlayable
                   && HapticResponseResolver.IsFinite(pulse.LowFrequencyMotor)
                   && HapticResponseResolver.IsFinite(pulse.HighFrequencyMotor)
                   && HapticResponseResolver.IsFinite(pulse.Duration)
                   && HapticResponseResolver.IsFinite(pulse.Strength)
                   && HapticResponseResolver.IsFinite(pulse.Energy)
                   && pulse.LowFrequencyMotor >= 0f
                   && pulse.LowFrequencyMotor
                   <= HapticResponseResolver.MaximumLowFrequencyMotor
                   && pulse.HighFrequencyMotor >= 0f
                   && pulse.HighFrequencyMotor
                   <= HapticResponseResolver.MaximumHighFrequencyMotor
                   && pulse.Duration > 0f
                   && pulse.Duration <= HapticResponseResolver.MaximumDuration
                   && pulse.Strength > 0f
                   && pulse.Strength <= HapticResponseResolver.MaximumStrength
                   && pulse.Energy > 0f;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
