using System;

namespace ZeroEngine.Qte
{
    // No scene, input backend, renderer, time singleton, inventory or game-specific difficulty types.
    public sealed class QteSession
    {
        private QteSettings settings = new QteSettings();
        private float overload;
        public QteState State { get; private set; }
        public QteMode Mode { get; private set; }
        public float Age { get; private set; }
        public float Progress { get; private set; }
        public float Load { get; private set; }
        public bool Engaged { get; private set; }
        public bool Active => State == QteState.Active;
        public bool Succeeded => State == QteState.Succeeded;
        public bool Failed => State == QteState.Failed;
        public float TargetCenter => settings.TargetCenter;
        public float TargetWidth => settings.TargetWidth;
        public float Marker => Clamp01(Age / settings.Duration);
        public float Remaining => Clamp01(1 - Age / settings.Duration);
        private float CycleAge => Age % (settings.CalmDuration + settings.TelegraphDuration + settings.SurgeDuration);
        public bool Preparing => Active && Mode == QteMode.HoldRelease && CycleAge >= settings.CalmDuration &&
            CycleAge < settings.CalmDuration + settings.TelegraphDuration;
        public bool Surging => Active && Mode == QteMode.HoldRelease &&
            CycleAge >= settings.CalmDuration + settings.TelegraphDuration;
        public bool ShouldRelease => Surging || Load > settings.ReleaseWarning;
        public QteSnapshot Snapshot => new QteSnapshot(this);

        public void Begin(QteMode mode, QteSettings configuration)
        {
            if (mode != QteMode.TimingWindow && mode != QteMode.HoldRelease)
                throw new ArgumentOutOfRangeException(nameof(mode));
            var copy = (configuration ?? new QteSettings()).Snapshot();
            if (mode == QteMode.HoldRelease)
                ValidateHoldRelease(copy);
            Reset();
            Mode = mode;
            settings = copy;
            Load = Clamp01(settings.InitialLoad);
            State = QteState.Active;
        }

        public void Press()
        {
            if (!Active) return;
            if (Mode == QteMode.HoldRelease) { Engaged = true; return; }
            State = Math.Abs(Marker - TargetCenter) <= TargetWidth * .5f ? QteState.Succeeded : QteState.Failed;
            if (Succeeded) Progress = 1;
        }

        public void Advance(float delta, bool held)
        {
            if (!Active || !(delta > 0) || !QteSettings.Finite(delta)) return;
            if (Mode == QteMode.TimingWindow)
            {
                Age += delta;
                if (Age >= settings.Duration) State = QteState.Failed;
                return;
            }
            float remaining = Math.Min(delta, settings.Duration - Age);
            while (remaining > .000001f && Active)
            {
                float step = Math.Min(.02f, remaining);
                remaining -= step;
                Age += step;
                if (!Engaged)
                {
                    if (Age >= Math.Min(settings.Duration, Math.Max(.1f, settings.EngageTimeout))) State = QteState.Failed;
                    continue;
                }
                float load = held ? (Surging ? settings.SurgeLoad : Preparing ? settings.TelegraphLoad : settings.CalmLoad) * settings.ForceMultiplier : -settings.ReleaseRate;
                Load = Clamp01(Load + load * step);
                Progress = Clamp01(Progress + step * (held ? settings.ProgressPerSecond * (Surging ? settings.SurgeProgressScale : 1) : -settings.ReleasedProgressLoss));
                overload = Load >= settings.OverloadThreshold ? overload + step : 0;
                if (overload >= Math.Max(.01f, settings.OverloadGrace)) State = QteState.Failed;
                else if (Progress >= 1) State = QteState.Succeeded;
            }
            if (Active && Age >= settings.Duration - .001f) State = QteState.Failed;
        }

        public void Cancel()
        {
            if (Active) State = QteState.Cancelled;
        }

        public void Reset()
        {
            State = QteState.Ready;
            Age = Progress = Load = overload = 0;
            Engaged = false;
        }

        private static float Clamp01(float value) => Math.Max(0, Math.Min(1, value));
        private static void ValidateHoldRelease(QteSettings value)
        {
            var values = new[] { value.EngageTimeout, value.CalmDuration, value.TelegraphDuration, value.SurgeDuration,
                value.ForceMultiplier, value.ProgressPerSecond, value.InitialLoad, value.CalmLoad, value.TelegraphLoad,
                value.SurgeLoad, value.ReleaseRate, value.SurgeProgressScale, value.ReleasedProgressLoss,
                value.ReleaseWarning, value.OverloadThreshold, value.OverloadGrace };
            foreach (float item in values)
                if (!QteSettings.Finite(item) || item < 0) throw new ArgumentException("Use finite, non-negative hold/release settings.");
            if (value.CalmDuration + value.TelegraphDuration + value.SurgeDuration <= .001f || value.ForceMultiplier <= 0)
                throw new ArgumentException("A hold/release challenge needs a positive cycle and force multiplier.");
        }
    }
}
